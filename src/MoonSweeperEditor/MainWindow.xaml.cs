using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

public sealed record BackupItem(string Path, string Name, string When, string Size);

public partial class MainWindow : Window
{
    const decimal MaxBalance = 1_000_000_000_000m;

    readonly SaveStore store = new(App.SaveRoot);
    readonly AppSettings settings = AppSettings.Load();
    readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    readonly ObservableCollection<WalletRow> walletRows = [];
    readonly ObservableCollection<StatRow> statRows = [];
    readonly ObservableCollection<ToolRow> toolRows = [];
    readonly ObservableCollection<NodeRow> nodeRows = [];
    readonly ObservableCollection<CanisterRow> canisterRows = [];
    readonly ObservableCollection<HotbarSlotRow> hotbarRows = [];
    readonly ICollectionView canisterView;

    SaveSlot? current;
    bool loading, dirty, gameRunning;

    /// <summary>Choices for the hotbar slots ("" = empty).</summary>
    public ObservableCollection<Choice> ToolChoices { get; } = [];

    static CultureInfo Culture => Loc.Instance.Culture;

    public MainWindow()
    {
        var language = App.LanguageOverride switch
        {
            "de" => UiLanguage.German,
            "en" => UiLanguage.English,
            _ => settings.ResolveLanguage(),
        };
        Loc.Instance.SetLanguage(language);
        ApplyCulture();

        InitializeComponent();
        DataContext = this;

        WalletItems.ItemsSource = walletRows;
        StatItems.ItemsSource = statRows;
        ToolItems.ItemsSource = toolRows;
        NodeItems.ItemsSource = nodeRows;
        HotbarItems.ItemsSource = hotbarRows;
        canisterView = CollectionViewSource.GetDefaultView(canisterRows);
        CanisterGrid.ItemsSource = canisterView;
        CanisterGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(UpdateCanisterSummary, DispatcherPriority.Background);
        TreeAllLevel.ItemsSource = Enumerable.Range(0, GameData.MaxTreeLevel + 1).ToList();
        TreeAllLevel.SelectedItem = 5;

        Row.AnyChanged += MarkDirty;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += (_, _) =>
        {
            ApplyLanguageVisuals();
            CheckGame();
            LoadSlots(null);
            if (App.SnapshotDir is { } dir) Dispatcher.BeginInvoke(() => Snapshot(dir), DispatcherPriority.ApplicationIdle);
        };
        Closing += Window_Closing;
        gameTimer.Tick += (_, _) => CheckGame();
        gameTimer.Start();
    }

    // ---------------------------------------------------------------- language

