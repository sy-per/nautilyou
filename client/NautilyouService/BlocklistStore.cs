using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace NautilyouService;

public enum BlocklistFormat
{
    // Archive .tar.gz de l'Universite Toulouse Capitole : un dossier par categorie, fichier "domains".
    UtCapitoleTarGz,
    // Fichier "hosts" : lignes "0.0.0.0 domaine".
    Hosts,
    // Un domaine par ligne.
    DomainList,
}

// Une liste publique de domaines (ou le DNS "famille" de Cloudflare, sans liste : Url nulle).
public record BlocklistSource(string Id, string Url, BlocklistFormat Format, string? ArchiveEntry = null);

public static class BlocklistCatalog
{
    // Les identifiants doivent rester identiques a ceux de dashboard-ui/src/blocklists.js.
    public const string CloudflareFamilyId = "cloudflare-family";

    public static readonly IReadOnlyList<BlocklistSource> Sources = new[]
    {
        new BlocklistSource("ut1-adult", "https://dsi.ut-capitole.fr/blacklists/download/adult.tar.gz",
            BlocklistFormat.UtCapitoleTarGz, "adult/domains"),
        new BlocklistSource("blp-porn", "https://blocklistproject.github.io/Lists/porn.txt", BlocklistFormat.Hosts),
        new BlocklistSource("stevenblack-porn", "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/porn-only/hosts", BlocklistFormat.Hosts),
        new BlocklistSource("hagezi-nsfw", "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/wildcard/nsfw-onlydomains.txt", BlocklistFormat.DomainList),
    };

    public static BlocklistSource? Find(string id) => Sources.FirstOrDefault(s => s.Id == id);
}

// Empreinte 64 bits (FNV-1a) d'un nom de domaine en minuscules. Les listes sont stockees sous forme
// de tableaux tries d'empreintes plutot que de textes : la liste UT1 compte plus de 4,6 millions de
// domaines, soit environ 37 Mo en empreintes contre plus de 250 Mo en chaines. Le risque de collision
// (un domaine innocent qui aurait la meme empreinte qu'un domaine de la liste) est de l'ordre de
// 1 sur 10^12 par requete, negligeable.
public static class DomainHash
{
    public static ulong Of(ReadOnlySpan<char> domain)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in domain)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= 1099511628211UL;
        }
        return hash;
    }
}

// Telecharge chaque liste depuis sa source, la convertit en tableau d'empreintes triees et la garde en
// cache dans %ProgramData%\Nautilyou\blocklists. Une liste est retelechargee au plus une fois par semaine ;
// si le telechargement echoue, la copie en cache (meme ancienne) reste utilisee : le filtre continue de
// fonctionner hors connexion.
public class BlocklistStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const int MaxEntries = 20_000_000;

    private readonly ILogger _logger;
    private readonly string _dir;

    public BlocklistStore(ILogger logger, string? directory = null)
    {
        _logger = logger;
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nautilyou", "blocklists");
    }

    private string CachePath(string id) => Path.Combine(_dir, id + ".bin");

    public async Task<ulong[]?> EnsureAsync(BlocklistSource source, CancellationToken ct)
    {
        var path = CachePath(source.Id);
        var cached = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;

        if (cached is not null && DateTime.UtcNow - cached < MaxAge)
        {
            return Load(path);
        }

        try
        {
            _logger.LogInformation("Filtre enfant : telechargement de la liste {Id}...", source.Id);
            var hashes = await DownloadAsync(source, ct);
            Directory.CreateDirectory(_dir);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, MemoryMarshal.AsBytes<ulong>(hashes).ToArray());
            File.Move(tmp, path, overwrite: true);
            _logger.LogInformation("Filtre enfant : liste {Id} chargee ({Count} domaines)", source.Id, hashes.Length);
            return hashes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Filtre enfant : telechargement de {Id} impossible ({Message})", source.Id, ex.Message);
            if (cached is not null)
            {
                _logger.LogInformation("Filtre enfant : copie locale de {Id} utilisee (du {Date})", source.Id, cached);
                return Load(path);
            }
            return null;
        }
    }

    private static ulong[] Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return MemoryMarshal.Cast<byte, ulong>(bytes).ToArray();
    }

    private async Task<ulong[]> DownloadAsync(BlocklistSource source, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Nautilyou/1.0 (controle parental auto-heberge)");
        using var response = await http.GetAsync(source.Url!, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var network = await response.Content.ReadAsStreamAsync(ct);

        var hashes = new List<ulong>(1_000_000);

        if (source.Format == BlocklistFormat.UtCapitoleTarGz)
        {
            await using var gzip = new GZipStream(network, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                if (entry.Name != source.ArchiveEntry || entry.DataStream is null) continue;
                using var reader = new StreamReader(entry.DataStream);
                await ReadLinesAsync(reader, BlocklistFormat.DomainList, hashes, ct);
                break;
            }
        }
        else
        {
            using var reader = new StreamReader(network);
            await ReadLinesAsync(reader, source.Format, hashes, ct);
        }

        if (hashes.Count == 0) throw new InvalidDataException("liste vide ou format inattendu");

        var array = hashes.ToArray();
        Array.Sort(array);
        return Dedupe(array);
    }

    private static async Task ReadLinesAsync(StreamReader reader, BlocklistFormat format, List<ulong> hashes, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            var domain = ParseLine(line, format);
            if (domain is null) continue;
            hashes.Add(DomainHash.Of(domain));
            if (hashes.Count > MaxEntries) throw new InvalidDataException("liste anormalement grande");
        }
    }

    // Extrait le domaine d'une ligne ; null pour un commentaire, une ligne vide ou une entree inutilisable.
    public static string? ParseLine(string line, BlocklistFormat format)
    {
        var text = line.Trim();
        if (text.Length == 0 || text[0] == '#' || text[0] == '!') return null;

        if (format == BlocklistFormat.Hosts)
        {
            var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || (parts[0] != "0.0.0.0" && parts[0] != "127.0.0.1")) return null;
            text = parts[1];
        }

        if (text.StartsWith("*.")) text = text[2..];
        text = text.TrimEnd('.').ToLowerInvariant();

        if (text.Length < 4 || !text.Contains('.') || text.Contains('/') || text.Contains(' ')) return null;
        if (text is "localhost.localdomain" or "broadcasthost") return null;
        return text;
    }

    private static ulong[] Dedupe(ulong[] sorted)
    {
        if (sorted.Length == 0) return sorted;
        var write = 1;
        for (var read = 1; read < sorted.Length; read++)
        {
            if (sorted[read] != sorted[write - 1]) sorted[write++] = sorted[read];
        }
        return write == sorted.Length ? sorted : sorted[..write];
    }
}
