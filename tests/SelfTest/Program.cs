using MoonSweeperEditor.Core;

if (args.Length == 2 && args[0] == "--e2e") return EndToEnd(args[1]);

// Read-only checks: every session.moon below the given folders must parse, round-trip byte-identically,
// and survive an edit + re-encode cycle (done in memory only).
int failures = 0, files = 0;
foreach (string root in args)
foreach (string path in Directory.EnumerateFiles(root, "session.moon*", SearchOption.AllDirectories))
{
    if (path.EndsWith(".editor-tmp")) continue;
    files++;
    try
    {
        byte[] raw = MoonContainer.ReadRaw(path);
        var data = SessionData.Parse(raw);
        if (!data.Build().AsSpan().SequenceEqual(raw)) throw new Exception("round-trip differs");

        // Edit everything the UI can touch, then re-encode and parse again.
        data.Team!.Dollars = 123456789.12m;
        foreach (var s in data.Stats) s.Value += 1;
        foreach (var inv in data.Inventories)
            foreach (var t in GameData.Tools.Where(t => !t.AlwaysOwned))
                if (!inv.Tools.Any(x => x.Id == t.Id)) inv.Tools.Add(new OwnedTool { Id = t.Id });
        foreach (var set in data.ToolLevels)
        {
            foreach (var t in GameData.Tools.Where(t => t.Upgradable))
                if (!set.Levels.Any(l => l.Id == t.Id)) set.Levels.Add(new ToolLevel { Id = t.Id, Level = 1 });
            set.Levels.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var l in set.Levels) l.Level = GameData.MaxToolLevel;
        }
        foreach (var tree in data.Trees)
            foreach (var n in GameData.Nodes)
                if (tree.Nodes.FirstOrDefault(x => x.Id == n.Id) is { } node) node.Level++;
                else tree.Nodes.Add(new TreeNode { Id = n.Id, Level = 1 });
        foreach (var c in data.Canisters) c.Amount = c.Capacity;

        byte[] edited = data.Build();
        var again = SessionData.Parse(MoonContainer.Decode(MoonContainer.Encode(edited)));
        if (again.Team!.Dollars != 123456789.12m) throw new Exception("balance not kept");
        if (again.Stats.Count != data.Stats.Count || again.Stats.Zip(data.Stats).Any(p => p.First.Value != p.Second.Value)) throw new Exception("stats not kept");
        if (again.Trees[0].Nodes.Count < GameData.Nodes.Count) throw new Exception("tree not kept");
        if (!again.Raw.AsSpan(again.End).SequenceEqual(raw.AsSpan(data.End))) throw new Exception("tail changed");

        Console.WriteLine($"OK   {path}\n     start=0x{data.Start:X} end=0x{data.End:X} size={raw.Length}, " +
            $"wallet={data.Team.Dollars}, tools={data.Inventories.Sum(i => i.Tools.Count)}, nodes={data.Trees.Sum(t => t.Nodes.Count)}, " +
            $"canisters={data.Canisters.Count}, stats={string.Join(" | ", data.Stats.Select(s => s.Label + "=" + s.Value))}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {path}: {ex.Message}");
    }
}
Console.WriteLine($"{files} files, {failures} failures");
return failures == 0 ? 0 : 1;

// Writes through SaveStore exactly like the editor does. Only use on a COPY of the save folder.
static int EndToEnd(string root)
{
    var store = new SaveStore(root);
    var slot = store.LoadSlots().First(s => s.CanEdit);
    byte[] before = File.ReadAllBytes(slot.SessionPath!);
    var data = SessionData.Parse(slot.Data!.Raw);
    data.Team!.Dollars = 4242424.42m;
    data.Trees[0].Nodes[0].Level += 1;
    data.Canisters[1].Amount = data.Canisters[1].Capacity;
    string backup = store.Save(slot, data);

    var reloaded = store.LoadSlots().First(s => s.Name == slot.Name);
    Check(reloaded.Data!.Team!.Dollars == 4242424.42m, "balance written");
    Check(reloaded.Dollars == 4242424.42, "slot.json dollars updated");
    Check(reloaded.Data.Trees[0].Nodes[0].Level == slot.Data.Trees[0].Nodes[0].Level + 1, "tree written");
    Check(File.ReadAllBytes(Path.Combine(backup, "session.moon")).AsSpan().SequenceEqual(before), "backup has old file");

    // Stale data must be refused (file changed on disk since load).
    try { store.Save(slot, SessionData.Parse(slot.Data.Raw)); Check(false, "stale save refused"); }
    catch (InvalidOperationException) { Check(true, "stale save refused"); }

    store.Restore(reloaded, backup);
    Check(File.ReadAllBytes(slot.SessionPath!).AsSpan().SequenceEqual(before), "restore puts old file back");
    Console.WriteLine("end-to-end OK");
    return 0;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) Environment.Exit(1);
    }
}
