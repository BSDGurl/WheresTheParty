using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WTP.Windows;

internal static class AddressParser
{
    public sealed class Result
    {
        public int SelectedDistrictIndex { get; set; } = 0;
        public int SelectedApartment { get; set; } = -1;
        public int SelectedSubdivision { get; set; } = -1;
        public bool UseApartment { get; set; } = false;
        public bool UsePlot { get; set; } = true;
        public int SelectedWard { get; set; } = 1;
        public int SelectedPlot { get; set; } = 1;
    }

    // Parse a stored address that may be in the composed display format or a few legacy variants
    public static Result Parse(string addr, List<string> housingDistricts)
    {
        var res = new Result();
        if (string.IsNullOrWhiteSpace(addr)) return res;

        var trimmed = addr.Trim();
        var parts = trimmed.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
        if (parts.Count >= 3)
        {
            var districtName = parts[2];
            var didx = housingDistricts.FindIndex(d => string.Equals(d, districtName, StringComparison.OrdinalIgnoreCase) || string.Equals(UiHelpers.GetDistrictDisplay(d), districtName, StringComparison.OrdinalIgnoreCase));
            if (didx >= 0) res.SelectedDistrictIndex = didx;

            if (parts.Count >= 4)
            {
                var fourth = parts[3];
                var titles = UiHelpers.GetTitlesForDistrictIndex(res.SelectedDistrictIndex);
                var aptTitle = titles.aptTitle ?? string.Empty;
                var subdivTitle = titles.subdivTitle ?? string.Empty;

                var m = Regex.Match(fourth, "#\\s*(\\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var num))
                {
                    if (!string.IsNullOrEmpty(subdivTitle) && fourth.StartsWith(subdivTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        res.SelectedSubdivision = num; res.UseApartment = true; res.UsePlot = false;
                    }
                    else if (!string.IsNullOrEmpty(aptTitle) && fourth.StartsWith(aptTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        res.SelectedApartment = num; res.UseApartment = true; res.UsePlot = false;
                    }
                    else
                    {
                        if (fourth.IndexOf("subd", StringComparison.OrdinalIgnoreCase) >= 0 || fourth.IndexOf("sub", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            res.SelectedSubdivision = num; res.UseApartment = true; res.UsePlot = false;
                        }
                        else
                        {
                            res.SelectedApartment = num; res.UseApartment = true; res.UsePlot = false;
                        }
                    }
                }
                else
                {
                    var wardMatch = Regex.Match(fourth, "W\\s*(\\d+)", RegexOptions.IgnoreCase);
                    if (wardMatch.Success && int.TryParse(wardMatch.Groups[1].Value, out var wv)) res.SelectedWard = wv;
                    if (parts.Count >= 5)
                    {
                        var fifth = parts[4];
                        var plotMatch = Regex.Match(fifth, "P\\s*(\\d+)", RegexOptions.IgnoreCase);
                        if (plotMatch.Success && int.TryParse(plotMatch.Groups[1].Value, out var pv)) res.SelectedPlot = pv;
                    }
                }
            }
        }
        else
        {
            var mApt = Regex.Match(trimmed, "Apt(?:s)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (mApt.Success && int.TryParse(mApt.Groups[1].Value, out var an)) { res.SelectedApartment = an; res.UseApartment = true; res.UsePlot = false; }
            var mSub = Regex.Match(trimmed, "Subdiv(?:ision)?\\s*#?\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (mSub.Success && int.TryParse(mSub.Groups[1].Value, out var sn)) { res.SelectedSubdivision = sn; res.UseApartment = true; res.UsePlot = false; }
        }

        return res;
    }
}
