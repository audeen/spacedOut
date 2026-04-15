using SpacedOut.Poi;

namespace SpacedOut.State;

/// <summary>Which contacts appear in the Gunner station list and may be locked.</summary>
public static class GunnerContactRules
{
    public static bool IsSelectableForCombat(Contact c) =>
        c.Type == ContactType.Hostile || c.IsDesignated;

    public static bool IsDrillablePoiForGunnerList(Contact c)
    {
        if (string.IsNullOrEmpty(c.PoiType)) return false;
        if (c.Discovery != DiscoveryState.Scanned || c.IsDestroyed) return false;
        if (c.PoiPhase != PoiPhase.Analyzed) return false;
        var bp = PoiBlueprintCatalog.GetOrNull(c.PoiType);
        return bp != null && bp.RequiresDrill;
    }
}
