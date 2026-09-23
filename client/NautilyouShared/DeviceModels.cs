namespace NautilyouShared;

public class TimeConfig
{
    public bool QuotaEnabled { get; set; } = true;
    public int DailyQuotaMinutes { get; set; }
    public bool WindowEnabled { get; set; } = true;
    public string WindowStart { get; set; } = "00:00";
    public string WindowEnd { get; set; } = "23:59";
}

public class WebConfig
{
    public bool SupervisionOn { get; set; }
    public bool WhitelistMode { get; set; }
    public List<string> Blacklist { get; set; } = new();
    public List<string> Whitelist { get; set; } = new();
}

public class AppLimit
{
    public string Id { get; set; } = "";
    public List<string> Apps { get; set; } = new();
    public int MinutesPerDay { get; set; }
}

public class AppsConfig
{
    public bool SupervisionOn { get; set; }
    public List<AppLimit> Limits { get; set; } = new();
    public List<string> BlockedApps { get; set; } = new();
}

public class DeviceConfig
{
    public TimeConfig Time { get; set; } = new();
    public WebConfig Web { get; set; } = new();
    public AppsConfig Apps { get; set; } = new();
}

public class DeviceStatus
{
    public int RemainingMinutes { get; set; }
    public bool WithinWindow { get; set; }
    public bool QuotaEnabled { get; set; } = true;
    public bool WindowEnabled { get; set; } = true;
    public bool Ok { get; set; }
}

public class DeviceActivity
{
    public int UsedMinutesToday { get; set; }
    public List<object> TopCategories { get; set; } = new();
    public List<object> TopApps { get; set; } = new();
    public int BlockedAppsCount { get; set; }
}

public class DeviceDetail
{
    public string Id { get; set; } = "";
    public string ChildId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Model { get; set; }
    public bool Online { get; set; }
    public DeviceConfig Config { get; set; } = new();
    public int BonusMinutesToday { get; set; }
    public DeviceStatus Status { get; set; } = new();
    public DeviceActivity Activity { get; set; } = new();
}

public class DeviceDetailEnvelope
{
    public DeviceDetail Device { get; set; } = new();
}

public class PairingCompleteRequest
{
    public string Code { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Model { get; set; } = "";
    public string PublicKey { get; set; } = "";
}

public class ActivityReport
{
    public int UsedMinutes { get; set; }
}
