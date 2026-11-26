using System.Net;
using System.Net.Http.Headers;
using Felix.Core.Hashing;

namespace Felix.Core.Net;

public readonly record struct DownloadProgress(long BytesDone, long BytesTotal);

/// <summary>
/// Téléchargement d'un fichier en flux : reprise (.part), nouvelles tentatives,
/// abandon d'une connexion bloquée, vérification SHA-256, écriture finale atomique.
/// </summary>
public sealed class FileDownloader
{
    /// <summary>Si plus aucun octet n'arrive pendant ce délai, la tentative est abandonnée puis reprise là où elle s'était arrêtée.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly TimeSpan _stallTimeout;

    public FileDownloader(HttpClient http, TimeSpan? stallTimeout = null)
    {
        _http = http;
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        string? expectedSha256,
        IProgress<DownloadProgress>? progress = null,
        int maxAttempts = 3,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var part = destination + ".part";
        var canVerify = !string.IsNullOrEmpty(expectedSha256);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, progress, canVerify, ct);

                if (canVerify)
                {
                    var actual = await Sha256Helper.ComputeAsync(part, ct);
                    if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(part); // fichier corrompu ou périmé : on repart de zéro
                        throw new InvalidDataException(
                            $"Empreinte (SHA-256) invalide pour {Path.GetFileName(destination)}.");
                    }
                }

                File.Move(part, destination, overwrite: true);
                return;
            }
            // Seule une annulation demandée par le joueur arrête tout. Un délai dépassé
            // (connexion bloquée) est une erreur réseau comme une autre : on réessaie.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(1.5 * attempt), ct);
            }
        }

        throw new IOException(
            $"Échec du téléchargement après {maxAttempts} tentative(s) : {url} ({Describe(lastError)})", lastError);
    }

    private static string Describe(Exception? ex) => ex switch
    {
        null => "erreur inconnue",
        OperationCanceledException => "connexion bloquée, aucune donnée reçue",
        _ => ex.Message,
    };

    private async Task DownloadOnceAsync(
        string url, string part, IProgress<DownloadProgress>? progress, bool canVerify, CancellationToken ct)
    {
        // Ce jeton s'annule tout seul si le serveur ne répond plus ; il est relancé à chaque lecture réussie.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(_stallTimeout);

        long resumeFrom = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Le fichier partiel est déjà complet : l'empreinte le confirmera.
            // Sans empreinte on ne peut pas le savoir : on repart de zéro à la tentative suivante.
            if (canVerify)
                return;
            File.Delete(part);
            throw new IOException("Fichier partiel inutilisable, nouvelle tentative depuis zéro.");
        }

        response.EnsureSuccessStatusCode();

        // Le serveur ne gère pas la reprise : on repart de zéro.
        var resumed = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            resumeFrom = 0;

        long? contentLength = response.Content.Headers.ContentLength;
        long total = contentLength.HasValue ? resumeFrom + contentLength.Value : 0;

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var target = new FileStream(
            part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long done = resumeFrom;
        int read;
        while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
        {
            stall.CancelAfter(_stallTimeout);
            await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
            done += read;
            progress?.Report(new DownloadProgress(done, total));
        }
    }
}
