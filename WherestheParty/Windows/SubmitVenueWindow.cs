using System;
using System.Numerics;
using System.Text.Json;
using System.Net.Http;
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
    // One HttpClient is enough — anything else and the sockets go OVER 9000
    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    // simple HEAD check to see if the URL actually responds
    // Used to validate user-provided external URLs (e.g. Carrd) without downloading the body.
    private async Task<bool> UrlExistsAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            return (int)response.StatusCode < 400;
        }
        catch
        {
            return false;
        }
    }

    // Parse common server JSON error shapes into a concise, readable message.
    // Looks for fields like `error`, `message`, `retryAfterSeconds`, `conflictOwner`, `conflictId`.
    // Falls back to a trimmed raw body when parsing fails.
    private static string FormatServerErrorDetails(int statusCode, string? respBody)
    {
        var body = respBody ?? string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string message = string.Empty;
            if (root.TryGetProperty("error", out var err))
                message = err.GetString() ?? string.Empty;
            else if (root.TryGetProperty("message", out var msg))
                message = msg.GetString() ?? string.Empty;
            else
                message = body.Trim();

            var extras = new List<string>();

            if (root.TryGetProperty("retryAfterSeconds", out var retryEl) && retryEl.ValueKind == JsonValueKind.Number)
            {
                var secs = retryEl.GetInt32();
                if (secs >= 60)
                {
                    var mins = (int)Math.Ceiling(secs / 60.0);
                    extras.Add($"Please try again in {mins} {(mins == 1 ? "minute" : "minutes")}");
                }
                else
                {
                    extras.Add($"Please try again in {secs} {(secs == 1 ? "second" : "seconds")}");
                }
            }

            if (root.TryGetProperty("conflictOwner", out var conflictOwner))
            {
                var co = conflictOwner.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(co)) extras.Add($"Conflict: {co}");
            }

            if (root.TryGetProperty("conflictId", out var conflictId))
            {
                var cid = conflictId.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(cid)) extras.Add($"ID: {cid}");
            }

            var detail = message;
            if (extras.Count > 0)
                detail = detail + ". " + string.Join("; ", extras);

            if (string.IsNullOrWhiteSpace(detail))
                return body.Length > 300 ? body[..300] + "..." : body;

            return detail;
        }
        catch
        {
            var trimmed = body.Replace("\r", "").Replace("\n", " ").Trim();
            if (trimmed.Length > 300) trimmed = trimmed[..300] + "...";
            return string.IsNullOrWhiteSpace(trimmed) ? "An unknown error occurred." : trimmed;
        }
    }

    private static readonly string[] hourOptions12 = Enumerable.Range(1, 12).Select(i => i.ToString()).ToArray();
    private static readonly string[] hourOptions24 = Enumerable.Range(0, 24).Select(i => i.ToString("D2")).ToArray();
    private static readonly string[] minuteOptions = Enumerable.Range(0, 12).Select(i => (i * 5).ToString("D2")).ToArray();
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
    private Guid editingId = Guid.Empty;

    // Helpers for minute rounding / conversion.
    // UTC/local conversion and stored minute values stay consistent across the UI.
    private static int RoundMinutesToNearest5(int minute)
    {
        var rm = (int)Math.Round(minute / 5.0) * 5;
        return rm == 60 ? 0 : Math.Clamp(rm, 0, 55);
    }

    // Round a DateTime's minute value to the nearest 5 minutes and adjust hour if rounding rolls over.
    // Returns (hour, minute) suitable for populating the UI controls.
    private static (int hour, int minute) RoundDateTimeToNearest5(DateTime dt)
    {
        var rm = (int)Math.Round(dt.Minute / 5.0) * 5;
        if (rm == 60)
        {
            dt = dt.AddHours(1);
            rm = 0;
        }
        return (Math.Clamp(dt.Hour, 0, 23), Math.Clamp(rm, 0, 55));
    }

    // Convert a total-minute-of-day value into (hour, minute) with minutes rounded to nearest 5.
    // Handles wrap-around when rounding pushes minutes to 60.
    private static (int hour, int minute) FromTotalMinutesRounded(int totalMinutes)
    {
        var h = (totalMinutes / 60) % 24;
        var m = totalMinutes % 60;
        var rm = (int)Math.Round(m / 5.0) * 5;
        if (rm == 60)
        {
            rm = 0;
            h = (h + 1) % 24;
        }
        return (Math.Clamp(h, 0, 23), Math.Clamp(rm, 0, 55));
    }

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
            MaximumSize = new Vector2(800, float.MaxValue)
        };

        selectedDcIndex = dataCenters.IndexOf("Primal");
        selectedDistrictIndex = 0;
        selectedWard = 18;
        selectedPlot = 35;
        world = "Behemoth";
    }

    public void Dispose() { }

    // Load venues from the server and populate the form with the current user's existing listing (if any).
    // Matching prefers the explicit `CharacterId` field returned by the worker, and falls back to
    // matching against the owner/name formats for backwards compatibility.
    public async Task OpenForCurrentUserAsync()
    {
        try
        {
            var charId = identityService.GetCharacterId();
            var charName = identityService.GetCharacterName();

            var list = await venueService.FetchVenuesAsync();
            var existing = list.FirstOrDefault(v =>
                // Prefer explicit CharacterId when available
                (!string.IsNullOrEmpty(v.CharacterId) && !string.IsNullOrEmpty(charId) &&
                    v.CharacterId.Equals(charId, StringComparison.OrdinalIgnoreCase))
                // Fallback: owner string may contain the character name ("Name @ World")
                || (!string.IsNullOrEmpty(v.Owner) && !string.IsNullOrEmpty(charName) &&
                    v.Owner.IndexOf(charName, StringComparison.OrdinalIgnoreCase) >= 0)
                // Also allow legacy owner equal to raw charId or charName
                || (!string.IsNullOrEmpty(v.Owner) && !string.IsNullOrEmpty(charId) &&
                    v.Owner.Equals(charId, StringComparison.OrdinalIgnoreCase))
            );

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

    // Populate the submit form fields from a Venue instance. Prefers UTC opens/closes fields
    // when available and converts them to local time for display. Also records the venue Id
    // in `editingId` so subsequent submits update the same listing.
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

        // Prefer UTC times (they will be converted to the viewer's local time). Fall back to stored local-minute values.
        if (v.OpensAtUtc != default && v.ClosesAtUtc != default)
        {
            try
            {
                var openLocal = v.OpensAtUtc.ToLocalTime();
                var closeLocal = v.ClosesAtUtc.ToLocalTime();

                (openHour, openMinute) = RoundDateTimeToNearest5(openLocal);
                (closeHour, closeMinute) = RoundDateTimeToNearest5(closeLocal);
            }
            catch { }
        }
        else
        {
            (openHour, openMinute) = FromTotalMinutesRounded(v.OpenTimeMinutesLocal);
            (closeHour, closeMinute) = FromTotalMinutesRounded(v.CloseTimeMinutesLocal);
        }

        confirmWorld = string.Empty;
        editingId = v.Id;
    }

    // Reset the submit form to a blank/new state. Clears `editingId` so a new submit will create
    // a fresh listing rather than update an existing one.
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
        editingId = Guid.Empty;
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

                if (selectedApartment == -1 && selectedSubdivision == -1)
                    selectedApartment = 1;
            }
        }

        
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

            
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Unit");

            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);

            var availW = ImGui.GetContentRegionAvail().X;
            var styleLocal = ImGui.GetStyle();
            var gutter = Math.Max(4f, styleLocal.WindowPadding.X);

            var baseWidth = Math.Max(120f, availW - gutter);
            float gridWidth = Math.Min(availW, baseWidth + 5f);

            int columns = 10;
            int current = isSub ? selectedSubdivision : selectedApartment;

            int totalUnits = 90;
            int rows = (int)Math.Ceiling(totalUnits / (float)columns);
            var style2 = ImGui.GetStyle();
            float itemH = ImGui.GetFrameHeight();
            float spacingY = style2.ItemSpacing.Y;
            float childPadY = 1f; 
            float gridHeight = rows * itemH + Math.Max(0, rows - 1) * spacingY + (childPadY * 2f) - 8f;
            if (gridHeight < 100f) gridHeight = 100f;

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

            ImGui.PopStyleVar(2); 
            ImGui.PopStyleVar(); 

            ImGui.EndChild();


            ImGui.PopItemWidth();
        }

        address = ComposeAddress();
    }

    // Compose the readable address string from the selected DC/world/district and plot/apartment selections.
    private string ComposeAddress()
    {
        var dc = dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty;
        var district = housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty;

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
                parts.Add($"W{selectedWard}");
                parts.Add($"P{selectedPlot}");
            }
        }
        else
        {
        
            parts.Add($"W{selectedWard}");
            parts.Add($"P{selectedPlot}");
        }

        return string.Join(" > ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    // Validate Carrd URL by checking if it exists. This is not a guarantee of validity, but it can catch obvious mistakes.
    // Draw the Carrd URL input and kick off async validation checks. The validation is best-effort
    // (HEAD request) and only used to indicate obvious mistakes to the user.
    private async void DrawCarrd()
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Carrd URL");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputText("##CarrdUrl", ref carrdUrl, 256);

        string tmp = carrdUrl?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(tmp) && !Regex.IsMatch(tmp, @"^[a-zA-Z][a-zA-Z0-9+.-]*:"))
            tmp = "https://" + tmp;

        if (lastCarrdChecked != tmp)
        {
            lastCarrdChecked = tmp;
            _ = ValidateCarrdAsync(tmp);
        }

        if (!isCarrdValid)
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f),
                "This URL does not appear to exist.");
        }
    }

    private string lastCarrdChecked = "";
    // Validate that the given URL responds to a HEAD request. Sets `isCarrdValid` used by the UI.
    private async Task ValidateCarrdAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            isCarrdValid = true;
            return;
        }

        isCarrdValid = await UrlExistsAsync(url);
    }

    // Render the schedule controls (open days, open/close times). Uses local variables
    // `openHour/openMinute` and `closeHour/closeMinute` which come from PopulateFromVenue or user input.
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

        
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Open Time");

        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(80f);
        if (configuration.Use24HourClock)
        {
            // 24-hour clock for those who complained about AM/PM.
            var idx = Math.Clamp(openHour, 0, 23);
            if (ImGui.BeginCombo("##OpenHour", hourOptions24[idx]))
            {
                for (int i = 0; i < hourOptions24.Length; i++)
                {
                    bool sel = (i == idx);
                    if (ImGui.Selectable(hourOptions24[i], sel))
                        openHour = i;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            // 12-hour Clock, for everyone else.
            var openHourIndex = (openHour + 11) % 12;
            if (ImGui.BeginCombo("##OpenHour", hourOptions12[openHourIndex]))
            {
                for (int i = 0; i < hourOptions12.Length; i++)
                {
                    bool sel = (i == openHourIndex);
                    if (ImGui.Selectable(hourOptions12[i], sel))
                    {
                        var isPm = openHour >= 12;
                        openHour = (isPm ? 12 : 0) + ((i + 1) % 12);
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();

        
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

        
        if (!configuration.Use24HourClock)
        {
            bool openPm = openHour >= 12;
            if (ImGui.RadioButton("AM##Open", !openPm)) openHour %= 12;
            ImGui.SameLine();
            if (ImGui.RadioButton("PM##Open", openPm)) openHour = (openHour % 12) + 12;
        }

        
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Close Time");

        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(80f);
        if (configuration.Use24HourClock)
        {
            var idx = Math.Clamp(closeHour, 0, 23);
            if (ImGui.BeginCombo("##CloseHour", hourOptions24[idx]))
            {
                for (int i = 0; i < hourOptions24.Length; i++)
                {
                    bool sel = (i == idx);
                    if (ImGui.Selectable(hourOptions24[i], sel))
                        closeHour = i;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            var closeHourIndex = (closeHour + 11) % 12;
            if (ImGui.BeginCombo("##CloseHour", hourOptions12[closeHourIndex]))
            {
                for (int i = 0; i < hourOptions12.Length; i++)
                {
                    bool sel = (i == closeHourIndex);
                    if (ImGui.Selectable(hourOptions12[i], sel))
                    {
                        var isPm = closeHour >= 12;
                        closeHour = (isPm ? 12 : 0) + ((i + 1) % 12);
                    }
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();

        
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

        
        if (!configuration.Use24HourClock)
        {
            bool closePm = closeHour >= 12;
            if (ImGui.RadioButton("AM##Close", !closePm)) closeHour %= 12;
            ImGui.SameLine();
            if (ImGui.RadioButton("PM##Close", closePm)) closeHour = (closeHour % 12) + 12;
        }
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
        ImGui.Text("Length (minutes) 8hrs Max");

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

    // Build a Venue from the form, request a short-lived token, and POST the venue to the server.
    // On success the server id is captured to `editingId` so future submits update the same listing.
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

            // Record absolute UTC times based on the submitter's local system date/time so viewers
            // in other timezones can convert correctly. If the close time is earlier or equal to
            // the open time, assume it crosses midnight and add a day to the close time.
            try
            {
                var todayLocal = DateTime.Today;
                var openLocalDt = DateTime.SpecifyKind(todayLocal.AddMinutes(v.OpenTimeMinutesLocal), DateTimeKind.Local);
                var closeLocalDt = DateTime.SpecifyKind(todayLocal.AddMinutes(v.CloseTimeMinutesLocal), DateTimeKind.Local);
                if (closeLocalDt <= openLocalDt)
                    closeLocalDt = closeLocalDt.AddDays(1);

                v.OpensAtUtc = openLocalDt.ToUniversalTime();
                v.ClosesAtUtc = closeLocalDt.ToUniversalTime();
            }
            catch { }

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

            // Preserve character id for server-side matching and set Id when editing
            v.CharacterId = string.IsNullOrWhiteSpace(charId) ? string.Empty : charId;
            if (editingId != Guid.Empty)
                v.Id = editingId;

            // Owner will be normalized by the server to the form "Name @ World".
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
                statusMessage = $"Token request failed ({tokenStatus}): {FormatServerErrorDetails(tokenStatus, tokenBody)}";
                return;
            }

            var (ok, statusCode, respBody) =
                await venueService.SubmitVenueAsync(v, tokenStr);

            if (!ok)
            {
                statusMessage = $"Submission Failed ({statusCode}): {FormatServerErrorDetails(statusCode, respBody)}";
                return;
            }

            // Try to extract returned id and set editingId so subsequent edits update the same listing
            try
            {
                if (!string.IsNullOrWhiteSpace(respBody))
                {
                    using var doc = JsonDocument.Parse(respBody);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    {
                        var idStr = idEl.GetString();
                        if (Guid.TryParse(idStr, out var gid))
                            editingId = gid;
                    }
                }
            }
            catch { }

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
