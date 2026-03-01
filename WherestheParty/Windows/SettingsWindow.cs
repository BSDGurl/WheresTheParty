using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace WTP.Windows;

public class SettingsWindow : Window
{
    private readonly WTP plugin;
    private readonly Configuration configuration;

    public SettingsWindow(WTP plugin, Configuration configuration) : base("Where's the Party - Settings###WTPSettings")
    {
        this.plugin = plugin;
        this.configuration = configuration;

        Size = new Vector2(420, 220);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Separator();

        var use24 = configuration.Use24HourClock;
        if (ImGui.Checkbox("Use 24-hour clock", ref use24))
        {
            configuration.Use24HourClock = use24;
            configuration.Save();
        }

        ImGui.NewLine();

        var ar = configuration.AutoRefreshEnabled;
        if (ImGui.Checkbox("Auto Refresh (venue list)", ref ar))
        {
            configuration.AutoRefreshEnabled = ar;
            configuration.Save();
        }

        ImGui.NewLine();

        ImGui.Text("Venue API Base URL");
        ImGui.TextWrapped(configuration.VenueApiBaseUrl ?? string.Empty);

        ImGui.NewLine();
        if (ImGui.Button("Save"))
            configuration.Save();
    }
}
