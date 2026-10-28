using System.ComponentModel;
using System.Globalization;

namespace MoonSaveEditor.Core;

public enum UiLanguage { German, English }

/// <summary>
/// UI and error texts in German and English. XAML binds through the indexer
/// ({Binding [Key], Source={x:Static core:Loc.Instance}}), so switching the language updates the window live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLanguage Language { get; private set; } = UiLanguage.German;

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == UiLanguage.German ? "de-DE" : "en-US");

    public string this[string key] => Texts.TryGetValue(key, out var t) ? (Language == UiLanguage.German ? t.De : t.En) : key;

    public void SetLanguage(UiLanguage language)
    {
        if (language == Language) return;
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public static string T(string key, params object[] args) =>
        args.Length == 0 ? Instance[key] : string.Format(Instance.Culture, Instance[key], args);

    static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        // Window
        ["Subtitle"] = ("Savegame Editor", "Savegame Editor"),
        ["Minimize"] = ("Minimieren", "Minimize"),
        ["Close"] = ("Schließen", "Close"),
        ["Saves"] = ("SPIELSTÄNDE", "SAVE GAMES"),
        ["Refresh"] = ("Aktualisieren", "Refresh"),
        ["NoSaves"] = ("Keine Spielstände gefunden in\n{0}", "No save games found in\n{0}"),
        ["PickSlot"] = ("Links einen Spielstand auswählen.", "Select a save game on the left."),
        ["GameRunning"] = ("Moon Sweepers läuft gerade. Bitte das Spiel komplett beenden, bevor du speicherst – sonst überschreibt das Spiel deine Änderung.",
                           "Moon Sweepers is running. Please quit the game completely before saving – otherwise the game overwrites your change."),
        ["CannotEdit"] = ("Dieser Spielstand kann nicht bearbeitet werden: {0}", "This save game cannot be edited: {0}"),
        ["Balance"] = ("KONTOSTAND", "BALANCE"),
        ["Regions"] = ("REGIONEN", "REGIONS"),
        ["PlayTime"] = ("SPIELZEIT", "PLAY TIME"),
        ["NewBalance"] = ("NEUER KONTOSTAND", "NEW BALANCE"),
        ["SaveMoney"] = ("Geld speichern", "Save money"),
        ["AddMillion"] = ("+ 1 Mio.", "+ 1 M"),
        ["Reset"] = ("Zurücksetzen", "Reset"),
        ["EditHint"] = ("Geändert wird das Team-Konto in session.moon (TeamWallet). Vor jeder Änderung wird automatisch ein Backup angelegt; Prüfsumme (SHA256) und Dateigröße werden neu berechnet, damit das Spiel den Stand annimmt.",
                        "This changes the team wallet in session.moon (TeamWallet). A backup is created automatically before every change; checksum (SHA256) and file size are recalculated so the game accepts the save."),
        ["Backups"] = ("BACKUPS", "BACKUPS"),
        ["RestoreLatest"] = ("Letztes Backup zurückspielen", "Restore latest backup"),
        ["OpenFolder"] = ("Ordner öffnen", "Open folder"),
        ["Ready"] = ("Bereit.", "Ready."),
        ["LanguageGerman"] = ("Deutsch", "German"),
        ["LanguageEnglish"] = ("Englisch", "English"),
        ["LanguageChanged"] = ("Sprache: Deutsch", "Language: English"),

        // Slot list / details
        ["SlotTitle"] = ("Slot {0}", "Slot {0}"),
        ["SlotSubtitle"] = ("{0} Regionen · {1}", "{0} regions · {1}"),
        ["Unreadable"] = ("Nicht lesbar", "Unreadable"),
        ["LastPlayed"] = ("Zuletzt gespielt: {0}", "Last played: {0}"),
        ["Unknown"] = ("unbekannt", "unknown"),
        ["NoBackups"] = ("Noch keine Editor-Backups für diesen Slot.", "No editor backups for this slot yet."),
        ["BackupCount"] = ("{0} Backup(s), zuletzt {1}", "{0} backup(s), latest {1}"),
        ["Loaded1"] = ("1 Spielstand geladen aus {0}", "1 save game loaded from {0}"),
        ["LoadedN"] = ("{0} Spielstände geladen aus {1}", "{0} save games loaded from {1}"),
        ["LoadFailed"] = ("Spielstände konnten nicht geladen werden: {0}", "Could not load save games: {0}"),
        ["InvalidAmount"] = ("Ungültiger Betrag (0 bis 1 Billion). Beispiel: 1.000.000 oder 25000,50",
                             "Invalid amount (0 to 1 trillion). Example: 1,000,000 or 25000.50"),
        ["Saved"] = ("Gespeichert: {0} → {1}. Prüfsumme ok. Backup: {2}", "Saved: {0} → {1}. Checksum ok. Backup: {2}"),
        ["NotSaved"] = ("Nicht gespeichert: {0}", "Not saved: {0}"),
        ["RestoreTitle"] = ("Backup zurückspielen", "Restore backup"),
        ["RestoreQuestion"] = ("Backup \"{0}\" für {1} zurückspielen?\n\nDer aktuelle Stand von session.moon und slot.json wird dabei überschrieben.",
                               "Restore backup \"{0}\" for {1}?\n\nThe current session.moon and slot.json will be overwritten."),
        ["Restored"] = ("Backup {0} zurückgespielt.", "Backup {0} restored."),
        ["RestoreFailed"] = ("Zurückspielen fehlgeschlagen: {0}", "Restore failed: {0}"),

        // Core errors
        ["ErrNotPcm"] = ("Keine 1PCM-Datei (falsche Signatur).", "Not a 1PCM file (wrong signature)."),
        ["ErrHeader"] = ("Header abgeschnitten.", "Header truncated."),
        ["ErrSize"] = ("Größe stimmt nicht: Header {0}, entpackt {1}.", "Size mismatch: header {0}, decompressed {1}."),
        ["ErrHash"] = ("SHA256-Prüfsumme stimmt nicht – Datei ist beschädigt.", "SHA256 checksum mismatch – file is corrupt."),
        ["ErrNoWallet"] = ("Geld-Datensatz nicht gefunden – unbekannte Spielversion?", "Money record not found – unknown game version?"),
        ["ErrAmbiguous"] = ("Geld-Datensatz nicht eindeutig ({0} Treffer) – Abbruch zur Sicherheit.", "Money record ambiguous ({0} matches) – aborted for safety."),
        ["ErrNegative"] = ("Betrag muss eine Zahl ≥ 0 sein.", "Amount must be a number ≥ 0."),
        ["ErrNoSession"] = ("Keine session.moon gefunden.", "No session.moon found."),
        ["ErrNoDollars"] = ("Feld \"dollars\" in slot.json nicht gefunden.", "Field \"dollars\" not found in slot.json."),
        ["ErrGameRunning"] = ("Moon Sweepers läuft noch. Bitte das Spiel zuerst komplett beenden.", "Moon Sweepers is still running. Please quit the game completely first."),
        ["ErrNotEditable"] = ("Slot kann nicht bearbeitet werden.", "Slot cannot be edited."),
        ["ErrVerify"] = ("Kontrolle nach dem Schreiben fehlgeschlagen – Backup wurde zurückgespielt.", "Verification after writing failed – backup was restored."),
    };
}
