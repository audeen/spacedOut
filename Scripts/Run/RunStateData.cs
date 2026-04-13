using System.Collections.Generic;

namespace SpacedOut.Run;

public class RunStateData
{
    public string RunId { get; set; } = "";

    /// <summary>One seed for the whole campaign: levels use <see cref="RunSeed.DeriveLevelSeed"/>; future tree generator can use the same value.</summary>
    public int CampaignSeed { get; set; }

    public string? CurrentNodeId { get; set; }
    public string? LastResolvedNodeId { get; set; }
    public int CurrentDepth { get; set; }

    public Dictionary<string, RunNodeRuntime> NodeStates { get; set; } = new();

    /// <summary>Nodes locked because a sibling branch was chosen (no backtracking to parallel paths).</summary>
    public HashSet<string> BranchLockedNodeIds { get; set; } = new();

    public HashSet<string> Flags { get; set; } = new();
    public Dictionary<string, int> Resources { get; set; } = new();
    public List<string> VisitedNodeIds { get; set; } = new();
}
