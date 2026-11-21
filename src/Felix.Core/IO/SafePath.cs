namespace Felix.Core.IO;

public static class SafePath
{
    /// <summary>
    /// Transforme un chemin relatif du manifest en chemin complet, et refuse tout chemin
    /// qui sortirait du dossier du jeu (« ../ », chemin absolu...).
    /// </summary>
    public static string Resolve(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Chemin vide.", nameof(relativePath));

        var rootFull = Path.GetFullPath(root);
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(rootFull) + Path.DirectorySeparatorChar;

        var normalized = relativePath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(rootFull, normalized));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(rootWithSeparator, comparison))
            throw new InvalidOperationException($"Chemin refusé (hors du dossier du jeu) : {relativePath}");

        return full;
    }
}
