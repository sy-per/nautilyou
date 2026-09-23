using System.Text.Json;

namespace NautilyouService;

// État d'appairage persisté localement : une fois appairé, l'appareil réutilise ces infos
// à chaque démarrage tant qu'il n'est pas supprimé côté dashboard.
public class PairingState
{
    public string ServerUrl { get; set; } = "";
    public string DeviceId { get; set; } = "";
    // Empreinte SHA-256 du certificat auto-signe du serveur, epinglee au pairing (voir TlsPinning).
    public string? CertSha256 { get; set; }
    // La clé privée RSA ne vit pas ici : voir DeviceKeyStore.cs (chiffrée au repos via DPAPI).

    private static string StatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nautilyou", "pairing.json");

    public static PairingState? Load()
    {
        if (!File.Exists(StatePath)) return null;
        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<PairingState>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(StatePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(this));
    }
}
