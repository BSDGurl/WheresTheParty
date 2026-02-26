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
    // Category was replaced by an explicit Carrd URL field for listings that point to a Carrd page.
    // Keep free-form tags for other classification.
    public string CarrdUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    // UTC timestamps for when the venue opens and closes
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }

    public string Owner { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Last updated and expiration
    public DateTime LastUpdatedUtc { get; set; }

    // Length in minutes the listing should remain active. Backend should respect this and delete when expired.
    public int LengthMinutes { get; set; }

    // Computed expiration time in UTC. Clients should set this when creating a listing.
    public DateTime ExpiresAtUtc { get; set; }

    // Optional weekly schedule information. Stored as local-times relative to the submitter's system.
    // OpenDaysMask: bitmask where bit 0 = Sunday, bit 1 = Monday, ... bit 6 = Saturday.
    // OpenTimeMinutesLocal / CloseTimeMinutesLocal: minutes after midnight in the submitter's local time.
    public int OpenDaysMask { get; set; } = 0;
    public int OpenTimeMinutesLocal { get; set; } = 20 * 60; // default 20:00
    public int CloseTimeMinutesLocal { get; set; } = 23 * 60; // default 23:00

    // Helper
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
}

// Wi-Fi options supported by listings. Flags allow combinations (e.g. Lightless + PlayerSync).
[Flags]
public enum WifiOption
{
    None = 0,
    Lightless = 1,
    PlayerSync = 2,
    SnowCloak = 4
}

// Extension of Venue to include connectivity details
public partial class Venue
{
    // Which Wi-Fi/sync options are available for this listing
    public WifiOption WifiOptions { get; set; } = WifiOption.None;

    // Optional Syncshell details (may be empty). These are short strings and may contain sensitive info.
    // Per-WiFi Syncshell credentials
    public string LightlessSyncshellId { get; set; } = string.Empty;
    public string LightlessSyncshellPassword { get; set; } = string.Empty;

    public string PlayerSyncSyncshellId { get; set; } = string.Empty;
    public string PlayerSyncSyncshellPassword { get; set; } = string.Empty;

    public string SnowCloakSyncshellId { get; set; } = string.Empty;
    public string SnowCloakSyncshellPassword { get; set; } = string.Empty;
}
