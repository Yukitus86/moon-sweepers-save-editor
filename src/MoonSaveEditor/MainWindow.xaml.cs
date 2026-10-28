using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MoonSaveEditor.Core;

namespace MoonSaveEditor;

public sealed class SlotView(SaveSlot slot)
{
    public SaveSlot Slot { get; } = slot;
    public string Title => MainWindow.SlotTitle(Slot);
    public string Subtitle => Slot.Error is not null
        ? Loc.T("Unreadable")
        : Loc.T("SlotSubtitle", Slot.Regions ?? 0, MainWindow.FormatDate(Slot.LastPlayed));
    public string MoneyText => Slot.Balance is { } d ? MainWindow.FormatMoney(d) : "–";
    public Brush MoneyBrush => (Brush)Application.Current.Resources[Slot.CanEdit ? "Gold" : "Bad"];
}

public partial class MainWindow : Window
{
    const decimal MaxAmount = 1_000_000_000_000m;
    static CultureInfo Culture => Loc.Instance.Culture;

    readonly SaveStore store = new(SaveStore.DefaultRoot);
    readonly AppSettings settings = AppSettings.Load();
    readonly DispatcherTimer gameWatch = new() { Interval = TimeSpan.FromSeconds(2) };
    bool gameRunning;

    public MainWindow()
    {
        Loc.Instance.SetLanguage(settings.ResolveLanguage());
        InitializeComponent();
        gameWatch.Tick += (_, _) => UpdateGameRunning();
        gameWatch.Start();
        Loaded += (_, _) => { ApplyLanguageVisuals(); UpdateGameRunning(); LoadSlots(); };
    }

    SaveSlot? Selected => (SlotList.SelectedItem as SlotView)?.Slot;

    void LoadSlots(string? keepSelection = null)
    {
        keepSelection ??= Selected?.Name;
        IReadOnlyList<SaveSlot> slots;
        try { slots = store.LoadSlots(); }
        catch (Exception ex) { SetStatus(Loc.T("LoadFailed", ex.Message), false); return; }

        var views = slots.Select(s => new SlotView(s)).ToList();
        SlotList.ItemsSource = views;
        EmptyText.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T("NoSaves", store.SessionsDir);

        SlotList.SelectedItem = views.FirstOrDefault(v => v.Slot.Name == keepSelection) ?? views.FirstOrDefault();
        if (views.Count > 0)
            SetStatus(views.Count == 1 ? Loc.T("Loaded1", store.SessionsDir) : Loc.T("LoadedN", views.Count, store.SessionsDir), null);
    }

    void ShowDetails()
    {
        var slot = Selected;
        NoSelectionText.Visibility = slot is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = slot is null ? Visibility.Collapsed : Visibility.Visible;
        if (slot is null) return;

        SlotTitleText.Text = SlotTitle(slot);
        SlotSubtitle.Text = Loc.T("LastPlayed", FormatDate(slot.LastPlayed));
        BalanceText.Text = slot.Balance is { } d ? FormatMoney(d) : "–";
        RegionsText.Text = slot.Regions?.ToString("N0", Culture) ?? "–";
        PlayTimeText.Text = slot.PlaySeconds is { } s ? FormatDuration(s) : "–";

        ErrorBanner.Visibility = slot.Error is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = slot.Error is null ? "" : Loc.T("CannotEdit", slot.Error);

        AmountBox.Text = slot.Balance is { } cur ? cur.ToString("N2", Culture) : "";
        var backups = store.ListBackups(slot);
        BackupInfo.Text = backups.Count == 0
            ? Loc.T("NoBackups")
            : Loc.T("BackupCount", backups.Count, Path.GetFileName(backups[0]));
        UpdateButtons();
    }

    void UpdateGameRunning()
    {
        bool running = SaveStore.IsGameRunning();
        if (running == gameRunning && IsLoaded) return;
        gameRunning = running;
        GameRunningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var slot = Selected;
        SaveButton.IsEnabled = slot is { CanEdit: true } && !gameRunning;
        AmountBox.IsEnabled = slot is { CanEdit: true };
        RestoreButton.IsEnabled = slot is not null && !gameRunning && store.ListBackups(slot).Count > 0;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var slot = Selected;
        if (slot is null) return;
        if (!TryParseAmount(AmountBox.Text, Culture, out decimal amount))
        {
            SetStatus(Loc.T("InvalidAmount"), false);
            AmountBox.Focus();
            return;
        }

        try
        {
            var result = store.SetBalance(slot, amount);
            LoadSlots(slot.Name);
            SetStatus(Loc.T("Saved", FormatMoney(result.OldBalance), FormatMoney(result.NewBalance), result.BackupDir), true);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("NotSaved", ex.Message), false);
        }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        var slot = Selected;
        if (slot is null) return;
        var backups = store.ListBackups(slot);
        if (backups.Count == 0) return;

