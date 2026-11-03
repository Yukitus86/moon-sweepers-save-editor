using Avalonia;

namespace MoonSweeperEditor;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.ParseArgs(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
