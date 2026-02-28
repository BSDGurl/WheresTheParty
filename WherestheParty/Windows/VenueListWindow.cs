using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
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

    private Dictionary<string, bool> dcFilters = new();
    private Dictionary<string, bool> tagFilters = new();
    private readonly List<string> initialDcList = new();

    private bool showFiltersWindow = false;
    private bool autoRefresh = true;
    private DateTime lastAutoRefresh = DateTime.UtcNow;
    private readonly TimeSpan autoRefreshInterval = TimeSpan.FromSeconds(30);

    public VenueListWindow(WTP plugin, string wtpImagePath, VenueService venueService, PlayerIdentityService identityService)
            : base("Where's the Party##VenueListWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        this.wtpImagePath = wtpImagePath;
        this.venueService = venueService;

        var knownDcs = new[]
        {
            "Aether", "Crystal", "Dynamis", "Primal", "Chaos", "Light",
            "Materia", "Elemental", "Gaia", "Mana", "Meteor"
        };

        this.initialDcList.AddRange(knownDcs);
        foreach (var dc in knownDcs)
            dcFilters[dc] = true;
    }

    public void Dispose()
    {
    }

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

        // Refresh button (Unicode icons, no custom font)
        var icon = loading ? "" : ""; // hourglass vs clockwise arrow
        if (UiHelpers.IconButton("Refresh", icon))
        {
            if (!loading)
                _ = RefreshAsync();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Refresh");

        ImGui.SameLine();
        if (ImGui.Button("Submit Venue"))
        {
            plugin.ToggleSubmitUi();
        }

        // Filters button aligned to the right
        ImGui.SameLine();
        var contentMax = ImGui.GetWindowContentRegionMax().X;
        var filtersBtnWidth = 100f;
        ImGui.SetCursorPosX(Math.Max(0, contentMax - filtersBtnWidth));
        if (ImGui.Button("Filters", new Vector2(filtersBtnWidth, 0f)))
            showFiltersWindow = true;

        // Filters window
        if (showFiltersWindow)
        {
            ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiCond.Appearing);
            if (ImGui.Begin("Filters", ref showFiltersWindow, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.Columns(2);

                var leftWidth = 240f;
                var rightWidth = 240f;

                // Left: DC filters
                ImGui.PushID("DCColumn");
                ImGui.BeginChild("DCChild", new Vector2(leftWidth, 0), true);
                ImGui.TextUnformatted("Data Centers");
                if (ImGui.SmallButton("All"))
                {
                    var keys = dcFilters.Keys.ToList();
                    foreach (var k in keys)
                        dcFilters[k] = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("None"))
                {
                    var keys = dcFilters.Keys.ToList();
                    foreach (var k in keys)
                        dcFilters[k] = false;
                }
                ImGui.Separator();
                if (dcFilters.Count == 0)
                {
                    ImGui.TextUnformatted("(No data centers available)");
                }
                else
                {
                    foreach (var key in dcFilters.Keys.ToList())
                    {
                        var val = dcFilters[key];
                        if (ImGui.Checkbox(key, ref val))
                            dcFilters[key] = val;
                    }
                }
                ImGui.EndChild();
                ImGui.PopID();

                ImGui.NextColumn();

                // Right: Tag filters
                ImGui.PushID("TagColumn");
                ImGui.BeginChild("TagChild", new Vector2(rightWidth, 0), true);
                ImGui.TextUnformatted("Tags");
                if (ImGui.SmallButton("All Tags"))
                {
                    var keys = tagFilters.Keys.ToList();
                    foreach (var k in keys)
                        tagFilters[k] = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("No Tags"))
                {
                    var keys = tagFilters.Keys.ToList();
                    foreach (var k in keys)
                        tagFilters[k] = false;
                }
                ImGui.Separator();
                foreach (var tkey in tagFilters.Keys.ToList())
                {
                    var tval = tagFilters[tkey];
                    if (ImGui.Checkbox(tkey, ref tval))
                        tagFilters[tkey] = tval;
                }
                ImGui.EndChild();
                ImGui.PopID();

                ImGui.Columns(1);
            }
            ImGui.End();
        }

        ImGui.Spacing();

        ImGui.InputText("Search", ref search, 256);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), status);
        ImGui.Spacing();

        // Lazy initial fetch
        if (!fetchedOnce && !loading)
        {
            fetchedOnce = true;
            _ = RefreshAsync();
        }

        // Auto-refresh
        if (autoRefresh && !loading)
        {
            if ((DateTime.UtcNow - lastAutoRefresh) >= autoRefreshInterval)
            {
                lastAutoRefresh = DateTime.UtcNow;
                _ = RefreshAsync();
            }
        }

        // Venue list
        ImGui.BeginChild("VenueListChild", Vector2.Zero, true, ImGuiWindowFlags.None);
        {
            if (loading)
            {
                ImGui.Text("Loading...");
                ImGui.EndChild();
                return;
            }

            // Tag filter behavior:
            // - If there are no tag filters, don't filter by tags.
            // - If at least one tag is checked, show venues that have any of the checked tags.
            // - If tag filters exist but none are checked, show venues with no tags.
            var hasTagFilters = tagFilters.Count > 0;
            var anyTagSelected = hasTagFilters && tagFilters.Values.Any(x => x);

            var filtered = venues.Where(v =>
                !v.IsExpired &&
                (
                    string.IsNullOrWhiteSpace(search) ||
                    v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    v.World.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    v.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(v.CarrdUrl) && v.CarrdUrl.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    v.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))
                ) &&
                (dcFilters.Count == 0 ||
                    (!string.IsNullOrEmpty(v.DC) && dcFilters.TryGetValue(v.DC, out var dcSel) && dcSel)) &&
                (
                    !hasTagFilters ||
                    (anyTagSelected
                        ? (v.Tags != null && v.Tags.Any(t => tagFilters.TryGetValue(t, out var sel) && sel))
                        : (v.Tags == null || v.Tags.Count == 0))
                )
            ).ToList();

            if (filtered.Count == 0)
            {
                ImGui.Text("No venues found.");
                ImGui.EndChild();
                return;
            }

            foreach (var v in filtered)
            {
                ImGui.BeginChild($"VenueCard_{v.Id}", new Vector2(0, 170), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                try
                {
                    // Title row: name + Carrd + Wi-Fi badge
                    var nameSize = ImGui.CalcTextSize(v.Name);
                    ImGui.TextColored(new Vector4(0.98f, 0.8f, 0.16f, 1f), v.Name);

                    var carrdUrl = v.CarrdUrl ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(carrdUrl))
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Open Carrd##{v.Id}"))
                        {
                            ImGui.OpenPopup($"ConfirmOpenCarrd_{v.Id}");
                        }

                        // Confirmation modal per-venue
                        if (ImGui.BeginPopupModal($"ConfirmOpenCarrd_{v.Id}", ImGuiWindowFlags.AlwaysAutoResize))
                        {
                            var display = string.IsNullOrWhiteSpace(carrdUrl) ? "(empty)" : carrdUrl.Trim();
                            ImGui.TextWrapped($"You are about to open link:\n{display}");
                            ImGui.NewLine();
                            ImGui.TextWrapped("Do you want to continue?");
                            ImGui.Separator();
                            ImGui.TextColored(new Vector4(1f, 0.25f, 0.25f, 1f), "Never enter your FFXIV, Discord, Steam or E-mail account data on this website.");
                            ImGui.NewLine();

                            if (ImGui.Button("Open link"))
                            {
                                var url = carrdUrl.Trim();
                                try
                                {
                                    if (!Regex.IsMatch(url, "^[a-zA-Z][a-zA-Z0-9+.-]*:"))
                                        url = "https://" + url;
                                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                                    WTP.Log?.Information($"Opened Carrd URL: {url}");
                                }
                                catch (Exception ex)
                                {
                                    WTP.Log?.Warning(ex, $"Failed to open Carrd URL: {url}");
                                }
                                ImGui.CloseCurrentPopup();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("Cancel"))
                                ImGui.CloseCurrentPopup();

                            ImGui.EndPopup();
                        }
                    }

                    // Wi-Fi badge aligned to the right
                    var innerContentMax = ImGui.GetWindowContentRegionMax().X;
                    var reserved = nameSize.X + 20f;
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(Math.Max(reserved, innerContentMax - 80));
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.6f));
                    if (ImGui.Button(" Wi-Fi", new Vector2(60, 0)))
                    {
                        ImGui.OpenPopup($"WifiPopup_{v.Id}");
                    }
                    ImGui.PopStyleColor();

                    // Wi-Fi popup
                    if (ImGui.BeginPopup($"WifiPopup_{v.Id}"))
                    {
                        ImGui.TextUnformatted("Syncshell credentials");
                        ImGui.Separator();

                        if ((v.WifiOptions & WifiOption.Lightless) == WifiOption.Lightless)
                        {
                            ImGui.TextUnformatted("Lightless:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.LightlessSyncshellId);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##LLID_{v.Id}"))
                                ImGui.SetClipboardText(v.LightlessSyncshellId ?? string.Empty);

                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.LightlessSyncshellPassword);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##LLPW_{v.Id}"))
                                ImGui.SetClipboardText(v.LightlessSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        if ((v.WifiOptions & WifiOption.PlayerSync) == WifiOption.PlayerSync)
                        {
                            ImGui.TextUnformatted("PlayerSync:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.PlayerSyncSyncshellId);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##PSID_{v.Id}"))
                                ImGui.SetClipboardText(v.PlayerSyncSyncshellId ?? string.Empty);

                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.PlayerSyncSyncshellPassword);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##PSPW_{v.Id}"))
                                ImGui.SetClipboardText(v.PlayerSyncSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        if ((v.WifiOptions & WifiOption.SnowCloak) == WifiOption.SnowCloak)
                        {
                            ImGui.TextUnformatted("SnowCloak:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.SnowCloakSyncshellId);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##SCID_{v.Id}"))
                                ImGui.SetClipboardText(v.SnowCloakSyncshellId ?? string.Empty);

                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.SnowCloakSyncshellPassword);
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Copy##SCPW_{v.Id}"))
                                ImGui.SetClipboardText(v.SnowCloakSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        if (v.WifiOptions == WifiOption.None)
                        {
                            ImGui.TextUnformatted("No Wi-Fi / Sync options available for this listing.");
                        }

                        ImGui.EndPopup();
                    }

                    // Schedule (submitter local → viewer local handled by ScheduleRenderer)
                    ScheduleRenderer.RenderSchedule(v);

                    ImGui.Spacing();

                    // Description + tags columns
                    ImGui.Columns(2);
                    ImGui.SetColumnWidth(0, ImGui.GetWindowContentRegionMax().X * 0.7f);

                    ImGui.BeginChild($"desc_{v.Id}", new Vector2(0, 60), false);
                    ImGui.TextWrapped(string.IsNullOrWhiteSpace(v.Description) ? "(No description)" : v.Description);
                    ImGui.EndChild();

                    ImGui.NextColumn();

                    ImGui.BeginChild($"tags_{v.Id}", new Vector2(0, 60), true);
                    ImGui.TextUnformatted("Tags");
                    ImGui.Separator();
                    foreach (var t in v.Tags)
                    {
                        ImGui.TextColored(new Vector4(0.2f, 0.6f, 0.86f, 1f), t);
                        ImGui.SameLine();
                    }
                    ImGui.EndChild();

                    ImGui.Columns(1);

                    ImGui.Spacing();

                    // Address footer

                    if (ImGui.Selectable(v.Address))
                    {
                        ImGui.SetClipboardText(v.Address ?? string.Empty);
                        status = "Address copied to clipboard";
                    }
                    ImGui.Dummy(new Vector2(0, 8));
                }
                finally
                {
                    ImGui.EndChild();
                }

                ImGui.Separator();
                ImGui.Spacing();
            }

            ImGui.EndChild();
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            loading = true;
            status = "Refreshing...";

            var list = await venueService.FetchVenuesAsync();
            venues = list.OrderBy(v => v.OpensAtUtc).ToList();

            // DC filters
            var dcs = venues
                .Select(v => v.DC ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            var effectiveDcs = dcs.Count > 0 ? dcs : this.initialDcList;
            var newFilters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in effectiveDcs)
            {
                if (dcFilters.ContainsKey(dc))
                    newFilters[dc] = dcFilters[dc];
                else
                    newFilters[dc] = true;
            }
            dcFilters = newFilters;

            // Tag filters
            var tags = venues
                .SelectMany(v => v.Tags ?? new List<string>())
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();

            if (tags.Count > 0)
            {
                var newTagFilters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var tag in tags)
                {
                    if (tagFilters.ContainsKey(tag))
                        newTagFilters[tag] = tagFilters[tag];
                    else
                        newTagFilters[tag] = true;
                }
                tagFilters = newTagFilters;
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
