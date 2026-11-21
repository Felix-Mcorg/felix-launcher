using System.Text;
using Felix.Core.Hashing;
using Felix.Core.IO;
using Felix.Core.Models;
using Felix.Core.Net;
using Felix.Core.Sync;

namespace Felix.Core.Tests;

public class SafePathTests
{
    [Fact]
    public void Resolve_InsideRoot_ReturnsFullPath()
    {
        using var dir = new TempDir();
        var path = SafePath.Resolve(dir.Path, "mods/a.jar");
        Assert.StartsWith(System.IO.Path.GetFullPath(dir.Path), path);
        Assert.EndsWith("a.jar", path);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("mods/../../evil.txt")]
    [InlineData("..\\evil.txt")]
    public void Resolve_Traversal_Throws(string relative)
    {
        using var dir = new TempDir();
        Assert.Throws<InvalidOperationException>(() => SafePath.Resolve(dir.Path, relative));
    }

    [Fact]
    public void Resolve_AbsolutePath_Throws()
    {
        using var dir = new TempDir();
        var outside = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir.Path, "..", "outside.txt"));
        Assert.Throws<InvalidOperationException>(() => SafePath.Resolve(dir.Path, outside));
    }
}

public class Sha256Tests
{
    [Fact]
    public async Task Compute_KnownValue()
    {
        using var dir = new TempDir();
        var file = System.IO.Path.Combine(dir.Path, "abc.txt");
        await File.WriteAllTextAsync(file, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            await Sha256Helper.ComputeAsync(file));
    }
}

public class ManifestJsonTests
{
    private const string Sample = """
        {
          "schema": 1,
          "name": "Felix Factory",
          "version": "2026.11.17.1200",
          "minecraft": { "version": "1.20.1", "loader": "forge", "loaderVersion": null },
          "server": { "address": "exemple.mcsh.gg", "autoConnect": true },
          "files": [
            { "path": "mods/a.jar", "url": "https://x/a.jar", "sha256": "abc", "size": 3, "mode": "sync" },
            { "path": "options.txt", "url": "https://x/o.txt", "sha256": "def", "size": 5, "mode": "default" }
          ],
          "launcherAssets": [
            { "path": "logo.png", "url": "https://x/logo.png", "sha256": "123", "size": 9, "mode": "sync" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsAllFields()
    {
        var manifest = ManifestJson.Parse(Sample);
        Assert.Equal("Felix Factory", manifest.Name);
        Assert.Equal("1.20.1", manifest.Minecraft.Version);
        Assert.Equal("exemple.mcsh.gg", manifest.Server!.Address);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(SyncMode.Sync, manifest.Files[0].Mode);
        Assert.Equal(SyncMode.Default, manifest.Files[1].Mode);
        Assert.Equal("logo.png", Assert.Single(manifest.LauncherAssets).Path);
    }

    [Fact]
    public void Parse_UnsupportedSchema_Throws()
    {
        Assert.Throws<NotSupportedException>(() => ManifestJson.Parse("""{ "schema": 99 }"""));
    }
}

public class FileDownloaderTests
{
    private static string Sha(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task Download_ValidHash_WritesFile()
    {
        using var dir = new TempDir();
        var data = Encoding.UTF8.GetBytes("contenu du mod");
        var handler = new StubHandler(new() { ["https://x/a.jar"] = data });
        var downloader = new FileDownloader(new HttpClient(handler));
        var dest = System.IO.Path.Combine(dir.Path, "mods", "a.jar");

        await downloader.DownloadAsync("https://x/a.jar", dest, Sha(data), maxAttempts: 1);

        Assert.Equal(data, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Download_BadHash_ThrowsAndLeavesNoFile()
    {
        using var dir = new TempDir();
        var handler = new StubHandler(new() { ["https://x/a.jar"] = new byte[] { 1, 2, 3 } });
        var downloader = new FileDownloader(new HttpClient(handler));
        var dest = System.IO.Path.Combine(dir.Path, "a.jar");

        await Assert.ThrowsAsync<IOException>(() =>
            downloader.DownloadAsync("https://x/a.jar", dest, new string('0', 64), maxAttempts: 1));

        Assert.False(File.Exists(dest));
    }
}

public class ManifestSyncServiceTests
{
    private static string Sha(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task Plan_SplitsMissingAndUpToDate_AndDetectsRemovedFiles()
    {
        using var dir = new TempDir();
        var existing = Encoding.UTF8.GetBytes("abc");
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "mods"));
        await File.WriteAllBytesAsync(System.IO.Path.Combine(dir.Path, "mods", "a.jar"), existing);

        // Le launcher se souvient d'avoir installé un ancien mod, retiré depuis du modpack.
        var state = new InstalledState();
        state.Files["mods/old.jar"] = new InstalledEntry();
        await InstalledStateStore.SaveAsync(dir.Path, state);

        var manifest = new Manifest
        {
            Files = new[]
            {
                new ManifestFile { Path = "mods/a.jar", Url = "https://x/a.jar", Sha256 = Sha(existing), Size = 3 },
                new ManifestFile { Path = "mods/b.jar", Url = "https://x/b.jar", Sha256 = "00", Size = 9 },
            },
        };

        var service = new ManifestSyncService(new FileDownloader(new HttpClient(new StubHandler(new()))));
        var plan = await service.PlanAsync(manifest, dir.Path);

        Assert.Single(plan.UpToDate);
        Assert.Equal("mods/a.jar", plan.UpToDate[0].Path);
        Assert.Single(plan.ToDownload);
        Assert.Equal("mods/b.jar", plan.ToDownload[0].Path);
        Assert.Equal(new[] { "mods/old.jar" }, plan.ToDelete);
    }

    [Fact]
    public async Task Apply_DownloadsMissingFiles_AndRecordsState()
    {
        using var dir = new TempDir();
        var dataB = Encoding.UTF8.GetBytes("mod b");
        var handler = new StubHandler(new() { ["https://x/b.jar"] = dataB });
        var service = new ManifestSyncService(new FileDownloader(new HttpClient(handler)));

        var manifest = new Manifest
        {
            Files = new[]
            {
                new ManifestFile { Path = "mods/b.jar", Url = "https://x/b.jar", Sha256 = Sha(dataB), Size = dataB.Length },
            },
        };

        var plan = await service.PlanAsync(manifest, dir.Path);
        var result = await service.ApplyAsync(manifest, dir.Path, plan);

        Assert.True(result.Success);
        Assert.Equal(1, result.Downloaded);
        Assert.Equal(dataB, await File.ReadAllBytesAsync(System.IO.Path.Combine(dir.Path, "mods", "b.jar")));

        // Deuxième passage : plus rien à télécharger.
        var secondPlan = await service.PlanAsync(manifest, dir.Path);
        Assert.True(secondPlan.IsEmpty);
    }
}
