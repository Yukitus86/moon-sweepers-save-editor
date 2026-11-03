using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

public sealed record BackupItem(string Path, string Name, string When, string Size);

public partial class MainWindow : Window
{
    const decimal MaxBalance = 1_000_000_000_000m;

    readonly AppSettings settings = AppSettings.Load();
    readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    SaveStore store;

    readonly ObservableCollection<WalletRow> walletRows = [];
    readonly ObservableCollection<StatRow> statRows = [];
    readonly ObservableCollection<ToolRow> toolRows = [];
    readonly ObservableCollection<NodeRow> nodeRows = [];
    readonly ObservableCollection<CanisterRow> canisterRows = [];
    readonly ObservableCollection<HotbarSlotRow> hotbarRows = [];

    SaveSlot? current;
    bool loading, dirty, gameRunning, gameChecked, closeConfirmed;

    // Avalonia raises TextChanged after the fact (posted), so a balance shown by the editor itself is recognised by its text.
    string? balanceShown;

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
        store = new SaveStore(App.ResolveRoot(settings));

        InitializeComponent();

        WalletItems.ItemsSource = walletRows;
        StatItems.ItemsSource = statRows;
        ToolItems.ItemsSource = toolRows;
        NodeItems.ItemsSource = nodeRows;
        HotbarItems.ItemsSource = hotbarRows;
        TreeAllLevel.ItemsSource = Enumerable.Range(0, GameData.MaxTreeLevel + 1).ToList();
        TreeAllLevel.SelectedItem = 5;

