using System;
using System.Linq;

namespace SpacedOut.Poi;

/// <summary>
/// Weighted <see cref="PoiRewardProfile"/> selection and trap overlay (matches sector debug POI spawn rules).
/// </summary>
public static class PoiRewardRoller
{
    /// <summary>
    /// Picks a non-trap profile by weight, then may replace with the trap profile per <see cref="PoiBlueprint.TrapChance"/>.
    /// </summary>
    public static string RollRewardProfile(PoiBlueprint bp, Random rng)
    {
        if (bp.RewardProfiles.Length == 0) return "";

        string profileId = RollWeightedNonTrap(bp, rng);
        if (bp.TrapChance > 0 && rng.NextDouble() < bp.TrapChance)
        {
            var trap = bp.RewardProfiles.FirstOrDefault(p => p.IsTrap);
            if (trap != null) return trap.ProfileId;
        }

        return profileId;
    }

    private static string RollWeightedNonTrap(PoiBlueprint bp, Random rng)
    {
        var nonTrap = bp.RewardProfiles.Where(p => !p.IsTrap).ToArray();
        if (nonTrap.Length == 0) return "";
        float total = nonTrap.Sum(p => p.Weight);
        float roll = (float)rng.NextDouble() * total;
        float accum = 0;
        foreach (var p in nonTrap)
        {
            accum += p.Weight;
            if (roll <= accum) return p.ProfileId;
        }

        return nonTrap[^1].ProfileId;
    }
}
