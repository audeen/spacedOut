using System;

namespace SpacedOut.Run;

/// <summary>
/// Single campaign seed → deterministic per-node level seeds (and later: procedural tree).
/// </summary>
public static class RunSeed
{
    /// <summary>Derives a level generator seed from campaign seed + node id (each node differs, same inputs → same level).</summary>
    public static int DeriveLevelSeed(int campaignSeed, string nodeId) =>
        HashCode.Combine(campaignSeed, nodeId);

    /// <summary>Default: time-based, same style as LevelGenerator standalone.</summary>
    public static int CreateNewCampaignSeed() =>
        (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
}
