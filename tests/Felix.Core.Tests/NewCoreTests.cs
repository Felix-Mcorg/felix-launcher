using System.Net;
using System.Text;
using System.Text.Json;
using Felix.Core.Accounts;
using Felix.Core.Models;
using Felix.Core.Net;
using Felix.Core.Play;
using Felix.Core.Server;
using Felix.Core.Settings;
using Felix.Core.Sync;
using Felix.Core.Updates;
using Felix.Core.Util;

namespace Felix.Core.Tests;

public class OfflineAccountTests
{
    [Fact]
    public void Create_GivesTheSameUuidAsMinecraft()
    {
        var account = OfflineAccount.Create("Notch");
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", account.Uuid);
        Assert.Equal("Notch", account.Username);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("nom avec espace")]
    [InlineData("pseudo_beaucoup_trop_long")]
    [InlineData("")]
    public void Create_InvalidName_Throws(string name)
    {
        Assert.False(OfflineAccount.IsValidUsername(name));
        Assert.Throws<ArgumentException>(() => OfflineAccount.Create(name));
    }
}

public class ServerPingTests
{
    [Theory]
    [InlineData("exemple.fr", "exemple.fr", 25565)]
    [InlineData("exemple.fr:25570", "exemple.fr", 25570)]
    [InlineData("  exemple.fr:abc ", "exemple.fr:abc", 25565)]
    public void ParseAddress_HandlesPort(string input, string host, int port) =>
        Assert.Equal((host, port), ServerPing.ParseAddress(input));

    [Fact]
    public void EncodeVarInt_Matches_Protocol()
    {
        Assert.Equal(new byte[] { 0x00 }, ServerPing.EncodeVarInt(0));
        Assert.Equal(new byte[] { 0xAC, 0x02 }, ServerPing.EncodeVarInt(300));
    }

    [Fact]
    public void ParseResponse_ReadsPlayersMotdVersionAndFavicon()
    {
        var png = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var json = $$"""
{ "version": { "name": "1.20.1" },
  "players": { "online": 3, "max": 20, "sample": [ { "name": "Alice" }, { "name": "Bob" } ] },
  "description": { "text": "§aBienvenue", "extra": [ { "text": " sur Felix" } ] },
  "favicon": "data:image/png;base64,{{png}}" }
""";

        var status = ServerPing.ParseResponse(json);

        Assert.True(status.Online);
        Assert.Equal("Bienvenue sur Felix", status.Motd);
        Assert.Equal(3, status.PlayersOnline);
        Assert.Equal(20, status.PlayersMax);
        Assert.Equal("1.20.1", status.VersionName);
        Assert.Equal(new[] { "Alice", "Bob" }, status.PlayerNames);
        Assert.Equal(new byte[] { 1, 2, 3 }, status.Favicon);
    }

    [Fact]
    public async Task Query_UnreachableServer_ReturnsOffline()
    {
        var status = await ServerPing.QueryAsync("127.0.0.1:1", TimeSpan.FromSeconds(2));
        Assert.False(status.Online);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(System.IO.Path.Combine(dir.Path, "sub", "settings.json"));
        var servers = new[]
        {
            new ServerProfile { Id = "felix-factory", Name = "Felix Factory", ManifestUrl = "https://x/m.json" },
            new ServerProfile { Id = "autre", Name = "Autre", ManifestUrl = "https://y/m.json", GameDir = "D:\\Jeu" },
        };

        store.Save(new LauncherSettings { Username = "Felix", RamGb = 6, Servers = servers, SelectedServerId = "autre" });
        var loaded = store.Load();

        Assert.Equal("Felix", loaded.Username);
        Assert.Equal(6, loaded.RamGb);
        Assert.Equal(2, loaded.Servers.Count);
        Assert.Equal("D:\\Jeu", loaded.Servers[1].GameDir);
        Assert.Equal("autre", loaded.SelectedServerId);
    }

    [Fact]
    public void Load_CorruptedFile_GivesDefaults()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ pas du json");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(4, loaded.RamGb);
        Assert.Equal("", loaded.Username);
    }
}

public class UpdateCheckerTests
{
    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Check_NewerRelease_ReturnsUpdate()
    {
        var api = new HttpClient(new FuncHandler(_ => Json("""{ "tag_name": "v1.3.0" }""")));
        var checker = new UpdateChecker(api, new HttpClient(new ThrowingHandler()), "o", "r");

        var update = await checker.CheckAsync(new Version(1, 2, 0, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 0), update!.Version);
    }

