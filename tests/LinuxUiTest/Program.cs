using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MoonSweeperEditor;
using MoonSweeperEditor.Core;

// Edits a copy of a save through the real Linux window (headless) and checks what ends up on disk.
// Never point this at the real save folder.
if (args.Length != 1 || !Directory.Exists(Path.Combine(args[0], "MoonSessions")))
{
    Console.WriteLine("usage: LinuxUiTest <copy of the MoonSweepers folder>");
    return 2;
}
string root = args[0];
App.ParseArgs(["--root", root, "--lang", "en"]);
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) failures++;
}
void Pump() => Dispatcher.UIThread.RunJobs();
void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }

var window = new MainWindow();
window.Show();
Pump();
T Named<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new InvalidOperationException(name);
IEnumerable<T> All<T>(Control under) => under.GetVisualDescendants().OfType<T>();
var save = Named<Button>("SaveButton");
var discard = Named<Button>("DiscardButton");
var tabs = Named<TabControl>("Tabs");

var slot = (SaveSlot)Named<ListBox>("SlotList").SelectedItem!;
var before = slot.Data!;
Check(!discard.IsEnabled, "nothing is unsaved after loading");

// Language switch must not count as a change.
Click(Named<Button>("FlagDe"));
Check(!discard.IsEnabled, "switch to German keeps the editor clean");
Check(Named<TextBox>("BalanceBox").Text!.Contains(','), $"balance shown in German format ({Named<TextBox>("BalanceBox").Text})");
Click(Named<Button>("FlagEn"));
Check(!discard.IsEnabled, "switch back to English keeps the editor clean");

// Money: balance and first statistic value.
Named<TextBox>("BalanceBox").Text = "5,000,000.25";
Pump();
Check(discard.IsEnabled && save.IsEnabled, "typing a balance marks the editor dirty");
tabs.SelectedIndex = 0; Pump();
var statBox = All<TextBox>(Named<ItemsControl>("StatItems")).First();
statBox.Focus(); statBox.Text = "1,234.50"; save.Focus(); Pump();

// Tools: hand brush to level 1, hotbar slot 2 = toothbrush.
tabs.SelectedIndex = 1; Pump();
var toolRow = All<ContentPresenter>(Named<ItemsControl>("ToolItems")).First(p => p.DataContext is ToolRow { Id: "hand_brush" });
All<ComboBox>(toolRow).First().SelectedItem = 1;
var hotbarCombo = All<ComboBox>(Named<ItemsControl>("HotbarItems")).ElementAt(1);
hotbarCombo.SelectedItem = new Choice("toothbrush");
Pump();

// Tree: "+" on the first node.
tabs.SelectedIndex = 2; Pump();
var firstNode = All<ContentPresenter>(Named<ItemsControl>("NodeItems")).First();
var node = (NodeRow)firstNode.DataContext!;
int oldLevel = node.Level;
Click(All<Button>(firstNode).Last());

// Storage: robot/2 contents = 10.
tabs.SelectedIndex = 3; Pump();
var canisterRow = All<ListBoxItem>(Named<ListBox>("CanisterItems")).First(i => i.DataContext is CanisterRow { Key: "robot/2" });
var amountBox = All<TextBox>(canisterRow).First();
amountBox.Focus(); amountBox.Text = "10"; save.Focus(); Pump();

Click(save);
Check(!discard.IsEnabled, "saved without error (" + Named<TextBlock>("StatusText").Text + ")");

var after = SessionData.Parse(MoonContainer.ReadRaw(slot.SessionPath!));
Check(after.Team!.Dollars == 5_000_000.25m, $"balance on disk = {after.Team.Dollars}");
Check(after.Stats.Count == 0 || after.Stats[0].Value == 1234.5, $"statistic on disk = {after.Stats.FirstOrDefault()?.Value}");
var levels = after.ToolLevels.First(l => l.Owner == "local");
Check(levels.Levels.First(l => l.Id == "hand_brush").Level == 1, "hand brush level 1");
Check(after.Hotbars.First(h => h.Owner == "local").Slots[1] == "toothbrush", "hotbar slot 2 = toothbrush");
int expected = Math.Min(oldLevel + 1, GameData.MaxTreeLevel);
Check(after.Trees.First(t => t.Owner == "local").Nodes.First(n => n.Id == node.Id).Level == expected, $"{node.Id} level {expected}");
Check(after.Canisters.First(c => c.Key == "robot/2").Amount == Math.Min(10, after.Canisters.First(c => c.Key == "robot/2").Capacity), "robot/2 = 10 kg");
Check(after.Canisters.Where(c => c.Key != "robot/2").Select(c => c.Amount)
           .SequenceEqual(before.Canisters.Where(c => c.Key != "robot/2").Select(c => c.Amount)), "other storage unchanged");
Check(new SaveStore(root).ListBackups(slot).Count > 0, "backup written");
Check(File.ReadAllText(slot.SlotJsonPath).Contains("5000000.25"), "slot.json dollars updated");

window.Close();
Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
