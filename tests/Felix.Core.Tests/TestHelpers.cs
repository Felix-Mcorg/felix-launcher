using System.Net;

namespace Felix.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "felix-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* sans importance */ }
    }
}

/// <summary>Faux serveur HTTP : renvoie le contenu associé à l'adresse demandée.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _responses;
    public int Calls { get; private set; }

    public StubHandler(Dictionary<string, byte[]> responses) => _responses = responses;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var response = _responses.TryGetValue(request.RequestUri!.ToString(), out var data)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }
}

/// <summary>Faux serveur HTTP piloté par une fonction (pour renvoyer des erreurs, des redirections...).</summary>
internal sealed class FuncHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(_handler(request));
}

/// <summary>Simule l'absence de connexion internet.</summary>
internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throw new HttpRequestException("hors ligne");
}
