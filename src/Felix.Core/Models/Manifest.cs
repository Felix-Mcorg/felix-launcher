using System.Text.Json;
using System.Text.Json.Serialization;

namespace Felix.Core.Models;

/// <summary>Comment un fichier est géré chez le joueur.</summary>
public enum SyncMode
{
    /// <summary>Toujours identique à la version du modpack (remplacé s'il change, supprimé s'il est retiré).</summary>
    Sync,

    /// <summary>Copié seulement s'il n'existe pas ; le joueur peut ensuite le modifier librement.</summary>
    Default,
}

public sealed record MinecraftInfo
{
    public string Version { get; init; } = "";
    public string Loader { get; init; } = "forge";
    public string? LoaderVersion { get; init; }
}

public sealed record ServerInfo
{
    public string Address { get; init; } = "";
    public bool AutoConnect { get; init; } = true;
}

public sealed record ManifestFile
{
    /// <summary>Chemin relatif au dossier du jeu, avec des « / » (ex. « mods/jei.jar »).</summary>
    public string Path { get; init; } = "";

    /// <summary>Adresse de téléchargement complète du fichier.</summary>
    public string Url { get; init; } = "";

    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    public SyncMode Mode { get; init; } = SyncMode.Sync;
}

public sealed record Manifest
{
    public int Schema { get; init; } = 1;
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public MinecraftInfo Minecraft { get; init; } = new();
    public ServerInfo? Server { get; init; }
    public IReadOnlyList<ManifestFile> Files { get; init; } = Array.Empty<ManifestFile>();

    /// <summary>Fichiers du launcher lui-même (logo, fond, thème, actualités) : pas du dossier du jeu.</summary>
    public IReadOnlyList<ManifestFile> LauncherAssets { get; init; } = Array.Empty<ManifestFile>();
}

public static class ManifestJson
{
    public const int SupportedSchema = 1;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Manifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Options)
                       ?? throw new InvalidDataException("Le manifest est vide.");
        if (manifest.Schema != SupportedSchema)
            throw new NotSupportedException(
                $"Version de manifest non supportée ({manifest.Schema}). Mets à jour le launcher.");
        return manifest;
    }
}
