namespace MoonSweeperEditor.Core;

public sealed record ToolInfo(string Id, string En, string De, bool Upgradable, bool AlwaysOwned)
{
    public string Name => Loc.Instance.Language == UiLanguage.German ? De : En;
}

/// <summary>A node of the upgrade tree. Category is a Loc key.</summary>
public sealed record NodeInfo(string Id, string Category, string En, string De)
{
    public string Name => Loc.Instance.Language == UiLanguage.German ? De : En;
}

/// <summary>Ids taken from the game's string literals (global-metadata.dat).</summary>
public static class GameData
{
    public const int MaxToolLevel = 3;
    public const int MinToolLevel = 1;
    public const int MaxTreeLevel = 10;

    public static readonly IReadOnlyList<ToolInfo> Tools =
    [
        new("toothbrush", "Toothbrush", "Zahnbürste", true, true),
        new("nailbrush", "Nail brush", "Nagelbürste", true, false),
        new("hand_brush", "Hand brush", "Handfeger", true, false),
        new("household_broom", "Household broom", "Haushaltsbesen", true, false),
        new("wide_broom", "Wide broom", "Breiter Besen", true, false),
        new("extra_wide_broom", "Extra wide broom", "Extra breiter Besen", true, false),
        new("building_tool", "Building tool", "Bauwerkzeug", false, false),
    ];

    public static readonly IReadOnlyList<NodeInfo> Nodes =
    [
        new("clean.width", "CatClean", "Sweep width", "Kehrbreite"),
        new("clean.depth", "CatClean", "Sweep depth", "Kehrtiefe"),
        new("clean.reach", "CatClean", "Reach", "Reichweite"),
        new("clean.surge", "CatClean", "Surge", "Kehrschub"),
        new("clean.compactor", "CatClean", "Compactor", "Verdichter"),
        new("clean.autopack", "CatClean", "Auto-pack", "Auto-Verpacken"),
        new("eco.payout", "CatEco", "Payout", "Auszahlung"),
        new("eco.bounty", "CatEco", "Bounty", "Prämie"),
        new("eco.discount", "CatEco", "Discount", "Rabatt"),
        new("eco.exchange", "CatEco", "Exchange rate", "Wechselkurs"),
        new("gear.belt", "CatGear", "Belt", "Gürtel"),
        new("gear.boots", "CatGear", "Boots", "Stiefel"),
        new("gear.jetpack", "CatGear", "Jetpack", "Jetpack"),
        new("gear.nightvision", "CatGear", "Night vision", "Nachtsicht"),
        new("suit.stride", "CatSuit", "Stride", "Laufschritt"),
        new("suit.jump", "CatSuit", "Jump", "Sprungkraft"),
        new("suit.lamp", "CatSuit", "Helmet lamp", "Helmlampe"),
        new("suit.exo", "CatSuit", "Exoskeleton", "Exoskelett"),
        new("logi.transfer", "CatLogi", "Transfer", "Transfer"),
        new("logi.tether", "CatLogi", "Tether", "Halteseil"),
        new("logi.throw", "CatLogi", "Throw", "Werfen"),
        new("logi.express", "CatLogi", "Express", "Express"),
    ];

    public static string ToolName(string id) => Tools.FirstOrDefault(t => t.Id == id)?.Name ?? id;

    public static string NodeName(string id) => Nodes.FirstOrDefault(n => n.Id == id)?.Name ?? id;

    /// <summary>Loc key of the storage type behind a canister key such as "robot/12".</summary>
    public static string CanisterKindKey(string key) => key.Split('/')[0] switch
    {
        "garagesilo" => "KindSilo",
        "garage" => "KindGarage",
        "robotdock" => "KindDock",
        "robot" => "KindRobot",
        "catcher" => "KindCatcher",
        var other => other,
    };
}
