using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

public partial class App : Application
{
    /// <summary>"--root &lt;dir&gt;" points the editor at a copy (testing); overrides the saved and the detected folder.</summary>
    public static string? RootOverride { get; private set; }

    /// <summary>"--snapshot &lt;dir&gt;" renders every tab to PNG files and exits (UI check without clicking).</summary>
    public static string? SnapshotDir { get; private set; }

    /// <summary>"--lang de|en" overrides the saved language for this run (screenshots).</summary>
    public static string? LanguageOverride { get; private set; }

    /// <summary>"--mask-user" hides the user name and the account folder in displayed paths (screenshots).</summary>
    public static bool MaskUser { get; private set; }

    public static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--root" when next is not null: RootOverride = next; break;
                case "--snapshot" when next is not null: SnapshotDir = next; break;
                case "--lang" when next is not null: LanguageOverride = next; break;
                case "--mask-user": MaskUser = true; break;
            }
        }
    }

    /// <summary>--root, then the folder picked by hand, then the detected one, then the expected default (for the "not found" message).</summary>
    public static string ResolveRoot(AppSettings settings)
    {
        if (RootOverride is not null) return RootOverride;
        if (settings.SaveRoot is { } saved && Directory.Exists(Path.Combine(saved, "MoonSessions"))) return saved;
        if (!OperatingSystem.IsLinux()) return SaveStore.DefaultRoot;
        return LinuxSaveLocator.Find() ?? LinuxSaveLocator.NativePath;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Path for display; with --mask-user the user name is replaced.</summary>
    public static string ShowPath(string? path)
    {
        if (path is null || !MaskUser) return path ?? "";
        string user = Environment.UserName;
        path = path.Replace("/home/" + user + "/", "/home/<user>/", StringComparison.Ordinal)
                   .Replace(@"\Users\" + user + @"\", @"\Users\<user>\", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(path, "[0-9a-f]{64}", "<account>");
    }
}
