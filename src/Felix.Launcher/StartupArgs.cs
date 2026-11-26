namespace Felix.Launcher;

/// <summary>
/// Arguments donnés par le démarreur (voir le README de felix-launcher-bootstrap) :
/// --install-root, --bootstrapper, --bootstrap-version.
/// </summary>
public sealed record StartupArgs(string? InstallRoot, string? Bootstrapper, string? BootstrapVersion)
{
    public static StartupArgs Current { get; set; } = new(null, null, null);

    public static StartupArgs Parse(string[] args)
    {
        string? Get(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        return new StartupArgs(Get("--install-root"), Get("--bootstrapper"), Get("--bootstrap-version"));
    }
}
