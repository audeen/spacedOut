using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.Meta;

/// <summary>What kind of unlock a <see cref="UnlockDef"/> represents.</summary>
public enum UnlockKind
{
    /// <summary>Active loadout perk; one is selectable in the main menu before starting a run.</summary>
    Perk,
    /// <summary>Passive content pack that adds catalog entries (e.g. extra <c>NodeEvent</c>s).</summary>
    EventPack,
}

/// <summary>Static catalog entry for a purchasable unlock.</summary>
public class UnlockDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Cost { get; init; }
    public UnlockKind Kind { get; init; }
}

/// <summary>
/// Authored list of meta-progression unlocks (M7 v1: 5 perks + 2 content packs).
/// All ids are stable strings so save files survive catalog reorderings.
/// </summary>
public static class UnlockCatalog
{
    public static readonly UnlockDef PerkSalvage = new()
    {
        Id = "perk_salvage",
        Name = "Salvage Specialist",
        Description = "Run startet mit +2 Ersatzteilen.",
        Cost = 50,
        Kind = UnlockKind.Perk,
    };

    public static readonly UnlockDef PerkHaul = new()
    {
        Id = "perk_haul",
        Name = "Long Hauler",
        Description = "Run startet mit +3 Treibstoff.",
        Cost = 50,
        Kind = UnlockKind.Perk,
    };

    public static readonly UnlockDef PerkResearch = new()
    {
        Id = "perk_research",
        Name = "Forscher",
        Description = "Run startet mit +2 Forschungsdaten. Erster Knoten-Scan im Run ist kostenlos.",
        Cost = 75,
        Kind = UnlockKind.Perk,
    };

    public static readonly UnlockDef PerkTrader = new()
    {
        Id = "perk_trader",
        Name = "Handelsverbindung",
        Description = "Run startet mit +50 Credits.",
        Cost = 60,
        Kind = UnlockKind.Perk,
    };

    public static readonly UnlockDef PerkArmor = new()
    {
        Id = "perk_armor",
        Name = "Verstärkter Rumpf",
        Description = "Hüllen-Maximum auf 110 (Reparatur kann darüber hinaus kappen).",
        Cost = 75,
        Kind = UnlockKind.Perk,
    };

    public static readonly UnlockDef PackRareAnomalies = new()
    {
        Id = "pack_rare_anomalies",
        Name = "Anomalien-Archiv",
        Description = "Schaltet seltene Anomalie-Events frei (treten zusätzlich an Anomaly-Knoten auf).",
        Cost = 100,
        Kind = UnlockKind.EventPack,
    };

    public static readonly UnlockDef PackPirateDrama = new()
    {
        Id = "pack_pirate_drama",
        Name = "Piraten-Dossier",
        Description = "Schaltet zusätzliche Distress/Side-Events mit Piraten-Drama frei.",
        Cost = 100,
        Kind = UnlockKind.EventPack,
    };

    private static readonly List<UnlockDef> _all = new()
    {
        PerkSalvage, PerkHaul, PerkResearch, PerkTrader, PerkArmor,
        PackRareAnomalies, PackPirateDrama,
    };

    public static IReadOnlyList<UnlockDef> All => _all;

    public static UnlockDef? GetById(string id) => _all.Find(u => u.Id == id);

    public static IEnumerable<UnlockDef> Perks => _all.Where(u => u.Kind == UnlockKind.Perk);
    public static IEnumerable<UnlockDef> EventPacks => _all.Where(u => u.Kind == UnlockKind.EventPack);
}
