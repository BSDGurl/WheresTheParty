using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WTP.Models;

namespace WTP.Services;

public class VenueService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly List<Venue> _cache = new();

    public VenueService(string baseUrl)
    {
        _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
        _http = new HttpClient();
    }

    public IReadOnlyList<Venue> GetCachedVenues()
    {
        return _cache.Where(v => !v.IsExpired).ToList();
    }

    public async Task<List<Venue>> FetchVenuesAsync()
    {
        if (string.IsNullOrEmpty(_baseUrl)) return new List<Venue>();

        var resp = await _http.GetAsync(_baseUrl + "/venues");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Venue>>(json, opts) ?? new List<Venue>();

        // filter expired entries client-side
        var filtered = list.Where(v => !v.IsExpired).ToList();
        _cache.Clear();
        _cache.AddRange(filtered);
        return filtered;
    }

    public async Task<bool> SubmitVenueAsync(Venue v)
    {
        if (string.IsNullOrEmpty(_baseUrl)) return false;

        if (v.Id == Guid.Empty) v.Id = Guid.NewGuid();
        v.LastUpdatedUtc = DateTime.UtcNow;
        if (v.LengthMinutes > 0) v.ExpiresAtUtc = v.LastUpdatedUtc.AddMinutes(v.LengthMinutes);
        else v.ExpiresAtUtc = v.LastUpdatedUtc.AddDays(1);

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var body = JsonSerializer.Serialize(v, opts);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(_baseUrl + "/venues", content);
        return resp.IsSuccessStatusCode;
    }
}
