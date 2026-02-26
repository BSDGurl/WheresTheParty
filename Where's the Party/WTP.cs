using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Threading.Tasks;
using WTP.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using WTP.Windows;

namespace WTP;

public sealed class WTP : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/wtp";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("WTP");
    private VenueListWindow VenueListWindow { get; init; }
    private SubmitVenueWindow SubmitVenueWindow { get; init; }

    public WTP()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var assemblyDir = PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
        var dataIconPath = Path.Combine(assemblyDir, "data", "icon.png");
        var rootIconPath = Path.Combine(assemblyDir, "icon.png");
        var wtpImagePath = File.Exists(dataIconPath) ? dataIconPath : (File.Exists(rootIconPath) ? rootIconPath : dataIconPath);

        var venueService = new VenueService(Configuration.VenueApiBaseUrl);
        VenueListWindow = new VenueListWindow(this, wtpImagePath, venueService);
        SubmitVenueWindow = new SubmitVenueWindow(this, venueService);
        WindowSystem.AddWindow(VenueListWindow);
        WindowSystem.AddWindow(SubmitVenueWindow);
        // No custom font loading; use Unicode/private-use glyph fallbacks for icons.
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Where's the Party UI"
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // Add installer buttons
        // Only register the Open (main) action. Do not register OpenConfigUi so the installer
        // "Settings" button will have no plugin handler (effectively disabled).
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"===Some cool log messages from {PluginInterface.Manifest.Name}===");
    }

        // No custom font loading; use Unicode/private-use glyph fallbacks for icons.

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        // Only unregister OpenMainUi; OpenConfigUi was not registered.
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        VenueListWindow.Dispose();
        SubmitVenueWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // Toggle the venue list UI
        VenueListWindow.Toggle();
    }

    public void ToggleMainUi() => VenueListWindow.Toggle();
    public void ToggleSubmitUi() => _ = SubmitVenueWindow.OpenForCurrentUserAsync();


    // Allow other components to request a refresh of the venue list.
    public async Task RequestVenueRefreshAsync()
    {
        try
        {
            await VenueListWindow.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while requesting venue refresh");
        }
    }
}
