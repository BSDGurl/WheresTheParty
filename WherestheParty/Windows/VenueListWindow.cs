using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
    private Dictionary<string, bool> dcFilters = new();
    private Dictionary<string, bool> tagFilters = new();
    private readonly List<string> initialDcList = new();
    private bool showFiltersWindow = false;
    private bool autoRefresh = true;
    private DateTime lastAutoRefresh = DateTime.UtcNow;
    private readonly TimeSpan autoRefreshInterval = TimeSpan.FromSeconds(30);

    public VenueListWindow(WTP plugin, string wtpImagePath, VenueService venueService)
        : base("Where's the Party##VenueListWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(375, 330), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        this.wtpImagePath = wtpImagePath; this.plugin = plugin; this.venueService = venueService;
        var knownDcs = new[] { "Aether", "Crystal", "Dynamis", "Primal", "Chaos", "Light", "Materia", "Elemental", "Gaia", "Mana", "Meteor" };
        this.initialDcList.AddRange(knownDcs); foreach (var dc in knownDcs) dcFilters[dc] = true;
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

        // Use Unicode icons so no FontAwesome setup is required
        var icon = loading ? "" : ""; // hourglass when loading, clockwise arrow otherwise
        if (UiHelpers.IconButton("Refresh", icon))
        {
            if (!loading) _ = RefreshAsync();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Refresh");

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
        if (ImGui.Button("Filters", new System.Numerics.Vector2(filtersBtnWidth, 0f))) showFiltersWindow = true;
        // Show a regular resizable Filters window (better than popup for large lists)
        if (showFiltersWindow)
        {
            // Fixed small non-resizable filters window
            ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiCond.Appearing);
            if (ImGui.Begin("Filters", ref showFiltersWindow, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
            {
                // Layout DC filters and Tag filters side-by-side using child windows so each column can scroll
                ImGui.Columns(2);
                // Fixed child widths for a compact non-resizable window
                var leftWidth = 240f;
                var rightWidth = 240f;

                // Left: DC controls inside a child so it can scroll independently
                ImGui.PushID("DCColumn");
                ImGui.BeginChild("DCChild", new Vector2(leftWidth, 0), true);
                ImGui.TextUnformatted("Data Centers");
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
                if (dcFilters.Count == 0)
                {
                    ImGui.TextUnformatted("(No data centers available)");
                }
                else
                {
                    foreach (var key in dcFilters.Keys.ToList())
                    {
                        var val = dcFilters[key];
                        if (ImGui.Checkbox(key, ref val)) dcFilters[key] = val;
                    }
                }
                ImGui.EndChild();
                ImGui.PopID();

                ImGui.NextColumn();

                // Right: Tag controls in its own child
                ImGui.PushID("TagColumn");
                ImGui.BeginChild("TagChild", new Vector2(rightWidth, 0), true);
                ImGui.TextUnformatted("Tags");
                if (ImGui.SmallButton("All Tags"))
                {
                    var keys = tagFilters.Keys.ToList();
                    foreach (var k in keys) tagFilters[k] = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("No Tags"))
                {
                    var keys = tagFilters.Keys.ToList();
                    foreach (var k in keys) tagFilters[k] = false;
                }
                ImGui.Separator();
                foreach (var tkey in tagFilters.Keys.ToList())
                {
                    var tval = tagFilters[tkey];
                    if (ImGui.Checkbox(tkey, ref tval)) tagFilters[tkey] = tval;
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

        // Use an explicit child for the venue list but disable its scrollbar so only
        // per-field children (e.g. description) can scroll.
        // Allow the venue list to scroll when it exceeds the window height.
        ImGui.BeginChild("VenueListChild", Vector2.Zero, true, ImGuiWindowFlags.None);
        {
            if (loading)
            {
                ImGui.Text("Loading...");
                ImGui.EndChild();
                return;
            }

            // Tag filter behavior:
            // - If there are no tag filters configured, don't filter by tags.
            // - If at least one tag is checked, only show venues that have any of the checked tags.
            // - If tag filters exist but none are checked, only show venues with no tags.
            var hasTagFilters = tagFilters.Count > 0;
            var anyTagSelected = hasTagFilters && tagFilters.Values.Any(x => x);

            var filtered = venues.Where(v => !v.IsExpired && (
                (string.IsNullOrWhiteSpace(search) ||
                v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.World.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Address.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (v.CarrdUrl != null && v.CarrdUrl.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                v.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))))
                && (dcFilters.Count == 0 || (v.DC != null && dcFilters.ContainsKey(v.DC) && dcFilters[v.DC]))
                && (
                    !hasTagFilters
                    || (anyTagSelected ? (v.Tags != null && v.Tags.Any(t => tagFilters.TryGetValue(t, out var sel) && sel))
                                       : (v.Tags == null || v.Tags.Count == 0))
                  )
            ).ToList();

            if (filtered.Count == 0)
            {
                ImGui.Text("No venues found.");
                return;
            }

            // Render each venue as a card-style entry instead of a table row
            foreach (var v in filtered)
            {
                // Use a fixed card height (slightly smaller) and add bottom padding so content doesn't clip
                ImGui.BeginChild($"VenueCard_{v.Id}", new Vector2(0, 170), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                try
                {

                    // Title row: name (colored) and small Wi-Fi badge on the right
                    // Calculate text size to position the Wi-Fi button correctly
                    var nameSize = ImGui.CalcTextSize(v.Name);

                    ImGui.TextColored(new System.Numerics.Vector4(0.98f, 0.8f, 0.16f, 1f), v.Name);

                    // Use explicit CarrdUrl field (if provided) and show Open Carrd button
                    var carrdUrl = v.CarrdUrl ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(carrdUrl))
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Open Carrd##{v.Id}"))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(carrdUrl) { UseShellExecute = true });
                                WTP.Log?.Information($"Opened Carrd URL: {carrdUrl}");
                            }
                            catch (Exception ex)
                            {
                                WTP.Log?.Warning(ex, $"Failed to open Carrd URL: {carrdUrl}");
                            }
                        }
                        
                    }

                    // Wi-Fi badge aligned to the right
                    var innerContentMax = ImGui.GetWindowContentRegionMax().X;
                    // reserve space for name + some padding
                    var reserved = nameSize.X + 20f;
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(Math.Max(reserved, innerContentMax - 80));
                    ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.2f, 0.2f, 0.2f, 0.6f));
                    if (ImGui.Button(" Wi-Fi", new Vector2(60, 0)))
                    {
                        ImGui.OpenPopup($"WifiPopup_{v.Id}");
                    }
                    ImGui.PopStyleColor();

                    // Wi-Fi popup showing syncshell credentials with copy buttons
                    if (ImGui.BeginPopup($"WifiPopup_{v.Id}"))
                    {
                        ImGui.TextUnformatted("Syncshell credentials");
                        ImGui.Separator();

                        if ((v.WifiOptions & WifiOption.Lightless) == WifiOption.Lightless)
                        {
                            ImGui.TextUnformatted("Lightless:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.LightlessSyncshellId);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##LLID_{v.Id}")) ImGui.SetClipboardText(v.LightlessSyncshellId ?? string.Empty);
                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.LightlessSyncshellPassword);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##LLPW_{v.Id}")) ImGui.SetClipboardText(v.LightlessSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        if ((v.WifiOptions & WifiOption.PlayerSync) == WifiOption.PlayerSync)
                        {
                            ImGui.TextUnformatted("PlayerSync:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.PlayerSyncSyncshellId);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##PSID_{v.Id}")) ImGui.SetClipboardText(v.PlayerSyncSyncshellId ?? string.Empty);
                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.PlayerSyncSyncshellPassword);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##PSPW_{v.Id}")) ImGui.SetClipboardText(v.PlayerSyncSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        if ((v.WifiOptions & WifiOption.SnowCloak) == WifiOption.SnowCloak)
                        {
                            ImGui.TextUnformatted("SnowCloak:");
                            ImGui.Indent();
                            ImGui.TextUnformatted("ID:"); ImGui.SameLine(); ImGui.TextUnformatted(v.SnowCloakSyncshellId);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##SCID_{v.Id}")) ImGui.SetClipboardText(v.SnowCloakSyncshellId ?? string.Empty);
                            ImGui.TextUnformatted("Password:"); ImGui.SameLine(); ImGui.TextUnformatted(v.SnowCloakSyncshellPassword);
                            ImGui.SameLine(); if (ImGui.SmallButton($"Copy##SCPW_{v.Id}")) ImGui.SetClipboardText(v.SnowCloakSyncshellPassword ?? string.Empty);
                            ImGui.Unindent();
                            ImGui.Separator();
                        }

                        // If no Wi-Fi options, show a friendly message
                        if (v.WifiOptions == WifiOption.None)
                        {
                            ImGui.TextUnformatted("No Wi-Fi / Sync options available for this listing.");
                        }

                        ImGui.EndPopup();
                    }

                    // Render schedule under the Carrd button
                    ScheduleRenderer.RenderSchedule(v);

                    ImGui.Spacing();

                    // Two-column layout: description (left) and tags (right)
                    ImGui.Columns(2);
                    ImGui.SetColumnWidth(0, ImGui.GetWindowContentRegionMax().X * 0.7f);

                    // Description box (left)
                    ImGui.BeginChild($"desc_{v.Id}", new Vector2(0, 60), false);
                    ImGui.TextWrapped(string.IsNullOrWhiteSpace(v.Description) ? "(No description)" : v.Description);
                    ImGui.EndChild();

                    ImGui.NextColumn();
                    // Tags box (right)
                    ImGui.BeginChild($"tags_{v.Id}", new Vector2(0, 60), true);
                    ImGui.TextUnformatted("Tags");
                    ImGui.Separator();
                    foreach (var t in v.Tags)
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.2f, 0.6f, 0.86f, 1f), t);
                        ImGui.SameLine();
                    }
                    ImGui.EndChild();

                    ImGui.Columns(1);

                    ImGui.Spacing();
                    // Data center / world / address
                    ImGui.AlignTextToFramePadding();
                    var rawAddress = v.Address ?? string.Empty;
                    var displayAddress = rawAddress.Trim();

                    // Normalize separators first so we can reliably inspect parts
                    displayAddress = Regex.Replace(displayAddress, "\\s*,\\s*", " > ");
                    displayAddress = Regex.Replace(displayAddress, "\\s*>\\s*", " > ");

                    // If the saved address already includes the DC and/or World at the start,
                    // strip those leading parts to avoid duplication when we prepend them below.
                    if (!string.IsNullOrEmpty(displayAddress))
                    {
                        try
                        {
                            var parts = displayAddress.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
                            if (!string.IsNullOrEmpty(v.DC) && parts.Count > 0 && string.Equals(parts[0], v.DC, StringComparison.OrdinalIgnoreCase))
                                parts.RemoveAt(0);
                            if (!string.IsNullOrEmpty(v.World) && parts.Count > 0 && string.Equals(parts[0], v.World, StringComparison.OrdinalIgnoreCase))
                                parts.RemoveAt(0);
                            displayAddress = string.Join(" > ", parts.Where(p => !string.IsNullOrEmpty(p)));
                        }
                        catch { /* leave displayAddress as-is on error */ }
                    }

                    // If the stored address uses a generic "Apt 5" or "Subdiv 5" and includes a district
                    // (e.g. "Shirogane > Apt 5"), expand it to the district-specific title so listings show
                    // "Kobai Goten #5" instead of generic "Apt 5".
                    try
                    {
                        var parts = displayAddress.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        if (parts.Length >= 2)
                        {
                            var last = parts[^1];
                            var mApt = Regex.Match(last, "Apt(?:s)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
                            var mSub = Regex.Match(last, "Subdiv(?:ision)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
                            if (mApt.Success || mSub.Success)
                            {
                                var districtName = parts.Length >= 2 ? parts[^2] : string.Empty;
                                // map district to apt/sub titles
                                var (aptTitle, subdivTitle) = UiHelpers.GetTitlesForDistrictName(districtName);
                                if (!string.IsNullOrEmpty(aptTitle) || !string.IsNullOrEmpty(subdivTitle))
                                {
                                    if (mSub.Success && !string.IsNullOrEmpty(subdivTitle) && int.TryParse(mSub.Groups[1].Value, out var sn))
                                    {
                                        parts[^1] = $"{subdivTitle} #{sn}";
                                    }
                                    else if (mApt.Success && !string.IsNullOrEmpty(aptTitle) && int.TryParse(mApt.Groups[1].Value, out var an))
                                    {
                                        parts[^1] = $"{aptTitle} #{an}";
                                    }
                                    displayAddress = string.Join(" > ", parts.Where(p => !string.IsNullOrEmpty(p)));
                                }
                            }
                        }
                    }
                    catch { }

                    // Ensure we display a sane world name — some stored listings may contain
                    // Lumina row references instead of the plain world name. Prefer v.World
                    // when it looks valid; otherwise try to extract a candidate from the
                    // raw address and avoid duplicating it in the footer.
                    var worldName = (v.World ?? string.Empty).Trim();
                    var footerDistrict = string.Empty;

                    // Split raw address into parts and drop any Lumina rowrefs
                    var rawParts = rawAddress.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    var cleanedParts = rawParts.Where(p => p.IndexOf("Lumina.Excel", StringComparison.OrdinalIgnoreCase) < 0).ToList();

                    // If v.World is a Lumina rowref or empty, try to pick a candidate from cleanedParts
                    if (string.IsNullOrWhiteSpace(worldName) || worldName.IndexOf("Lumina.Excel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int offset = 0;
                        if (!string.IsNullOrEmpty(v.DC) && cleanedParts.Count > 0 && string.Equals(cleanedParts[0], v.DC, StringComparison.OrdinalIgnoreCase)) offset = 1;
                        if (cleanedParts.Count > offset)
                        {
                            worldName = cleanedParts[offset];
                        }
                    }

                    // Try to find a district candidate from cleanedParts after world
                    try
                    {
                        var knownDistricts = new[] { "Lavender Beds", "Lav Beds", "Mist", "The Goblet", "Empyreum", "Shirogane" };
                        int startIdx = 0;
                        if (!string.IsNullOrEmpty(v.DC) && cleanedParts.Count > 0 && string.Equals(cleanedParts[0], v.DC, StringComparison.OrdinalIgnoreCase)) startIdx = 1;
                        if (!string.IsNullOrEmpty(worldName) && cleanedParts.Count > startIdx && string.Equals(cleanedParts[startIdx], worldName, StringComparison.OrdinalIgnoreCase)) startIdx++;
                        for (var i = startIdx; i < cleanedParts.Count; ++i)
                        {
                            var part = cleanedParts[i];
                            if (knownDistricts.Any(d => string.Equals(d, part, StringComparison.OrdinalIgnoreCase) || string.Equals(UiHelpers.GetDistrictDisplay(d), part, StringComparison.OrdinalIgnoreCase)))
                            {
                                footerDistrict = knownDistricts.First(d => string.Equals(d, part, StringComparison.OrdinalIgnoreCase) || string.Equals(UiHelpers.GetDistrictDisplay(d), part, StringComparison.OrdinalIgnoreCase));
                                break;
                            }
                        }
                    }
                    catch { }

                    // Fallback defaults when detection fails
                    if (string.IsNullOrWhiteSpace(worldName)) worldName = "Behemoth";
                    if (string.IsNullOrWhiteSpace(footerDistrict)) footerDistrict = "Lavender Beds";

                    // Remove leading DC/world/district from displayAddress to avoid duplication
                    var toStrip = new[] { v.DC ?? string.Empty, worldName, footerDistrict }.Where(s => !string.IsNullOrEmpty(s)).ToList();
                    foreach (var s in toStrip)
                    {
                        if (displayAddress.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                        {
                            displayAddress = displayAddress.Substring(s.Length).TrimStart();
                            if (displayAddress.StartsWith(">")) displayAddress = displayAddress.Substring(1).TrimStart();
                        }
                    }

                    var footer = string.Empty;
                    if (string.IsNullOrWhiteSpace(displayAddress)) footer = $"{v.DC} > {worldName}";
                    else footer = $"{v.DC} > {worldName} > {displayAddress}";
                    // Make the footer selectable so clicking it copies the full address to clipboard
                    if (ImGui.Selectable(footer))
                    {
                        ImGui.SetClipboardText(footer ?? string.Empty);
                        status = "Address copied to clipboard";
                    }

                    // Small bottom padding to ensure the last lines don't get clipped by the card border
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
            // Update DC filters based on loaded venues; preserve previous selections when possible
            var dcs = venues.Select(v => v.DC ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            // If server returned no DCs, fall back to the initial known list
            var effectiveDcs = dcs.Count > 0 ? dcs : this.initialDcList;
            var newFilters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in effectiveDcs)
            {
                if (dcFilters.ContainsKey(dc)) newFilters[dc] = dcFilters[dc];
                else newFilters[dc] = true; // default enabled
            }
            dcFilters = newFilters;

                // Update tag filters based on loaded venues; preserve previous selections when possible
                var tags = venues.SelectMany(v => v.Tags ?? new List<string>()).Select(t => t?.Trim() ?? string.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
                if (tags.Count > 0)
                {
                    var newTagFilters = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (var tag in tags)
                    {
                        if (tagFilters.ContainsKey(tag)) newTagFilters[tag] = tagFilters[tag];
                        else newTagFilters[tag] = true; // default selected
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
