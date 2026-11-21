using Felix.Core.Settings;

namespace Felix.Launcher.ViewModels;

/// <summary>Une ligne de la liste des serveurs (colonne de gauche).</summary>
public sealed class ServerItem : ObservableObject
{
    private ServerProfile _profile;

    public ServerItem(ServerProfile profile) => _profile = profile;

    public ServerProfile Profile
    {
        get => _profile;
        set
        {
            if (Set(ref _profile, value))
            {
                Raise(nameof(Name));
                Raise(nameof(Initial));
            }
        }
    }

    public string Name => _profile.Name;

    /// <summary>La première lettre du nom : affichée sur la pastille du serveur.</summary>
    public string Initial => string.IsNullOrWhiteSpace(_profile.Name) ? "?" : _profile.Name.Trim()[..1].ToUpperInvariant();
}
