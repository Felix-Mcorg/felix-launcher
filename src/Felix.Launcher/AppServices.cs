using System.Reflection;
using Felix.Core.Net;
using Felix.Core.Play;
using Felix.Core.Settings;
using Felix.Core.Sync;
using Felix.Core.Updates;
using Felix.Minecraft;

namespace Felix.Launcher;

/// <summary>Assemble les services : un seul endroit où tout est branché.</summary>
public sealed class AppServices
{
    public required ManifestClient Manifests { get; init; }
    public required ManifestSyncService Sync { get; init; }
    public required PlayService Play { get; init; }
    public required UpdateChecker Updates { get; init; }
    public required SettingsStore Settings { get; init; }

    public static string CurrentVersionText =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";

    public static AppServices Create(StartupArgs args)
    {
        var http = FelixHttp.CreateClient(CurrentVersionText);

        var noRedirect = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        noRedirect.DefaultRequestHeaders.UserAgent.ParseAdd($"FelixLauncher/{CurrentVersionText}");

        var manifests = new ManifestClient(http);
        var sync = new ManifestSyncService(new FileDownloader(http));

        return new AppServices
        {
            Manifests = manifests,
            Sync = sync,
            Play = new PlayService(manifests, sync, new CmlMinecraftRuntime()),
            Updates = new UpdateChecker(http, noRedirect, AppConfig.LauncherOwner, AppConfig.LauncherRepository),
            Settings = new SettingsStore(
                Path.Combine(args.InstallRoot ?? LauncherPaths.DefaultInstallRoot, "settings.json")),
        };
    }
}
