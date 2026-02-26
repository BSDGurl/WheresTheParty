using Dalamud.Configuration;
using System;

namespace WTP;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    // Base URL for the venue API (e.g. Cloudflare Worker endpoint). Set to your deployed Cloudflare Worker URL.
    // Defaulted to the developer's worker for convenience; change as needed.
    public string VenueApiBaseUrl { get; set; } = "https://wtp-backend.bsdgurl.workers.dev";


    // The below exists just to make saving less cumbersome okay
    public void Save()
    {
        WTP.PluginInterface.SavePluginConfig(this);
    }
}
