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

    /// <summary>Hüllenintegrität, die zwischen Sektoren/Missionen erhalten bleibt (nicht Run-Ressource).</summary>
    public float CurrentHull { get; set; } = 100f;

    /// <summary>
    /// Ids of <see cref="SpacedOut.Mission.NodeEvent"/>s that have already fired this run (one-shot catalog events).
    /// Populated by <see cref="SpacedOut.Mission.NodeEventCatalog.PickForNode"/> to prevent repeats.
    /// </summary>
    public HashSet<string> FiredEventIds { get; set; } = new();

    /// <summary>
    /// M7: in-run flags driven by the active loadout perk (e.g. <c>free_first_scan</c>).
    /// Applied by <see cref="SpacedOut.Meta.UnlockApplier"/> at run start and consumed
    /// by gameplay systems (e.g. <see cref="RunController.ScanNode"/>).
    /// </summary>
    public HashSet<string> PerkFlags { get; set; } = new();

    /// <summary>M7 (perk_armor): when set, replaces the 100-hull cap for repairs/spawning.</summary>
    public float? MaxHullOverride { get; set; }

    /// <summary>
    /// Director-owned pacing state (hostile streak, breather counter, tension, intent map).
    /// Updated by <see cref="SpacedOut.Run.IRunDirector"/> on node enter/resolve.
    /// </summary>
    public PacingState Pacing { get; set; } = new();
}
