using System.Globalization;
using System.Text;
using System.Text.Json;
using Felix.Core.Models;

namespace Felix.Core.Settings;

/// <summary>Un serveur de la liste du hub : un modpack (lien vers son manifest) et son propre dossier de jeu.</summary>
public sealed record ServerProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ManifestUrl { get; init; } = "";

    /// <summary>Dossier du jeu choisi par le joueur pour ce serveur (null = dossier par défaut).</summary>
    public string? GameDir { get; init; }

    /// <summary>Dossier du jeu à utiliser : celui choisi, sinon Documents\FelixLauncher\&lt;id&gt;.</summary>
    public string ResolveGameDir() =>
        !string.IsNullOrWhiteSpace(GameDir) ? GameDir : Path.Combine(LauncherPaths.DefaultGameDir, Id);
}

public static class ServerProfiles
{
    /// <summary>« Felix Factory » devient « felix-factory » (sans accents ni caractères spéciaux).</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        var lastWasDash = true;
        foreach (var c in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // retire les accents
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 40)
            slug = slug[..40].Trim('-');
        return slug.Length == 0 ? "serveur" : slug;
    }

    /// <summary>Identifiant unique : « felix-factory », puis « felix-factory-2 », « -3 »...</summary>
    public static string UniqueId(string name, IEnumerable<string> existingIds)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var baseId = Slugify(name);
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }
}

public sealed record LauncherSettings
{
    public string Username { get; init; } = "";
    public int RamGb { get; init; } = 4;

    /// <summary>Les serveurs de la liste (vide au tout premier lancement : l'application ajoute le serveur par défaut).</summary>
    public IReadOnlyList<ServerProfile> Servers { get; init; } = Array.Empty<ServerProfile>();

    public string? SelectedServerId { get; init; }
}

public static class LauncherPaths
{
    /// <summary>%LOCALAPPDATA%\FelixLauncher : le même dossier que celui du démarreur.</summary>
    public static string DefaultInstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FelixLauncher");

    /// <summary>Dossier qui contient le jeu de chaque serveur : Documents\FelixLauncher (visible et modifiable par le joueur).</summary>
    public static string DefaultGameDir
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(documents))
                documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(documents, "FelixLauncher");
        }
    }
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new LauncherSettings();
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_path), ManifestJson.Options)
                   ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new LauncherSettings(); // fichier abîmé : on repart des valeurs par défaut
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, ManifestJson.Options));
        File.Move(temp, _path, overwrite: true);
    }
}
