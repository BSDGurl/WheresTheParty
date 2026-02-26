using Dalamud.Configuration;
using System;

namespace WTP;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public string VenueApiBaseUrl { get; set; } = "https://wtp-backend.bsdgurl.workers.dev";

    public void Save()
    {
        WTP.PluginInterface.SavePluginConfig(this);
    }
}