        string latest = backups[0];
        var answer = MessageBox.Show(this, Loc.T("RestoreQuestion", Path.GetFileName(latest), slot.Name),
            Loc.T("RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            store.Restore(slot, latest);
            LoadSlots(slot.Name);
            SetStatus(Loc.T("Restored", Path.GetFileName(latest)), true);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("RestoreFailed", ex.Message), false);
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = Selected?.SlotDir ?? store.SessionsDir;
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        decimal add = decimal.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        decimal current = TryParseAmount(AmountBox.Text, Culture, out decimal v) ? v : Selected?.Balance ?? 0;
        AmountBox.Text = Math.Min(current + add, MaxAmount).ToString("N2", Culture);
    }

    void ResetAmount_Click(object sender, RoutedEventArgs e) =>
        AmountBox.Text = Selected?.Balance?.ToString("N2", Culture) ?? "";

    void AmountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SaveButton.IsEnabled) Save_Click(sender, e);
    }

    void LanguageGerman_Click(object sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.German);
    void LanguageEnglish_Click(object sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.English);

    void ChangeLanguage(UiLanguage language)
    {
        if (language == Loc.Instance.Language) return;

        // Keep the typed amount across the switch even though the number format changes.
        bool hasAmount = TryParseAmount(AmountBox.Text, Culture, out decimal typed);

        Loc.Instance.SetLanguage(language);
        settings.Language = AppSettings.Code(language);
        settings.Save();

        ApplyLanguageVisuals();
        LoadSlots();
        if (hasAmount) AmountBox.Text = typed.ToString("N2", Culture);
        SetStatus(Loc.T("LanguageChanged"), null);
    }

    // Texts bound via {l:T} update by themselves; this covers the flags and culture-formatted labels.
    void ApplyLanguageVisuals()
    {
        bool german = Loc.Instance.Language == UiLanguage.German;
        FlagDe.Tag = german ? "active" : null;
        FlagEn.Tag = german ? null : "active";
        FlagDe.ToolTip = Loc.T("LanguageGerman");
        FlagEn.ToolTip = Loc.T("LanguageEnglish");

        foreach (var button in QuickAmounts.Children.OfType<Button>())
        {
            if (button.Tag is not string tag) continue;
            decimal value = decimal.Parse(tag, CultureInfo.InvariantCulture);
            button.Content = value == 1_000_000 ? Loc.T("AddMillion") : "+ " + value.ToString("N0", Culture);
        }
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => LoadSlots();
    void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void SetStatus(string text, bool? ok)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
        string brush = ok switch { true => "Good", false => "Bad", null => "Muted" };
        StatusDot.Fill = (Brush)FindResource(brush);
        StatusText.Foreground = (Brush)FindResource(ok is null ? "Muted" : "Text");
    }

    /// <summary>
    /// Accepts "1.000.000", "1000000,5", "1,000,000.50", "$ 25 000". A lone separator is read as the
    /// culture's thousands separator only when it groups digits in threes, otherwise as the decimal point.
    /// </summary>
    internal static bool TryParseAmount(string text, CultureInfo culture, out decimal value)
    {
        value = 0;
        string s = Regex.Replace(text, @"[\s $€'_]", "");
        if (s.Length == 0) return false;

        int lastDot = s.LastIndexOf('.'), lastComma = s.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            char dec = lastDot > lastComma ? '.' : ',';
            char group = dec == '.' ? ',' : '.';
            s = s.Replace(group.ToString(), "").Replace(dec, '.');
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            char sep = lastDot >= 0 ? '.' : ',';
            bool grouped = Regex.IsMatch(s, $@"^\d{{1,3}}(\{sep}\d{{3}})+$");
            bool isCultureGroup = culture.NumberFormat.NumberGroupSeparator == sep.ToString();
            int count = s.Count(c => c == sep);
            // "1.000.000" is always grouping; "1.000" only when '.' is this culture's group separator.
            s = grouped && (count > 1 || isCultureGroup) ? s.Replace(sep.ToString(), "") : s.Replace(sep, '.');
        }

        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
               && value is >= 0 and <= MaxAmount;
    }

    internal static string SlotTitle(SaveSlot slot) =>
        Loc.T("SlotTitle", slot.Number == int.MaxValue ? slot.Name : slot.Number.ToString(CultureInfo.InvariantCulture));

    internal static string FormatMoney(decimal d) => "$ " + d.ToString("N2", Culture);

    internal static string FormatDate(DateTime? dt) =>
        dt?.ToString(Loc.Instance.Language == UiLanguage.German ? "dd.MM.yyyy HH:mm" : "MM/dd/yyyy h:mm tt", Culture) ?? Loc.T("Unknown");

    static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{t.Minutes} min";
    }
}
