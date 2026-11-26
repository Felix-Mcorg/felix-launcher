using Avalonia;

namespace Felix.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs.Current = StartupArgs.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
