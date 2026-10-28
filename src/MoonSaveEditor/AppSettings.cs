using System.Globalization;
using System.IO;
using System.Text.Json;
using MoonSaveEditor.Core;

namespace MoonSaveEditor;

/// <summary>Small per-user settings file in %APPDATA%\MoonSaveEditor\settings.json.</summary>
public sealed class AppSettings
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoonSaveEditor", "settings.json");

    public string? Language { get; set; }

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

    public UiLanguage ResolveLanguage() => Language switch
    {
        "de" => UiLanguage.German,
        "en" => UiLanguage.English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? UiLanguage.German : UiLanguage.English,
    };

    public static string Code(UiLanguage language) => language == UiLanguage.German ? "de" : "en";
}