    void ApplyCulture()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        Language = XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
    }

    void LanguageGerman_Click(object sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.German);
    void LanguageEnglish_Click(object sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.English);

    void ChangeLanguage(UiLanguage language)
    {
        if (language == Loc.Instance.Language) return;

        // Keep the typed balance: parse it in the old format, show it in the new one.
        bool hadBalance = TryGetBalance(out decimal balance, out _);

        Loc.Instance.SetLanguage(language);
        if (App.SnapshotDir is null)
        {
            settings.Language = AppSettings.Code(language);
            settings.Save();
        }
        ApplyCulture();

        bool wasLoading = loading;
        loading = true;
        Row.Frozen = true;
        try
        {
            if (hadBalance && current?.CanEdit == true) BalanceBox.Text = balance.ToString("N2", Culture);

            // Rebuilding the item containers re-creates their bindings with the new culture and texts.
            foreach (var rows in new IEnumerable<Row>[] { walletRows, statRows, toolRows, nodeRows, canisterRows, hotbarRows })
                foreach (var row in rows) row.RaiseAll();
            foreach (var items in new ItemsControl[] { WalletItems, StatItems, ToolItems, NodeItems, HotbarItems, CanisterGrid, SlotList })
            {
                var selected = (items as Selector)?.SelectedItem;
                IEnumerable? source = items.ItemsSource;
                items.ItemsSource = null;
                items.ItemsSource = source;
                if (items is Selector s) s.SelectedItem = selected;
            }

            EquippedBox.Items.Refresh();
            CanisterFilter.Items.Refresh();
            if (SelectedSlotBox.ItemsSource is not null)
            {
                int index = SelectedSlotBox.SelectedIndex;
                SelectedSlotBox.ItemsSource = SlotLabels(hotbarRows.Count);
                SelectedSlotBox.SelectedIndex = index;
            }

            ShowHeader(current);
            if (current?.Data is { } data) InfoBox.Text = BuildInfo(current, data);
            RefreshBackups();
            UpdateCanisterSummary();
            ApplyLanguageVisuals();
            if (current is not null)
                SetStatus(dirty ? Loc.T("Unsaved") : Loc.T("SlotLoaded", current.Title), dirty ? StatusKind.Warn : StatusKind.Neutral);
        }
        finally
        {
            Row.Frozen = false;
            loading = wasLoading;
        }
    }

    void ApplyLanguageVisuals()
    {
        bool german = Loc.Instance.Language == UiLanguage.German;
        FlagDe.Tag = german ? "active" : null;
        FlagEn.Tag = german ? null : "active";
        FlagDe.ToolTip = Loc.T("LanguageGerman");
        FlagEn.ToolTip = Loc.T("LanguageEnglish");

        foreach (var button in QuickAmounts.Children.OfType<Button>())
        {
            string tag = (string)button.Tag;
            bool set = tag.StartsWith('=');
            decimal value = decimal.Parse(set ? tag[1..] : tag, CultureInfo.InvariantCulture);
            button.Content = value switch
            {
                1_000_000 when !set => Loc.T("AddMillion"),
                10_000_000 when !set => Loc.T("AddTenMillion"),
                _ => (set ? "= " : "+ ") + value.ToString("N0", Culture),
            };
        }
    }

    static List<string> SlotLabels(int count) => Enumerable.Range(1, count).Select(n => Loc.T("HotbarSlot", n)).ToList();

    // ---------------------------------------------------------------- slots

    void LoadSlots(string? selectName)
    {
        loading = true;
        try
        {
            var slots = store.LoadSlots();
            SlotList.ItemsSource = slots;
            var pick = slots.FirstOrDefault(s => s.Name == selectName) ?? slots.FirstOrDefault(s => s.CanEdit) ?? slots.FirstOrDefault();
            SlotList.SelectedItem = pick;
            ShowSlot(pick);
            if (pick is null) SetStatus(Loc.T("NoSavesFound", App.ShowPath(store.SessionsDir)), StatusKind.Error);
        }
        finally { loading = false; }
    }

    void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SlotList.SelectedItem is not SaveSlot slot || slot == current) return;
        if (dirty && !ConfirmDiscard())
        {
            loading = true;
            SlotList.SelectedItem = current;
            loading = false;
            return;
        }
        loading = true;
        try { ShowSlot(slot); }
        finally { loading = false; }
    }

    void ShowHeader(SaveSlot? slot)
    {
        HeaderTitle.Text = slot?.Title ?? Loc.T("NoSlot");
        HeaderPath.Text = App.ShowPath(slot?.SessionPath ?? slot?.SlotDir);
        HeaderPlayTime.Text = slot?.PlayTimeText ?? "–";
        HeaderRegions.Text = slot?.RegionsText ?? "–";
        HeaderLastPlayed.Text = slot?.LastPlayedText ?? "–";
    }

    void ShowSlot(SaveSlot? slot)
    {
        bool wasLoading = loading;
        loading = true;
        try
        {
            current = slot;
            walletRows.Clear(); statRows.Clear(); toolRows.Clear(); nodeRows.Clear();
            canisterRows.Clear(); hotbarRows.Clear(); ToolChoices.Clear();
            BalanceBox.Text = ""; BalanceError.Text = ""; InfoBox.Text = "";
            BackupList.ItemsSource = null;
            dirty = false;
            ShowHeader(slot);

            if (slot is null) { Tabs.IsEnabled = false; return; }
            RefreshBackups();

            if (!slot.CanEdit || slot.Data is not { } data)
            {
                Tabs.IsEnabled = false;
                SetStatus(Loc.T("CannotEdit", slot.Error ?? ""), StatusKind.Error);
                return;
            }

            FillMoney(data);
            FillTools(data);
            FillTree(data);
            FillCanisters(data);
            InfoBox.Text = BuildInfo(slot, data);

            Tabs.IsEnabled = true;
            SetStatus(Loc.T("SlotLoaded", slot.Title), StatusKind.Neutral);
        }
        finally
        {
            loading = wasLoading;
            UpdateButtons();
        }
    }

    static T? Pick<T>(List<T> list, Func<T, string> owner) where T : class =>
        list.FirstOrDefault(x => owner(x) == "local") ?? list.FirstOrDefault();

    void FillMoney(SessionData data)
    {
        BalanceBox.Text = (data.Team?.Dollars ?? 0).ToString("N2", Culture);
        foreach (var w in data.Wallet) walletRows.Add(new WalletRow(w));
        foreach (var s in data.Stats) statRows.Add(new StatRow(s));
        StatsMissing.Visibility = data.Stats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void FillTools(SessionData data)
    {
        var inv = Pick(data.Inventories, i => i.Owner);
        var levels = Pick(data.ToolLevels, l => l.Owner);
        var bar = Pick(data.Hotbars, h => h.Owner);

        var known = GameData.Tools.ToList();
        var extraIds = (inv?.Tools.Select(t => t.Id) ?? [])
            .Concat(levels?.Levels.Select(l => l.Id) ?? [])
            .Where(id => known.All(k => k.Id != id)).Distinct();
        foreach (string id in extraIds)
            known.Add(new ToolInfo(id, id, id, levels?.Levels.Any(l => l.Id == id) == true, false));

        foreach (var t in known)
        {
            var entry = levels?.Levels.FirstOrDefault(l => l.Id == t.Id);
            bool inInventory = inv?.Tools.Any(x => x.Id == t.Id) == true;
            var row = new ToolRow { Id = t.Id, Upgradable = t.Upgradable, AlwaysOwned = t.AlwaysOwned };
            row.InitState(t.AlwaysOwned || inInventory, Math.Clamp(entry?.Level ?? 1, GameData.MinToolLevel, GameData.MaxToolLevel));
            toolRows.Add(row);
        }

        ToolChoices.Add(new Choice(""));
        foreach (var r in toolRows) ToolChoices.Add(new Choice(r.Id));

        EquippedBox.ItemsSource = ToolChoices.Where(c => c.Id != "").ToList();
        EquippedBox.SelectedValue = levels?.Equipped;
        EquippedBox.IsEnabled = levels is not null;

        if (bar is not null)
        {
            for (int i = 0; i < bar.Slots.Count; i++) hotbarRows.Add(new HotbarSlotRow(i, bar.Slots[i]));
            SelectedSlotBox.ItemsSource = SlotLabels(bar.Slots.Count);
            SelectedSlotBox.SelectedIndex = Math.Clamp(bar.Selected, 0, Math.Max(0, bar.Slots.Count - 1));
        }
        else SelectedSlotBox.ItemsSource = null;
        SelectedSlotBox.IsEnabled = bar is not null;
    }

    void FillTree(SessionData data)
    {
        var tree = Pick(data.Trees, t => t.Owner);
        var known = GameData.Nodes.ToList();
        foreach (var n in tree?.Nodes ?? [])
            if (known.All(k => k.Id != n.Id)) known.Add(new NodeInfo(n.Id, "CatOther", n.Id, n.Id));

        foreach (var n in known)
        {
            var row = new NodeRow { Id = n.Id, CategoryKey = n.Category };
            row.InitLevel(tree?.Nodes.FirstOrDefault(x => x.Id == n.Id)?.Level ?? 0);
            nodeRows.Add(row);
        }
    }

    void FillCanisters(SessionData data)
    {
        foreach (var c in data.Canisters) canisterRows.Add(new CanisterRow(c));
        var kinds = new List<KindChoice> { new("") };
        kinds.AddRange(canisterRows.Select(r => r.KindKey).Distinct().Select(k => new KindChoice(k)));
        CanisterFilter.ItemsSource = kinds;
        CanisterFilter.SelectedIndex = 0;
        UpdateCanisterSummary();
    }

    static string BuildInfo(SaveSlot slot, SessionData data)
    {
        var sb = new StringBuilder();
        void Line(string key, object value) => sb.AppendLine($"{Loc.T(key) + ":",-24}{value}");
        Line("InfoSlotDir", App.ShowPath(slot.SlotDir));
        sb.AppendLine($"{"session.moon:",-24}{App.ShowPath(slot.SessionPath)}");
        Line("InfoAccount", App.ShowPath(Path.GetFileName(Path.GetDirectoryName(slot.SessionPath)) ?? ""));
        Line("InfoSize", $"{data.Raw.Length.ToString("N0", Culture)} {Loc.T("Bytes")}");
        sb.AppendLine($"{"SHA256:",-24}{MoonContainer.Hash(data.Raw)}");
        Line("InfoBlock", $"0x{data.Start:X} – 0x{data.End:X} ({(data.End - data.Start).ToString("N0", Culture)} {Loc.T("Bytes")})");
        Line("InfoStats", data.Stats.Count);
        Line("InfoAccounts", data.Wallet.Count);
        Line("InfoPiles", data.PileCount.ToString("N0", Culture));
        Line("InfoBuildings", data.BuildingCount.ToString("N0", Culture));
        Line("InfoConveyors", data.ConveyorCount.ToString("N0", Culture));
        Line("InfoStorage", data.Canisters.Count.ToString("N0", Culture));
        Line("InfoNodes", data.Trees.Sum(t => t.Nodes.Count));
        return sb.ToString();
    }

    void RefreshBackups()
    {
        if (current is null) { BackupList.ItemsSource = null; return; }
        BackupList.ItemsSource = store.ListBackups(current).Select(dir =>
        {
            string name = Path.GetFileName(dir);
            string when = DateTime.TryParseExact(name, "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t.ToString("G", Culture) : name;
            var file = new FileInfo(Path.Combine(dir, "session.moon"));
            return new BackupItem(dir, name, when, file.Exists ? $"{(file.Length / 1024.0).ToString("N0", Culture)} KB" : "");
        }).ToList();
    }

    // ---------------------------------------------------------------- state / status

    enum StatusKind { Neutral, Good, Warn, Error }

    void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        string key = kind switch { StatusKind.Good => "Good", StatusKind.Warn => "Warn", StatusKind.Error => "Bad", _ => "Muted" };
        StatusDot.Fill = (Brush)FindResource(key);
        StatusText.Foreground = kind == StatusKind.Neutral ? (Brush)FindResource("Muted") : (Brush)FindResource(key);
    }

    void MarkDirty()
    {
        if (loading || current is null) return;
        if (!dirty) SetStatus(Loc.T("Unsaved"), StatusKind.Warn);
        dirty = true;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        SaveButton.IsEnabled = dirty && !gameRunning && current?.CanEdit == true;
        DiscardButton.IsEnabled = dirty;
    }

    void CheckGame()
    {
        bool running = SaveStore.IsGameRunning();
        if (running == gameRunning && IsLoaded) return;
        gameRunning = running;
        GameRunningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    bool ConfirmDiscard() =>
        MessageBox.Show(this, Loc.T("DiscardQuestion"), Loc.T("DiscardTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (dirty && App.SnapshotDir is null && !ConfirmDiscard()) e.Cancel = true;
    }

    // ---------------------------------------------------------------- money

    void BalanceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        BalanceError.Text = TryGetBalance(out _, out string? error) ? "" : error;
        MarkDirty();
    }

    bool TryGetBalance(out decimal value, out string? error)
    {
        error = null;
        if (!Format.TryParseDecimal(BalanceBox.Text, out value)) { error = Loc.T("ErrNumber"); return false; }
        if (value < 0) { error = Loc.T("ErrNegative"); return false; }
        if (value > MaxBalance) { error = Loc.T("ErrTooLarge", MaxBalance.ToString("N0", Culture)); return false; }
        value = Math.Round(value, 2);
        return true;
    }

    void AddMoney_Click(object sender, RoutedEventArgs e)
    {
        string tag = (string)((Button)sender).Tag;
        decimal now = TryGetBalance(out decimal v, out _) ? v : 0;
        decimal next = tag.StartsWith('=') ? decimal.Parse(tag[1..], CultureInfo.InvariantCulture) : now + decimal.Parse(tag, CultureInfo.InvariantCulture);
        BalanceBox.Text = Math.Clamp(next, 0, MaxBalance).ToString("N2", Culture);
    }

    // ---------------------------------------------------------------- tools

    void UnlockAllTools_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in toolRows.Where(r => r.CanToggle)) r.Owned = true;
    }

    void MaxAllTools_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in toolRows.Where(r => r.Upgradable)) r.Level = GameData.MaxToolLevel;
    }

    void AnyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    // ---------------------------------------------------------------- tree

    void NodeStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NodeRow row, Tag: string tag }) row.Level += int.Parse(tag, CultureInfo.InvariantCulture);
    }

    void TreeAll_Click(object sender, RoutedEventArgs e)
    {
        int delta = int.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        foreach (var r in nodeRows) r.Level += delta;
    }

    void TreeSetAll_Click(object sender, RoutedEventArgs e)
    {
        if (TreeAllLevel.SelectedItem is int level)
            foreach (var r in nodeRows) r.Level = level;
    }

    // ---------------------------------------------------------------- canisters

    void CanisterFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string kind = (CanisterFilter.SelectedItem as KindChoice)?.KindKey ?? "";
        canisterView.Filter = kind.Length == 0 ? null : o => ((CanisterRow)o).KindKey == kind;
        UpdateCanisterSummary();
    }

    void CanisterBulk_Click(object sender, RoutedEventArgs e)
    {
        CanisterGrid.CommitEdit(DataGridEditingUnit.Row, true);
        bool fill = (string)((Button)sender).Tag == "fill";
        foreach (CanisterRow r in canisterView) r.Amount = fill ? r.Capacity : 0;
        UpdateCanisterSummary();
    }

    void UpdateCanisterSummary()
    {
        var rows = canisterView.Cast<CanisterRow>().ToList();
        double amount = rows.Sum(r => r.Amount), capacity = rows.Sum(r => r.Capacity);
        CanisterSummary.Text = Loc.T("StorageSummary", rows.Count, amount.ToString("N0", Culture), capacity.ToString("N0", Culture));
    }

    // ---------------------------------------------------------------- save

    void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (current is not null && ConfirmDiscard()) ShowSlot(current);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Data is not { } loaded) return;
        CanisterGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (!TryGetBalance(out decimal balance, out string? error))
        {
            MessageBox.Show(this, Loc.T("BalancePrefix", error ?? ""), Loc.T("InvalidInput"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var fresh = SessionData.Parse(loaded.Raw);
            Apply(fresh, balance);
            string backup = store.Save(current, fresh);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Loc.T("Saved", Path.GetFileName(backup)), StatusKind.Good);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("SaveFailedStatus", ex.Message), StatusKind.Error);
            MessageBox.Show(this, ex.Message, Loc.T("SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Cursor = null; }
    }

    void Apply(SessionData d, decimal balance)
    {
        // Wallet
        foreach (var row in walletRows)
        {
            var entry = d.Wallet.First(w => w.Owner == row.Owner);
            entry.Dollars = row.IsTeam ? balance : row.Dollars;
            entry.PendingKilograms = row.PendingKilograms;
        }

        // Statistics
        foreach (var row in statRows)
            d.Stats.First(s => s.Offset == row.Offset).Value = row.Value;

        // Owned tools
        bool Owned(string id) => toolRows.FirstOrDefault(r => r.Id == id)?.Owned == true;
        var inv = Pick(d.Inventories, i => i.Owner);
        if (inv is null && toolRows.Any(r => r.CanToggle && r.Owned))
        {
            inv = new Inventory { Owner = "local" };
            d.Inventories.Add(inv);
        }
        if (inv is not null)
            foreach (var row in toolRows.Where(r => r.CanToggle))
            {
                bool has = inv.Tools.Any(t => t.Id == row.Id);
                if (row.Owned && !has) inv.Tools.Add(new OwnedTool { Id = row.Id });
                else if (!row.Owned && has) inv.Tools.RemoveAll(t => t.Id == row.Id);
            }

        // Tool levels + equipped tool
        var levels = Pick(d.ToolLevels, l => l.Owner);
        if (levels is not null)
        {
            foreach (var row in toolRows.Where(r => r.Upgradable))
            {
                var entry = levels.Levels.FirstOrDefault(l => l.Id == row.Id);
                if (entry is not null) entry.Level = row.Level;
                else if (row.Owned) levels.Levels.Add(new ToolLevel { Id = row.Id, Level = row.Level });
            }
            // The game writes this list sorted by tool id.
            levels.Levels.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            string equipped = EquippedBox.SelectedValue as string ?? levels.Equipped;
            if (!Owned(equipped)) equipped = toolRows.FirstOrDefault(r => r.Owned)?.Id ?? levels.Equipped;
            levels.Equipped = equipped;
        }

        // Hotbar
        var bar = Pick(d.Hotbars, h => h.Owner);
        if (bar is not null)
        {
            for (int i = 0; i < bar.Slots.Count && i < hotbarRows.Count; i++)
            {
                string tool = hotbarRows[i].Tool;
                bar.Slots[i] = tool.Length > 0 && !Owned(tool) ? "" : tool;
            }
            if (SelectedSlotBox.SelectedIndex >= 0) bar.Selected = SelectedSlotBox.SelectedIndex;
        }

        // Upgrade tree
        var tree = Pick(d.Trees, t => t.Owner);
        if (tree is null && nodeRows.Any(r => r.Level > 0))
        {
            tree = new UpgradeTree { Owner = "local" };
            d.Trees.Add(tree);
        }
        if (tree is not null)
            foreach (var row in nodeRows)
            {
                var node = tree.Nodes.FirstOrDefault(n => n.Id == row.Id);
                if (node is not null)
                {
                    if (row.Level == 0) tree.Nodes.Remove(node);
                    else node.Level = row.Level;
                }
                else if (row.Level > 0) tree.Nodes.Add(new TreeNode { Id = row.Id, Level = row.Level });
            }

        // Canisters (rows were created in file order)
        for (int i = 0; i < d.Canisters.Count && i < canisterRows.Count; i++)
            if (d.Canisters[i].Key == canisterRows[i].Key) d.Canisters[i].Amount = canisterRows[i].Amount;
    }

    // ---------------------------------------------------------------- backups

    void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (current is null) return;
        try
        {
            string dir = store.Backup(current);
            RefreshBackups();
            SetStatus(Loc.T("BackupCreated", Path.GetFileName(dir)), StatusKind.Good);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (current is null || BackupList.SelectedItem is not BackupItem item)
        {
            MessageBox.Show(this, Loc.T("SelectBackupFirst"), Loc.T("RestoreTitle"));
            return;
        }
        if (MessageBox.Show(this, Loc.T("RestoreQuestion", current.Title, item.When), Loc.T("RestoreTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            store.Backup(current);
            store.Restore(current, item.Path);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Loc.T("Restored", item.When), StatusKind.Good);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = current is null ? store.BackupRoot : Path.Combine(store.BackupRoot, current.Name);
        Directory.CreateDirectory(dir);
        OpenFolder(dir);
    }

    void OpenSaveFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(Directory.Exists(store.SessionsDir) ? store.SessionsDir : store.Root);

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (dirty && !ConfirmDiscard()) return;
        dirty = false;
        LoadSlots(current?.Name);
    }

    static void OpenFolder(string dir) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });

    // ---------------------------------------------------------------- raw import / export

    void ExportRaw_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Data is not { } data) return;
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("RawFilter"),
            FileName = $"{current.Name}-session-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllBytes(dlg.FileName, data.Raw);
        SetStatus(Loc.T("Exported", dlg.FileName), StatusKind.Good);
    }

    void ImportRaw_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Data is not { } data) return;
        var dlg = new OpenFileDialog { Filter = Loc.T("RawFilterAll") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            byte[] raw = File.ReadAllBytes(dlg.FileName);
            SessionData.Parse(raw); // must still contain a readable player block
            if (MessageBox.Show(this, Loc.T("ImportQuestion", Path.GetFileName(dlg.FileName), raw.Length, current.Title),
                    Loc.T("ImportTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string backup = store.WriteRaw(current, raw, data.Raw);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Loc.T("Imported", Path.GetFileName(backup)), StatusKind.Good);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    void ShowError(Exception ex)
    {
        SetStatus(ex.Message, StatusKind.Error);
        MessageBox.Show(this, ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // ---------------------------------------------------------------- snapshot (--snapshot)

    async void Snapshot(string dir)
    {
        Directory.CreateDirectory(dir);
        if (App.SwitchLanguage is { } lang)
            ChangeLanguage(lang == "de" ? UiLanguage.German : UiLanguage.English);
        for (int i = 0; i < Tabs.Items.Count; i++)
        {
            Tabs.SelectedIndex = i;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, $"tab{i}.png"));
            png.Save(fs);
        }
        Close();
    }

    // ---------------------------------------------------------------- window chrome

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = 0x22 << 16 | 0x19 << 8 | 0x16;                 // COLORREF 0x00BBGGRR of #161922
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR (Windows 11)
    }
}
