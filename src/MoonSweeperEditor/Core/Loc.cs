using System.ComponentModel;
using System.Globalization;

namespace MoonSweeperEditor.Core;

public enum UiLanguage { German, English }

/// <summary>
/// UI and error texts in German and English. XAML binds through the indexer ({l:T Key}),
/// so switching the language updates the window live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLanguage Language { get; private set; } = UiLanguage.English;

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
        // Sidebar / header
        ["Subtitle"] = ("Savegame Editor", "Savegame Editor"),
        ["SaveGames"] = ("SPIELSTÄNDE", "SAVE GAMES"),
        ["Reload"] = ("↻  Neu laden", "↻  Reload"),
        ["OpenSaveFolder"] = ("📁  Speicherordner öffnen", "📁  Open save folder"),
        ["Regions"] = ("Regionen", "regions"),
        ["LanguageGerman"] = ("Deutsch", "German"),
        ["LanguageEnglish"] = ("Englisch", "English"),
        ["GameRunning"] = ("⚠  Moon Sweepers läuft gerade. Bitte das Spiel komplett beenden – sonst überschreibt es deine Änderungen und Speichern ist gesperrt.",
                           "⚠  Moon Sweepers is running. Close the game completely first – otherwise it overwrites your changes, and saving is locked."),
        ["NoSlot"] = ("Kein Spielstand gewählt", "No save game selected"),
        ["PlayTime"] = ("SPIELZEIT", "PLAY TIME"),
        ["RegionsHeader"] = ("REGIONEN", "REGIONS"),
        ["LastPlayed"] = ("ZULETZT GESPIELT", "LAST PLAYED"),
        ["SlotTitle"] = ("Slot {0}", "Slot {0}"),

        // Bottom bar / status
        ["Discard"] = ("Verwerfen", "Discard"),
        ["SaveChanges"] = ("💾  Änderungen speichern", "💾  Save changes"),
        ["Ready"] = ("Bereit.", "Ready."),
        ["SlotLoaded"] = ("{0} geladen.", "{0} loaded."),
        ["Unsaved"] = ("Ungespeicherte Änderungen.", "Unsaved changes."),
        ["NoSavesFound"] = ("Keine Spielstände gefunden in {0}", "No save games found in {0}"),
        ["CannotEdit"] = ("Dieser Spielstand kann nicht bearbeitet werden: {0}", "This save game cannot be edited: {0}"),
        ["Saved"] = ("Gespeichert ✓  Backup: {0}", "Saved ✓  Backup: {0}"),
        ["SaveFailed"] = ("Speichern fehlgeschlagen", "Saving failed"),
        ["SaveFailedStatus"] = ("Speichern fehlgeschlagen: {0}", "Saving failed: {0}"),
        ["InvalidInput"] = ("Ungültige Eingabe", "Invalid input"),
        ["BalancePrefix"] = ("Kontostand: {0}", "Balance: {0}"),
        ["DiscardTitle"] = ("Änderungen verwerfen", "Discard changes"),
        ["DiscardQuestion"] = ("Es gibt ungespeicherte Änderungen. Verwerfen?", "There are unsaved changes. Discard them?"),
        ["Error"] = ("Fehler", "Error"),
        ["UnexpectedError"] = ("Unerwarteter Fehler", "Unexpected error"),

        // Tabs
        ["TabMoney"] = ("💰  Geld", "💰  Money"),
        ["TabTools"] = ("🧹  Werkzeuge", "🧹  Tools"),
        ["TabTree"] = ("🌳  Upgrade-Baum", "🌳  Upgrade tree"),
        ["TabStorage"] = ("🛢  Lager & Roboter", "🛢  Storage & robots"),
        ["TabBackups"] = ("🗂  Backups", "🗂  Backups"),
        ["TabAdvanced"] = ("⚙  Erweitert", "⚙  Advanced"),

        // Money
        ["TeamBalance"] = ("Team-Kontostand", "Team balance"),
        ["BalanceHint"] = ("Das ist das Geld, das im Spiel oben angezeigt wird (MoonWorld.TeamWallet). Maximal 1 Billion. Die Vorschau im Hauptmenü (slot.json) wird automatisch mit angepasst.",
                           "This is the money shown at the top in the game (MoonWorld.TeamWallet). Up to 1 trillion. The main menu preview (slot.json) is updated as well."),
        ["AddMillion"] = ("+ 1 Mio.", "+ 1 M"),
        ["AddTenMillion"] = ("+ 10 Mio.", "+ 10 M"),
        ["Accounts"] = ("Konten & ausstehende Kilogramm", "Accounts & pending kilograms"),
        ["AccountsHint"] = ("\"Ausstehend\" ist eingesammelter Staub (kg), der noch nicht ausgezahlt wurde.", "\"Pending\" is collected dust (kg) that has not been paid out yet."),
        ["TeamEditedAbove"] = ("Team-Kasse wird oben bearbeitet", "The team balance is edited above"),
        ["TeamAccount"] = ("Team-Kasse", "Team account"),
        ["PlayerAccount"] = ("Spieler \"{0}\"", "Player \"{0}\""),
        ["Stats"] = ("Statistik", "Statistics"),
        ["StatsMissing"] = ("Statistik-Block in diesem Spielstand nicht erkannt.", "Statistics block not recognised in this save game."),
        ["StatsHint"] = ("Lebenszeit-Zähler für verdientes Geld. Ändern hat keinen Einfluss auf den Kontostand.", "Lifetime counters of earned money. Changing them does not change the balance."),
        ["StatEarned"] = ("Verdient gesamt", "Earned in total"),
        ["StatEarnedBy"] = ("Verdient von \"{0}\"", "Earned by \"{0}\""),
        ["StatEarned2"] = ("Verdient (zweiter Zähler)", "Earned (second counter)"),
        ["ErrNumber"] = ("Keine gültige Zahl", "Not a valid number"),
        ["ErrNegative"] = ("Darf nicht negativ sein", "Must not be negative"),
        ["ErrTooLarge"] = ("Maximal {0}", "At most {0}"),

        // Tools
        ["Tools"] = ("Werkzeuge", "Tools"),
        ["UnlockAll"] = ("Alle freischalten", "Unlock all"),
        ["MaxAll"] = ("Alle auf Max-Stufe", "All to max level"),
        ["ColTool"] = ("WERKZEUG", "TOOL"),
        ["ColOwned"] = ("IM BESITZ", "OWNED"),
        ["ColLevel"] = ("STUFE", "LEVEL"),
        ["ColNote"] = ("HINWEIS", "NOTE"),
        ["StarterTool"] = ("Startwerkzeug (immer vorhanden)", "Starter tool (always owned)"),
        ["ToolsHint"] = ("Werkzeug-Stufen gehen im Spiel bis 3. Das Bauwerkzeug hat keine Stufen.", "Tool levels go up to 3 in the game. The building tool has no levels."),
        ["Loadout"] = ("Ausrüstung & Schnellleiste", "Loadout & hotbar"),
        ["EquippedTool"] = ("Ausgerüstetes Werkzeug", "Equipped tool"),
        ["ActiveSlot"] = ("Aktiver Leistenplatz", "Active hotbar slot"),
        ["HotbarSlot"] = ("Platz {0}", "Slot {0}"),
        ["HotbarHint"] = ("Werkzeuge, die nicht im Besitz sind, werden beim Speichern aus der Leiste entfernt.", "Tools you do not own are removed from the hotbar when saving."),
        ["EmptySlot"] = ("(leer)", "(empty)"),

        // Tree
        ["TreeHint"] = ("Stufe 0 = nicht gekauft. Die Maximalstufe jedes Knotens legt das Spiel fest; höhere Werte begrenzt es ggf. selbst.",
                        "Level 0 = not bought. The game defines each node's maximum; it may cap higher values itself."),
        ["AllMinus"] = ("Alle −1", "All −1"),
        ["AllPlus"] = ("Alle +1", "All +1"),
        ["AllToLevel"] = ("Alle auf Stufe", "All to level"),
        ["Apply"] = ("Setzen", "Apply"),
        ["NotBought"] = ("nicht gekauft", "not bought"),
        ["LevelN"] = ("Stufe {0}", "Level {0}"),

        // Storage
        ["Filter"] = ("Filter", "Filter"),
        ["All"] = ("Alle", "All"),
        ["FillFiltered"] = ("Gefilterte füllen", "Fill filtered"),
        ["EmptyFiltered"] = ("Gefilterte leeren", "Empty filtered"),
        ["ColType"] = ("Typ", "Type"),
        ["ColKey"] = ("Kennung", "Key"),
        ["ColAmount"] = ("Inhalt (kg)", "Contents (kg)"),
        ["ColCapacity"] = ("Kapazität (kg)", "Capacity (kg)"),
        ["ColFill"] = ("Füllstand", "Fill level"),
        ["StorageSummary"] = ("{0} Einträge · {1} von {2} kg belegt. Inhalt in der Tabelle per Doppelklick ändern.",
                              "{0} entries · {1} of {2} kg used. Double-click a cell in the table to change the contents."),

        // Backups
        ["BackupNow"] = ("Backup jetzt erstellen", "Create backup now"),
        ["RestoreSelected"] = ("Ausgewähltes wiederherstellen", "Restore selected"),
        ["OpenBackupFolder"] = ("Backup-Ordner öffnen", "Open backup folder"),
        ["BackupHint"] = ("Vor jedem Speichern legt der Editor automatisch ein Backup an (EditorBackups im Spielordner).",
                          "The editor creates a backup automatically before every save (EditorBackups in the game folder)."),
        ["BackupCreated"] = ("Backup erstellt: {0}", "Backup created: {0}"),
        ["RestoreTitle"] = ("Backup wiederherstellen", "Restore backup"),
        ["SelectBackupFirst"] = ("Bitte zuerst ein Backup in der Liste auswählen.", "Select a backup in the list first."),
        ["RestoreQuestion"] = ("{0} durch das Backup vom {1} ersetzen?\n\nDer aktuelle Stand wird vorher selbst gesichert.",
                               "Replace {0} with the backup from {1}?\n\nThe current state is backed up first."),
        ["Restored"] = ("Backup vom {0} wiederhergestellt.", "Backup from {0} restored."),

        // Advanced
        ["RawData"] = ("Rohdaten", "Raw data"),
        ["RawHint"] = ("Exportiert die entpackten Spieldaten als .bin (z. B. für einen Hex-Editor). Beim Import wird die Datei wieder gepackt und Größe + SHA256-Prüfsumme neu berechnet, damit das Spiel sie annimmt.",
                       "Exports the decompressed game data as .bin (e.g. for a hex editor). On import the file is packed again and size + SHA256 checksum are recalculated so the game accepts it."),
        ["ExportRaw"] = ("Entpackt exportieren (.bin)", "Export decompressed (.bin)"),
        ["ImportRaw"] = ("Bearbeitete .bin importieren", "Import edited .bin"),
        ["RawFilter"] = ("Entpackte Spieldaten (*.bin)|*.bin", "Decompressed game data (*.bin)|*.bin"),
        ["RawFilterAll"] = ("Entpackte Spieldaten (*.bin)|*.bin|Alle Dateien|*.*", "Decompressed game data (*.bin)|*.bin|All files|*.*"),
        ["Exported"] = ("Exportiert: {0}", "Exported: {0}"),
        ["ImportTitle"] = ("Rohdaten importieren", "Import raw data"),
        ["ImportQuestion"] = ("{0} ({1:N0} Bytes) als neuen Spielstand für {2} schreiben?", "Write {0} ({1:N0} bytes) as the new save game for {2}?"),
        ["Imported"] = ("Importiert ✓  Backup: {0}", "Imported ✓  Backup: {0}"),
        ["TechDetails"] = ("Technische Details", "Technical details"),
        ["InfoSlotDir"] = ("Slot-Ordner", "Slot folder"),
        ["InfoAccount"] = ("Konto (Ordner)", "Account (folder)"),
        ["InfoSize"] = ("Entpackte Größe", "Decompressed size"),
        ["InfoBlock"] = ("Spielerdaten-Block", "Player data block"),
        ["InfoStats"] = ("Statistik-Werte", "Statistic values"),
        ["InfoAccounts"] = ("Konten", "Accounts"),
        ["InfoPiles"] = ("Staubhaufen", "Dust piles"),
        ["InfoBuildings"] = ("Gebäude/Fundamente", "Buildings/foundations"),
        ["InfoConveyors"] = ("Förderbänder", "Conveyors"),
        ["InfoStorage"] = ("Lager/Kanister", "Storage/canisters"),
        ["InfoNodes"] = ("Upgrade-Knoten", "Upgrade nodes"),
        ["Bytes"] = ("Bytes", "bytes"),

        // Game data
        ["CatClean"] = ("Reinigung", "Cleaning"),
        ["CatEco"] = ("Wirtschaft", "Economy"),
        ["CatGear"] = ("Ausrüstung", "Gear"),
        ["CatSuit"] = ("Anzug", "Suit"),
        ["CatLogi"] = ("Logistik", "Logistics"),
        ["CatOther"] = ("Sonstige", "Other"),
        ["KindSilo"] = ("Silo", "Silo"),
        ["KindGarage"] = ("Garagenlager", "Garage storage"),
        ["KindDock"] = ("Roboter-Dock", "Robot dock"),
        ["KindRobot"] = ("Roboter", "Robot"),
        ["KindCatcher"] = ("Staubfänger", "Dust catcher"),

        // Core errors
        ["ErrNotPcm"] = ("Keine gültige session.moon (Kennung 1PCM fehlt).", "Not a valid session.moon (1PCM marker missing)."),
        ["ErrHeader"] = ("Dateikopf ist beschädigt.", "File header is damaged."),
        ["ErrSize"] = ("Größe stimmt nicht: Kopf sagt {0} Bytes, entpackt {1} Bytes.", "Size mismatch: header says {0} bytes, decompressed {1} bytes."),
        ["ErrHash"] = ("Prüfsumme (SHA256) stimmt nicht – Datei ist beschädigt.", "Checksum (SHA256) mismatch – the file is damaged."),
        ["ErrNoPlayerData"] = ("Spielerdaten (Kontostand, Werkzeuge, Upgrades) nicht gefunden – unbekannte Spielversion?", "Player data (balance, tools, upgrades) not found – unknown game version?"),
        ["ErrAmbiguous"] = ("Spielerdaten nicht eindeutig ({0} Treffer).", "Player data is ambiguous ({0} matches)."),
        ["ErrNoSession"] = ("Keine session.moon gefunden.", "No session.moon found."),
        ["ErrGameRunning"] = ("Moon Sweepers läuft noch. Bitte das Spiel zuerst komplett beenden.", "Moon Sweepers is still running. Close the game completely first."),
        ["ErrNoSessionSlot"] = ("Für diesen Slot gibt es keine session.moon.", "This slot has no session.moon."),
        ["ErrStale"] = ("Der Spielstand wurde inzwischen vom Spiel geändert. Bitte \"Neu laden\" und die Änderungen erneut machen.",
                        "The game changed the save in the meantime. Click \"Reload\" and make your changes again."),
        ["ErrVerify"] = ("Prüfung nach dem Schreiben fehlgeschlagen – Backup wurde wiederhergestellt.", "Verification after writing failed – the backup was restored."),
    };
}
