using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Felix.Launcher.ViewModels;

namespace Felix.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://FelixLauncher/Assets/logo.png")));
        }
        catch
        {
            // pas de logo : on garde l'icône par défaut
        }

        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                await vm.InitializeAsync();
            }
        };
        Closing += (_, _) => (DataContext as MainViewModel)?.Shutdown();
    }

    /// <summary>Fait défiler le journal vers la dernière ligne.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Log))
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private async void OnAddServer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm)
            return;
        await new AddServerWindow(vm.AddServerAsync).ShowDialog(this);
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choisis le dossier du jeu",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
            await vm.ChangeGameDirAsync(path);
    }
}
