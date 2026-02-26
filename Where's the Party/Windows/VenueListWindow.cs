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
    // Data center filters (key = DC name, value = enabled)
    private Dictionary<string, bool> dcFilters = new();
    // Tag filters (key = tag text, value = enabled)
    private Dictionary<string, bool> tagFilters = new();
    // Preserve an initial known DC list so the filters can fall back when server returns none
    private readonly List<string> initialDcList = new();
    // Show filters as a resizable window instead of a popup when true
    private bool showFiltersWindow = false;
    // no image texture support; use simple text button for refresh
    private bool autoRefresh = true;
    private DateTime lastAutoRefresh = DateTime.UtcNow;
    private readonly TimeSpan autoRefreshInterval = TimeSpan.FromSeconds(30);
    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public VenueListWindow(WTP plugin, string wtpImagePath, VenueService venueService)
        : base("Where's the Party##VenueListWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.wtpImagePath = wtpImagePath;
        this.plugin = plugin;
        this.venueService = venueService;

        // Initialize DC filters from a known set so Filters popup has entries before first refresh
        var knownDcs = new[] { "Aether", "Crystal", "Dynamis", "Primal", "Chaos", "Light", "Materia", "Elemental", "Gaia", "Mana", "Meteor" };
        this.initialDcList.AddRange(knownDcs);
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
                // Use a taller fixed card height so cards don't expand to the full window and hide others
                // This prevents a single card from taking the entire area while still avoiding clipping
                ImGui.BeginChild($"VenueCard_{v.Id}", new Vector2(0, 220), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
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
                    if (ImGui.Button("Wi-Fi", new Vector2(60, 0)))
                    {
                        ImGui.OpenPopup($"WifiPopup_{v.Id}");
                    }
                    ImGui.PopStyleColor();

                    // Wi‑Fi popup showing syncshell credentials with copy buttons
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

                        // If no Wi‑Fi options, show a friendly message
                        if (v.WifiOptions == WifiOption.None)
                        {
                            ImGui.TextUnformatted("No Wi-Fi / Sync options available for this listing.");
                        }

                        ImGui.EndPopup();
                    }

                    // Show schedule under the Carrd button (converted to viewer's local time)
                    try
                    {
                        if ((v.OpenDaysMask != 0) || (v.OpensAtUtc != default && v.ClosesAtUtc != default))
                        {
                            // Build day list from mask if available
                            string daysText = string.Empty;
                            if (v.OpenDaysMask != 0)
                            {
                                var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                                var parts = new List<string>();
                                for (var di = 0; di < 7; ++di)
                                {
                                    if ((v.OpenDaysMask & (1 << di)) != 0) parts.Add(dayNames[di]);
                                }
                                daysText = parts.Count > 0 ? string.Join(',', parts) + " " : string.Empty;
                            }

                            // Show time range in viewer local time if UTC times are present, otherwise use stored local minutes as fallback
                            string timeText = string.Empty;
                            if (v.OpensAtUtc != default && v.ClosesAtUtc != default)
                            {
                                var openLocal = v.OpensAtUtc.ToLocalTime();
                                var closeLocal = v.ClosesAtUtc.ToLocalTime();
                                timeText = $"{openLocal:h:mm tt} - {closeLocal:h:mm tt}";
                            }
                            else
                            {
                                // fallback: use stored local minutes (submitter-local) and display in 12-hour format (no timezone conversion)
                                var openMinutes = Math.Max(0, v.OpenTimeMinutesLocal % (24*60));
                                var closeMinutes = Math.Max(0, v.CloseTimeMinutesLocal % (24*60));
                                var openDt = DateTime.Today.AddMinutes(openMinutes);
                                var closeDt = DateTime.Today.AddMinutes(closeMinutes);
                                timeText = $"{openDt:h:mm tt} - {closeDt:h:mm tt}";
                            }

                            ImGui.Separator();
                            ImGui.TextUnformatted($"Schedule: {daysText}{timeText}");
                        }
                    }
                    catch { }

                    ImGui.Spacing();

                    // Two-column layout: description (left) and tags (right)
                    ImGui.Columns(2);
                    ImGui.SetColumnWidth(0, ImGui.GetWindowContentRegionMax().X * 0.7f);

                    // Description box (left)
                    ImGui.BeginChild($"desc_{v.Id}", new Vector2(0, 76), false);
                    ImGui.TextWrapped(string.IsNullOrWhiteSpace(v.Description) ? "(No description)" : v.Description);
                    ImGui.EndChild();

                    ImGui.NextColumn();
                    // Tags box (right)
                    ImGui.BeginChild($"tags_{v.Id}", new Vector2(0, 76), true);
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
                    // If the saved address already starts with the world name, strip it to avoid duplication
                    var displayAddress = rawAddress.Trim();
                    if (!string.IsNullOrEmpty(v.World) && !string.IsNullOrEmpty(displayAddress))
                    {
                        // remove a leading world name followed by optional punctuation/whitespace
                        try
                        {
                            var pattern = "^" + Regex.Escape(v.World) + "\\s*[,>\\-–—]*\\s*";
                            displayAddress = Regex.Replace(displayAddress, pattern, string.Empty, RegexOptions.IgnoreCase);
                        }
                        catch { /* ignore regex errors and leave address as-is */ }
                    }

                    // Normalize separators in the address to use ' > '
                    displayAddress = Regex.Replace(displayAddress, "\\s*,\\s*", " > ");
                    displayAddress = Regex.Replace(displayAddress, "\\s*>\\s*", " > ");

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
                                string aptTitle = string.Empty, subdivTitle = string.Empty;
                                switch (districtName)
                                {
                                    case "Lavender Beds": aptTitle = "Lily Hills"; subdivTitle = "Lily Hills Sub"; break;
                                    case "Mist": aptTitle = "Topmast"; subdivTitle = "Topmast Sub"; break;
                                    case "The Goblet": aptTitle = "Sultana's Breath"; subdivTitle = "Sultana's Breath Sub"; break;
                                    case "Empyreum": aptTitle = "Ingleside"; subdivTitle = "Ingleside Sub"; break;
                                    case "Shirogane": aptTitle = "Kobai Goten"; subdivTitle = "Kobai Goten Sub"; break;
                                }
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

                    var footer = string.Empty;
                    if (string.IsNullOrWhiteSpace(displayAddress)) footer = $"{v.DC} > {v.World}";
                    else footer = $"{v.DC} > {v.World} > {displayAddress}";
                    // Make the footer selectable so clicking it copies the full address to clipboard
                    if (ImGui.Selectable(footer))
                    {
                        ImGui.SetClipboardText(footer ?? string.Empty);
                        status = "Address copied to clipboard";
                    }
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
