using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using WTP.Models;
using WTP.Services;

namespace WTP.Windows;

public class VenueListWindow : Window, IDisposable 
{
    private readonly string wtpImagePath;
    private readonly WTP plugin;
    private readonly VenueService venueService;
    private List<Venue> venues = new();
    private string search = string.Empty;
    private string status = string.Empty;
    private bool loading = false;
    private bool fetchedOnce = false;
    // Data center filters (key = DC name, value = enabled)
    private Dictionary<string, bool> dcFilters = new();
    // no image texture support; use simple text button for refresh
    private bool autoRefresh = true;
    private DateTime lastAutoRefresh = DateTime.UtcNow;
    private readonly TimeSpan autoRefreshInterval = TimeSpan.FromSeconds(30);
    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public VenueListWindow(WTP plugin, string wtpImagePath)
        : base("Where's the Party##VenueListWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.wtpImagePath = wtpImagePath;
        this.plugin = plugin;
        this.venueService = new VenueService(plugin.Configuration.VenueApiBaseUrl);

        // Initialize DC filters from a known set so Filters popup has entries before first refresh
        var knownDcs = new[] { "Aether", "Crystal", "Dynamis", "Primal", "Chaos", "Light", "Materia", "Elemental", "Gaia", "Mana", "Meteor" };
        foreach (var dc in knownDcs) dcFilters[dc] = true;

        // No image loading; we always use the text "Refresh" button
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Auto-refresh toggle and status
        ImGui.Text($"Auto Refresh is {(autoRefresh ? "Enabled" : "Disabled")}");
        ImGui.SameLine();
        var arLocal = autoRefresh;
        if (ImGui.Checkbox("Auto Refresh##Enable", ref arLocal))
        {
            autoRefresh = arLocal;
            if (autoRefresh)
                lastAutoRefresh = DateTime.UtcNow;
        }

        // Reload button
        if (ImGui.Button("Refresh"))
        {
            _ = RefreshAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("Submit Venue"))
        {
            plugin.ToggleSubmitUi();
        }

        // Place Filters button on the right side (keep on same line)
        ImGui.SameLine();
        var contentMax = ImGui.GetWindowContentRegionMax().X;
        var filtersBtnWidth = 100f;
        ImGui.SetCursorPosX(Math.Max(0, contentMax - filtersBtnWidth));
        if (ImGui.Button("Filters", new System.Numerics.Vector2(filtersBtnWidth, 0f))) ImGui.OpenPopup("FiltersPopup");
        if (ImGui.BeginPopup("FiltersPopup"))
        {
            // Select / Deselect all
            if (ImGui.SmallButton("All"))
            {
                var keys = dcFilters.Keys.ToList();
                foreach (var k in keys) dcFilters[k] = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("None"))
            {
                var keys = dcFilters.Keys.ToList();
                foreach (var k in keys) dcFilters[k] = false;
            }

            ImGui.Separator();
            // show checkboxes for each known DC (maintain insertion order)
            foreach (var key in dcFilters.Keys.ToList())
            {
                var val = dcFilters[key];
                if (ImGui.Checkbox(key, ref val)) dcFilters[key] = val;
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();

        ImGui.InputText("Search", ref search, 256);
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.7f,0.7f,0.7f,1f), status);
        ImGui.Spacing();

        // lazy fetch once
        if (!fetchedOnce && !loading)
        {
            fetchedOnce = true;
            _ = RefreshAsync();
        }

        // Auto-refresh handling
        if (autoRefresh && !loading)
        {
            if ((DateTime.UtcNow - lastAutoRefresh) >= autoRefreshInterval)
            {
                lastAutoRefresh = DateTime.UtcNow;
                _ = RefreshAsync();
            }
        }

        // Normally a BeginChild() would have to be followed by an unconditional EndChild(),
        // ImRaii takes care of this after the scope ends.
        // This works for all ImGui functions that require specific handling, examples are BeginTable() or Indent().
        using (var child = ImRaii.Child("VenueListChild", Vector2.Zero, true))
        {
            if (!child.Success) return;

            if (loading)
            {
                ImGui.Text("Loading...");
                return;
            }

            var filtered = venues.Where(v => !v.IsExpired && (
                (string.IsNullOrWhiteSpace(search) ||
                v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.World.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))))
                && (dcFilters.Count == 0 || (v.DC != null && dcFilters.ContainsKey(v.DC) && dcFilters[v.DC]))
            ).ToList();

