using System.Net.Http.Headers;
using Felix.Core.Models;

namespace Felix.Core.Net;

public sealed class ManifestClient
{
    private readonly HttpClient _http;

    public ManifestClient(HttpClient http) => _http = http;

    public async Task<Manifest> FetchAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ManifestJson.Parse(json);
    }
}
