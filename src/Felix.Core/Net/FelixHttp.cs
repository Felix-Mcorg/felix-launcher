namespace Felix.Core.Net;

public static class FelixHttp
{
    /// <summary>
    /// Crée le HttpClient à partager dans toute l'application (un seul, réutilisé).
    /// </summary>
    public static HttpClient CreateClient(string? appVersion = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FelixLauncher/{appVersion ?? "dev"}");
        return client;
    }
}
