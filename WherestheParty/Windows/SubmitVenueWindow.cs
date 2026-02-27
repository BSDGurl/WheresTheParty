using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Linq;
using System.Text.RegularExpressions;
using WTP.Models;
using WTP.Services;

namespace WTP.Windows;

public class SubmitVenueWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly WTP plugin;
    private readonly VenueService venueService;

    private string name = string.Empty;
    private string description = string.Empty;
    private string world = string.Empty;
    private string carrdUrl = string.Empty;
    private string address = string.Empty;
    private string tags = string.Empty;
    private int lengthMinutes = 60;
    private string statusMessage = string.Empty;
    private bool submitting = false;
    private string confirmWorld = string.Empty;

    private bool wifiLightless = false;
    private string lightlessSyncshellId = string.Empty;
    private string lightlessSyncshellPassword = string.Empty;
    private bool wifiPlayerSync = false;
    private string playerSyncSyncshellId = string.Empty;
    private string playerSyncSyncshellPassword = string.Empty;
    private bool wifiSnowCloak = false;
    private string snowCloakSyncshellId = string.Empty;
    private string snowCloakSyncshellPassword = string.Empty;

    // Data center / world
    private List<string> dataCenters = new();
    private int selectedDcIndex = 0;
    private Dictionary<string, List<string>> worldsByDc = new();
    private int selectedWorldIndex = 0;

    // Housing
    private readonly List<string> housingDistricts = new() { "Lavender Beds", "Mist", "The Goblet", "Empyreum", "Shirogane" };
    private int selectedDistrictIndex = 0;
    private int selectedWard = 18;
    private int selectedPlot = 35;

    // Address editing
    private bool editingAddress = false;
    private bool usePlot = true;
    private bool useApartment = false;
    private int selectedApartment = -1;
    private int selectedSubdivision = -1;

    // UI flags
    private bool showWifiOptions = false;

    // Weekly schedule
    private bool[] openDays = new bool[7];
    private int openHour = 20;
    private int openMinute = 0;
    private int closeHour = 23;
    private int closeMinute = 0;

    public SubmitVenueWindow(WTP plugin, VenueService venueService) : base("Submit Venue###SubmitVenueWindow")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(520, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        this.configuration = plugin.Configuration;
        this.venueService = venueService;

        // Hardcode DC -> worlds mapping so World is always a dropdown
        worldsByDc = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Aether"] = new List<string>{ "Adamantoise","Cactuar","Faerie","Gilgamesh","Jenova","Midgardsormr","Sargatanas","Siren" },
            ["Crystal"] = new List<string>{ "Balmung","Brynhildr","Coeurl","Diabolos","Goblin","Malboro","Mateus","Zalera" },
            ["Dynamis"] = new List<string>{ "Cuchulainn","Golem","Halicarnassus","Kraken","Maduin","Marilith","Rafflesia","Seraph" },
            ["Primal"] = new List<string>{ "Behemoth","Excalibur","Exodus","Famfrit","Hyperion","Lamia","Leviathan","Ultros" },
            ["Chaos"] = new List<string>{ "Cerberus","Louisoix","Moogle","Omega","Phantom","Ragnarok","Sagittarius","Spriggan" },
            ["Light"] = new List<string>{ "Alpha","Lich","Odin","Phoenix","Raiden","Shiva","Twintania","Zodiark" },
            ["Materia"] = new List<string>{ "Bismarck","Ravana","Sephirot","Sophia","Zurvan" },
            ["Elemental"] = new List<string>{ "Aegis","Atomos","Carbuncle","Garuda","Gungnir","Kujata","Tonberry","Typhon" },
            ["Gaia"] = new List<string>{ "Alexander","Bahamut","Durandal","Fenrir","Ifrit","Ridill","Tiamat","Ultima" },
            ["Mana"] = new List<string>{ "Anima","Asura","Chocobo","Hades","Ixion","Masamune","Pandaemonium","Titan" },
            ["Meteor"] = new List<string>{ "Belias","Mandragora","Ramuh","Shinryu","Unicorn","Valefor","Yojimbo","Zeromus" }
        };
        dataCenters = worldsByDc.Keys.OrderBy(k => k).ToList();

        // defaults
        var pidx = dataCenters.FindIndex(x => string.Equals(x, "Primal", StringComparison.OrdinalIgnoreCase));
        if (pidx >= 0) selectedDcIndex = pidx;
        selectedDistrictIndex = housingDistricts.FindIndex(x => x == "Lavender Beds");
        selectedWard = 18;
        selectedPlot = 35;
        world = "Behemoth"; // default world name until populated
    }

    // Try to obtain the player's current world (HomeWorld/World) in a compatibility-safe way.
    private static string? GetLocalPlayerWorldSafe()
    {
        // Helper: try to extract a human-readable world name from a reflected object
        static string? ResolveWorldObject(object? o)
        {
            if (o == null) return null;
            try
            {
                // If it's already a string and not a Lumina rowref, return it
                if (o is string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return null;
                    if (s.IndexOf("Lumina.Excel.RowRef", StringComparison.OrdinalIgnoreCase) >= 0) return null;
                    return s;
                }

                // If object has a Value property (Lumina.RowRef style), try to get its Name
                var t = o.GetType();
                var valueProp = t.GetProperty("Value") ?? t.GetProperty("Row") ?? t.GetProperty("_value");
                var candidate = valueProp?.GetValue(o);
                if (candidate != null)
                {
                    var nameProp = candidate.GetType().GetProperty("Name") ?? candidate.GetType().GetProperty("EnglishName") ?? candidate.GetType().GetProperty("Text");
                    var nameVal = nameProp?.GetValue(candidate)?.ToString();
                    if (!string.IsNullOrEmpty(nameVal)) return nameVal;
                    var candToStr = candidate.ToString();
                    if (!string.IsNullOrEmpty(candToStr) && candToStr.IndexOf("Lumina.Excel.RowRef", StringComparison.OrdinalIgnoreCase) < 0) return candToStr;
                }

                // Try direct Name property on the object
                var directName = t.GetProperty("Name") ?? t.GetProperty("WorldName") ?? t.GetProperty("HomeWorld");
                var dn = directName?.GetValue(o)?.ToString();
                if (!string.IsNullOrEmpty(dn) && dn.IndexOf("Lumina.Excel.RowRef", StringComparison.OrdinalIgnoreCase) < 0) return dn;

                // Fallback to ToString if it's not a Lumina rowref string
                var s2 = o.ToString();
                if (!string.IsNullOrEmpty(s2) && s2.IndexOf("Lumina.Excel.RowRef", StringComparison.OrdinalIgnoreCase) < 0) return s2;
            }
            catch { }
            return null;
        }

        try
        {
            var ps = WTP.PlayerState;
            if (ps != null)
            {
                try
                {
                    var hwProp = ps.GetType().GetProperty("HomeWorld") ?? ps.GetType().GetProperty("HomeWorldId") ?? ps.GetType().GetProperty("World");
                    var hwObj = hwProp?.GetValue(ps);
                    var fromPs = ResolveWorldObject(hwObj);
                    if (!string.IsNullOrEmpty(fromPs)) return fromPs;
                }
                catch { }
            }

            // Try ObjectTable.LocalPlayer via PluginInterface
            try
            {
                var pi = WTP.PluginInterface;
                if (pi != null)
                {
                    var otProp = pi.GetType().GetProperty("ObjectTable");
                    var ot = otProp?.GetValue(pi);
                    var lpProp = ot?.GetType().GetProperty("LocalPlayer");
                    var lp = lpProp?.GetValue(ot);
                    if (lp != null)
                    {
                        var worldProp = lp.GetType().GetProperty("HomeWorld") ?? lp.GetType().GetProperty("HomeWorldId") ?? lp.GetType().GetProperty("World");
                        var wObj = worldProp?.GetValue(lp);
                        var fromLp = ResolveWorldObject(wObj);
                        if (!string.IsNullOrEmpty(fromLp)) return fromLp;
                    }
                }
            }
            catch { }
        }
        catch { }

        return null;
    }

    // Compare an existing Owner string against the current character identity.
    // Returns true if the stored owner matches the character id or display name.
    private static bool IsOwnerMatch(string existingOwner, string? currentCharId, string? currentName)
    {
        if (string.IsNullOrEmpty(existingOwner)) return false;
        // Exact match to character id
        if (!string.IsNullOrEmpty(currentCharId) && string.Equals(existingOwner, currentCharId, StringComparison.OrdinalIgnoreCase)) return true;
        // Match to display name
        if (!string.IsNullOrEmpty(currentName) && string.Equals(existingOwner, currentName, StringComparison.OrdinalIgnoreCase)) return true;
        // Some older entries may have been stored as name@world or other variants; try contains checks
        if (!string.IsNullOrEmpty(currentName) && existingOwner.IndexOf(currentName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string FormatHourLabel(int h)
    {
        var hour12 = h % 12;
        if (hour12 == 0) hour12 = 12;
        var ampm = h < 12 ? "AM" : "PM";
        return $"{hour12} {ampm}";
    }

    private static string FormatTimeLabel(int h, int m)
    {
        var hour12 = h % 12;
        if (hour12 == 0) hour12 = 12;
        var ampm = h < 12 ? "AM" : "PM";
        return $"{hour12}:{m:D2} {ampm}";
    }

    public void Dispose() { }

    // Populate the form fields from an existing venue
    public void PopulateFromVenue(Venue v)
    {
        if (v == null) return;

        name = v.Name ?? string.Empty;
        description = v.Description ?? string.Empty;
        carrdUrl = v.CarrdUrl ?? string.Empty;
        tags = v.Tags != null ? string.Join(',', v.Tags) : string.Empty;
        // Sanitize stored address to remove any Lumina rowref artifacts the server may have stored
        address = SanitizeStoredAddress(v.Address ?? string.Empty);
        lengthMinutes = v.LengthMinutes > 0 ? v.LengthMinutes : lengthMinutes;

        // Data center / world selection
        if (!string.IsNullOrEmpty(v.DC))
        {
            var idx = dataCenters.FindIndex(x => string.Equals(x, v.DC, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) selectedDcIndex = idx;
        }
        // set world selection if possible
        var dcKey = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty;
        // Sanitize possible Lumina rowref tokens in stored world string before matching
        var rawWorld = v.World ?? string.Empty;
        var sanitizedWorld = Regex.Replace(rawWorld, @"Lumina\.Excel\.RowRef`?\d*\[[^\]]*\]", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrEmpty(sanitizedWorld) && worldsByDc != null && worldsByDc.ContainsKey(dcKey))
        {
            var worlds = worldsByDc[dcKey];
            var widx = worlds.FindIndex(x => string.Equals(x, sanitizedWorld, StringComparison.OrdinalIgnoreCase));
            if (widx >= 0) { selectedWorldIndex = widx; world = worlds.ElementAtOrDefault(selectedWorldIndex) ?? sanitizedWorld; }
            else world = sanitizedWorld ?? world;
        }
        else
        {
            world = sanitizedWorld ?? world;
        }

        // Try to parse apartment / subdivision / plot selection from the stored address so
        // the editor reflects the existing selection. Handles addresses composed by
        // ComposeDisplayAddress() or a few legacy formats like "Apt 5"/"Subdiv 5".
        selectedApartment = -1;
        selectedSubdivision = -1;
        useApartment = false;
        usePlot = true;
        var addr = (v.Address ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(addr))
        {
            // Split on the display separator ' > ' if present
            var parts = addr.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            // If the composed display format was used, parts are: DC, World, District, "Title #N" or "W{n}" etc.
            if (parts.Count >= 3)
            {
                // Try to find district index from part[2]
                var districtName = parts[2];
                var didx = housingDistricts.FindIndex(d => string.Equals(d, districtName, StringComparison.OrdinalIgnoreCase) || string.Equals(GetDistrictDisplay(d), districtName, StringComparison.OrdinalIgnoreCase));
                if (didx >= 0) selectedDistrictIndex = didx;

                // If there's a 4th part, it may contain apt/sub title or ward/plot
                if (parts.Count >= 4)
                {
                    var fourth = parts[3];
                    // Determine apt/sub titles for selected district
                    string aptTitle = string.Empty, subdivTitle = string.Empty;
                    switch (selectedDistrictIndex)
                    {
                        case 0: aptTitle = "Lily Hills"; subdivTitle = "Lily Hills Sub"; break;
                        case 1: aptTitle = "Topmast"; subdivTitle = "Topmast Sub"; break;
                        case 2: aptTitle = "Sultana's Breath"; subdivTitle = "Sultana's Breath Sub"; break;
                        case 3: aptTitle = "Ingleside"; subdivTitle = "Ingleside Sub"; break;
                        case 4: aptTitle = "Kobai Goten"; subdivTitle = "Kobai Goten Sub"; break;
                    }

                    // Match patterns like "Kobai Goten #5" or "Lily Hills #12"
                    var m = Regex.Match(fourth, @"#\s*(\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var num))
                    {
                        // Decide if the title indicates subdiv or apt
                        if (!string.IsNullOrEmpty(subdivTitle) && fourth.StartsWith(subdivTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedSubdivision = num;
                            useApartment = true;
                            usePlot = false;
                        }
                        else if (!string.IsNullOrEmpty(aptTitle) && fourth.StartsWith(aptTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedApartment = num;
                            useApartment = true;
                            usePlot = false;
                        }
                        else
                        {
                            // Fallback: if the fragment contains the word 'Subdiv' or 'Sub' treat as subdivision
                            if (fourth.IndexOf("subd", StringComparison.OrdinalIgnoreCase) >= 0 || fourth.IndexOf("sub", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                selectedSubdivision = num;
                                useApartment = true;
                                usePlot = false;
                            }
                            else
                            {
                                // treat as apartment number but leave title unknown
                                selectedApartment = num;
                                useApartment = true;
                                usePlot = false;
                            }
                        }
                    }
                    else
                    {
                        // Try to parse ward/plot like "W18" or "P35" or "Ward 18 > Plot 35"
                        var wardMatch = Regex.Match(fourth, "W\\s*(\\d+)", RegexOptions.IgnoreCase);
                        if (wardMatch.Success && int.TryParse(wardMatch.Groups[1].Value, out var wv)) selectedWard = wv;
                        // If more parts, try to read plot
                        if (parts.Count >= 5)
                        {
                            var fifth = parts[4];
                            var plotMatch = Regex.Match(fifth, "P\\s*(\\d+)", RegexOptions.IgnoreCase);
                            if (plotMatch.Success && int.TryParse(plotMatch.Groups[1].Value, out var pv)) selectedPlot = pv;
                        }
                    }
                }
            }
            else
            {
                // legacy formats: look for explicit "Apt" or "Subdiv" tokens
                var mApt = Regex.Match(addr, "Apt(?:s)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (mApt.Success && int.TryParse(mApt.Groups[1].Value, out var an)) { selectedApartment = an; useApartment = true; usePlot = false; }
                var mSub = Regex.Match(addr, "Subdiv(?:ision)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (mSub.Success && int.TryParse(mSub.Groups[1].Value, out var sn)) { selectedSubdivision = sn; useApartment = true; usePlot = false; }
            }
        }

        // Wi-Fi options
        wifiLightless = (v.WifiOptions & WifiOption.Lightless) == WifiOption.Lightless;
        wifiPlayerSync = (v.WifiOptions & WifiOption.PlayerSync) == WifiOption.PlayerSync;
        wifiSnowCloak = (v.WifiOptions & WifiOption.SnowCloak) == WifiOption.SnowCloak;
        lightlessSyncshellId = v.LightlessSyncshellId ?? string.Empty;
        lightlessSyncshellPassword = v.LightlessSyncshellPassword ?? string.Empty;
        playerSyncSyncshellId = v.PlayerSyncSyncshellId ?? string.Empty;
        playerSyncSyncshellPassword = v.PlayerSyncSyncshellPassword ?? string.Empty;
        snowCloakSyncshellId = v.SnowCloakSyncshellId ?? string.Empty;
        snowCloakSyncshellPassword = v.SnowCloakSyncshellPassword ?? string.Empty;

        // Do NOT prefill confirmWorld. Leave empty so the user must manually
        // confirm the world (anti-bot/anti-spam protection).
        confirmWorld = string.Empty;

        // Restore schedule fields if present
        if (v.OpenDaysMask != 0)
        {
            for (var i = 0; i < 7; ++i) openDays[i] = (v.OpenDaysMask & (1 << i)) != 0;
            var ot = Math.Max(0, Math.Min(24 * 60 - 1, v.OpenTimeMinutesLocal));
            openHour = ot / 60; openMinute = ot % 60;
            var ct = Math.Max(0, Math.Min(24 * 60 - 1, v.CloseTimeMinutesLocal));
            closeHour = ct / 60; closeMinute = ct % 60;
            usePlot = false; useApartment = true;
        }
    }

    // Clear the form to defaults
    public void ClearForm()
    {
        name = string.Empty;
        description = string.Empty;
        carrdUrl = string.Empty;
        tags = string.Empty;
        address = string.Empty;
        lengthMinutes = 60;
        wifiLightless = wifiPlayerSync = wifiSnowCloak = false;
        lightlessSyncshellId = lightlessSyncshellPassword = string.Empty;
        playerSyncSyncshellId = playerSyncSyncshellPassword = string.Empty;
        snowCloakSyncshellId = snowCloakSyncshellPassword = string.Empty;
        selectedApartment = -1;
        selectedSubdivision = -1;
        for (var i = 0; i < openDays.Length; ++i) openDays[i] = false;
        openHour = 20; openMinute = 0; closeHour = 23; closeMinute = 0;
        // Do NOT prefill confirmWorld for new listings. Require manual confirmation
        // to avoid making automated submissions easier.
        confirmWorld = string.Empty;
    }

    // Fetch current user's listing (if any), populate the form, and open the submit window.
    public async Task OpenForCurrentUserAsync()
    {
        try
        {
            var currentCharId = GetLocalCharacterId();
            var currentName = GetLocalPlayerNameSafe();
            var list = await venueService.FetchVenuesAsync();
            var usersListing = list.FirstOrDefault(x => !string.IsNullOrEmpty(x.Owner) && IsOwnerMatch(x.Owner, currentCharId, currentName));
            if (usersListing != null)
            {
                PopulateFromVenue(usersListing);
            }
            else
            {
                ClearForm();
            }
        }
        catch
        {
            ClearForm();
        }

        if (!IsOpen) Toggle();
    }

    private void PopulateWorldsFromLumina()
    {
        var worldType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => string.Equals(t.Name, "World", StringComparison.OrdinalIgnoreCase));

        if (worldType == null) return;

        var dm = WTP.DataManager;
        if (dm == null) return;

        var dmType = dm.GetType();
        var method = dmType.GetMethod("GetExcelSheet", Type.EmptyTypes);
        if (method == null) return;

        var generic = method.MakeGenericMethod(worldType);
        var sheetObj = generic.Invoke(dm, null);
        if (!(sheetObj is System.Collections.IEnumerable sheet)) return;

        var map = new Dictionary<string, List<string>>();
        var nameProp = worldType.GetProperty("Name");
        var dcProp = worldType.GetProperty("DataCenter");

        foreach (var row in sheet)
        {
            try
            {
                var nameVal = nameProp?.GetValue(row)?.ToString() ?? string.Empty;
                var dcName = "Unknown";
                if (dcProp != null)
                {
                    var dcRef = dcProp.GetValue(row);
                    var valueProp = dcRef?.GetType().GetProperty("Value");
                    var dcRow = valueProp?.GetValue(dcRef);
                    var dcNameVal = dcRow?.GetType().GetProperty("Name")?.GetValue(dcRow)?.ToString();
                    dcName = dcNameVal ?? "Unknown";
                }

                if (!map.ContainsKey(dcName)) map[dcName] = new List<string>();
                if (!map[dcName].Contains(nameVal)) map[dcName].Add(nameVal);
            }
            catch { }
        }

        if (map.Count > 0)
        {
            worldsByDc = map;
            dataCenters = map.Keys.OrderBy(k => k).ToList();
            selectedDcIndex = Math.Min(selectedDcIndex, Math.Max(0, dataCenters.Count - 1));
        }
    }

    public override void Draw()
    {
        // No Lumina population at runtime; using hardcoded world lists
        if (selectedDcIndex >= dataCenters.Count) selectedDcIndex = 0;

        if (!ImGui.BeginTable("SubmitVenueTable", 2, ImGuiTableFlags.SizingFixedFit)) return;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 140f);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);

        // Name
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Venue Name");
        ImGui.TableSetColumnIndex(1);
        // Determine a consistent input width for single-line controls so they match the multiline box
        var defaultAvail = ImGui.GetContentRegionAvail();
        var defaultPad = ImGui.GetStyle().FramePadding.X * 2;
        var fieldWidth = Math.Max(32f, defaultAvail.X - defaultPad);
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputText("##VenueName", ref name, 50);

        // Description (allow longer text)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Description");
        ImGui.TableSetColumnIndex(1);
        // Size the multiline control to match the table column width so it aligns with other controls.
        // Some ImGui bindings don't expose the table column width API; use the available content region instead.
        var style = ImGui.GetStyle();
        var avail = ImGui.GetContentRegionAvail();
        // account for frame padding
        var inputWidth = Math.Max(32f, avail.X - (style.FramePadding.X * 2));
        ImGui.PushItemWidth(inputWidth);
        ImGui.InputTextMultiline("##VenueDescription", ref description, 1500, new Vector2(0, 80));
        ImGui.PopItemWidth();

        // Address (composed)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Address");
        ImGui.TableSetColumnIndex(1);
        var composed = string.IsNullOrWhiteSpace(address) ? ComposeDisplayAddress() : address;
        ImGui.TextUnformatted(composed);
        ImGui.SameLine();
        if (ImGui.SmallButton(editingAddress ? "Done##Addr" : "Edit##Addr"))
        {
            // If we are finishing address editing, save the composed address into the address field
            if (editingAddress)
            {
                address = ComposeDisplayAddress();
            }
            editingAddress = !editingAddress;
        }

        if (editingAddress)
        {
            // Data Center
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Data Center");
            ImGui.TableSetColumnIndex(1);
            if (ImGui.BeginCombo("##EditDC", dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty))
            {
                for (var i = 0; i < dataCenters.Count; ++i)
                {
                    var sel = i == selectedDcIndex;
                    if (ImGui.Selectable(dataCenters[i], sel)) { selectedDcIndex = i; selectedWorldIndex = 0; }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // World
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("World");
            ImGui.TableSetColumnIndex(1);
            var dcKey = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty;
            var worlds = worldsByDc.ContainsKey(dcKey) ? worldsByDc[dcKey] : new List<string>();
            if (worlds.Count > 0)
            {
                if (selectedWorldIndex >= worlds.Count) selectedWorldIndex = 0;
                if (ImGui.BeginCombo("##EditWorld", worlds.ElementAtOrDefault(selectedWorldIndex) ?? string.Empty))
                {
                    for (var i = 0; i < worlds.Count; ++i)
                    {
                        var sel = i == selectedWorldIndex;
                        if (ImGui.Selectable(worlds[i], sel)) { selectedWorldIndex = i; world = worlds[i]; }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.InputText("##ManualWorld", ref world, 32);
            }

            // Housing district
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Housing District");
            ImGui.TableSetColumnIndex(1);
            if (ImGui.BeginCombo("##EditDistrict", housingDistricts[selectedDistrictIndex]))
            {
                for (var i = 0; i < housingDistricts.Count; ++i)
                {
                    var sel = i == selectedDistrictIndex;
                    if (ImGui.Selectable(housingDistricts[i], sel)) selectedDistrictIndex = i;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Ward
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Ward");
            ImGui.TableSetColumnIndex(1);
            if (ImGui.BeginCombo("##EditWard", selectedWard.ToString()))
            {
                for (var w = 1; w <= 30; ++w)
                {
                    var sel = w == selectedWard;
                    if (ImGui.Selectable(w.ToString(), sel)) selectedWard = w;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Plot / Apartment toggle row
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty);
            ImGui.TableSetColumnIndex(1);
            ImGui.Checkbox("Plot", ref usePlot); ImGui.SameLine(); ImGui.Checkbox("Apartment", ref useApartment);
            if (usePlot && useApartment) useApartment = false;

            if (usePlot)
            {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Plot"); ImGui.TableSetColumnIndex(1);
                if (ImGui.BeginCombo("##EditPlot", selectedPlot.ToString()))
                {
                    for (var p = 1; p <= 60; ++p)
                    {
                        var sel = p == selectedPlot;
                        if (ImGui.Selectable(p.ToString(), sel)) selectedPlot = p;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else if (useApartment)
            {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Apartments"); ImGui.TableSetColumnIndex(1);
                ImGui.BeginChild("ApartmentSelect", new Vector2(0, 160), true);
                ImGui.Columns(2);
                // column titles depend on district
                string aptTitle = "Apt #";
                string subdivTitle = "Subdiv #";
                switch (selectedDistrictIndex)
                {
                    case 0: // Lavender Beds
                        aptTitle = "Lily Hills"; subdivTitle = "Lily Hills Subs"; break;
                    case 1: // Mist
                        aptTitle = "Topmast"; subdivTitle = "Topmast Subs"; break;
                    case 2: // The Goblet
                        aptTitle = "Sultana's Breath"; subdivTitle = "Sultana's Breath Subs"; break;
                    case 3: // Empyreum
                        aptTitle = "Ingleside"; subdivTitle = "Ingleside Subs"; break;
                    case 4: // Shirogane
                        aptTitle = "Kobai Goten"; subdivTitle = "Kobai Goten Subs"; break;
                }
                ImGui.TextUnformatted(aptTitle); ImGui.NextColumn(); ImGui.TextUnformatted(subdivTitle); ImGui.NextColumn();
                for (var i = 0; i < 90; ++i)
                {
                    ImGui.PushID(i);
                    // Radio buttons for single-select apartment/subdivision. Values are 1-based.
                    // Use hidden-id suffixes so the two columns don't collide (same visible label)
                    var aptLabel = $"{i+1}##apt{i}";
                    var subLabel = $"{i+1}##sub{i}";
                    if (ImGui.RadioButton(aptLabel, ref selectedApartment, i + 1))
                    {
                        // selecting an apartment clears subdivision
                        selectedSubdivision = -1;
                    }
                    ImGui.NextColumn();
                    if (ImGui.RadioButton(subLabel, ref selectedSubdivision, i + 1))
                    {
                        // selecting a subdivision clears apartment
                        selectedApartment = -1;
                    }
                    ImGui.NextColumn();
                    ImGui.PopID();
                }
                ImGui.Columns(1);
                ImGui.EndChild();
            }
        }

        // Carrd URL
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Carrd URL");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputText("##CarrdUrl", ref carrdUrl, 256);

        // Schedule (separate from address editing)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Open Days");
        ImGui.TableSetColumnIndex(1);
        var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        for (var d = 0; d < 7; ++d)
        {
            ImGui.PushID(d + 300);
            ImGui.Checkbox(dayNames[d], ref openDays[d]);
            ImGui.SameLine();
            ImGui.PopID();
        }
        ImGui.NewLine();
        if (ImGui.SmallButton("Weekdays"))
        {
            for (var i = 0; i < 7; ++i) openDays[i] = i >= 1 && i <= 5;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Weekends"))
        {
            for (var i = 0; i < 7; ++i) openDays[i] = (i == 0 || i == 6);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Every Day"))
        {
            for (var i = 0; i < 7; ++i) openDays[i] = true;
        }

        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Open Time (local)");
        ImGui.TableSetColumnIndex(1);
        // Hour dropdown (show 12-hour labels with AM/PM)
        ImGui.SetNextItemWidth(52f);
        if (ImGui.BeginCombo("##OpenHour", FormatHourLabel(openHour)))
        {
            for (var h = 0; h < 24; ++h)
            {
                var label = FormatHourLabel(h);
                var sel = h == openHour;
                if (ImGui.Selectable(label, sel)) openHour = h;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextUnformatted(":" ); ImGui.SameLine();
        // Minute dropdown (compact increments)
        var minuteOptions = new[] { 0, 15, 30, 45 };
        ImGui.SetNextItemWidth(48f);
        if (ImGui.BeginCombo("##OpenMinute", openMinute.ToString("D2")))
        {
            foreach (var mVal in minuteOptions)
            {
                var sel = mVal == openMinute;
                if (ImGui.Selectable(mVal.ToString("D2"), sel)) openMinute = mVal;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextUnformatted(FormatTimeLabel(openHour, openMinute));

        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Close Time (local)");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(52f);
        if (ImGui.BeginCombo("##CloseHour", FormatHourLabel(closeHour)))
        {
            for (var h = 0; h < 24; ++h)
            {
                var label = FormatHourLabel(h);
                var sel = h == closeHour;
                if (ImGui.Selectable(label, sel)) closeHour = h;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextUnformatted(":" ); ImGui.SameLine();
        ImGui.SetNextItemWidth(48f);
        if (ImGui.BeginCombo("##CloseMinute", closeMinute.ToString("D2")))
        {
            foreach (var mVal in minuteOptions)
            {
                var sel = mVal == closeMinute;
                if (ImGui.Selectable(mVal.ToString("D2"), sel)) closeMinute = mVal;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.TextUnformatted(FormatTimeLabel(closeHour, closeMinute));

        // Tags
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Tags");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputText("##Tags", ref tags, 64);

        // Length (minutes) - limit to 8 hours (480 minutes)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Length (minutes)");
        ImGui.TableSetColumnIndex(1);
        // Use a slider for length between 1 and 480 minutes (8 hours)
        ImGui.SliderInt("##LengthMinutes", ref lengthMinutes, 1, 480);
        // Show selected minutes and equivalent hours
        ImGui.SameLine(); ImGui.TextColored(new System.Numerics.Vector4(0.7f,0.7f,0.7f,1f), $"{lengthMinutes} min ({(lengthMinutes/60.0):F2}h)");

        // padding
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty);
        ImGui.TableSetColumnIndex(1); ImGui.Spacing();

        // Wi‑Fi toggle
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Wi‑Fi / Sync");
        ImGui.TableSetColumnIndex(1);
        if (ImGui.SmallButton(showWifiOptions ? "Hide Wi-Fi" : "Edit Wi-Fi")) showWifiOptions = !showWifiOptions;

        if (showWifiOptions)
        {
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty); ImGui.TableSetColumnIndex(1);
            ImGui.Checkbox("Lightless", ref wifiLightless);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Syncshell ID:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##LightlessID", ref lightlessSyncshellId, 64);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Password:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##LightlessPW", ref lightlessSyncshellPassword, 64);

            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty); ImGui.TableSetColumnIndex(1);
            ImGui.Checkbox("PlayerSync", ref wifiPlayerSync);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Syncshell ID:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##PlayerSyncID", ref playerSyncSyncshellId, 64);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Password:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##PlayerSyncPW", ref playerSyncSyncshellPassword, 64);

            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty); ImGui.TableSetColumnIndex(1);
            ImGui.Checkbox("SnowCloak", ref wifiSnowCloak);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Syncshell ID:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##SnowCloakID", ref snowCloakSyncshellId, 64);
            ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Password:"); ImGui.TableSetColumnIndex(1);
            ImGui.InputText("##SnowCloakPW", ref snowCloakSyncshellPassword, 64);
        }

        // Show logged in name and require world confirmation to reduce spoofing/bot submissions
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Logged in as:");
        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(GetLocalPlayerNameSafe());

        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Confirm world");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputText("##ConfirmWorld", ref confirmWorld, 64);

        // Submit (button disabled until world is manually confirmed)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty); ImGui.TableSetColumnIndex(1);
        // The confirm field is intended to confirm the submitter's home/world used
        // for ownership token issuance. Use the player's world when available.
        var effectiveTokenWorld = GetLocalPlayerWorldSafe() ?? (world ?? string.Empty);
        var worldConfirmed = !string.IsNullOrWhiteSpace(confirmWorld) && string.Equals(confirmWorld.Trim(), effectiveTokenWorld?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        ImGui.BeginDisabled(!worldConfirmed || submitting);
        if (ImGui.Button("Submit")) { submitting = true; statusMessage = "Submitting..."; _ = SubmitAsync(); }
        ImGui.EndDisabled();
        if (submitting)
        {
            // simple visual state while submitting
            ImGui.TextUnformatted("Submitting...");
        }

        // show last status message (success/failure/error)
        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Separator();
            ImGui.TextUnformatted(statusMessage);
        }

        ImGui.EndTable();
    }

    private string ComposeDisplayAddress()
    {
        var district = GetDistrictDisplay(housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty);
        if (useApartment)
        {
            // Determine apartment/subdivision titles based on district
            string aptTitle = string.Empty, subdivTitle = string.Empty;
            switch (selectedDistrictIndex)
            {
                case 0: // Lavender Beds
                    aptTitle = "Lily Hills"; subdivTitle = "Lily Hills Subs"; break;
                case 1: // Mist
                    aptTitle = "Topmast"; subdivTitle = "Topmast Subs"; break;
                case 2: // The Goblet
                    aptTitle = "Sultana's Breath"; subdivTitle = "Sultana's Breath Subs"; break;
                case 3: // Empyreum
                    aptTitle = "Ingleside"; subdivTitle = "Ingleside Subs"; break;
                case 4: // Shirogane
                    aptTitle = "Kobai Goten"; subdivTitle = "Kobai Goten Subs"; break;
            }

            var parts = new List<string> { dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty, world, district };

            // Use single-selection indices for apartment/subdivision (1-based, -1 = none)
            if (selectedSubdivision != -1 && !string.IsNullOrEmpty(subdivTitle))
            {
                parts.Add($"{subdivTitle} #{selectedSubdivision}");
            }
            else if (selectedApartment != -1 && !string.IsNullOrEmpty(aptTitle))
            {
                parts.Add($"{aptTitle} #{selectedApartment}");
            }
            else
            {
                // fallback to ward/plot style if no apartment/subdivision selected
                parts.Add($"W{selectedWard}");
                parts.Add($"P{selectedPlot}");
            }

            return string.Join(" > ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        return $"{dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty} > {world} > {district} > W{selectedWard} > P{selectedPlot}";
    }

    private static string GetDistrictDisplay(string district)
    {
        if (string.IsNullOrEmpty(district)) return district;
        return district switch
        {
            "Lavender Beds" => "Lav Beds",
            _ => district
        };
    }

    // Remove Lumina rowref tokens and normalize separators so stored addresses
    // containing rowrefs (e.g. "Lumina.Excel.RowRef`1[...]") don't render raw in the UI.
    private static string SanitizeStoredAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            // Remove Lumina rowref tokens like: Lumina.Excel.RowRef`1[...]
            var s = Regex.Replace(raw, @"Lumina\.Excel\.RowRef`?\d*\[[^\]]*\]", string.Empty, RegexOptions.IgnoreCase);
            // Normalize separators and whitespace
            s = Regex.Replace(s, @"\s*>\s*", " > ");
            // Collapse repeated separators
            s = Regex.Replace(s, @">(\s*>)+", "> ");
            // Trim leftover separators/spaces
            s = s.Trim();
            s = s.Trim('>', ' ', '\t');
            // Final normalize
            s = Regex.Replace(s, @"\s*>\s*", " > ");
            return s;
        }
        catch
        {
            return raw;
        }
    }

    private string GetSubmittedAddress()
    {
        if (editingAddress && !string.IsNullOrWhiteSpace(address)) return address; // custom override
        // For submission prefer the composed, user-visible address format (matches listing display)
        if (useApartment || usePlot)
        {
            return ComposeDisplayAddress();
        }

        return address;
    }

    // Obtain a displayable player name in a compatibility-safe way.
    // Prefer IPlayerState when available, then try PluginInterface.ObjectTable.LocalPlayer via reflection.
    private static string GetLocalPlayerNameSafe()
    {
        try
        {
            var ps = WTP.PlayerState;
            if (ps != null)
            {
                var t = ps.GetType();
                var prop = t.GetProperty("Name") ?? t.GetProperty("DisplayName") ?? t.GetProperty("CharacterName");
                if (prop != null)
                {
                    var val = prop.GetValue(ps);
                    if (val != null) return val.ToString() ?? string.Empty;
                }
            }

            // Try ObjectTable.LocalPlayer via PluginInterface (some Dalamud versions expose this)
            try
            {
                var pi = WTP.PluginInterface;
                if (pi != null)
                {
                    var otProp = pi.GetType().GetProperty("ObjectTable");
                    var ot = otProp?.GetValue(pi);
                    if (ot != null)
                    {
                        var lpProp = ot.GetType().GetProperty("LocalPlayer");
                        var lp = lpProp?.GetValue(ot);
                        if (lp != null)
                        {
                            var lpType = lp.GetType();
                            var nameProp = lpType.GetProperty("Name") ?? lpType.GetProperty("DisplayName");
                            var nm = nameProp?.GetValue(lp)?.ToString();
                            if (!string.IsNullOrEmpty(nm)) return nm;
                            var s = lp.ToString();
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
            }
            catch { }
        }
        catch { }
        return string.Empty;
    }

    // Return a stable character identifier. Preferred: PlayerState.ContentId when available.
    // Fallbacks: reflect over PluginInterface.ObjectTable.LocalPlayer for ContentId/ObjectId/Id,
    // finally fall back to "name@world" using PlayerState values.
    private static string? GetLocalCharacterId()
    {
        try
        {
            // Prefer ContentId from IPlayerState if present
            var ps = WTP.PlayerState;
            if (ps != null)
            {
                try
                {
                    var cidProp = ps.GetType().GetProperty("ContentId");
                    if (cidProp != null)
                    {
                        var cidVal = cidProp.GetValue(ps);
                        if (cidVal is long l && l != 0) return l.ToString();
                        if (cidVal is ulong ul && ul != 0) return ul.ToString();
                        if (cidVal != null)
                        {
                            var s = cidVal.ToString();
                            if (!string.IsNullOrEmpty(s) && s != "0") return s;
                        }
                    }
                }
                catch { }
            }

            // Try ObjectTable.LocalPlayer via PluginInterface (preferred to obsolete IClientState.LocalPlayer)
            try
            {
                var pi = WTP.PluginInterface;
                var otProp = pi?.GetType().GetProperty("ObjectTable");
                var ot = otProp?.GetValue(pi);
                var lpProp = ot?.GetType().GetProperty("LocalPlayer");
                var lp = lpProp?.GetValue(ot);
                if (lp != null)
                {
                    var props = lp.GetType().GetProperties();
                    foreach (var prop in props)
                    {
                        if (string.Equals(prop.Name, "ContentId", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(prop.Name, "ObjectId", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var val = prop.GetValue(lp);
                                if (val != null)
                                {
                                    var s = val.ToString();
                                    if (!string.IsNullOrEmpty(s) && s != "0") return s;
                                }
                            }
                            catch { }
                        }
                    }

                    // Final fallback using LocalPlayer name + world properties
                    try
                    {
                        var nameProp = lp.GetType().GetProperty("Name") ?? lp.GetType().GetProperty("DisplayName");
                        var worldProp = lp.GetType().GetProperty("HomeWorld") ?? lp.GetType().GetProperty("HomeWorldId") ?? lp.GetType().GetProperty("World");
                        var nameVal = nameProp?.GetValue(lp)?.ToString();
                        var worldVal = worldProp?.GetValue(lp)?.ToString();
                        if (!string.IsNullOrEmpty(nameVal))
                        {
                            if (!string.IsNullOrEmpty(worldVal)) return $"{nameVal}@{worldVal}";
                            return nameVal;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Last resort: try PlayerState name and HomeWorldId
            try
            {
                if (ps != null)
                {
                    var nameProp = ps.GetType().GetProperty("Name") ?? ps.GetType().GetProperty("DisplayName");
                    var hwProp = ps.GetType().GetProperty("HomeWorldId");
                    var n = nameProp?.GetValue(ps)?.ToString();
                    var hw = hwProp?.GetValue(ps)?.ToString();
                    if (!string.IsNullOrEmpty(n)) return !string.IsNullOrEmpty(hw) ? $"{n}@{hw}" : n;
                }
            }
            catch { }
        }
        catch { }
        return null;
    }

    private async Task SubmitAsync()
    {
        try
        {
            var v = new Venue
            {
                Id = Guid.NewGuid(),
                Name = name,
                World = world,
                DC = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty,
                Address = GetSubmittedAddress(),
                CarrdUrl = carrdUrl ?? string.Empty,
                Tags = tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
                Description = description,
                Owner = string.Empty,
                LastUpdatedUtc = DateTime.UtcNow,
                LengthMinutes = Math.Max(1, Math.Min(lengthMinutes, 480)),
                // OpensAtUtc/ClosesAtUtc will be set below based on selected days/times converted to UTC
                OpensAtUtc = DateTime.UtcNow,
                ClosesAtUtc = DateTime.UtcNow.AddHours(1),
                // persist schedule info (local minutes)
                OpenDaysMask = Enumerable.Range(0,7).Where(i => openDays[i]).Aggregate(0, (acc, i) => acc | (1 << i)),
                OpenTimeMinutesLocal = Math.Max(0, Math.Min(24*60-1, openHour*60 + openMinute)),
                CloseTimeMinutesLocal = Math.Max(0, Math.Min(24*60-1, closeHour*60 + closeMinute))
            };

            WifiOption options = WifiOption.None;
            if (wifiLightless) options |= WifiOption.Lightless;
            if (wifiPlayerSync) options |= WifiOption.PlayerSync;
            if (wifiSnowCloak) options |= WifiOption.SnowCloak;
            v.WifiOptions = options;

            v.LightlessSyncshellId = lightlessSyncshellId ?? string.Empty;
            v.LightlessSyncshellPassword = lightlessSyncshellPassword ?? string.Empty;
            v.PlayerSyncSyncshellId = playerSyncSyncshellId ?? string.Empty;
            v.PlayerSyncSyncshellPassword = playerSyncSyncshellPassword ?? string.Empty;
            v.SnowCloakSyncshellId = snowCloakSyncshellId ?? string.Empty;
            v.SnowCloakSyncshellPassword = snowCloakSyncshellPassword ?? string.Empty;

            // Determine current player identity for ownership: prefer stable character id, fall back to name.
            var currentCharId = GetLocalCharacterId();
            var currentName = GetLocalPlayerNameSafe();
            var ownerId = currentCharId ?? currentName ?? string.Empty;
            v.Owner = ownerId;

            // Check for duplicates and existing ownership
            try
            {
                var existing = (await venueService.FetchVenuesAsync()) ?? new List<Venue>();
                // Find by name (case-insensitive)
                var sameName = existing.FirstOrDefault(x => string.Equals(x.Name?.Trim(), v.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    // If the same owner, treat as update; otherwise reject
                    if (!string.IsNullOrEmpty(sameName.Owner) && IsOwnerMatch(sameName.Owner, currentCharId, currentName))
                    {
                        v.Id = sameName.Id; // update owner's existing listing with same name
                    }
                    else
                    {
                        // Do not leak server-side conflict details; show a generic message
                        statusMessage = "That venue already exists.";
                        submitting = false;
                        return;
                    }
                }

                // Ensure one entry per user: if the user already has a different listing, update it instead of creating a new one
                var usersListing = existing.FirstOrDefault(x => !string.IsNullOrEmpty(x.Owner) && IsOwnerMatch(x.Owner, currentCharId, currentName));
                if (usersListing != null && usersListing.Id != v.Id)
                {
                    // Update the user's existing listing (overwrite)
                    v.Id = usersListing.Id;
                }
            }
            catch
            {
                // If checking fails, continue and let the submit attempt proceed; the server will enforce uniqueness if necessary
            }

            // Calculate actual opening/closing UTC datetimes based on submitter's local time and selected days
            try
            {
                var daysMask = v.OpenDaysMask;
                if (daysMask == 0)
                {
                    // no schedule selected: fall back to immediate open/length
                    v.OpensAtUtc = DateTime.UtcNow;
                    v.ClosesAtUtc = DateTime.UtcNow.AddMinutes(v.LengthMinutes);
                }
                else
                {
                    var nowLocal = DateTime.Now;
                    DateTime? nextOpenLocal = null;
                    for (var offset = 0; offset < 7; ++offset)
                    {
                        var check = nowLocal.Date.AddDays(offset);
                        var dow = (int)check.DayOfWeek; // 0..6
                        if ((daysMask & (1 << dow)) == 0) continue;
                        // construct candidate local open time on that date
                        var openMinutes = v.OpenTimeMinutesLocal;
                        var openHourCandidate = openMinutes / 60;
                        var openMinCandidate = openMinutes % 60;
                        var candidate = new DateTime(check.Year, check.Month, check.Day, openHourCandidate, openMinCandidate, 0, DateTimeKind.Local);
                        if (candidate >= nowLocal || offset > 0)
                        {
                            nextOpenLocal = candidate;
                            break;
                        }
                    }
                    if (!nextOpenLocal.HasValue) // should not happen, but fallback
                        nextOpenLocal = DateTime.Now;

                    // compute close local: on same day unless close time <= open time -> next day
                    var closeMinutes = v.CloseTimeMinutesLocal;
                    var closeHourCandidate = closeMinutes / 60;
                    var closeMinCandidate = closeMinutes % 60;
                    var closeLocal = new DateTime(nextOpenLocal.Value.Year, nextOpenLocal.Value.Month, nextOpenLocal.Value.Day, closeHourCandidate, closeMinCandidate, 0, DateTimeKind.Local);
                    if (closeLocal <= nextOpenLocal.Value)
                    {
                        closeLocal = closeLocal.AddDays(1);
                    }

                    v.OpensAtUtc = nextOpenLocal.Value.ToUniversalTime();
                    v.ClosesAtUtc = closeLocal.ToUniversalTime();
                }
            }
            catch { }

            // Request a short-lived token for this character, then submit using the token.
            bool ok = false;
            int statusCode = 0;
            string respBody = string.Empty;
            try
            {
                object? GetMember(object o, string name)
                {
                    var tt = o.GetType();
                    var prop = tt.GetProperty(name);
                    if (prop != null) return prop.GetValue(o);
                    var field = tt.GetField(name);
                    if (field != null) return field.GetValue(o);
                    return null;
                }

                var charId = GetLocalCharacterId() ?? string.Empty;
                var charName = GetLocalPlayerNameSafe() ?? string.Empty;

                // Determine the world to use for token issuance: prefer the player's
                // home/world (so confirmWorld is confirming the player's world).
                var tokenWorld = GetLocalPlayerWorldSafe() ?? v.World ?? string.Empty;

                // Require the user to confirm the world that will be used for the submission
                if (string.IsNullOrWhiteSpace(confirmWorld) || !string.Equals(confirmWorld.Trim(), tokenWorld?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    statusMessage = "Please confirm the selected world before submitting.";
                    submitting = false;
                    return;
                }

                var tokenResultObj = await venueService.RequestTokenAsync(charId, charName, tokenWorld);
                var r_ok = GetMember(tokenResultObj, "ok") ?? GetMember(tokenResultObj, "Item1");
                var r_token = GetMember(tokenResultObj, "token") ?? GetMember(tokenResultObj, "Item2");
                var r_status = GetMember(tokenResultObj, "statusCode") ?? GetMember(tokenResultObj, "Item3");
                var r_body = GetMember(tokenResultObj, "body") ?? GetMember(tokenResultObj, "Item4");

                var tokenOk = false;
                var tokenStr = string.Empty;
                var tokenStatus = 0;
                var tokenBody = string.Empty;
                if (r_ok != null) tokenOk = Convert.ToBoolean(r_ok);
                if (r_token != null) tokenStr = r_token.ToString() ?? string.Empty;
                if (r_status != null) { try { tokenStatus = Convert.ToInt32(r_status); } catch { tokenStatus = 0; } }
                if (r_body != null) tokenBody = r_body.ToString() ?? string.Empty;

                if (!tokenOk || string.IsNullOrEmpty(tokenStr))
                {
                    var body = tokenBody.Replace("\r", "").Replace("\n", " ").Trim();
                    if (body.Length > 300) body = body.Substring(0, 300) + "...";
                    statusMessage = $"Token request failed ({tokenStatus}): {body}";
                    submitting = false;
                    return;
                }

                // Submit using token
                var submitResultObj = await venueService.SubmitVenueAsync(v, tokenStr);

                var s1 = GetMember(submitResultObj, "ok") ?? GetMember(submitResultObj, "Item1");
                var s2 = GetMember(submitResultObj, "statusCode") ?? GetMember(submitResultObj, "Item2");
                var s3 = GetMember(submitResultObj, "body") ?? GetMember(submitResultObj, "Item3");

                if (s1 != null) ok = Convert.ToBoolean(s1);
                if (s2 != null) { try { statusCode = Convert.ToInt32(s2); } catch { statusCode = 0; } }
                if (s3 != null) respBody = s3.ToString() ?? string.Empty;
            }
            catch
            {
                ok = false; statusCode = -1; respBody = string.Empty;
            }

                if (ok)
                {
                    statusMessage = "Submitted successfully.";
                    try { await plugin.RequestVenueRefreshAsync(); } catch { }
                }
                else
                {
                    // For known conflict status avoid leaking server details
                    if (statusCode == 409)
                    {
                        statusMessage = "That venue already exists.";
                    }
                    else
                    {
                        var body = respBody ?? string.Empty;
                        body = body.Replace("\r", "").Replace("\n", " ").Trim();
                        if (body.Length > 300) body = body.Substring(0, 300) + "...";
                        statusMessage = $"Submission failed ({statusCode}): {body}";
                    }
                }
        }
        catch (Exception ex)
        {
            statusMessage = "Submission error: " + ex.Message;
        }
        finally
        {
            submitting = false;
        }
    }
}
