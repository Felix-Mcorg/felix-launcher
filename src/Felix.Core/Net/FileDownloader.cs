using System.Net;
using System.Net.Http.Headers;
using Felix.Core.Hashing;

namespace Felix.Core.Net;

public readonly record struct DownloadProgress(long BytesDone, long BytesTotal);

/// <summary>
/// Téléchargement d'un fichier en flux : reprise (.part), nouvelles tentatives,
/// vérification SHA-256, écriture finale atomique.
/// </summary>
public sealed class FileDownloader
{
    private readonly HttpClient _http;

    public FileDownloader(HttpClient http) => _http = http;

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
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, part, progress, ct);

                if (!string.IsNullOrEmpty(expectedSha256))
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
            catch (OperationCanceledException)
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

        throw new IOException($"Échec du téléchargement après {maxAttempts} tentative(s) : {url}", lastError);
    }

    private async Task DownloadOnceAsync(
        string url, string part, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long resumeFrom = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // 416 : le fichier partiel est déjà complet, il reste à vérifier son empreinte.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return;

        response.EnsureSuccessStatusCode();

        // Le serveur ne gère pas la reprise : on repart de zéro.
        var resumed = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            resumeFrom = 0;

        long? contentLength = response.Content.Headers.ContentLength;
        long total = contentLength.HasValue ? resumeFrom + contentLength.Value : 0;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(
            part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long done = resumeFrom;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report(new DownloadProgress(done, total));
        }
    }
}
