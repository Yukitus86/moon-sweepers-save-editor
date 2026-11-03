using System.Text.RegularExpressions;

namespace MoonSweeperEditor;

/// <summary>
/// Finds the Moon Sweepers save folder on Linux. The game is a Windows build, so under Steam Play (Proton) it saves into the
/// Wine prefix of its compatdata folder; a native Unity build would use ~/.config/unity3d.
/// </summary>
static class LinuxSaveLocator
{
    const string AppId = "5335510";
    const string ProtonSubPath = "pfx/drive_c/users/steamuser/AppData/LocalLow/DragDropGames/MoonSweepers";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Shown when nothing is found.</summary>
    public static string NativePath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(Home, ".config"),
            "unity3d", "DragDropGames", "MoonSweepers");

    /// <summary>The folder with the most recently written MoonSessions, or null.</summary>
    public static string? Find() =>
        Candidates()
            .Distinct()
            .Where(dir => Directory.Exists(Path.Combine(dir, "MoonSessions")))
            .OrderByDescending(dir => Directory.GetLastWriteTimeUtc(Path.Combine(dir, "MoonSessions")))
            .FirstOrDefault();

    static IEnumerable<string> Candidates()
    {
        yield return NativePath;
        foreach (string library in SteamLibraries())
        {
            string compat = Path.Combine(library, "steamapps", "compatdata");
            yield return Path.Combine(compat, AppId, ProtonSubPath);
            // A non-Steam shortcut gets a generated id, so look at every prefix as well.
            IEnumerable<string> prefixes;
            try { prefixes = Directory.EnumerateDirectories(compat).ToList(); }
            catch (Exception) { continue; }
            foreach (string prefix in prefixes)
                yield return Path.Combine(prefix, ProtonSubPath);
        }
    }

    static IEnumerable<string> SteamLibraries()
    {
        string[] roots =
        [
            Path.Combine(Home, ".steam", "steam"),
            Path.Combine(Home, ".steam", "root"),
            Path.Combine(Home, ".local", "share", "Steam"),
            Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
            Path.Combine(Home, "snap", "steam", "common", ".local", "share", "Steam"),
        ];
        var seen = new HashSet<string>();
        foreach (string root in roots.Where(Directory.Exists))
        {
            if (seen.Add(root)) yield return root;
            foreach (string library in LibraryFolders(Path.Combine(root, "steamapps", "libraryfolders.vdf")))
                if (seen.Add(library)) yield return library;
        }
    }

    // libraryfolders.vdf lists every Steam library as  "path"  "/mnt/games/SteamLibrary"
    static IEnumerable<string> LibraryFolders(string vdf)
    {
        string text;
        try { text = File.ReadAllText(vdf); }
        catch (Exception) { yield break; }
        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            string path = m.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }
}
