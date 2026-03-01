using System;
using System.Collections.Generic;

namespace WTP.Models;

[Serializable]
public partial class Venue
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string DC { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string CarrdUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }

    public string Owner { get; set; } = string.Empty;
    // CharacterId is persisted by the worker to allow reliable owner matching
    // (the owner string may include the world). Add this so the client can
    // identify a user's existing listing.
    public string CharacterId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public DateTime LastUpdatedUtc { get; set; }
    public int LengthMinutes { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public int OpenDaysMask { get; set; } = 0;
    public int OpenTimeMinutesLocal { get; set; } = 20 * 60;
    public int CloseTimeMinutesLocal { get; set; } = 23 * 60;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
}

[Flags]
public enum WifiOption
{
    None = 0,
    Lightless = 1,
    PlayerSync = 2,
    SnowCloak = 4
}

public partial class Venue
{
    public WifiOption WifiOptions { get; set; } = WifiOption.None;
    public string LightlessSyncshellId { get; set; } = string.Empty;
    public string LightlessSyncshellPassword { get; set; } = string.Empty;
    public string PlayerSyncSyncshellId { get; set; } = string.Empty;
    public string PlayerSyncSyncshellPassword { get; set; } = string.Empty;
    public string SnowCloakSyncshellId { get; set; } = string.Empty;
    public string SnowCloakSyncshellPassword { get; set; } = string.Empty;
}
