using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace WTP.Windows;

internal static class UiHelpers
{
    public static readonly int[] MinuteOptions = new[] { 0, 15, 30, 45 };
    public static readonly string[] DayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    public const float ComboHourWidth = 52f;
    public const float ComboMinuteWidth = 48f;

    public static string FormatHourLabel(int h)
    {
        var hour12 = h % 12; if (hour12 == 0) hour12 = 12;
        var ampm = h < 12 ? "AM" : "PM";
        return $"{hour12} {ampm}";
    }

    public static string FormatTimeLabel(int h, int m)
    {
        var hour12 = h % 12; if (hour12 == 0) hour12 = 12;
        var ampm = h < 12 ? "AM" : "PM";
        return $"{hour12}:{m:D2} {ampm}";
    }

    public static (string aptTitle, string subdivTitle) GetTitlesForDistrictIndex(int idx) => idx switch
    {
        0 => ("Lily Hills", "Lily Hills Sub"), 1 => ("Topmast", "Topmast Sub"), 2 => ("Sultana's Breath", "Sultana's Breath Sub"),
        3 => ("Ingleside", "Ingleside Sub"), 4 => ("Kobai Goten", "Kobai Goten Sub"), _ => (string.Empty, string.Empty)
    };

    public static (string aptTitle, string subdivTitle) GetTitlesForDistrictName(string districtName)
    {
        if (string.IsNullOrEmpty(districtName)) return (string.Empty, string.Empty);
        return districtName switch
        {
            "Lavender Beds" => ("Lily Hills", "Lily Hills Sub"), "Mist" => ("Topmast", "Topmast Sub"),
            "The Goblet" => ("Sultana's Breath", "Sultana's Breath Sub"), "Empyreum" => ("Ingleside", "Ingleside Sub"),
            "Shirogane" => ("Kobai Goten", "Kobai Goten Sub"), _ => (string.Empty, string.Empty)
        };
    }

    public static string GetDistrictDisplay(string district) => string.IsNullOrEmpty(district) ? district : (district == "Lavender Beds" ? "Lav Beds" : district);

    public static int RoundToNearestIncrement(int totalMinutes)
    {
        var clamped = Math.Max(0, Math.Min(totalMinutes, 24 * 60 - 1));
        var rounded = (int)Math.Round(clamped / 15.0) * 15;
        return rounded >= 24 * 60 ? 24 * 60 - 1 : rounded;
    }

    public static bool IconButton(string id, string icon = "\u21BB", float width = 28f)
    {
        ImGui.SetNextItemWidth(width);
        var label = icon + "##" + id;
        return ImGui.Button(label, new Vector2(width, 0));
    }
}
