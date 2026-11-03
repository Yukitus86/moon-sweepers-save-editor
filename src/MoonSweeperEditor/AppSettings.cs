using System.IO;
using System.Text.Json;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

/// <summary>Small per-user settings file in %APPDATA%\MoonSweeperEditor\settings.json.</summary>
public sealed class AppSettings
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonSweeperEditor", "settings.json");

    public string? Language { get; set; }

    /// <summary>Save folder picked by hand (Linux build; the Windows build always uses the default folder).</summary>
    public string? SaveRoot { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* a broken settings file just falls back to defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { /* losing the language choice is not worth an error dialog */ }
    }

    /// <summary>English unless German was picked before.</summary>
    public UiLanguage ResolveLanguage() => Language == "de" ? UiLanguage.German : UiLanguage.English;

    public static string Code(UiLanguage language) => language == UiLanguage.German ? "de" : "en";
}
