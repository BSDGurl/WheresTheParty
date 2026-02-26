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

    // Wi-Fi
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
    private bool editingAddress = false; // toggles the address editor
    private bool usePlot = true;
    private bool useApartment = false;
    // Only one apartment or subdivision may be selected per listing
    private int selectedApartment = -1; // 1-based index, -1 = none
    private int selectedSubdivision = -1; // 1-based index, -1 = none

    // Wi-Fi UI
    private bool showWifiOptions = false;

    // Weekly schedule
    private bool[] openDays = new bool[7]; // 0=Sun .. 6=Sat
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
        address = v.Address ?? string.Empty;
        lengthMinutes = v.LengthMinutes > 0 ? v.LengthMinutes : lengthMinutes;

        // Data center / world selection
        if (!string.IsNullOrEmpty(v.DC))
        {
            var idx = dataCenters.FindIndex(x => string.Equals(x, v.DC, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) selectedDcIndex = idx;
        }
        // set world selection if possible
        var dcKey = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty;
        if (!string.IsNullOrEmpty(v.World) && worldsByDc != null && worldsByDc.ContainsKey(dcKey))
        {
            var worlds = worldsByDc[dcKey];
            var widx = worlds.FindIndex(x => string.Equals(x, v.World, StringComparison.OrdinalIgnoreCase));
            if (widx >= 0) { selectedWorldIndex = widx; world = worlds.ElementAtOrDefault(selectedWorldIndex) ?? v.World; }
            else world = v.World ?? world;
        }
        else
        {
            world = v.World ?? world;
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
    }

    // Fetch current user's listing (if any), populate the form, and open the submit window.
    public async Task OpenForCurrentUserAsync()
    {
        try
        {
            var currentOwner = WTP.ClientState?.LocalPlayer?.Name?.ToString() ?? string.Empty;
            var list = await venueService.FetchVenuesAsync();
            var usersListing = list.FirstOrDefault(x => !string.IsNullOrEmpty(x.Owner) && string.Equals(x.Owner, currentOwner, StringComparison.OrdinalIgnoreCase));
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
        ImGui.TableSetColumnIndex(1); ImGui.InputText("##VenueName", ref name, 50);

        // Description (allow longer text)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Description");
        ImGui.TableSetColumnIndex(1); ImGui.InputTextMultiline("##VenueDescription", ref description, 1500, new Vector2(-1, 80));

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
        ImGui.TableSetColumnIndex(1); ImGui.InputText("##CarrdUrl", ref carrdUrl, 256);

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
        ImGui.TableSetColumnIndex(1); ImGui.InputText("##Tags", ref tags, 64);

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

        // Submit
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(string.Empty); ImGui.TableSetColumnIndex(1);
        if (!submitting)
        {
            if (ImGui.Button("Submit")) { submitting = true; statusMessage = "Submitting..."; _ = SubmitAsync(); }
        }
        else
        {
            // simple visual disabled state while submitting
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

            // Determine current player name to mark ownership
            // LocalPlayer.Name is a SeString; convert to string. IClientState.LocalPlayer is marked obsolete but still available.
            string currentOwner = WTP.ClientState?.LocalPlayer?.Name?.ToString() ?? string.Empty;
            // No reliable Name on IPlayerState across Dalamud versions; keep client-local fallback only
            v.Owner = currentOwner;

            // Check for duplicates and existing ownership
            try
            {
                var existing = (await venueService.FetchVenuesAsync()) ?? new List<Venue>();
                // Find by name (case-insensitive)
                var sameName = existing.FirstOrDefault(x => string.Equals(x.Name?.Trim(), v.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    // If the same owner, treat as update; otherwise reject
                    if (!string.IsNullOrEmpty(sameName.Owner) && string.Equals(sameName.Owner, currentOwner, StringComparison.OrdinalIgnoreCase))
                    {
                        v.Id = sameName.Id; // update owner's existing listing with same name
                    }
                    else
                    {
                        statusMessage = "A venue with that name already exists.";
                        submitting = false;
                        return;
                    }
                }

                // Ensure one entry per user: if the user already has a different listing, update it instead of creating a new one
                var usersListing = existing.FirstOrDefault(x => !string.IsNullOrEmpty(x.Owner) && string.Equals(x.Owner, currentOwner, StringComparison.OrdinalIgnoreCase));
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

            var ok = await venueService.SubmitVenueAsync(v);
            if (ok)
            {
                statusMessage = "Submitted successfully.";
                // Refresh the shared venue list so the new entry appears immediately.
                try { await plugin.RequestVenueRefreshAsync(); } catch { }
            }
            else
            {
                statusMessage = "Submission failed.";
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
