using System.Windows;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

public partial class App : Application
{
    /// <summary>Game data folder; "--root &lt;dir&gt;" points the editor at a copy (testing).</summary>
    public static string SaveRoot { get; private set; } = SaveStore.DefaultRoot;

    /// <summary>"--snapshot &lt;dir&gt;" renders every tab to PNG files and exits (UI check without clicking).</summary>
    public static string? SnapshotDir { get; private set; }

    /// <summary>"--lang de|en" overrides the saved language for this run (screenshots).</summary>
    public static string? LanguageOverride { get; private set; }

    /// <summary>"--mask-user" hides the Windows user name and the account folder in displayed paths (screenshots).</summary>
    public static bool MaskUser { get; private set; }

    /// <summary>"--switch-lang de|en" switches the language live right before a snapshot (tests the switch).</summary>
    public static string? SwitchLanguage { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        for (int i = 0; i < e.Args.Length; i++)
        {
            string? next = i + 1 < e.Args.Length ? e.Args[i + 1] : null;
            switch (e.Args[i])
            {
                case "--root" when next is not null: SaveRoot = next; break;
                case "--snapshot" when next is not null: SnapshotDir = next; break;
                case "--lang" when next is not null: LanguageOverride = next; break;
                case "--mask-user": MaskUser = true; break;
                case "--switch-lang" when next is not null: SwitchLanguage = next; break;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, Loc.T("UnexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }

    /// <summary>Path for display; with --mask-user the user name is replaced.</summary>
    public static string ShowPath(string? path)
    {
        if (path is null || !MaskUser) return path ?? "";
        string user = Environment.UserName;
        path = path.Replace(@"\Users\" + user + @"\", @"\Users\<user>\", StringComparison.OrdinalIgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(path, "[0-9a-f]{64}", "<account>");
    }
}
