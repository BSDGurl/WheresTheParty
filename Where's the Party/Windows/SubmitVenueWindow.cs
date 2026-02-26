using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Linq;
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
    private string category = string.Empty;
    private string address = string.Empty;
    private string tags = string.Empty;
    private int lengthMinutes = 60;
    private string statusMessage = string.Empty;
    private bool submitting = false;

    // Wi‑Fi
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
    private bool[] apartmentSelected = new bool[90];
    private bool[] subdivisionSelected = new bool[90];

    // Wi‑Fi UI
    private bool showWifiOptions = false;

    public SubmitVenueWindow(WTP plugin) : base("Submit Venue###SubmitVenueWindow")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(520, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        this.configuration = plugin.Configuration;
        this.venueService = new VenueService(configuration.VenueApiBaseUrl);

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

    public void Dispose() { }

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

        // Description
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Description");
        ImGui.TableSetColumnIndex(1); ImGui.InputTextMultiline("##VenueDescription", ref description, 250, new Vector2(-1, 80));

        // Address (composed)
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Address");
        ImGui.TableSetColumnIndex(1);
        var composed = string.IsNullOrWhiteSpace(address) ? ComposeDisplayAddress() : address;
        ImGui.TextUnformatted(composed);
        ImGui.SameLine();
        if (ImGui.SmallButton(editingAddress ? "Done##Addr" : "Edit##Addr")) editingAddress = !editingAddress;

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
                    ImGui.Checkbox($"{i+1}", ref apartmentSelected[i]);
                    ImGui.NextColumn();
                    ImGui.Checkbox($"{i+1}", ref subdivisionSelected[i]);
                    ImGui.NextColumn();
                    ImGui.PopID();
                }
                ImGui.Columns(1);
                ImGui.EndChild();
            }
        }

        // Category
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Category");
        ImGui.TableSetColumnIndex(1); ImGui.InputText("##Category", ref category, 32);

        // Tags
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Tags");
        ImGui.TableSetColumnIndex(1); ImGui.InputText("##Tags", ref tags, 64);

        // Length
        ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Length (minutes)");
        ImGui.TableSetColumnIndex(1); ImGui.InputInt("##LengthMinutes", ref lengthMinutes);

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
        if (submitting) ImGui.TextUnformatted(statusMessage);
        else if (ImGui.Button("Submit")) { submitting = true; statusMessage = "Submitting..."; _ = SubmitAsync(); }

        ImGui.EndTable();
    }

    private string ComposeDisplayAddress()
    {
        var district = GetDistrictDisplay(housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty);
        if (useApartment)
        {
            var apts = Enumerable.Range(1, 90).Where(i => apartmentSelected[i - 1]).Select(i => i.ToString()).ToList();
            var subs = Enumerable.Range(1, 90).Where(i => subdivisionSelected[i - 1]).Select(i => i.ToString()).ToList();
            var parts = new List<string> { dataCenters.ElementAtOrDefault(selectedDcIndex) ?? string.Empty, world, district, $"W{selectedWard}" };
            if (apts.Count > 0) parts.Add($"Apt(s) {string.Join(',', apts)}");
            if (subs.Count > 0) parts.Add($"Subdiv(s) {string.Join(',', subs)}");
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
        if (useApartment)
        {
            var apts = Enumerable.Range(1, 90).Where(i => apartmentSelected[i - 1]).Select(i => i.ToString()).ToList();
            var subs = Enumerable.Range(1, 90).Where(i => subdivisionSelected[i - 1]).Select(i => i.ToString()).ToList();
            var parts = new List<string> { world, housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty, $"Ward {selectedWard}" };
            if (apts.Count > 0) parts.Add($"Apt(s) {string.Join(',', apts)}");
            if (subs.Count > 0) parts.Add($"Subdiv(s) {string.Join(',', subs)}");
            return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        return usePlot
            ? $"{world}, {housingDistricts.ElementAtOrDefault(selectedDistrictIndex) ?? string.Empty}, Ward {selectedWard}, Plot {selectedPlot}"
            : address;
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
                Category = category,
                Tags = tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList(),
                Description = description,
                Owner = string.Empty,
                LastUpdatedUtc = DateTime.UtcNow,
                LengthMinutes = Math.Max(1, lengthMinutes),
                OpensAtUtc = DateTime.UtcNow,
                ClosesAtUtc = DateTime.UtcNow.AddHours(1)
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

            var ok = await venueService.SubmitVenueAsync(v);
            statusMessage = ok ? "Submitted successfully." : "Submission failed.";
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