            if (filtered.Count == 0)
            {
                ImGui.Text("No venues found.");
                return;
            }

            if (ImGui.BeginTable("VenuesTable", 7, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Wi-FI", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableHeadersRow();

                foreach (var v in filtered)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(v.Name);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(v.World);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(v.Address);

                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(v.Category);

                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(string.Join(", ", v.Tags));

                    ImGui.TableSetColumnIndex(5);
                    var wifi = v.WifiOptions == WifiOption.None ? "None" : string.Join(", ", new[] { (v.WifiOptions & WifiOption.Lightless) == WifiOption.Lightless ? "Lightless" : null, (v.WifiOptions & WifiOption.PlayerSync) == WifiOption.PlayerSync ? "PlayerSync" : null, (v.WifiOptions & WifiOption.SnowCloak) == WifiOption.SnowCloak ? "SnowCloak" : null }.Where(x => x != null));
                    ImGui.TextUnformatted(wifi);

                    ImGui.TableSetColumnIndex(6);
                    ImGui.PushID(v.Id.ToString());
                    if (ImGui.SmallButton("Sync"))
                    {
                        // No action; tooltip will show credentials on hover
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        // Show per-WiFi Syncshell credentials when available
                        if ((v.WifiOptions & WifiOption.Lightless) == WifiOption.Lightless)
                        {
                            ImGui.TextUnformatted("Lightless:");
                            ImGui.TextUnformatted($"  ID: {v.LightlessSyncshellId}");
                            ImGui.TextUnformatted($"  PW: {v.LightlessSyncshellPassword}");
                            ImGui.Separator();
                        }
                        if ((v.WifiOptions & WifiOption.PlayerSync) == WifiOption.PlayerSync)
                        {
                            ImGui.TextUnformatted("PlayerSync:");
                            ImGui.TextUnformatted($"  ID: {v.PlayerSyncSyncshellId}");
                            ImGui.TextUnformatted($"  PW: {v.PlayerSyncSyncshellPassword}");
                            ImGui.Separator();
                        }
                        if ((v.WifiOptions & WifiOption.SnowCloak) == WifiOption.SnowCloak)
                        {
                            ImGui.TextUnformatted("SnowCloak:");
                            ImGui.TextUnformatted($"  ID: {v.SnowCloakSyncshellId}");
                            ImGui.TextUnformatted($"  PW: {v.SnowCloakSyncshellPassword}");
                            ImGui.Separator();
                        }

                        ImGui.TextUnformatted($"Opens: {v.OpensAtUtc.ToLocalTime():g}");
                        ImGui.TextUnformatted($"Closes: {v.ClosesAtUtc.ToLocalTime():g}");
                        ImGui.TextUnformatted($"Expires: {v.ExpiresAtUtc.ToLocalTime():g}");
                        ImGui.EndTooltip();
                    }
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            loading = true;
            status = "Refreshing...";
            var list = await venueService.FetchVenuesAsync();
            venues = list.OrderBy(v => v.OpensAtUtc).ToList();
            // Update DC filters based on loaded venues; preserve previous selections when possible
            var dcs = venues.Select(v => v.DC ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            if (dcs.Count > 0)
            {
                var newFilters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var dc in dcs)
                {
                    if (dcFilters.ContainsKey(dc)) newFilters[dc] = dcFilters[dc];
                    else newFilters[dc] = true; // default enabled
                }
                dcFilters = newFilters;
            }

            status = $"Loaded {venues.Count} venues";
        }
        catch (Exception ex)
        {
            status = "Error: " + ex.Message;
        }
        finally
        {
            loading = false;
        }
    }
}
