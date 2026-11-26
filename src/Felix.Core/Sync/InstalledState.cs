using System.Text.Json;
using Felix.Core.Models;

namespace Felix.Core.Sync;

public sealed class InstalledEntry
{
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteTicks { get; set; }
}

/// <summary>
/// Mémoire du launcher : fichiers « sync » qu'il a installés (pour les supprimer s'ils sont retirés
/// du modpack) et leur empreinte (pour éviter de recalculer le hash de gros fichiers à chaque lancement).
/// </summary>
public sealed class InstalledState
{
    public Dictionary<string, InstalledEntry> Files { get; set; } = new();
}

public static class InstalledStateStore
{
    public static string PathFor(string gameDir) => Path.Combine(gameDir, ".felix", "installed.json");

    public static async Task<InstalledState> LoadAsync(string gameDir, CancellationToken ct = default)
    {
        var path = PathFor(gameDir);
        if (!File.Exists(path))
            return new InstalledState();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<InstalledState>(stream, ManifestJson.Options, ct)
                   ?? new InstalledState();
        }
        catch (JsonException)
        {
            return new InstalledState(); // fichier abîmé : on repart de zéro
        }
    }

    public static async Task SaveAsync(string gameDir, InstalledState state, CancellationToken ct = default)
    {
        var path = PathFor(gameDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, state, ManifestJson.Options, ct);
        File.Move(temp, path, overwrite: true);
    }
}
