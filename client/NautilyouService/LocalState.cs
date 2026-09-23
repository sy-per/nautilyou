using System.Text.Json;

using NautilyouShared;

namespace NautilyouService;

// Persistance locale du compteur de temps du jour et de la dernière config reçue, pour que le
// client applique la dernière config connue en continu même hors ligne / après un redémarrage
// du service, conformément au comportement "fail-safe hors ligne" décidé pour Nautilyou :
// pas de blocage total ni de retour à un mode libre tant que la config n'a pas changé côté serveur.
public class LocalState
{
    public string TrackedDate { get; set; } = "";

    // Stocké en secondes (pas en minutes arrondies) : avec un cycle de 15s, accumuler des minutes
    // arrondies à chaque tick fait que Math.Round(15s) = 0min à chaque fois et le compteur ne
    // bouge jamais. Les minutes ne sont calculées qu'au moment de les exposer (affichage, rapport
    // au serveur, comparaison au quota) — voir Worker.UsedMinutesToday / AppMinutesToday.
    public int UsedSecondsToday { get; set; }
    public Dictionary<string, int> AppSecondsToday { get; set; } = new();
    // Minutes de presence par site aujourd'hui (voir DnsFilterServer.CountVisit), persistees pour
    // survivre a un redemarrage du Service.
    public Dictionary<string, int> SiteMinutesToday { get; set; } = new();

    public DeviceConfig? CachedConfig { get; set; }
    public int CachedBonusMinutesToday { get; set; }

    private static string StatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nautilyou", "local-state.json");

    public static LocalState Load()
    {
        if (!File.Exists(StatePath)) return new LocalState();
        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<LocalState>(json) ?? new LocalState();
        }
        catch
        {
            return new LocalState();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StatePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // best-effort : une écriture ratée ne doit pas interrompre le cycle d'enforcement
        }
    }
}
