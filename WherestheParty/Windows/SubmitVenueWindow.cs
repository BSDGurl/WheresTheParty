using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using WTP.Models;
using WTP.Services;

namespace WTP.Windows;

public class SubmitVenueWindow : Window, IDisposable
{

    private readonly WTP plugin;
    private readonly VenueService venueService;
    private readonly PlayerIdentityService identityService;
    private readonly IPluginLog log;
    private readonly Configuration configuration;

    private string name = string.Empty;
    private string description = string.Empty;
    private string world = string.Empty;
    private string address = string.Empty;
    private bool useApartment = false;
    private int selectedApartment = -1;
    private int selectedSubdivision = -1;
    private string carrdUrl = string.Empty;
    private bool isCarrdValid = true;
    private readonly string[] hourOptions = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };
    private readonly string[] minuteOptions = { "00", "05", "10", "15", "20", "25", "30", "35", "40", "45", "50", "55" };
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

    private readonly List<string> dataCenters = new()
    {
        "Aether","Crystal","Dynamis","Primal","Chaos","Light",
        "Materia","Elemental","Gaia","Mana","Meteor"
    };

    private readonly Dictionary<string, List<string>> worldsByDc = new()
    {
        ["Aether"] = new() { "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren" },
        ["Crystal"] = new() { "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera" },
        ["Dynamis"] = new() { "Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph" },
        ["Primal"] = new() { "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros" },
        ["Chaos"] = new() { "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan" },
        ["Light"] = new() { "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark" },
        ["Materia"] = new() { "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan" },
        ["Elemental"] = new() { "Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon" },
        ["Gaia"] = new() { "Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima" },
        ["Mana"] = new() { "Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan" },
        ["Meteor"] = new() { "Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus" }
    };

    private int selectedDcIndex = 0;
    private int selectedWorldIndex = 0;

    private readonly List<string> housingDistricts = new()
    {
        "Lavender Beds","Mist","The Goblet","Empyreum","Shirogane"
    };

    private int selectedDistrictIndex = 0;
    private int selectedWard = 0;
    private int selectedPlot = 0;

    private bool editingAddress = false;

    private bool showWifiOptions = false;

    private bool[] openDays = new bool[7];
    private int openHour = 20;
    private int openMinute = 0;
    private int closeHour = 23;
    private int closeMinute = 0;

    public SubmitVenueWindow(
        WTP plugin,
        VenueService venueService,
        PlayerIdentityService identityService,
        IPluginLog log
    ) : base("Submit Venue###SubmitVenueWindow")
    {
        this.plugin = plugin;
        this.venueService = venueService;
        this.identityService = identityService;
        this.log = log;
        this.configuration = plugin.Configuration;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(520, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 240),
            // Increase maximum width slightly so the unit grid and controls have more room
            MaximumSize = new Vector2(800, float.MaxValue)
        };

        selectedDcIndex = dataCenters.IndexOf("Primal");
        selectedDistrictIndex = 0;
        selectedWard = 18;
        selectedPlot = 35;
        world = "Behemoth";
    }

    public void Dispose() { }

    public async Task OpenForCurrentUserAsync()
    {
        try
        {
            var charId = identityService.GetCharacterId();
            var charName = identityService.GetCharacterName();

            var list = await venueService.FetchVenuesAsync();
            var existing = list.FirstOrDefault(v =>
                !string.IsNullOrEmpty(v.Owner) &&
                (v.Owner.Equals(charId, StringComparison.OrdinalIgnoreCase) ||
                 v.Owner.Equals(charName, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
                PopulateFromVenue(existing);
            else
                ClearForm();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to open submit window");
            ClearForm();
        }

        if (!IsOpen)
            Toggle();
    }

    private void PopulateFromVenue(Venue v)
    {
        name = v.Name ?? string.Empty;
        description = v.Description ?? string.Empty;
        carrdUrl = v.CarrdUrl ?? string.Empty;
        tags = v.Tags != null ? string.Join(',', v.Tags) : string.Empty;
        address = v.Address ?? string.Empty;
        lengthMinutes = v.LengthMinutes;

        if (!string.IsNullOrEmpty(v.DC))
        {
            var idx = dataCenters.IndexOf(v.DC);
            if (idx >= 0) selectedDcIndex = idx;
        }

        var dc = dataCenters[selectedDcIndex];
        if (worldsByDc.TryGetValue(dc, out var list))
        {
            var widx = list.IndexOf(v.World ?? string.Empty);
            if (widx >= 0)
            {
                selectedWorldIndex = widx;
                world = list[widx];
            }
        }

        wifiLightless = (v.WifiOptions & WifiOption.Lightless) != 0;
        wifiPlayerSync = (v.WifiOptions & WifiOption.PlayerSync) != 0;
        wifiSnowCloak = (v.WifiOptions & WifiOption.SnowCloak) != 0;

        lightlessSyncshellId = v.LightlessSyncshellId ?? string.Empty;
        lightlessSyncshellPassword = v.LightlessSyncshellPassword ?? string.Empty;
        playerSyncSyncshellId = v.PlayerSyncSyncshellId ?? string.Empty;
        playerSyncSyncshellPassword = v.PlayerSyncSyncshellPassword ?? string.Empty;
        snowCloakSyncshellId = v.SnowCloakSyncshellId ?? string.Empty;
        snowCloakSyncshellPassword = v.SnowCloakSyncshellPassword ?? string.Empty;

        for (int i = 0; i < 7; i++)
            openDays[i] = (v.OpenDaysMask & (1 << i)) != 0;

        openHour = v.OpenTimeMinutesLocal / 60;
        openMinute = v.OpenTimeMinutesLocal % 60;
        closeHour = v.CloseTimeMinutesLocal / 60;
        closeMinute = v.CloseTimeMinutesLocal % 60;

        confirmWorld = string.Empty;
    }

    private void ClearForm()
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

        for (int i = 0; i < 7; i++) openDays[i] = false;

        openHour = 20;
        openMinute = 0;
        closeHour = 23;
        closeMinute = 0;

        confirmWorld = string.Empty;
        submitting = false;
        statusMessage = string.Empty;
    }

    public override void Draw()
    {
        ImGui.PushItemWidth(-1);

        ImGui.BeginTable("SubmitVenueTable", 2, ImGuiTableFlags.SizingStretchSame);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 140f);
        ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);

        DrawName();
        DrawDescription();
        DrawAddress();
        DrawCarrd();
        DrawSchedule();
        DrawTags();
        DrawLength();
        DrawWifi();
        DrawIdentity();
        DrawSubmitButton();

        ImGui.EndTable();

        ImGui.PopItemWidth();
    }

    private void DrawName()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        var nameEmpty = string.IsNullOrWhiteSpace(name);
        if (nameEmpty)
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Venue Name");
        else
            ImGui.Text("Venue Name");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (nameEmpty)
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.35f, 0.06f, 0.06f, 1f));

        ImGui.InputText("##VenueName", ref name, 50);

        if (nameEmpty)
            ImGui.PopStyleColor();
    }

    private void DrawDescription()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Description");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextMultiline("##VenueDescription", ref description, 1500, new Vector2(ImGui.GetContentRegionAvail().X, 80));
    }

    private void DrawAddress()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Address");

        ImGui.TableSetColumnIndex(1);

        var composed = string.IsNullOrWhiteSpace(address) ? ComposeAddress() : address;
        ImGui.Text(composed);

        ImGui.SameLine();
        if (ImGui.SmallButton(editingAddress ? "Done##Addr" : "Edit##Addr"))
        {
            if (editingAddress)
                address = ComposeAddress();
            editingAddress = !editingAddress;
        }

        if (!editingAddress)
            return;

        DrawAddressEditor();
    }

    private void DrawAddressEditor()
    {
        // --- Data Center ---
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Data Center");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##EditDC", dataCenters[selectedDcIndex]))
        {
            for (int i = 0; i < dataCenters.Count; i++)
            {
                bool sel = i == selectedDcIndex;
                if (ImGui.Selectable(dataCenters[i], sel))
                {
                    selectedDcIndex = i;
                    selectedWorldIndex = 0;
                }
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // --- World ---
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("World");

        ImGui.TableSetColumnIndex(1);
        var dc = dataCenters[selectedDcIndex];
        var worlds = worldsByDc[dc];

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##EditWorld", worlds[selectedWorldIndex]))
        {
            for (int i = 0; i < worlds.Count; i++)
            {
                bool sel = i == selectedWorldIndex;
                if (ImGui.Selectable(worlds[i], sel))
                {
                    selectedWorldIndex = i;
                    world = worlds[i];
                }
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // --- District ---
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("District");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##EditDistrict", housingDistricts[selectedDistrictIndex]))
        {
            for (int i = 0; i < housingDistricts.Count; i++)
            {
                bool sel = i == selectedDistrictIndex;
                if (ImGui.Selectable(housingDistricts[i], sel))
                    selectedDistrictIndex = i;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // --- Plot vs Apartment toggle ---
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Address Type");

        ImGui.TableSetColumnIndex(1);

        bool plotMode = !useApartment;

        if (ImGui.Checkbox("Plot", ref plotMode))
        {
            if (plotMode)
            {
                useApartment = false;
                selectedApartment = -1;
                selectedSubdivision = -1;
            }
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Apartment", ref useApartment))
        {
            if (useApartment)
            {
                plotMode = false;

                // Ensure a default unit is selected
                if (selectedApartment == -1 && selectedSubdivision == -1)
                    selectedApartment = 1;
            }
        }

        // --- Plot Mode UI ---
        if (plotMode)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Ward");

            ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##EditWard", $"W{selectedWard}"))
            {
                for (int w = 1; w <= 30; w++)
                {
                    bool sel = w == selectedWard;
                    if (ImGui.Selectable(w.ToString(), sel))
                        selectedWard = w;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Plot");

            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##EditPlot", $"P{selectedPlot}"))
            {
                for (int p = 1; p <= 60; p++)
                {
                    bool sel = p == selectedPlot;
                    if (ImGui.Selectable(p.ToString(), sel))
                        selectedPlot = p;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        // --- Apartment Mode UI ---
        if (useApartment)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Building");

            ImGui.TableSetColumnIndex(1);

            bool isMain = selectedSubdivision == -1;
            bool isSub = selectedSubdivision != -1;

            if (ImGui.RadioButton("Main", isMain))
            {
                selectedSubdivision = -1;
                if (selectedApartment < 1) selectedApartment = 1;
            }

            ImGui.SameLine();

            if (ImGui.RadioButton("Subdivision", isSub))
            {
                selectedApartment = -1;
                if (selectedSubdivision < 1) selectedSubdivision = 1;
            }

            // --- Unit selector (bordered grid, fixed width 320px, 9x10) ---
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Unit");

            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);

            // Child sized to available width minus a small gutter so the right border is visible.
            var availW = ImGui.GetContentRegionAvail().X;
            var styleLocal = ImGui.GetStyle();
            var gutter = Math.Max(4f, styleLocal.WindowPadding.X);
            // make the child a bit wider ( +5px ) but never exceed availW
            var baseWidth = Math.Max(120f, availW - gutter);
            float gridWidth = Math.Min(availW, baseWidth + 5f);

            // Force a clean 10x9 grid and avoid table borders so there are no divider lines.
            int columns = 10;
            int current = isSub ? selectedSubdivision : selectedApartment;

            // Compute child height to exactly fit 9 rows (90 items / 10 columns)
            int totalUnits = 90;
            int rows = (int)Math.Ceiling(totalUnits / (float)columns); // should be 9
            var style2 = ImGui.GetStyle();
            float itemH = ImGui.GetFrameHeight();
            float spacingY = style2.ItemSpacing.Y;
            // Reduce inner padding slightly and compute a tighter child height so there's less gap below the grid
            float childPadY = 1f; // matches pushed WindowPadding Y below
            // Compute child height to exactly fit rows so no vertical scrollbar is needed
            // Subtract a small fudge to avoid extra bottom gap
            float gridHeight = rows * itemH + Math.Max(0, rows - 1) * spacingY + (childPadY * 2f) - 8f;
            if (gridHeight < 100f) gridHeight = 100f;

            // Disable mouse-wheel scrolling and hide the scrollbar to prevent scrolling interaction
            ImGui.BeginChild("UnitGridFrame", new Vector2(gridWidth, gridHeight), true,
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(childPadY, childPadY));

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 1));

            if (ImGui.BeginTable("UnitTable", columns, ImGuiTableFlags.SizingStretchSame))
            {
                for (int n = 1; n <= 90; n++)
                {
                    ImGui.TableNextColumn();
                    bool sel = (n == current);
                    ImGui.PushID(n);
                    var label = $"{n}##unit{n}";
                    if (ImGui.RadioButton(label, sel))
                    {
                        if (isSub)
                            selectedSubdivision = n;
                        else
                            selectedApartment = n;
                    }
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }

            ImGui.PopStyleVar(2); // ItemSpacing + FramePadding
            ImGui.PopStyleVar();  // WindowPadding

            ImGui.EndChild();

            // bottom padding removed to tighten spacing

            ImGui.PopItemWidth();
        }

        // Always update preview
        address = ComposeAddress();
    }

    private string ComposeAddress()
    {
        var dc = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty;
        var district = housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty;

        // Apartment building names
        string aptTitle = string.Empty;
        string subdivTitle = string.Empty;

        switch (selectedDistrictIndex)
        {
            case 0: // Lavender Beds
                aptTitle = "Lily Hills";
                subdivTitle = "Lily Hills Sub";
                break;
            case 1: // Mist
                aptTitle = "Topmast";
                subdivTitle = "Topmast Sub";
                break;
            case 2: // The Goblet
                aptTitle = "Sultana's Breath";
                subdivTitle = "Sultana's Breath Sub";
                break;
            case 3: // Empyreum
                aptTitle = "Ingleside";
                subdivTitle = "Ingleside Sub";
                break;
            case 4: // Shirogane
                aptTitle = "Kobai Goten";
                subdivTitle = "Kobai Goten Sub";
                break;
        }

        var parts = new List<string> { dc, world, district };

        // Apartment mode
        if (useApartment)
        {
            if (selectedSubdivision != -1)
            {
                parts.Add($"{subdivTitle} #{selectedSubdivision}");
            }
            else if (selectedApartment != -1)
            {
                parts.Add($"{aptTitle} #{selectedApartment}");
            }
            else
            {
                // fallback to plot
                parts.Add($"W{selectedWard}");
                parts.Add($"P{selectedPlot}");
            }
        }
        else
        {
            // Plot mode
            parts.Add($"W{selectedWard}");
            parts.Add($"P{selectedPlot}");
        }

        return string.Join(" > ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private void DrawCarrd()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Carrd URL");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##CarrdUrl", ref carrdUrl, 256);

        // Validate carrd url if present
        if (string.IsNullOrWhiteSpace(carrdUrl))
        {
            isCarrdValid = true;
        }
        else
        {
            var tmp = carrdUrl.Trim();
            if (!Regex.IsMatch(tmp, "^[a-zA-Z][a-zA-Z0-9+.-]*:"))
                tmp = "https://" + tmp;
            if (Uri.TryCreate(tmp, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                isCarrdValid = true;
            else
                isCarrdValid = false;
        }

        if (!isCarrdValid)
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Please enter a valid URL (http/https).");
        }
    }

    private void DrawSchedule()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Open Days");

        ImGui.TableSetColumnIndex(1);

        var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        for (int i = 0; i < 7; i++)
        {
            ImGui.Checkbox(dayNames[i], ref openDays[i]);
            if (i < 6) ImGui.SameLine();
        }
        ImGui.NewLine();

        //
        // OPEN TIME
        //
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Open Time");

        ImGui.TableSetColumnIndex(1);

        // Hour
        ImGui.SetNextItemWidth(80f);
        if (ImGui.BeginCombo("##OpenHour", hourOptions[openHour % 12]))
        {
            for (int i = 0; i < hourOptions.Length; i++)
            {
                bool sel = (i == openHour % 12);
                if (ImGui.Selectable(hourOptions[i], sel))
                    openHour = (openHour >= 12 ? 12 : 0) + i;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // Minute
        ImGui.SetNextItemWidth(80f);
        if (ImGui.BeginCombo("##OpenMinute", minuteOptions[openMinute / 5]))
        {
            for (int i = 0; i < minuteOptions.Length; i++)
            {
                bool sel = (i == openMinute / 5);
                if (ImGui.Selectable(minuteOptions[i], sel))
                    openMinute = i * 5;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // AM/PM
        bool openPm = openHour >= 12;
        if (ImGui.RadioButton("AM", !openPm)) openHour %= 12;
        ImGui.SameLine();
        if (ImGui.RadioButton("PM", openPm)) openHour = (openHour % 12) + 12;

        ImGui.PopItemWidth();   // ← FIX


        //
        // CLOSE TIME
        //
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Close Time");

        ImGui.TableSetColumnIndex(1);

        // Hour
        ImGui.SetNextItemWidth(80f);
        if (ImGui.BeginCombo("##CloseHour", hourOptions[closeHour % 12]))
        {
            for (int i = 0; i < hourOptions.Length; i++)
            {
                bool sel = (i == closeHour % 12);
                if (ImGui.Selectable(hourOptions[i], sel))
                    closeHour = (closeHour >= 12 ? 12 : 0) + i;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // Minute
        ImGui.SetNextItemWidth(80f);
        if (ImGui.BeginCombo("##CloseMinute", minuteOptions[closeMinute / 5]))
        {
            for (int i = 0; i < minuteOptions.Length; i++)
            {
                bool sel = (i == closeMinute / 5);
                if (ImGui.Selectable(minuteOptions[i], sel))
                    closeMinute = i * 5;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // AM/PM
        bool closePm = closeHour >= 12;
        if (ImGui.RadioButton("AM", !closePm)) closeHour %= 12;
        ImGui.SameLine();
        if (ImGui.RadioButton("PM", closePm)) closeHour = (closeHour % 12) + 12;

        ImGui.PopItemWidth();   // ← FIX
    }

    private void DrawTags()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Tags");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##Tags", ref tags, 128);
    }

    private void DrawLength()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Length (minutes)");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.SliderInt("##LengthMinutes", ref lengthMinutes, 1, 480);
    }

    private void DrawWifi()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Wi-Fi / Sync");

        ImGui.TableSetColumnIndex(1);
        if (ImGui.SmallButton(showWifiOptions ? "Hide Wi-Fi" : "Edit Wi-Fi")) showWifiOptions = !showWifiOptions;

        if (!showWifiOptions)
            return;

        DrawWifiSection("Lightless", ref wifiLightless, ref lightlessSyncshellId, ref lightlessSyncshellPassword);
        DrawWifiSection("PlayerSync", ref wifiPlayerSync, ref playerSyncSyncshellId, ref playerSyncSyncshellPassword);
        DrawWifiSection("SnowCloak", ref wifiSnowCloak, ref snowCloakSyncshellId, ref snowCloakSyncshellPassword);
    }

    private void DrawWifiSection(string label, ref bool enabled, ref string id, ref string pw)
    {
        ImGui.Checkbox(label, ref enabled);

        ImGui.Text("ID:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText($"##{label}ID", ref id, 64);

        ImGui.Text("Password:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText($"##{label}PW", ref pw, 64);

        // If enabled, require both ID and Password
        if (enabled)
        {
            var missing = string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pw);
            if (missing)
            {
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "ID and Password are required when this option is enabled.");
            }
        }

        ImGui.Separator();
    }

    private bool ValidateWifi(out List<string> errors)
    {
        errors = new List<string>();
        if (wifiLightless)
        {
            if (string.IsNullOrWhiteSpace(lightlessSyncshellId) || string.IsNullOrWhiteSpace(lightlessSyncshellPassword))
                errors.Add("Lightless: ID and password required.");
        }
        if (wifiPlayerSync)
        {
            if (string.IsNullOrWhiteSpace(playerSyncSyncshellId) || string.IsNullOrWhiteSpace(playerSyncSyncshellPassword))
                errors.Add("PlayerSync: ID and password required.");
        }
        if (wifiSnowCloak)
        {
            if (string.IsNullOrWhiteSpace(snowCloakSyncshellId) || string.IsNullOrWhiteSpace(snowCloakSyncshellPassword))
                errors.Add("SnowCloak: ID and password required.");
        }
        return errors.Count == 0;
    }

    private void DrawIdentity()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Logged in as:");

        ImGui.TableSetColumnIndex(1);
        ImGui.Text(identityService.GetCharacterName() ?? "(unknown)");

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Confirm World");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##ConfirmWorld", ref confirmWorld, 64);
    }

    private void DrawSubmitButton()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);

        ImGui.TableSetColumnIndex(1);
        if (!string.IsNullOrEmpty(statusMessage))
            ImGui.TextWrapped(statusMessage);
        // Determine whether submission should be allowed
        var tokenWorld = identityService.GetWorldName() ?? world ?? string.Empty;
        var worldConfirmed = !string.IsNullOrWhiteSpace(confirmWorld) &&
                             string.Equals(confirmWorld.Trim(), tokenWorld.Trim(), StringComparison.OrdinalIgnoreCase);
        var namePresent = !string.IsNullOrWhiteSpace(name);
        var wifiValid = ValidateWifi(out var wifiErrors);
        var canSubmit = !submitting && namePresent && worldConfirmed && isCarrdValid && wifiValid;

        if (!canSubmit)
            ImGui.BeginDisabled();

        if (ImGui.Button(submitting ? "Submitting..." : "Submit Venue"))
        {
            _ = SubmitAsync();
        }

        if (!canSubmit)
        {
            ImGui.EndDisabled();

            // Explain why submission is disabled
            ImGui.NewLine();
            if (!namePresent)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Venue name is required.");
            if (!worldConfirmed)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Please confirm your world to enable submission.");
            if (!isCarrdValid)
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Carrd URL is invalid.");
            if (!wifiValid)
            {
                foreach (var e in wifiErrors)
                    ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), e);
            }
        }
    }

    private async Task SubmitAsync()
    {
        if (submitting)
            return;

        submitting = true;
        statusMessage = "Submitting...";

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                statusMessage = "Venue name is required.";
                return;
            }

            var dc = dataCenters[selectedDcIndex];
            var dcWorlds = worldsByDc[dc];
            world = dcWorlds[selectedWorldIndex];

            var v = new Venue
            {
                Name = name.Trim(),
                Description = description.Trim(),
                DC = dc,
                World = world,
                Address = string.IsNullOrWhiteSpace(address) ? ComposeAddress() : address.Trim(),
                CarrdUrl = string.IsNullOrWhiteSpace(carrdUrl) ? null : carrdUrl.Trim(),
                Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(t => t.Trim())
                           .Where(t => !string.IsNullOrEmpty(t))
                           .ToList(),
                LengthMinutes = lengthMinutes
            };

            var mask = 0;
            for (int i = 0; i < 7; i++)
                if (openDays[i]) mask |= 1 << i;
            v.OpenDaysMask = mask;

            openHour = Math.Clamp(openHour, 0, 23);
            closeHour = Math.Clamp(closeHour, 0, 23);
            openMinute = Math.Clamp(openMinute, 0, 59);
            closeMinute = Math.Clamp(closeMinute, 0, 59);

            v.OpenTimeMinutesLocal = openHour * 60 + openMinute;
            v.CloseTimeMinutesLocal = closeHour * 60 + closeMinute;

            WifiOption wifi = WifiOption.None;
            if (wifiLightless) wifi |= WifiOption.Lightless;
            if (wifiPlayerSync) wifi |= WifiOption.PlayerSync;
            if (wifiSnowCloak) wifi |= WifiOption.SnowCloak;
            v.WifiOptions = wifi;

            v.LightlessSyncshellId = string.IsNullOrWhiteSpace(lightlessSyncshellId) ? null : lightlessSyncshellId.Trim();
            v.LightlessSyncshellPassword = string.IsNullOrWhiteSpace(lightlessSyncshellPassword) ? null : lightlessSyncshellPassword.Trim();
            v.PlayerSyncSyncshellId = string.IsNullOrWhiteSpace(playerSyncSyncshellId) ? null : playerSyncSyncshellId.Trim();
            v.PlayerSyncSyncshellPassword = string.IsNullOrWhiteSpace(playerSyncSyncshellPassword) ? null : playerSyncSyncshellPassword.Trim();
            v.SnowCloakSyncshellId = string.IsNullOrWhiteSpace(snowCloakSyncshellId) ? null : snowCloakSyncshellId.Trim();
            v.SnowCloakSyncshellPassword = string.IsNullOrWhiteSpace(snowCloakSyncshellPassword) ? null : snowCloakSyncshellPassword.Trim();

            var charId = identityService.GetCharacterId() ?? string.Empty;
            var charName = identityService.GetCharacterName() ?? string.Empty;
            var tokenWorld = identityService.GetWorldName() ?? v.World ?? string.Empty;

            v.Owner = string.IsNullOrEmpty(charId) ? charName : charId;

            if (string.IsNullOrWhiteSpace(confirmWorld) ||
                !string.Equals(confirmWorld.Trim(), tokenWorld.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                statusMessage = "Please confirm the selected world before submitting.";
                return;
            }

            // Validate Wi-Fi sections
            if (!ValidateWifi(out var wifiErrors))
            {
                statusMessage = string.Join(" ", wifiErrors);
                return;
            }

            var (tokenOk, tokenStr, tokenStatus, tokenBody) =
                await venueService.RequestTokenAsync(charId, charName, tokenWorld);

            if (!tokenOk || string.IsNullOrEmpty(tokenStr))
            {
                var body = (tokenBody ?? string.Empty).Replace("\r", "").Replace("\n", " ").Trim();
                if (body.Length > 300) body = body[..300] + "...";
                statusMessage = $"Token request failed ({tokenStatus}): {body}";
                return;
            }

            var (ok, statusCode, respBody) =
                await venueService.SubmitVenueAsync(v, tokenStr);

            if (!ok)
            {
                var body = (respBody ?? string.Empty).Replace("\r", "").Replace("\n", " ").Trim();
                if (body.Length > 300) body = body[..300] + "...";
                statusMessage = $"Submit failed ({statusCode}): {body}";
                return;
            }

            statusMessage = "Venue submitted successfully.";
            await plugin.RequestVenueRefreshAsync();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Error while submitting venue");
            statusMessage = "An error occurred while submitting the venue.";
        }
        finally
        {
            submitting = false;
        }
    }
}
