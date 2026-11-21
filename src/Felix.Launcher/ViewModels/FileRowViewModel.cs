using Avalonia.Media;

namespace Felix.Launcher.ViewModels;

public enum FileState { UpToDate, NeedsDownload, ToRemove }

/// <summary>Une ligne de la liste des fichiers du modpack.</summary>
public sealed class FileRowViewModel : ObservableObject
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#6bff95"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#ffb347"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#ff6b6b"));

    private FileState _state;

    public FileRowViewModel(string name, FileState state)
    {
        Name = name;
        _state = state;
    }

    public string Name { get; }

    public FileState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(StatusText));
                Raise(nameof(StatusBrush));
            }
        }
    }

    public string StatusText => _state switch
    {
        FileState.UpToDate => "✔ À jour",
        FileState.NeedsDownload => "⬇ À télécharger",
        _ => "✖ À supprimer",
    };

    public IBrush StatusBrush => _state switch
    {
        FileState.UpToDate => Green,
        FileState.NeedsDownload => Orange,
        _ => Red,
    };
}
