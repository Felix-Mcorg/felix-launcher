using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Felix.Core.Accounts;
using Felix.Core.Models;
using Felix.Core.Play;
using Felix.Core.Server;
using Felix.Core.Settings;
using Felix.Core.Sync;
using Felix.Core.Updates;

namespace Felix.Launcher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#6bff95"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#ff9f43"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#9aa0a6"));

    private readonly AppServices _services;
    private readonly StartupArgs _startup;
    private readonly DispatcherTimer _serverTimer;
    private Manifest? _manifest;
    private LauncherUpdate? _update;
    private CancellationTokenSource? _playCts;
    private PlayStep? _lastStep;
    private ServerItem? _selectedServer;
    private bool _switching;

    // Valeurs affichées
    private string _title = AppConfig.AppName;
    private string _subtitle = "Chargement…";
    private string _packInfo = "";
    private bool _logExpanded;
    private Bitmap? _logo;
    private bool _updateVisible;
    private string _updateText = "";
    private string _username = "";
    private double _ramGb = 4;
    private string _gameDir = "";
    private string _serverName = "Serveur";
    private string _serverStatus = "● Vérification…";
    private IBrush _serverStatusBrush = Grey;
    private string _serverMotd = "";
    private string _serverPlayers = "";
    private string _serverVersion = "";
    private Bitmap? _serverIcon;
    private string _filesTitle = "Fichiers du modpack";
    private string _stepText = "En attente";
    private double _progressPercent;
    private bool _isIndeterminate;
    private string _detail = "";
    private string _log = "";
    private string _playText = "JOUER";
    private bool _isBusy;

    public MainViewModel(AppServices services, StartupArgs startup)
    {
        _services = services;
        _startup = startup;

        PlayCommand = new AsyncCommand(PlayAsync, () => !IsBusy);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        OpenFolderCommand = new AsyncCommand(() => { OpenFolder(GameDir); return Task.CompletedTask; });
        ResetFolderCommand = new AsyncCommand(ResetGameDirAsync, () => !IsBusy);
        RemoveServerCommand = new AsyncCommand(RemoveServerAsync, () => !IsBusy && Servers.Count > 1);
        UpdateCommand = new AsyncCommand(() => { ApplyUpdate(); return Task.CompletedTask; }, () => !IsBusy);

        _serverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverTimer.Tick += async (_, _) => await RefreshServerAsync();
    }

    // ---------------------------------------------------------------- propriétés

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    /// <summary>« Minecraft 1.20.1 · Forge » : affiché sous le nom du serveur.</summary>
    public string PackInfo { get => _packInfo; private set => Set(ref _packInfo, value); }

    /// <summary>Journal déplié ou replié.</summary>
    public bool LogExpanded { get => _logExpanded; set => Set(ref _logExpanded, value); }
    public Bitmap? Logo { get => _logo; private set => Set(ref _logo, value); }
    public bool UpdateVisible { get => _updateVisible; private set => Set(ref _updateVisible, value); }
    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }
    public string Username { get => _username; set => Set(ref _username, value); }

    public double RamGb
    {
        get => _ramGb;
        set
        {
            if (Set(ref _ramGb, Math.Round(value)))
                Raise(nameof(RamText));
        }
    }

    public string RamText => $"Mémoire : {(int)RamGb} Go";
    public string GameDir { get => _gameDir; private set => Set(ref _gameDir, value); }
    public string ServerName { get => _serverName; private set => Set(ref _serverName, value); }
    public string ServerStatus { get => _serverStatus; private set => Set(ref _serverStatus, value); }
    public IBrush ServerStatusBrush { get => _serverStatusBrush; private set => Set(ref _serverStatusBrush, value); }
    public string ServerMotd { get => _serverMotd; private set => Set(ref _serverMotd, value); }
    public string ServerPlayers { get => _serverPlayers; private set => Set(ref _serverPlayers, value); }
    public string ServerVersion { get => _serverVersion; private set => Set(ref _serverVersion, value); }
    public Bitmap? ServerIcon { get => _serverIcon; private set => Set(ref _serverIcon, value); }
    public string FilesTitle { get => _filesTitle; private set => Set(ref _filesTitle, value); }
    public string StepText { get => _stepText; private set => Set(ref _stepText, value); }
    public double ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public string PlayText { get => _playText; private set => Set(ref _playText, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            Raise(nameof(IsNotBusy));
            PlayCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            ResetFolderCommand.RaiseCanExecuteChanged();
            UpdateCommand.RaiseCanExecuteChanged();
            RemoveServerCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<FileRowViewModel> Files { get; } = new();

    /// <summary>La liste des serveurs du hub.</summary>
    public ObservableCollection<ServerItem> Servers { get; } = new();

    public ServerItem? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (Set(ref _selectedServer, value) && !_switching && value is not null)
                _ = SwitchServerAsync();
        }
    }

    public bool IsNotBusy => !_isBusy;

    /// <summary>Adresse du manifest du serveur choisi.</summary>
    private string ManifestUrl => SelectedServer?.Profile.ManifestUrl ?? AppConfig.DefaultManifestUrl;

    public AsyncCommand PlayCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand OpenFolderCommand { get; }
    public AsyncCommand ResetFolderCommand { get; }
    public AsyncCommand UpdateCommand { get; }
    public AsyncCommand RemoveServerCommand { get; }

    // ---------------------------------------------------------------- démarrage et arrêt

    public async Task InitializeAsync()
    {
        Logo = LoadAsset("logo.png");
        Title = $"{AppConfig.AppName}  v{AppServices.CurrentVersionText}";

        var settings = _services.Settings.Load();
        Username = settings.Username;
        RamGb = settings.RamGb;

        var profiles = settings.Servers.Count > 0
            ? settings.Servers
            : new[]
            {
                new ServerProfile
                {
                    Id = AppConfig.DefaultServerId,
                    Name = AppConfig.DefaultServerName,
                    ManifestUrl = AppConfig.DefaultManifestUrl,
                },
            };

        _switching = true;
        foreach (var profile in profiles)
            Servers.Add(new ServerItem(profile));
        SelectedServer = Servers.FirstOrDefault(s => s.Profile.Id == settings.SelectedServerId) ?? Servers[0];
        _switching = false;
        GameDir = SelectedServer.Profile.ResolveGameDir();
        RemoveServerCommand.RaiseCanExecuteChanged();

        LogLine($"Serveur : {SelectedServer.Name} · dossier du jeu : {GameDir}");
        _ = CheckUpdateAsync();
        await RefreshAsync();
        _serverTimer.Start();
    }

    public void Shutdown()
    {
        _serverTimer.Stop();
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _services.Settings.Save(new LauncherSettings
            {
                Username = Username.Trim(),
                RamGb = (int)RamGb,
                Servers = Servers.Select(s => s.Profile).ToList(),
                SelectedServerId = SelectedServer?.Profile.Id,
            });
        }
        catch (IOException ex)
        {
            LogLine($"Impossible d'enregistrer les réglages : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- liste des fichiers et serveur

    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        try
        {
            var manifest = await _services.Manifests.FetchAsync(ManifestUrl);
            _manifest = manifest;
            Subtitle = $"{manifest.Name} · Minecraft {manifest.Minecraft.Version} · {Capitalize(manifest.Minecraft.Loader)}";
            PackInfo = $"Minecraft {manifest.Minecraft.Version} · {Capitalize(manifest.Minecraft.Loader)}";
            ServerName = manifest.Server?.Address is { Length: > 0 } address ? address : "Aucun serveur";

            var gameDir = GameDir;
            var plan = await Task.Run(() => _services.Sync.PlanAsync(manifest, gameDir));
            BuildRows(manifest, plan);
            await RefreshServerAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidDataException
                                       or System.Text.Json.JsonException or InvalidOperationException)
        {
            Subtitle = "Modpack indisponible";
            LogLine($"Impossible de lire la liste du modpack : {ex.Message}");
        }
    }

    private void BuildRows(Manifest manifest, SyncPlan plan)
    {
        var toDownload = plan.ToDownload.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = manifest.Files
            .Where(f => f.Mode == SyncMode.Sync)
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FileRowViewModel(f.Path, toDownload.Contains(f.Path) ? FileState.NeedsDownload : FileState.UpToDate))
            .ToList();
        rows.AddRange(plan.ToDelete.Select(p => new FileRowViewModel(p, FileState.ToRemove)));

        Files.Clear();
        foreach (var row in rows)
            Files.Add(row);

        var current = rows.Count(r => r.State != FileState.ToRemove);
        var upToDate = rows.Count(r => r.State == FileState.UpToDate);
        FilesTitle = $"Fichiers du modpack · {upToDate}/{current} à jour";
    }

    private async Task RefreshServerAsync()
    {
        var address = _manifest?.Server?.Address;
        if (string.IsNullOrWhiteSpace(address))
        {
            ServerStatus = "—";
            return;
        }

        var status = await ServerPing.QueryAsync(address, TimeSpan.FromSeconds(5));
        if (!status.Online)
        {
            ServerStatus = "● Statut indisponible";
            ServerStatusBrush = Orange;
            ServerMotd = "";
            ServerPlayers = "";
            ServerVersion = "";
            return;
        }

        ServerStatus = "● En ligne";
        ServerStatusBrush = Green;
        ServerMotd = status.Motd;
        var players = $"👥 {status.PlayersOnline}/{status.PlayersMax} joueurs";
        if (status.PlayerNames.Count > 0)
            players += "\n" + string.Join(", ", status.PlayerNames.Take(8));
        ServerPlayers = players;
        ServerVersion = status.VersionName;

        if (status.Favicon is { Length: > 0 })
        {
            try
            {
                ServerIcon = new Bitmap(new MemoryStream(status.Favicon));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
            {
                // image du serveur illisible : on garde la précédente
            }
        }
    }

    // ---------------------------------------------------------------- jouer

    private async Task PlayAsync()
    {
        if (!OfflineAccount.IsValidUsername(Username))
        {
            StepText = "Pseudo invalide";
            LogLine("Pseudo invalide : 3 à 16 caractères (lettres, chiffres, _).");
            return;
        }

        SaveSettings();
        IsBusy = true;
        PlayText = "EN COURS…";
        IsIndeterminate = true;
        ProgressPercent = 0;
        _lastStep = null;
        _playCts = new CancellationTokenSource();
        LogExpanded = true;

        try
        {
            var progress = new Progress<PlayProgress>(OnProgress);
            var options = new PlayOptions(ManifestUrl, GameDir, Username.Trim(), (int)RamGb * 1024);
            var exitCode = await _services.Play.PlayAsync(options, progress, LogGameLine, _playCts.Token);

            StepText = exitCode == 0 ? "Jeu fermé" : "Le jeu s'est arrêté de façon inattendue";
            LogLine(exitCode == 0
                ? "Le jeu s'est fermé normalement."
                : $"Le jeu s'est arrêté avec le code {exitCode} (plantage ?). Regarde le journal ci-dessus.");
        }
        catch (OperationCanceledException)
        {
            StepText = "Annulé";
        }
        catch (PlayException ex)
        {
            StepText = "Erreur";
            Detail = ex.Message;
            LogLine(ex.Message);
        }
        catch (Exception ex)
        {
            StepText = "Erreur";
            LogLine($"Erreur inattendue : {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            PlayText = "JOUER";
            IsIndeterminate = false;
            ProgressPercent = 0;
            Detail = "";
            await RefreshAsync();
        }
    }

    private void OnProgress(PlayProgress p)
    {
        StepText = $"Étape {(int)p.Step}/4 · {p.Message}";
        if (p.Fraction is { } fraction)
        {
            IsIndeterminate = false;
            ProgressPercent = Math.Clamp(fraction, 0, 1) * 100;
        }
        else
        {
            IsIndeterminate = true;
        }
        Detail = p.Detail ?? "";

        if (_lastStep != p.Step)
        {
            _lastStep = p.Step;
            LogLine(p.Message);
            if (p.Step == PlayStep.Launch)
                PlayText = "JEU EN COURS";
        }
    }

    // ---------------------------------------------------------------- dossier du jeu

    public async Task ChangeGameDirAsync(string path)
    {
        if (IsBusy)
            return;
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogLine($"Impossible d'utiliser ce dossier : {ex.Message}");
            return;
        }

        if (SelectedServer is not null)
            SelectedServer.Profile = SelectedServer.Profile with { GameDir = path };
        GameDir = path;
        SaveSettings();
        LogLine($"Dossier du jeu : {path}");
        await RefreshAsync();
    }

    private async Task ResetGameDirAsync()
    {
        if (IsBusy || SelectedServer is null)
            return;
        SelectedServer.Profile = SelectedServer.Profile with { GameDir = null };
        GameDir = SelectedServer.Profile.ResolveGameDir();
        SaveSettings();
        LogLine($"Dossier du jeu : {GameDir}");
        await RefreshAsync();
    }

    // ---------------------------------------------------------------- le hub : liste des serveurs

    /// <summary>Appelé par la fenêtre « Ajouter un serveur ». Retourne un message d'erreur, ou null si c'est réussi.</summary>
    public async Task<string?> AddServerAsync(string link)
    {
        if (IsBusy)
            return "Attends la fin de l'opération en cours.";
        if (!ManifestLink.TryNormalize(link, out var url, out var error))
            return error;
        if (Servers.Any(s => string.Equals(s.Profile.ManifestUrl, url, StringComparison.OrdinalIgnoreCase)))
            return "Ce serveur est déjà dans ta liste.";

        Manifest manifest;
        try
        {
            manifest = await _services.Manifests.FetchAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or InvalidDataException
                                       or System.Text.Json.JsonException or OperationCanceledException)
        {
            return $"Impossible de lire ce modpack ({ex.Message}). Vérifie le lien : il doit mener à un manifest.json de Felix Launcher.";
        }

        var name = string.IsNullOrWhiteSpace(manifest.Name) ? new Uri(url).Host : manifest.Name.Trim();
        var id = ServerProfiles.UniqueId(name, Servers.Select(s => s.Profile.Id));
        var item = new ServerItem(new ServerProfile { Id = id, Name = name, ManifestUrl = url });

        Servers.Add(item);
        RemoveServerCommand.RaiseCanExecuteChanged();
        LogLine($"Serveur ajouté : {name}");
        SelectedServer = item; // change de serveur et enregistre
        return null;
    }

    private Task RemoveServerAsync()
    {
        if (IsBusy || SelectedServer is null || Servers.Count <= 1)
            return Task.CompletedTask;

        var removed = SelectedServer;
        _switching = true;
        Servers.Remove(removed);
        SelectedServer = Servers[0];
        _switching = false;
        RemoveServerCommand.RaiseCanExecuteChanged();
        LogLine($"Serveur retiré de la liste : {removed.Name} (ses fichiers restent dans {removed.Profile.ResolveGameDir()}).");
        return SwitchServerAsync();
    }

    private async Task SwitchServerAsync()
    {
        if (SelectedServer is null || IsBusy)
            return;

        GameDir = SelectedServer.Profile.ResolveGameDir();
        SaveSettings();
        _manifest = null;
        Files.Clear();
        FilesTitle = "Fichiers du modpack";
        Subtitle = "Chargement…";
        PackInfo = "";
        ServerName = SelectedServer.Name;
        ServerStatus = "● Vérification…";
        ServerStatusBrush = Grey;
        ServerMotd = "";
        ServerPlayers = "";
        ServerVersion = "";
        ServerIcon = null;
        StepText = "En attente";
        LogLine($"Serveur : {SelectedServer.Name} · dossier du jeu : {GameDir}");
        await RefreshAsync();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogLine($"Impossible d'ouvrir le dossier : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- mise à jour du launcher

    private async Task CheckUpdateAsync()
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        var update = await _services.Updates.CheckAsync(current);
        if (update is null)
            return;

        _update = update;
        UpdateText = $"Une nouvelle version du launcher est disponible : {update.Version}";
        UpdateVisible = true;
        LogLine($"Nouvelle version du launcher : {update.Version}");
    }

    /// <summary>
    /// Passe le relais au démarreur : il télécharge la nouvelle version et la relance.
    /// (Un programme ne peut pas se remplacer lui-même pendant qu'il tourne.)
    /// </summary>
    private void ApplyUpdate()
    {
        if (_update is null)
            return;

        var bootstrapper = _startup.Bootstrapper;
        try
        {
            if (!string.IsNullOrEmpty(bootstrapper) && File.Exists(bootstrapper))
            {
                Process.Start(new ProcessStartInfo(bootstrapper) { UseShellExecute = false });
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            }
            else
            {
                // lancé à la main (sans démarreur) : on ouvre la page des releases
                Process.Start(new ProcessStartInfo(AppConfig.ReleasesUrl) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            LogLine($"Impossible de lancer la mise à jour : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- journal et outils

    /// <summary>Ajoute une ligne au journal (utilisable depuis n'importe quel thread).</summary>
    private void LogLine(string text) => Dispatcher.UIThread.Post(() => AppendLog($"[{DateTime.Now:HH:mm:ss}] {text}"));

    private void LogGameLine(string line) => Dispatcher.UIThread.Post(() => AppendLog(line));

    private void AppendLog(string line)
    {
        var text = _log + line + "\n";
        if (text.Length > 80_000)
            text = text[^60_000..];
        Log = text;
    }

    private static Bitmap? LoadAsset(string name)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://FelixLauncher/Assets/{name}")));
        }
        catch
        {
            return null; // pas de logo : l'interface s'affiche quand même
        }
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