    [Fact]
    public async Task Check_SameVersion_ReturnsNull()
    {
        var api = new HttpClient(new FuncHandler(_ => Json("""{ "tag_name": "v1.3.0" }""")));
        var checker = new UpdateChecker(api, new HttpClient(new ThrowingHandler()), "o", "r");

        // L'assembly se déclare « 1.3.0.0 » : ça doit compter comme la même version que « 1.3.0 ».
        Assert.Null(await checker.CheckAsync(new Version(1, 3, 0, 0)));
    }

    [Fact]
    public async Task Check_ApiDown_FallsBackToRedirect()
    {
        var api = new HttpClient(new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var redirect = new HttpClient(new FuncHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("/o/r/releases/tag/v2.0.0", UriKind.Relative);
            return response;
        }));

        var update = await new UpdateChecker(api, redirect, "o", "r").CheckAsync(new Version(1, 0, 0));

        Assert.Equal(new Version(2, 0, 0), update!.Version);
    }

    [Fact]
    public async Task Check_Offline_ReturnsNullWithoutThrowing()
    {
        var offline = new HttpClient(new ThrowingHandler());
        var checker = new UpdateChecker(offline, new HttpClient(new ThrowingHandler()), "o", "r");
        Assert.Null(await checker.CheckAsync(new Version(1, 0, 0)));
    }
}

public class ByteSizeTests
{
    [Fact]
    public void Format_PicksTheRightUnit()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture; // le test ne dépend pas de la langue de Windows
        Assert.Equal("512 o", ByteSize.Format(512, culture));
        Assert.Equal("1.5 Ko", ByteSize.Format(1536, culture));
        Assert.Equal("2.0 Mo", ByteSize.Format(2 * 1024 * 1024, culture));

        var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal("1,5 Ko", ByteSize.Format(1536, french));
    }
}

public class PlayServiceTests
{
    private sealed class FakeRuntime : IMinecraftRuntime
    {
        public LaunchRequest? Launched { get; private set; }

        public Task<string> EnsureInstalledAsync(
            MinecraftInfo minecraft, string gameDir, IProgress<PlayProgress> progress, CancellationToken ct) =>
            Task.FromResult("1.20.1-forge-47.2.0");

        public Task<int> LaunchAsync(LaunchRequest request, Action<string> onOutput, CancellationToken ct)
        {
            Launched = request;
            onOutput("Minecraft démarre");
            return Task.FromResult(0);
        }
    }

    private static string Sha(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    private static (PlayService Service, FakeRuntime Runtime, byte[] ModData) Build(string? serverJson = null)
    {
        var modData = Encoding.UTF8.GetBytes("contenu du mod");
        var server = serverJson ?? "{ \"address\": \"exemple.test:25570\", \"autoConnect\": true }";
        var manifestJson = $$"""
{ "schema": 1, "name": "Test", "version": "1",
  "minecraft": { "version": "1.20.1", "loader": "forge" },
  "server": {{server}},
  "files": [ { "path": "mods/a.jar", "url": "https://x/a.jar", "sha256": "{{Sha(modData)}}", "size": {{modData.Length}}, "mode": "sync" } ] }
""";
        var http = new HttpClient(new StubHandler(new()
        {
            ["https://x/manifest.json"] = Encoding.UTF8.GetBytes(manifestJson),
            ["https://x/a.jar"] = modData,
        }));
        var runtime = new FakeRuntime();
        var service = new PlayService(
            new ManifestClient(http), new ManifestSyncService(new FileDownloader(http)), runtime);
        return (service, runtime, modData);
    }

    [Fact]
    public async Task Play_DownloadsFilesThenLaunchesOnTheServer()
    {
        using var dir = new TempDir();
        var (service, runtime, modData) = Build();
        var lines = new List<string>();

        var exitCode = await service.PlayAsync(
            new PlayOptions("https://x/manifest.json", dir.Path, "Felix", 4096),
            new Progress<PlayProgress>(_ => { }),
            lines.Add);

        Assert.Equal(0, exitCode);
        Assert.Equal(modData, await File.ReadAllBytesAsync(System.IO.Path.Combine(dir.Path, "mods", "a.jar")));
        Assert.NotNull(runtime.Launched);
        Assert.Equal("exemple.test:25570", runtime.Launched!.ServerAddress);
        Assert.Equal("Felix", runtime.Launched.Account.Username);
        Assert.Equal("1.20.1-forge-47.2.0", runtime.Launched.VersionId);
        Assert.Contains("Minecraft démarre", lines);
    }

    [Fact]
    public async Task Play_AutoConnectOff_LaunchesWithoutServer()
    {
        using var dir = new TempDir();
        var (service, runtime, _) = Build("""{ "address": "exemple.test", "autoConnect": false }""");

        await service.PlayAsync(
            new PlayOptions("https://x/manifest.json", dir.Path, "Felix", 4096),
            new Progress<PlayProgress>(_ => { }),
            _ => { });

        Assert.Null(runtime.Launched!.ServerAddress);
    }

    [Fact]
    public async Task Play_InvalidUsername_ThrowsPlayException()
    {
        using var dir = new TempDir();
        var (service, runtime, _) = Build();

        await Assert.ThrowsAsync<PlayException>(() => service.PlayAsync(
            new PlayOptions("https://x/manifest.json", dir.Path, "x", 4096),
            new Progress<PlayProgress>(_ => { }),
            _ => { }));

        Assert.Null(runtime.Launched);
    }
}

public class ManifestLinkTests
{
    private const string Raw = "https://raw.githubusercontent.com/Felix-Mcorg/felix-modpack/main/manifest.json";

