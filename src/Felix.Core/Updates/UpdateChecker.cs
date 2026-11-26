using System.Text.Json;

namespace Felix.Core.Updates;

public sealed record LauncherUpdate(string Tag, Version Version);

public static class VersionUtil
{
    /// <summary>Lit « v1.2.3 », « 1.2.3-beta.1 » ou « 2 » comme une version.</summary>
    public static bool TryParseTag(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            s = s[..cut];
        if (!s.Contains('.'))
            s += ".0";

        if (!Version.TryParse(s, out var parsed))
            return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>Ramène une version à 3 chiffres (1.2.3.0 devient 1.2.3), pour comparer sans surprise.</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}

/// <summary>
/// Vérifie s'il existe une release du launcher plus récente. API GitHub d'abord,
/// puis la redirection de « /releases/latest » si l'API est limitée ou en panne.
/// Ne lève jamais d'erreur : en cas de problème, il n'y a simplement pas de mise à jour proposée.
/// </summary>
public sealed class UpdateChecker
{
    private readonly HttpClient _http;
    private readonly HttpClient _noRedirectHttp;
    private readonly string _owner;
    private readonly string _repository;

    public UpdateChecker(HttpClient http, HttpClient noRedirectHttp, string owner, string repository)
    {
        _http = http;
        _noRedirectHttp = noRedirectHttp;
        _owner = owner;
        _repository = repository;
    }

    public async Task<LauncherUpdate?> CheckAsync(Version current, CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct);
        return latest is not null && latest.Version > VersionUtil.Normalize(current) ? latest : null;
    }

    public async Task<LauncherUpdate?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            return await ViaApiAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // on essaie le plan B
        }

        try
        {
            return await ViaRedirectAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<LauncherUpdate?> ViaApiAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "";
        return VersionUtil.TryParseTag(tag, out var version) ? new LauncherUpdate(tag, version) : null;
    }

    private async Task<LauncherUpdate?> ViaRedirectAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://github.com/{_owner}/{_repository}/releases/latest");
        using var response = await _noRedirectHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var status = (int)response.StatusCode;
        var location = response.Headers.Location?.OriginalString;
        if (status is not (301 or 302 or 303 or 307 or 308) || location is null || !location.Contains("/tag/"))
            return null;

        var tag = Uri.UnescapeDataString(location.TrimEnd('/').Split('/')[^1]);
        return VersionUtil.TryParseTag(tag, out var version) ? new LauncherUpdate(tag, version) : null;
    }
}
