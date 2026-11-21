using System.Diagnostics;
using Felix.Core.Accounts;
using Felix.Core.Models;
using Felix.Core.Net;
using Felix.Core.Sync;
using Felix.Core.Util;

namespace Felix.Core.Play;

public enum PlayStep { Manifest = 1, Minecraft = 2, Files = 3, Launch = 4 }

public sealed record PlayProgress(PlayStep Step, string Message, double? Fraction = null, string? Detail = null);

public sealed record PlayOptions(string ManifestUrl, string GameDir, string Username, int RamMb);

public sealed record LaunchRequest(string GameDir, string VersionId, Account Account, int RamMb, string? ServerAddress);

/// <summary>Erreur dont le message peut être montré tel quel au joueur.</summary>
public sealed class PlayException : Exception
{
    public PlayException(string message) : base(message) { }
}

/// <summary>
/// Ce que l'application demande à « Minecraft » : l'installer (jeu, Java, Forge...) puis le lancer.
/// Séparé du reste pour que la bibliothèque de lancement (CmlLib) reste remplaçable.
/// </summary>
public interface IMinecraftRuntime
{
    /// <summary>Installe Minecraft et son loader si besoin, retourne l'identifiant de version à lancer.</summary>
    Task<string> EnsureInstalledAsync(MinecraftInfo minecraft, string gameDir, IProgress<PlayProgress> progress, CancellationToken ct);

    /// <summary>Lance le jeu, renvoie sa sortie ligne par ligne, retourne le code de sortie quand il se ferme.</summary>
    Task<int> LaunchAsync(LaunchRequest request, Action<string> onOutput, CancellationToken ct);
}

/// <summary>Le déroulé complet du bouton « Jouer ».</summary>
public sealed class PlayService
{
    private readonly ManifestClient _manifests;
    private readonly ManifestSyncService _sync;
    private readonly IMinecraftRuntime _runtime;

    public PlayService(ManifestClient manifests, ManifestSyncService sync, IMinecraftRuntime runtime)
    {
        _manifests = manifests;
        _sync = sync;
        _runtime = runtime;
    }

    public async Task<int> PlayAsync(
        PlayOptions options,
        IProgress<PlayProgress> progress,
        Action<string> onGameOutput,
        CancellationToken ct = default)
    {
        if (!OfflineAccount.IsValidUsername(options.Username))
            throw new PlayException("Pseudo invalide : 3 à 16 caractères (lettres, chiffres, _).");
        var account = OfflineAccount.Create(options.Username);

        progress.Report(new PlayProgress(PlayStep.Manifest, "Lecture de la liste du modpack…"));
        Manifest manifest;
        try
        {
            manifest = await _manifests.FetchAsync(options.ManifestUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidDataException
                                       or System.Text.Json.JsonException)
        {
            throw new PlayException($"Impossible de lire la liste du modpack : {ex.Message}");
        }

        progress.Report(new PlayProgress(PlayStep.Minecraft, $"Minecraft {manifest.Minecraft.Version}…"));
        var versionId = await _runtime.EnsureInstalledAsync(manifest.Minecraft, options.GameDir, progress, ct);

        progress.Report(new PlayProgress(PlayStep.Files, "Vérification des fichiers du modpack…"));
        var plan = await _sync.PlanAsync(manifest, options.GameDir, ct);
        var clock = Stopwatch.StartNew();
        var syncProgress = new InlineProgress<SyncProgress>(p =>
        {
            var seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
            var speed = p.BytesDone / seconds;
            double? remaining = p.BytesTotal > 0 && speed > 0 ? Math.Max(p.BytesTotal - p.BytesDone, 0) / speed : null;
            var fraction = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal
                : p.FilesTotal > 0 ? (double)p.FilesDone / p.FilesTotal : 1.0;
            progress.Report(new PlayProgress(
                PlayStep.Files,
                "Téléchargement des fichiers du modpack…",
                Math.Clamp(fraction, 0, 1),
                $"{p.FilesDone}/{p.FilesTotal} fichiers · {ByteSize.Format(p.BytesDone)} / {ByteSize.Format(p.BytesTotal)}"
                + $" · {ByteSize.Format(speed)}/s · reste {ByteSize.FormatDuration(remaining)}"));
        });

        var result = await _sync.ApplyAsync(manifest, options.GameDir, plan, syncProgress, ct: ct);
        if (!result.Success)
        {
            var names = string.Join(", ", result.Failures.Take(5).Select(f => f.Path));
            throw new PlayException(
                $"{result.Failures.Count} fichier(s) n'ont pas pu être téléchargés ({names}). Vérifie ta connexion puis réessaie.");
        }

        progress.Report(new PlayProgress(PlayStep.Launch, "Lancement du jeu…", 1.0));
        var server = manifest.Server is { AutoConnect: true, Address.Length: > 0 } ? manifest.Server.Address : null;
        var request = new LaunchRequest(options.GameDir, versionId, account, options.RamMb, server);
        return await _runtime.LaunchAsync(request, onGameOutput, ct);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public InlineProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
