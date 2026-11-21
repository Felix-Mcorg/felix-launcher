using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Felix.Launcher;

/// <summary>La fenêtre où le joueur colle le lien d'un serveur (modpack) à ajouter au hub.</summary>
public partial class AddServerWindow : Window
{
    private readonly Func<string, Task<string?>> _add;

    public AddServerWindow() : this(_ => Task.FromResult<string?>(null))
    {
    }

    /// <param name="add">Reçoit le lien collé ; retourne un message d'erreur, ou null si le serveur est ajouté.</param>
    public AddServerWindow(Func<string, Task<string?>> add)
    {
        InitializeComponent();
        _add = add;
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        AddButton.IsEnabled = false;
        StatusText.Foreground = Brushes.LightGray;
        StatusText.Text = "Vérification du modpack…";

        var error = await _add(LinkBox.Text ?? "");
        if (error is null)
        {
            Close();
            return;
        }

        StatusText.Foreground = Brushes.IndianRed;
        StatusText.Text = error;
        AddButton.IsEnabled = true;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
