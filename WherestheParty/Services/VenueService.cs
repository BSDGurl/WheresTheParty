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

    public IReadOnlyList<Venue> GetCachedVenues() =>
        _cache.Where(v => !v.IsExpired).ToList();

    public async Task<List<Venue>> FetchVenuesAsync()
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return new List<Venue>();

        var resp = await _http.GetAsync(_baseUrl + "/venues");
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            WTP.Log?.Warning($"FetchVenuesAsync: GET {(int)resp.StatusCode} {resp.ReasonPhrase}");
            resp.EnsureSuccessStatusCode();
        }

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Venue>>(json, opts) ?? new List<Venue>();

        var filtered = list.Where(v => !v.IsExpired).ToList();
        _cache.Clear();
        _cache.AddRange(filtered);

        return filtered;
    }

    public async Task<(bool ok, string token, int statusCode, string body)>
        RequestTokenAsync(string characterId, string characterName, string world)
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return (false, string.Empty, 0, "Base URL not configured");

        var payload = new Dictionary<string, string>
        {
            ["characterId"] = characterId ?? string.Empty,
            ["characterName"] = characterName ?? string.Empty,
            ["world"] = world ?? string.Empty,
        };

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(payload, opts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _http.PostAsync(_baseUrl + "/auth", content);
        var respBody = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            WTP.Log?.Warning($"RequestTokenAsync: POST /auth {(int)resp.StatusCode} {resp.ReasonPhrase}");
            return (false, string.Empty, (int)resp.StatusCode, respBody ?? string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(respBody ?? string.Empty);
            if (doc.RootElement.TryGetProperty("token", out var tokEl))
            {
                var token = tokEl.GetString() ?? string.Empty;
                return (true, token, (int)resp.StatusCode, respBody ?? string.Empty);
            }
        }
        catch { }

        return (false, string.Empty, (int)resp.StatusCode, respBody ?? string.Empty);
    }

    public async Task<(bool ok, int statusCode, string body)>
        SubmitVenueAsync(Venue v, string token)
    {
        if (string.IsNullOrEmpty(_baseUrl))
            return (false, 0, "Base URL not configured");

        if (string.IsNullOrEmpty(token))
            return (false, 0, "Missing token");

        if (v.Id == Guid.Empty)
            v.Id = Guid.NewGuid();

        v.LastUpdatedUtc = DateTime.UtcNow;
        v.ExpiresAtUtc = v.LengthMinutes > 0
            ? v.LastUpdatedUtc.AddMinutes(v.LengthMinutes)
            : v.LastUpdatedUtc.AddDays(1);

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var venueJson = JsonSerializer.Serialize(v, opts);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(venueJson)
                   ?? new Dictionary<string, object?>();

        dict["token"] = token;

        var body = JsonSerializer.Serialize(dict, opts);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.PostAsync(_baseUrl + "/venues", content);
        var respBody = await resp.Content.ReadAsStringAsync();

        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, respBody ?? string.Empty);
    }
}
