using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using WTP.Models;

namespace WTP.Windows;

internal static class ScheduleRenderer
{
    public static void RenderSchedule(Venue v)
    {
        try
        {
            if ((v.OpenDaysMask != 0) || (v.OpensAtUtc != default && v.ClosesAtUtc != default))
            {
                // Build day list from mask if available
                string daysText = string.Empty;
                if (v.OpenDaysMask != 0)
                {
                    var parts = new List<string>();
                    for (var di = 0; di < 7; ++di)
                    {
                        if ((v.OpenDaysMask & (1 << di)) != 0) parts.Add(UiHelpers.DayNames[di]);
                    }
                    daysText = parts.Count > 0 ? string.Join(',', parts) + " " : string.Empty;
                }

                // Show time range in viewer local time if UTC times are present, otherwise use stored local minutes as fallback
                string timeText = string.Empty;
                var cfg = WTP.PluginInterface.GetPluginConfig() as Configuration;
                var use24 = cfg?.Use24HourClock ?? false;
                var fmt = use24 ? "HH:mm" : "h:mm tt";

                if (v.OpensAtUtc != default && v.ClosesAtUtc != default)
                {
                    var openLocal = v.OpensAtUtc.ToLocalTime();
                    var closeLocal = v.ClosesAtUtc.ToLocalTime();
                    timeText = $"{openLocal.ToString(fmt)} - {closeLocal.ToString(fmt)}";
                }
                else
                {
                    var openMinutes = Math.Max(0, v.OpenTimeMinutesLocal % (24 * 60));
                    var closeMinutes = Math.Max(0, v.CloseTimeMinutesLocal % (24 * 60));
                    var openDt = DateTime.Today.AddMinutes(openMinutes);
                    var closeDt = DateTime.Today.AddMinutes(closeMinutes);
                    timeText = $"{openDt.ToString(fmt)} - {closeDt.ToString(fmt)}";
                }

                ImGui.Separator();
                ImGui.TextUnformatted($"Schedule: {daysText}{timeText}");
            }
        }
        catch { }
    }
}
