namespace Felix.Core.Settings;

/// <summary>
/// Transforme ce que le joueur colle (lien d'un dépôt GitHub, lien d'un manifest.json...) en adresse du manifest.
/// </summary>
public static class ManifestLink
{
    public static bool TryNormalize(string? input, out string url, out string error)
    {
        url = "";
        error = "";

        var text = input?.Trim() ?? "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            error = "Ce n'est pas un lien valide.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Le lien doit commencer par https://";
            return false;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            // https://github.com/compte/depot[/tree/branche | /blob/branche/chemin/manifest.json]
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = "Lien GitHub incomplet : il faut au moins https://github.com/compte/depot";
                return false;
            }

            var owner = parts[0];
            var repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
            var branch = "main";
            var path = "manifest.json";
            if (parts.Length >= 4 && parts[2] is "tree" or "blob")
            {
                branch = parts[3];
                if (parts[2] == "blob" && parts.Length > 4)
                    path = string.Join('/', parts[4..]);
            }

            url = $"https://raw.githubusercontent.com/{owner}/{repository}/{branch}/{path}";
            return true;
        }

        url = uri.ToString(); // raw.githubusercontent.com ou n'importe quel autre site en https
        return true;
    }
}
