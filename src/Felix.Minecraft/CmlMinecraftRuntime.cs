using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ProcessBuilder;
using Felix.Core.Models;
using Felix.Core.Play;

namespace Felix.Minecraft;

/// <summary>
/// Installe et lance Minecraft avec CmlLib.Core. Gère Forge (et le jeu « vanilla »).
/// Fabric : pas encore.
/// </summary>
public sealed class CmlMinecraftRuntime : IMinecraftRuntime
{
    public async Task<string> EnsureInstalledAsync(
        MinecraftInfo minecraft, string gameDir, IProgress<PlayProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(gameDir);
        var launcher = CreateLauncher(gameDir, progress);

        var loader = minecraft.Loader.Trim().ToLowerInvariant();
        switch (loader)
        {
            case "vanilla":
                progress.Report(new PlayProgress(PlayStep.Minecraft, $"Installation de Minecraft {minecraft.Version}…"));
                await launcher.InstallAsync(minecraft.Version);
                return minecraft.Version;

            case "forge":
                progress.Report(new PlayProgress(PlayStep.Minecraft, $"Installation de Minecraft {minecraft.Version} et de Forge…"));
                var forge = new ForgeInstaller(launcher);
                return string.IsNullOrWhiteSpace(minecraft.LoaderVersion)
                    ? await forge.Install(minecraft.Version)
                    : await forge.Install(minecraft.Version, minecraft.LoaderVersion);

            default:
                throw new PlayException($"Le loader « {minecraft.Loader} » n'est pas encore géré (Forge ou vanilla).");
        }
    }

    public async Task<int> LaunchAsync(LaunchRequest request, Action<string> onOutput, CancellationToken ct)
    {
        var launcher = CreateLauncher(request.GameDir, null);
        var option = new MLaunchOption
        {
            Session = MSession.CreateOfflineSession(request.Account.Username),
            MaximumRamMb = request.RamMb,
        };

        var process = await launcher.InstallAndBuildProcessAsync(request.VersionId, option);

        if (!string.IsNullOrWhiteSpace(request.ServerAddress))
            AddServerArguments(process.StartInfo, request.ServerAddress!, request.VersionId);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static MinecraftLauncher CreateLauncher(string gameDir, IProgress<PlayProgress>? progress)
    {
        var launcher = new MinecraftLauncher(new MinecraftPath(gameDir));
        if (progress is not null)
        {
            launcher.FileProgressChanged += (_, e) =>
                progress.Report(new PlayProgress(
                    PlayStep.Minecraft,
                    "Installation de Minecraft…",
                    e.TotalTasks > 0 ? (double)e.ProgressedTasks / e.TotalTasks : null,
                    e.Name));
        }
        return launcher;
    }

    /// <summary>
    /// Connexion directe au serveur : Quick Play pour Minecraft 1.20 et plus,
    /// arguments --server / --port pour les versions plus anciennes.
    /// </summary>
    private static void AddServerArguments(System.Diagnostics.ProcessStartInfo info, string address, string versionId)
    {
        var colon = address.LastIndexOf(':');
        var host = colon > 0 ? address[..colon] : address;
        var port = colon > 0 && int.TryParse(address[(colon + 1)..], out var p) ? p : 25565;

        string[] extra = UsesQuickPlay(versionId)
            ? new[] { "--quickPlayMultiplayer", $"{host}:{port}" }
            : new[] { "--server", host, "--port", port.ToString() };

        if (info.ArgumentList.Count > 0)
        {
            foreach (var argument in extra)
                info.ArgumentList.Add(argument);
        }
        else
        {
            info.Arguments += " " + string.Join(" ", extra);
        }
    }

    /// <summary>Lit « 1.20.1 » dans un identifiant comme « 1.20.1-forge-47.2.0 » ; true si c'est 1.20 ou plus.</summary>
    internal static bool UsesQuickPlay(string versionId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(versionId, @"(\d+)\.(\d+)");
        if (!match.Success)
            return true;
        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        return major > 1 || (major == 1 && minor >= 20);
    }
}