        Row.AnyChanged += () =>
        {
            MarkDirty();
            UpdateCanisterSummary();
        };
        Opened += (_, _) =>
        {
            ApplyLanguageVisuals();
            CheckGame();
            LoadSlots(null);
            if (App.SnapshotDir is { } dir) Dispatcher.UIThread.Post(() => _ = Snapshot(dir), DispatcherPriority.Background);
        };
        Closing += Window_Closing;
        gameTimer.Tick += (_, _) => CheckGame();
        gameTimer.Start();
    }

    // ---------------------------------------------------------------- language

    static void ApplyCulture()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
    }

    void LanguageGerman_Click(object? sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.German);
    void LanguageEnglish_Click(object? sender, RoutedEventArgs e) => ChangeLanguage(UiLanguage.English);

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
            if (hadBalance && current?.CanEdit == true) ShowBalance(balance);

            // Choice / KindChoice names are computed, so the lists get new instances.
            var equipped = EquippedBox.SelectedItem as Choice;
            var choices = ToolChoices.Select(c => c.Id).ToList();
            ToolChoices.Clear();
            foreach (string id in choices) ToolChoices.Add(new Choice(id));
            EquippedBox.ItemsSource = ToolChoices.Where(c => c.Id != "").ToList();
            EquippedBox.SelectedItem = equipped is null ? null : new Choice(equipped.Id);

            int filter = CanisterFilter.SelectedIndex;
            CanisterFilter.ItemsSource = (CanisterFilter.ItemsSource as IEnumerable<KindChoice>)?.Select(k => new KindChoice(k.KindKey)).ToList();
            CanisterFilter.SelectedIndex = filter;

            if (SelectedSlotBox.ItemsSource is not null)
            {
                int index = SelectedSlotBox.SelectedIndex;
                SelectedSlotBox.ItemsSource = SlotLabels(hotbarRows.Count);
                SelectedSlotBox.SelectedIndex = index;
            }

            // Rebuilding the item containers re-creates their bindings with the new culture and texts.
            foreach (var rows in new IEnumerable<Row>[] { walletRows, statRows, toolRows, nodeRows, canisterRows, hotbarRows })
                foreach (var row in rows) row.RaiseAll();
            foreach (var items in new ItemsControl[] { WalletItems, StatItems, ToolItems, NodeItems, HotbarItems, CanisterItems, SlotList })
            {
                var selected = (items as SelectingItemsControl)?.SelectedItem;
                IEnumerable? source = items.ItemsSource;
                items.ItemsSource = null;
                items.ItemsSource = source;
                if (items is SelectingItemsControl s) s.SelectedItem = selected;
            }

            ShowHeader(current);
            if (current?.Data is { } data) InfoBox.Text = BuildInfo(current, data);
            RefreshBackups();
            UpdateCanisterSummary();
            ApplyLanguageVisuals();
            if (current is not null)
                SetStatus(dirty ? Text.Of("Unsaved") : Text.Of("SlotLoaded", current.Title), dirty ? StatusKind.Warn : StatusKind.Neutral);
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
        FlagDe.Classes.Set("active", german);
        FlagEn.Classes.Set("active", !german);
        ToolTip.SetTip(FlagDe, Text.Of("LanguageGerman"));
        ToolTip.SetTip(FlagEn, Text.Of("LanguageEnglish"));

        foreach (var button in QuickAmounts.Children.OfType<Button>())
        {
            string tag = (string)button.Tag!;
            bool set = tag.StartsWith('=');
            decimal value = decimal.Parse(set ? tag[1..] : tag, CultureInfo.InvariantCulture);
            button.Content = value switch
            {
                1_000_000 when !set => Text.Of("AddMillion"),
                10_000_000 when !set => Text.Of("AddTenMillion"),
                _ => (set ? "= " : "+ ") + value.ToString("N0", Culture),
            };
        }
    }

    static List<string> SlotLabels(int count) => Enumerable.Range(1, count).Select(n => Text.Of("HotbarSlot", n)).ToList();

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
            if (pick is null)
            {
                HeaderPath.Text = App.ShowPath(store.SessionsDir);
                SetStatus(Text.Of("NoSavesFoundHint"), StatusKind.Error);
            }
        }
        finally { loading = false; }
    }

    async void SlotList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || SlotList.SelectedItem is not SaveSlot slot || slot == current) return;
        if (dirty)
        {
            loading = true;
            SlotList.SelectedItem = current;
            loading = false;
            if (!await ConfirmDiscard()) return;
            loading = true;
            SlotList.SelectedItem = slot;
            loading = false;
        }
        loading = true;
        try { ShowSlot(slot); }
        finally { loading = false; }
    }

    void ShowHeader(SaveSlot? slot)
    {
        HeaderTitle.Text = slot?.Title ?? Text.Of("NoSlot");
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
            CanisterItems.ItemsSource = null;
            balanceShown = BalanceBox.Text = ""; BalanceError.Text = ""; InfoBox.Text = "";
            BackupList.ItemsSource = null;
            dirty = false;
            ShowHeader(slot);

            if (slot is null) { Tabs.IsEnabled = false; return; }
            RefreshBackups();

            if (!slot.CanEdit || slot.Data is not { } data)
            {
                Tabs.IsEnabled = false;
                SetStatus(Text.Of("CannotEdit", slot.Error ?? ""), StatusKind.Error);
                return;
            }

            FillMoney(data);
            FillTools(data);
            FillTree(data);
            FillCanisters(data);
            InfoBox.Text = BuildInfo(slot, data);

            Tabs.IsEnabled = true;
            SetStatus(Text.Of("SlotLoaded", slot.Title), StatusKind.Neutral);
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
        ShowBalance(data.Team?.Dollars ?? 0);
        foreach (var w in data.Wallet) walletRows.Add(new WalletRow(w));
        foreach (var s in data.Stats) statRows.Add(new StatRow(s));
        StatsMissing.IsVisible = data.Stats.Count == 0;
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
        EquippedBox.SelectedItem = levels is null ? null : new Choice(levels.Equipped);
        EquippedBox.IsEnabled = levels is not null;

        if (bar is not null)
        {
            for (int i = 0; i < bar.Slots.Count; i++) hotbarRows.Add(new HotbarSlotRow(i, bar.Slots[i]));
            SelectedSlotBox.ItemsSource = SlotLabels(bar.Slots.Count);
            SelectedSlotBox.SelectedIndex = Math.Clamp(bar.Selected, 0, Math.Max(0, bar.Slots.Count - 1));
        }
        else SelectedSlotBox.ItemsSource = null;
        SelectedSlotBox.IsEnabled = bar is not null;

        // Containers of the hotbar were created before ToolChoices was filled; rebuild them.
        HotbarItems.ItemsSource = null;
        HotbarItems.ItemsSource = hotbarRows;
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
        ApplyCanisterFilter();
    }

    static string BuildInfo(SaveSlot slot, SessionData data)
    {
        var sb = new StringBuilder();
        void Line(string key, object value) => sb.AppendLine($"{Text.Of(key) + ":",-24}{value}");
        Line("InfoSlotDir", App.ShowPath(slot.SlotDir));
        sb.AppendLine($"{"session.moon:",-24}{App.ShowPath(slot.SessionPath)}");
        Line("InfoAccount", App.ShowPath(Path.GetFileName(Path.GetDirectoryName(slot.SessionPath)) ?? ""));
        Line("InfoSize", $"{data.Raw.Length.ToString("N0", Culture)} {Text.Of("Bytes")}");
        sb.AppendLine($"{"SHA256:",-24}{MoonContainer.Hash(data.Raw)}");
        Line("InfoBlock", $"0x{data.Start:X} – 0x{data.End:X} ({(data.End - data.Start).ToString("N0", Culture)} {Text.Of("Bytes")})");
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
        var brush = (IBrush)this.FindResource(key)!;
        StatusDot.Fill = brush;
        StatusText.Foreground = kind == StatusKind.Neutral ? (IBrush)this.FindResource("Muted")! : brush;
    }

    void MarkDirty()
    {
        if (loading || current is null) return;
        if (!dirty) SetStatus(Text.Of("Unsaved"), StatusKind.Warn);
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
        if (running == gameRunning && gameChecked) return;
        gameChecked = true;
        gameRunning = running;
        GameRunningBanner.IsVisible = running;
        UpdateButtons();
    }

    Task<bool> ConfirmDiscard() => Dialog.Ask(this, Text.Of("DiscardQuestion"), Text.Of("DiscardTitle"));

    async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!dirty || closeConfirmed || App.SnapshotDir is not null) return;
        e.Cancel = true;
        if (!await ConfirmDiscard()) return;
        closeConfirmed = true;
        Close();
    }

    // ---------------------------------------------------------------- money

    void BalanceBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        BalanceError.Text = TryGetBalance(out _, out string? error) || string.IsNullOrEmpty(BalanceBox.Text) ? "" : error;
        if (!loading && BalanceBox.Text != balanceShown) MarkDirty();
    }

    void ShowBalance(decimal value) => balanceShown = BalanceBox.Text = value.ToString("N2", Culture);

    bool TryGetBalance(out decimal value, out string? error)
    {
        error = null;
        if (!Format.TryParseDecimal(BalanceBox.Text ?? "", out value)) { error = Text.Of("ErrNumber"); return false; }
        if (value < 0) { error = Text.Of("ErrNegative"); return false; }
        if (value > MaxBalance) { error = Text.Of("ErrTooLarge", MaxBalance.ToString("N0", Culture)); return false; }
        value = Math.Round(value, 2);
        return true;
    }

    void AddMoney_Click(object? sender, RoutedEventArgs e)
    {
        string tag = (string)((Button)sender!).Tag!;
        decimal now = TryGetBalance(out decimal v, out _) ? v : 0;
        decimal next = tag.StartsWith('=') ? decimal.Parse(tag[1..], CultureInfo.InvariantCulture) : now + decimal.Parse(tag, CultureInfo.InvariantCulture);
        BalanceBox.Text = Math.Clamp(next, 0, MaxBalance).ToString("N2", Culture);
    }

    // ---------------------------------------------------------------- tools

    void UnlockAllTools_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var r in toolRows.Where(r => r.CanToggle)) r.Owned = true;
    }

    void MaxAllTools_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var r in toolRows.Where(r => r.Upgradable)) r.Level = GameData.MaxToolLevel;
    }

    void AnyCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => MarkDirty();

    // ---------------------------------------------------------------- tree

    void NodeStep_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NodeRow row, Tag: string tag }) row.Level += int.Parse(tag, CultureInfo.InvariantCulture);
    }

    void TreeAll_Click(object? sender, RoutedEventArgs e)
    {
        int delta = int.Parse((string)((Button)sender!).Tag!, CultureInfo.InvariantCulture);
        foreach (var r in nodeRows) r.Level += delta;
    }

    void TreeSetAll_Click(object? sender, RoutedEventArgs e)
    {
        if (TreeAllLevel.SelectedItem is int level)
            foreach (var r in nodeRows) r.Level = level;
    }

    // ---------------------------------------------------------------- canisters

    IEnumerable<CanisterRow> FilteredCanisters
    {
        get
        {
            string kind = (CanisterFilter.SelectedItem as KindChoice)?.KindKey ?? "";
            return kind.Length == 0 ? canisterRows : canisterRows.Where(r => r.KindKey == kind);
        }
    }

    void CanisterFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ApplyCanisterFilter();

    void ApplyCanisterFilter()
    {
        CanisterItems.ItemsSource = FilteredCanisters.ToList();
        UpdateCanisterSummary();
    }

    void CanisterBulk_Click(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEdit();
        bool fill = (string)((Button)sender!).Tag! == "fill";
        foreach (var r in FilteredCanisters) r.Amount = fill ? r.Capacity : 0;
        UpdateCanisterSummary();
    }

    void UpdateCanisterSummary()
    {
        var rows = FilteredCanisters.ToList();
        double amount = rows.Sum(r => r.Amount), capacity = rows.Sum(r => r.Capacity);
        CanisterSummary.Text = Text.Of("StorageSummaryShort", rows.Count, amount.ToString("N0", Culture), capacity.ToString("N0", Culture));
    }

    /// <summary>Text boxes write on lost focus; move the focus so a value typed last is not lost.</summary>
    void CommitFocusedEdit()
    {
        if (FocusManager?.GetFocusedElement() is TextBox) SaveButton.Focus();
    }

    // ---------------------------------------------------------------- save

    async void Discard_Click(object? sender, RoutedEventArgs e)
    {
        if (current is not null && await ConfirmDiscard()) ShowSlot(current);
    }

    async void Save_Click(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEdit();
        if (current?.Data is not { } loaded) return;

        if (!TryGetBalance(out decimal balance, out string? error))
        {
            await Dialog.Info(this, Text.Of("BalancePrefix", error ?? ""), Text.Of("InvalidInput"));
            return;
        }

        try
        {
            var fresh = SessionData.Parse(loaded.Raw);
            Apply(fresh, balance);
            string backup = store.Save(current, fresh);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Text.Of("Saved", Path.GetFileName(backup)), StatusKind.Good);
        }
        catch (Exception ex)
        {
            SetStatus(Text.Of("SaveFailedStatus", ex.Message), StatusKind.Error);
            await Dialog.Info(this, ex.Message, Text.Of("SaveFailed"));
        }
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

            string equipped = (EquippedBox.SelectedItem as Choice)?.Id ?? levels.Equipped;
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

    async void BackupNow_Click(object? sender, RoutedEventArgs e)
    {
        if (current is null) return;
        try
        {
            string dir = store.Backup(current);
            RefreshBackups();
            SetStatus(Text.Of("BackupCreated", Path.GetFileName(dir)), StatusKind.Good);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    async void RestoreBackup_Click(object? sender, RoutedEventArgs e)
    {
        if (current is null || BackupList.SelectedItem is not BackupItem item)
        {
            await Dialog.Info(this, Text.Of("SelectBackupFirst"), Text.Of("RestoreTitle"));
            return;
        }
        if (!await Dialog.Ask(this, Text.Of("RestoreQuestion", current.Title, item.When), Text.Of("RestoreTitle"))) return;
        try
        {
            store.Backup(current);
            store.Restore(current, item.Path);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Text.Of("Restored", item.When), StatusKind.Good);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    void OpenBackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        string dir = current is null ? store.BackupRoot : Path.Combine(store.BackupRoot, current.Name);
        Directory.CreateDirectory(dir);
        OpenFolder(dir);
    }

    void OpenSaveFolder_Click(object? sender, RoutedEventArgs e) =>
        OpenFolder(Directory.Exists(store.SessionsDir) ? store.SessionsDir : store.Root);

    async void Reload_Click(object? sender, RoutedEventArgs e)
    {
        if (dirty && !await ConfirmDiscard()) return;
        dirty = false;
        LoadSlots(current?.Name);
    }

    async void ChooseSaveFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (dirty && !await ConfirmDiscard()) return;
        try
        {
            var start = Directory.Exists(store.Root) ? await StorageProvider.TryGetFolderFromPathAsync(store.Root) : null;
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Text.Of("ChooseSaveFolderTitle"),
                SuggestedStartLocation = start,
            });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;

            // Accept the MoonSweepers folder itself or its MoonSessions subfolder.
            if (Path.GetFileName(path.TrimEnd('/', '\\')) == "MoonSessions") path = Path.GetDirectoryName(path.TrimEnd('/', '\\'))!;
            if (!Directory.Exists(Path.Combine(path, "MoonSessions")))
            {
                await Dialog.Info(this, Text.Of("NotASaveFolder"), Text.Of("Error"));
                return;
            }

            settings.SaveRoot = path;
            settings.Save();
            store = new SaveStore(path);
            dirty = false;
            current = null;
            LoadSlots(null);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    static void OpenFolder(string dir)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                : new ProcessStartInfo("xdg-open") { ArgumentList = { dir } };
            Process.Start(start);
        }
        catch (Exception) { /* no file manager available */ }
    }

    // ---------------------------------------------------------------- raw import / export

    /// <summary>"Name|*.bin|Name2|*.*" (the Windows dialog format of the shared texts) as Avalonia file types.</summary>
    static List<FilePickerFileType> FileTypes(string key)
    {
        string[] parts = Text.Of(key).Split('|');
        var types = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
            types.Add(new FilePickerFileType(parts[i]) { Patterns = [parts[i + 1]] });
        return types;
    }

    async void ExportRaw_Click(object? sender, RoutedEventArgs e)
    {
        if (current?.Data is not { } data) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"{current.Name}-session-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
                DefaultExtension = "bin",
                FileTypeChoices = FileTypes("RawFilter"),
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            File.WriteAllBytes(path, data.Raw);
            SetStatus(Text.Of("Exported", path), StatusKind.Good);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    async void ImportRaw_Click(object? sender, RoutedEventArgs e)
    {
        if (current?.Data is not { } data) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = FileTypes("RawFilterAll"),
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

            byte[] raw = File.ReadAllBytes(path);
            SessionData.Parse(raw); // must still contain a readable player block
            if (!await Dialog.Ask(this, Text.Of("ImportQuestion", Path.GetFileName(path), raw.Length, current.Title), Text.Of("ImportTitle"))) return;
            string backup = store.WriteRaw(current, raw, data.Raw);
            dirty = false;
            LoadSlots(current.Name);
            SetStatus(Text.Of("Imported", Path.GetFileName(backup)), StatusKind.Good);
        }
        catch (Exception ex) { await ShowError(ex); }
    }

    async Task ShowError(Exception ex)
    {
        SetStatus(ex.Message, StatusKind.Error);
        await Dialog.Info(this, ex.Message, Text.Of("Error"));
    }

    // ---------------------------------------------------------------- snapshot (--snapshot)

    async Task Snapshot(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int i = 0; i < Tabs.ItemCount; i++)
        {
            Tabs.SelectedIndex = i;
            await Task.Delay(400);
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bmp = new RenderTargetBitmap(size);
            bmp.Render(this);
            bmp.Save(Path.Combine(dir, $"tab{i}.png"));
        }
        Close();
    }
}
