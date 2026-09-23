namespace NautilyouCompanion;

// Snapshot thread-safe de l'état courant, mis à jour à chaque message "status" reçu du service
// via PipeClient et lu par l'interface systray (event du pipe sur un thread, UI sur le thread STA
// dédié qui a la boucle de messages).
public class ClientStatus
{
    private readonly object _lock = new();
    private int _remainingMinutes;
    private bool _quotaEnabled = true;
    private Dictionary<string, int> _appMinutes = new();
    private bool _connected;

    public void Update(int remainingMinutes, bool quotaEnabled, Dictionary<string, int> appMinutes, bool connected)
    {
        lock (_lock)
        {
            _remainingMinutes = remainingMinutes;
            _quotaEnabled = quotaEnabled;
            _appMinutes = new Dictionary<string, int>(appMinutes);
            _connected = connected;
        }
    }

    public (int RemainingMinutes, bool QuotaEnabled, Dictionary<string, int> AppMinutes, bool Connected) Snapshot()
    {
        lock (_lock)
        {
            return (_remainingMinutes, _quotaEnabled, new Dictionary<string, int>(_appMinutes), _connected);
        }
    }
}
