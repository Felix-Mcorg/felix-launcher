namespace Felix.Launcher;

/// <summary>Les adresses et noms du projet : c'est ici qu'on les change (par ex. pour un autre serveur).</summary>
internal static class AppConfig
{
    public const string AppName = "Felix Launcher";

    /// <summary>Le serveur ajouté d'office dans la liste au premier lancement (le modpack Felix).</summary>
    public const string DefaultServerName = "Felix Factory";
    public const string DefaultServerId = "felix-factory";
    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/Felix-Mcorg/felix-modpack/main/manifest.json";

    /// <summary>Le dépôt de ce launcher : on y cherche les nouvelles versions.</summary>
    public const string LauncherOwner = "Felix-Mcorg";
    public const string LauncherRepository = "felix-launcher";

    public static string ReleasesUrl => $"https://github.com/{LauncherOwner}/{LauncherRepository}/releases/latest";
}
