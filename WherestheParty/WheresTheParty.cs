using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using WTP.Services;
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
        Configuration.Initialize(PluginInterface);

        var assemblyDir = PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
        var dataIconPath = Path.Combine(assemblyDir, "Images", "icon.png");
        var rootIconPath = Path.Combine(assemblyDir, "icon.png");
        var wtpImagePath = File.Exists(dataIconPath) ? dataIconPath : (File.Exists(rootIconPath) ? rootIconPath : dataIconPath);

        var venueService = new VenueService(Configuration.VenueApiBaseUrl);
        var identityService = new PlayerIdentityService(ClientState, PlayerState, Log);

        VenueListWindow = new VenueListWindow(this, wtpImagePath, venueService, identityService);
        SubmitVenueWindow = new SubmitVenueWindow(this, venueService, identityService, Log);

        WindowSystem.AddWindow(VenueListWindow);
        WindowSystem.AddWindow(SubmitVenueWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Where's the Party UI"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("WTP plugin loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        VenueListWindow.Dispose();
        SubmitVenueWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        VenueListWindow.Toggle();
    }

    public void ToggleMainUi() => VenueListWindow.Toggle();
    public void ToggleSubmitUi() => _ = SubmitVenueWindow.OpenForCurrentUserAsync();

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
