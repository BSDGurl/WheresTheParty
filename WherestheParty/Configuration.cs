using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace WTP;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public string VenueApiBaseUrl { get; set; } = "https://wtp-backend.bsdgurl.workers.dev";
    public bool Use24HourClock { get; set; } = false;
    public bool AutoRefreshEnabled { get; set; } = true;

    // Store a reference to the plugin interface (DI-friendly)
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface.SavePluginConfig(this);
    }
}
