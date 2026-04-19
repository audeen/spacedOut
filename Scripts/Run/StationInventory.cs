using System;

namespace SpacedOut.Run;

/// <summary>
/// Per-station dock pricing and repair conversion. Deterministic from the run node id.
/// </summary>
/// <remarks>
/// Captain buys resources with <see cref="RunResourceIds.Credits"/> and can sell
/// Fuel/SpareParts/ScienceData back at <see cref="SellPriceFor"/> (below buy price).
/// Engineer converts <see cref="RunResourceIds.SpareParts"/> into hull points at the rate <see cref="HullPerPart"/>.
/// </remarks>
public class StationInventory
{
    public int FuelPrice { get; set; } = 4;
    public int PartsPrice { get; set; } = 6;
    public int DataPrice { get; set; } = 5;
    public int HullPerPart { get; set; } = 10;

    /// <summary>Seeded inventory: prices vary ± a couple of credits around the base values.</summary>
    public static StationInventory Build(string nodeId)
    {
        int seed = string.IsNullOrEmpty(nodeId) ? 0 : nodeId.GetHashCode();
        var rng = new Random(seed);
        return new StationInventory
        {
            FuelPrice = 3 + rng.Next(0, 3),   // 3..5
            PartsPrice = 5 + rng.Next(0, 4),  // 5..8
            DataPrice = 4 + rng.Next(0, 4),   // 4..7
            HullPerPart = 10,
        };
    }

    public int PriceFor(string resourceId) => resourceId switch
    {
        RunResourceIds.Fuel => FuelPrice,
        RunResourceIds.SpareParts => PartsPrice,
        RunResourceIds.ScienceData => DataPrice,
        _ => 0,
    };

    public bool IsBuyable(string resourceId) =>
        resourceId == RunResourceIds.Fuel
        || resourceId == RunResourceIds.SpareParts
        || resourceId == RunResourceIds.ScienceData;

    /// <summary>Credits per unit when the station buys resources from the ship (no Credits sales).</summary>
    public int SellPriceFor(string resourceId)
    {
        int buy = PriceFor(resourceId);
        if (buy <= 0) return 0;
        return Math.Max(1, buy / 2);
    }

    public bool IsSellable(string resourceId) => IsBuyable(resourceId);
}
