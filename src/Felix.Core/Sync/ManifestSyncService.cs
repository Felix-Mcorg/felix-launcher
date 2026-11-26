using System.Collections.Concurrent;
using System.Diagnostics;
using Felix.Core.Hashing;
using Felix.Core.IO;
using Felix.Core.Models;
using Felix.Core.Net;

namespace Felix.Core.Sync;

public sealed record SyncPlan(
    IReadOnlyList<ManifestFile> ToDownload,
    IReadOnlyList<ManifestFile> UpToDate,
    IReadOnlyList<string> ToDelete)
{
    public long BytesToDownload => ToDownload.Sum(f => f.Size);
    public bool IsEmpty => ToDownload.Count == 0 && ToDelete.Count == 0;
}

public sealed record SyncProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentFile);

public sealed record SyncFailure(string Path, string Message);

public sealed record SyncResult(int Downloaded, int Deleted, IReadOnlyList<SyncFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}

/// <summary>
/// Compare le manifest au dossier du jeu, puis télécharge uniquement ce qui manque ou a changé.
/// Étape 1 : PlanAsync (rapide, ne télécharge rien). Étape 2 : ApplyAsync.
/// </summary>
public sealed class ManifestSyncService
{
    private readonly FileDownloader _downloader;

    public ManifestSyncService(FileDownloader downloader) => _downloader = downloader;

    public async Task<SyncPlan> PlanAsync(Manifest manifest, string gameDir, CancellationToken ct = default)
    {
        var state = await InstalledStateStore.LoadAsync(gameDir, ct);
        var toDownload = new List<ManifestFile>();
        var upToDate = new List<ManifestFile>();

        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(SafePath.Resolve(gameDir, file.Path));

            if (!info.Exists)
                toDownload.Add(file);
            else if (file.Mode == SyncMode.Default)
                upToDate.Add(file); // existe déjà : on ne touche pas aux choix du joueur
            else if (await IsCurrentAsync(file, info, state, ct))
                upToDate.Add(file);
            else
                toDownload.Add(file);
        }

        var wanted = manifest.Files
            .Where(f => f.Mode == SyncMode.Sync)
            .Select(f => Normalize(f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toDelete = state.Files.Keys.Where(p => !wanted.Contains(p)).ToList();

        return new SyncPlan(toDownload, upToDate, toDelete);
    }

    public async Task<SyncResult> ApplyAsync(
        Manifest manifest,
        string gameDir,
        SyncPlan plan,
        IProgress<SyncProgress>? progress = null,
        int parallelism = 4,
        CancellationToken ct = default)
    {
        var state = await InstalledStateStore.LoadAsync(gameDir, ct);
        var stateLock = new object();

        var deleted = 0;
        foreach (var relative in plan.ToDelete)
        {
            var path = SafePath.Resolve(gameDir, relative);
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted++;
            }
            state.Files.Remove(relative);
        }

        foreach (var file in plan.UpToDate.Where(f => f.Mode == SyncMode.Sync))
            Record(state, gameDir, file);

        var failures = new ConcurrentBag<SyncFailure>();
        var perFileBytes = new ConcurrentDictionary<string, long>();
        var filesTotal = plan.ToDownload.Count;
        var totalBytes = plan.BytesToDownload;
        long bytesDone = 0;
        var filesDone = 0;
        long lastReport = 0;

        // Un événement par bloc de 80 Ko noyait l'interface : on limite à ~10 mises à jour par seconde.
        bool ShouldReport()
        {
            var now = Stopwatch.GetTimestamp();
            var last = Volatile.Read(ref lastReport);
            if (Stopwatch.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(100))
                return false;
            return Interlocked.CompareExchange(ref lastReport, now, last) == last;
        }

        progress?.Report(new SyncProgress(0, filesTotal, 0, totalBytes, null));

        try
        {
            await Parallel.ForEachAsync(
                plan.ToDownload,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct },
                async (file, token) =>
                {
                    var destination = SafePath.Resolve(gameDir, file.Path);
                    var reporter = new InlineProgress<DownloadProgress>(p =>
                    {
                        var previous = perFileBytes.GetOrAdd(file.Path, 0);
                        perFileBytes[file.Path] = p.BytesDone;
                        var now = Interlocked.Add(ref bytesDone, p.BytesDone - previous);
                        if (!ShouldReport())
                            return;
                        progress?.Report(new SyncProgress(
                            Volatile.Read(ref filesDone), filesTotal, now, totalBytes, file.Path));
                    });

                    try
                    {
                        await _downloader.DownloadAsync(
                            file.Url, destination, file.Sha256, reporter, ct: token);

                        if (file.Mode == SyncMode.Sync)
                        {
                            lock (stateLock)
                                Record(state, gameDir, file);
                        }

                        var done = Interlocked.Increment(ref filesDone);
                        progress?.Report(new SyncProgress(
                            done, filesTotal, Interlocked.Read(ref bytesDone), totalBytes, file.Path));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw; // annulation voulue par le joueur uniquement
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new SyncFailure(file.Path, ex.Message));
                    }
                });
        }
        finally
        {
            // On enregistre même en cas d'annulation : ce qui est déjà installé n'est pas perdu.
            await InstalledStateStore.SaveAsync(gameDir, state, CancellationToken.None);
        }

        return new SyncResult(filesTotal - failures.Count, deleted, failures.ToList());
    }

    private static async Task<bool> IsCurrentAsync(
        ManifestFile file, FileInfo info, InstalledState state, CancellationToken ct)
    {
        if (file.Size > 0 && info.Length != file.Size)
            return false;
        if (string.IsNullOrEmpty(file.Sha256))
            return true;

        // Même taille et même date que la dernière fois : on fait confiance, sans recalculer le hash.
        if (state.Files.TryGetValue(Normalize(file.Path), out var entry)
            && entry.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)
            && entry.Size == info.Length
            && entry.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
            return true;

        var actual = await Sha256Helper.ComputeAsync(info.FullName, ct);
        return actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void Record(InstalledState state, string gameDir, ManifestFile file)
    {
        var info = new FileInfo(SafePath.Resolve(gameDir, file.Path));
        state.Files[Normalize(file.Path)] = new InstalledEntry
        {
            Sha256 = file.Sha256.ToLowerInvariant(),
            Size = info.Length,
            LastWriteTicks = info.LastWriteTimeUtc.Ticks,
        };
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public InlineProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
