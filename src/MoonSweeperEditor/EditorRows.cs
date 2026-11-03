using System.ComponentModel;
using System.Runtime.CompilerServices;
using MoonSweeperEditor.Core;

namespace MoonSweeperEditor;

/// <summary>
/// Base for editable rows; every change marks the editor as dirty. Translated labels are computed on read,
/// so <see cref="RaiseAll"/> after a language switch refreshes them.
/// </summary>
public abstract class Row : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? AnyChanged;

    /// <summary>While true, writes from bindings are ignored (item containers are being rebuilt).</summary>
    public static bool Frozen { get; set; }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Frozen || EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        AnyChanged?.Invoke();
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void RaiseAll() => Raise(string.Empty);
}

/// <summary>Hotbar / equipped-tool choice; "" is the empty slot.</summary>
public sealed record Choice(string Id)
{
    public string Name => Id.Length == 0 ? Loc.T("EmptySlot") : GameData.ToolName(Id);
    public override string ToString() => Name;
}

public sealed class WalletRow(WalletEntry e) : Row
{
    decimal dollars = e.Dollars, pending = e.PendingKilograms;

    public string Owner { get; } = e.Owner;
    public bool IsTeam => Owner == "team";
    public bool IsOther => !IsTeam;
    public string Label => IsTeam ? Loc.T("TeamAccount") : Loc.T("PlayerAccount", Owner);
    public decimal Dollars { get => dollars; set => Set(ref dollars, Math.Max(0, value)); }
    public decimal PendingKilograms { get => pending; set => Set(ref pending, Math.Max(0, value)); }
}

public sealed class StatRow(StatValue s) : Row
{
    double value = s.Value;

    public int Offset { get; } = s.Offset;
    public string Label => s.Label;
    public double Value { get => value; set => Set(ref this.value, Math.Max(0, value)); }
}

public sealed class ToolRow : Row
{
    bool owned;
    int level;

    public required string Id { get; init; }
    public bool Upgradable { get; init; }
    public bool AlwaysOwned { get; init; }

    public string Name => GameData.ToolName(Id);
    public bool CanToggle => !AlwaysOwned;
    public string OwnedHint => AlwaysOwned ? Loc.T("StarterTool") : "";
    public IReadOnlyList<int> LevelOptions { get; } = Enumerable.Range(GameData.MinToolLevel, GameData.MaxToolLevel).ToList();

    public bool Owned { get => owned; set => Set(ref owned, value); }
    public int Level { get => level; set => Set(ref level, Math.Clamp(value, GameData.MinToolLevel, GameData.MaxToolLevel)); }

    internal void InitState(bool isOwned, int lvl) { owned = isOwned; level = lvl; }
}

public sealed class NodeRow : Row
{
    int level;

    public required string Id { get; init; }
    public required string CategoryKey { get; init; }

    public string Category => Loc.T(CategoryKey);
    public string Name => GameData.NodeName(Id);
    public int Level
    {
        get => level;
        set { if (Set(ref level, Math.Clamp(value, 0, GameData.MaxTreeLevel))) Raise(nameof(LevelText)); }
    }
    public string LevelText => level == 0 ? Loc.T("NotBought") : Loc.T("LevelN", level);

    internal void InitLevel(int lvl) => level = lvl;
}

public sealed class CanisterRow(Canister c) : Row
{
    double amount = c.Amount;

    public string Key { get; } = c.Key;
    public string KindKey { get; } = GameData.CanisterKindKey(c.Key);
    public string Kind => Loc.T(KindKey);
    public double Capacity { get; } = c.Capacity;
    public double Amount
    {
        get => amount;
        set { if (Set(ref amount, Math.Clamp(value, 0, Math.Max(0, Capacity)))) Raise(nameof(Fill)); }
    }
    public string Fill => Capacity > 0 ? (amount / Capacity).ToString("P0", Loc.Instance.Culture) : "–";
}

public sealed class HotbarSlotRow(int index, string tool) : Row
{
    string tool = tool;

    public string Label => Loc.T("HotbarSlot", index + 1);
    public string Tool { get => tool; set => Set(ref tool, value ?? ""); }
}

/// <summary>Storage filter entry; KindKey "" means all.</summary>
public sealed record KindChoice(string KindKey)
{
    public string Name => KindKey.Length == 0 ? Loc.T("All") : Loc.T(KindKey);
    public override string ToString() => Name;
}