    [Theory]
    [InlineData("https://github.com/Felix-Mcorg/felix-modpack", Raw)]
    [InlineData("https://github.com/Felix-Mcorg/felix-modpack/", Raw)]
    [InlineData("https://github.com/Felix-Mcorg/felix-modpack.git", Raw)]
    [InlineData("https://github.com/Felix-Mcorg/felix-modpack/tree/main", Raw)]
    [InlineData("https://github.com/Felix-Mcorg/felix-modpack/blob/main/manifest.json", Raw)]
    [InlineData("  https://github.com/Felix-Mcorg/felix-modpack  ", Raw)]
    [InlineData(Raw, Raw)]
    [InlineData("https://exemple.fr/pack/manifest.json", "https://exemple.fr/pack/manifest.json")]
    public void TryNormalize_AcceptsUsualLinks(string input, string expected)
    {
        Assert.True(ManifestLink.TryNormalize(input, out var url, out _));
        Assert.Equal(expected, url);
    }

    [Fact]
    public void TryNormalize_OtherBranch_IsKept()
    {
        Assert.True(ManifestLink.TryNormalize("https://github.com/a/b/tree/dev", out var url, out _));
        Assert.Equal("https://raw.githubusercontent.com/a/b/dev/manifest.json", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas un lien")]
    [InlineData("http://exemple.fr/manifest.json")]
    [InlineData("https://github.com/seulement-un-compte")]
    [InlineData("ftp://exemple.fr/manifest.json")]
    public void TryNormalize_RejectsBadLinks(string input)
    {
        Assert.False(ManifestLink.TryNormalize(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public class ServerProfilesTests
{
    [Theory]
    [InlineData("Felix Factory", "felix-factory")]
    [InlineData("  Été & Co !  ", "ete-co")]
    [InlineData("###", "serveur")]
    [InlineData("", "serveur")]
    public void Slugify_MakesSafeFolderNames(string name, string expected) =>
        Assert.Equal(expected, ServerProfiles.Slugify(name));

    [Fact]
    public void UniqueId_AddsANumberWhenTaken()
    {
        Assert.Equal("felix-factory", ServerProfiles.UniqueId("Felix Factory", Array.Empty<string>()));
        Assert.Equal("felix-factory-2", ServerProfiles.UniqueId("Felix Factory", new[] { "felix-factory" }));
        Assert.Equal("felix-factory-3", ServerProfiles.UniqueId("Felix Factory", new[] { "felix-factory", "felix-factory-2" }));
    }

    [Fact]
    public void ResolveGameDir_UsesChosenFolderOrDefaultPerServer()
    {
        var chosen = new ServerProfile { Id = "a", GameDir = "D:\\Jeux\\A" };
        var byDefault = new ServerProfile { Id = "b" };

        Assert.Equal("D:\\Jeux\\A", chosen.ResolveGameDir());
        Assert.Equal(System.IO.Path.Combine(LauncherPaths.DefaultGameDir, "b"), byDefault.ResolveGameDir());
    }
}
