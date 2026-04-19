using System;
using System.Collections.Generic;
using System.Linq;
using SpacedOut.Mission;

namespace SpacedOut.Run;

/// <summary>Coordinates run graph progression: reveal rules, OR convergence, flags, resources.</summary>
public class RunController
{
    /// <summary>ScienceData cost per manual <see cref="ScanNode"/>.</summary>
    public const int ScanCostScience = 1;

    /// <summary>
    /// Editable horizon for the <see cref="IRunDirector"/>: nodes with <c>Depth > CurrentDepth() + MaxScanDepthAhead</c>
    /// remain invisible for scans, so the director can reshape them without breaking player knowledge.
    /// </summary>
    public const int MaxScanDepthAhead = 2;

    private readonly Dictionary<string, List<string>> _predecessors = new();

    /// <summary>Fired after <see cref="EnterNode"/> finished updating run state. Used by orchestration to bind the director.</summary>
    public Action<string>? OnNodeEnteredHook { get; set; }

    /// <summary>Fired after <see cref="ResolveNode"/> finished updating run state. Used by orchestration to let the director adjust upcoming nodes.</summary>
    public Action<string, NodeResolution>? OnNodeResolvedHook { get; set; }

    public RunDefinition CurrentDefinition { get; private set; } = new();
    public RunStateData CurrentRun { get; private set; } = new();

    /// <param name="campaignSeed">If null, a new seed is created (see <see cref="RunSeed.CreateNewCampaignSeed"/>).</param>
    public void StartRun(RunDefinition definition, int? campaignSeed = null)
    {
        CurrentDefinition = definition;
        _predecessors.Clear();
        BuildPredecessorMap(definition);

        int seed = campaignSeed ?? RunSeed.CreateNewCampaignSeed();
        var runId = $"{definition.Id}_{seed:x8}_{Guid.NewGuid():N}";
        CurrentRun = new RunStateData
        {
            RunId = runId,
            CampaignSeed = seed,
            CurrentNodeId = null,
            LastResolvedNodeId = null,
            CurrentDepth = 0,
            Flags = new HashSet<string>(),
            Resources = new Dictionary<string, int>
            {
                [RunResourceIds.SpareParts] = 2,
                [RunResourceIds.ScienceData] = 3,
                [RunResourceIds.Fuel] = 10,
                [RunResourceIds.Credits] = 0,
            },
            VisitedNodeIds = new List<string>(),
            BranchLockedNodeIds = new HashSet<string>(),
            NodeStates = new Dictionary<string, RunNodeRuntime>(),
            CurrentHull = 100f,
        };

        foreach (var id in definition.Nodes.Keys)
        {
            CurrentRun.NodeStates[id] = new RunNodeRuntime
            {
                NodeId = id,
                State = RunNodeState.Visible,
                Resolution = NodeResolution.Unresolved,
                WasVisited = false,
                Knowledge = NodeKnowledgeState.Silhouette,
            };
        }

        ReevaluateNodeStates();
    }

    /// <summary>M7: in-run perk flag that lets the very first <see cref="ScanNode"/> skip the ScienceData cost.</summary>
    public const string FreeFirstScanFlag = "free_first_scan";

    /// <summary>
    /// Effective player depth for scan/director scoping: the depth of the active node when one is
    /// entered, otherwise the depth of the last resolved node, otherwise 0 (start).
    /// </summary>
    public int CurrentDepth()
    {
        if (!string.IsNullOrEmpty(CurrentRun.CurrentNodeId)
            && CurrentDefinition.Nodes.TryGetValue(CurrentRun.CurrentNodeId, out var cn))
            return cn.Depth;
        if (!string.IsNullOrEmpty(CurrentRun.LastResolvedNodeId)
            && CurrentDefinition.Nodes.TryGetValue(CurrentRun.LastResolvedNodeId, out var ln))
            return ln.Depth;
        return CurrentRun.CurrentDepth;
    }

    /// <summary>True if <paramref name="nodeId"/> lies within <see cref="MaxScanDepthAhead"/> of the
    /// current player depth (or behind it). Nodes beyond stay editable for the director.</summary>
    public bool IsWithinScanRange(string nodeId)
    {
        if (!CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
            return false;
        return data.Depth <= CurrentDepth() + MaxScanDepthAhead;
    }

    /// <summary>True if the node exists, is not already Scanned, lies within <see cref="MaxScanDepthAhead"/>,
    /// and the run has enough ScienceData (or holds the <see cref="FreeFirstScanFlag"/> perk).</summary>
    public bool CanScanNode(string nodeId)
    {
        if (!CurrentRun.NodeStates.TryGetValue(nodeId, out var rt))
            return false;
        if (rt.Knowledge == NodeKnowledgeState.Scanned)
            return false;
        if (!IsWithinScanRange(nodeId))
            return false;
        if (CurrentRun.PerkFlags.Contains(FreeFirstScanFlag))
            return true;
        CurrentRun.Resources.TryGetValue(RunResourceIds.ScienceData, out int have);
        return have >= ScanCostScience;
    }

    /// <summary>Spends ScienceData and promotes the node to <see cref="NodeKnowledgeState.Scanned"/>.
    /// M7: when <see cref="FreeFirstScanFlag"/> is set, the first scan consumes the flag instead of ScienceData.</summary>
    public bool ScanNode(string nodeId)
    {
        if (!CanScanNode(nodeId))
            return false;

        CurrentRun.Resources.TryGetValue(RunResourceIds.ScienceData, out int have);
        bool freeScan = CurrentRun.PerkFlags.Remove(FreeFirstScanFlag);
        if (!freeScan)
            CurrentRun.Resources[RunResourceIds.ScienceData] = Math.Max(0, have - ScanCostScience);

        var rt = CurrentRun.NodeStates[nodeId];
        rt.Knowledge = NodeKnowledgeState.Scanned;
        if (freeScan)
            Godot.GD.Print($"[Run] Scan '{nodeId}' -> Scanned (Perk: free_first_scan).");
        else
            Godot.GD.Print($"[Run] Scan '{nodeId}' -> Scanned (ScienceData {have}->{have - ScanCostScience}).");
        return true;
    }

    private IEnumerable<string> NeighborIds(string nodeId)
    {
        if (CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
        {
            foreach (var n in data.NextNodeIds)
                yield return n;
        }

        if (_predecessors.TryGetValue(nodeId, out var preds))
        {
            foreach (var p in preds)
                yield return p;
        }
    }

    /// <summary>
    /// Three-tier knowledge: <see cref="NodeKnowledgeState.Scanned"/> is permanent (auto for
    /// current + visited + start node, manual via <see cref="ScanNode"/>),
    /// <see cref="NodeKnowledgeState.Sighted"/> are the neighbors of the current (or last
    /// resolved) node, everything else is <see cref="NodeKnowledgeState.Silhouette"/>.
    /// </summary>
    public void RefreshNodeKnowledge()
    {
        var autoScanned = new HashSet<string> { CurrentDefinition.StartNodeId };

        foreach (var id in CurrentRun.VisitedNodeIds)
            autoScanned.Add(id);

        foreach (var kvp in CurrentRun.NodeStates)
        {
            if (kvp.Value.WasVisited)
                autoScanned.Add(kvp.Key);
        }

        if (!string.IsNullOrEmpty(CurrentRun.CurrentNodeId))
            autoScanned.Add(CurrentRun.CurrentNodeId);

        var sighted = new HashSet<string>();
        string? anchor = CurrentRun.CurrentNodeId
            ?? CurrentRun.LastResolvedNodeId
            ?? CurrentDefinition.StartNodeId;
        if (!string.IsNullOrEmpty(anchor))
        {
            foreach (var n in NeighborIds(anchor))
                sighted.Add(n);
        }

        foreach (var id in CurrentDefinition.Nodes.Keys)
        {
            var rt = CurrentRun.NodeStates[id];

            // Scanned is persistent: never demote.
            if (rt.Knowledge == NodeKnowledgeState.Scanned)
                continue;

            if (autoScanned.Contains(id))
            {
                rt.Knowledge = NodeKnowledgeState.Scanned;
                continue;
            }

            rt.Knowledge = sighted.Contains(id)
                ? NodeKnowledgeState.Sighted
                : NodeKnowledgeState.Silhouette;
        }
    }

    private void BuildPredecessorMap(RunDefinition definition)
    {
        foreach (var node in definition.Nodes.Values)
        {
            foreach (var nextId in node.NextNodeIds)
            {
                if (!_predecessors.TryGetValue(nextId, out var list))
                {
                    list = new List<string>();
                    _predecessors[nextId] = list;
                }
                if (!list.Contains(node.Id))
                    list.Add(node.Id);
            }
        }
    }

    private bool HasCompletedPredecessor(string nodeId)
    {
        if (nodeId == CurrentDefinition.StartNodeId)
            return true;

        if (!_predecessors.TryGetValue(nodeId, out var preds) || preds.Count == 0)
            return true;

        return preds.Any(p =>
            CurrentRun.NodeStates.TryGetValue(p, out var rt) &&
            rt.State == RunNodeState.Completed);
    }

    public bool MeetsNodeConditions(RunNodeData data)
    {
        foreach (var f in data.RequiredFlags)
        {
            if (!CurrentRun.Flags.Contains(f))
                return false;
        }

        foreach (var f in data.ForbiddenFlags)
        {
            if (CurrentRun.Flags.Contains(f))
                return false;
        }

        return true;
    }

    public void ReevaluateNodeStates()
    {
        foreach (var id in CurrentDefinition.Nodes.Keys)
        {
            var rt = CurrentRun.NodeStates[id];
            var data = CurrentDefinition.Nodes[id];

            if (rt.State is RunNodeState.Completed or RunNodeState.Failed)
                continue;

            if (CurrentRun.BranchLockedNodeIds.Contains(id))
            {
                rt.State = RunNodeState.Locked;
                continue;
            }

            if (id == CurrentDefinition.StartNodeId)
            {
                rt.State = RunNodeState.Reachable;
                continue;
            }

            if (!HasCompletedPredecessor(id))
            {
                rt.State = RunNodeState.Visible;
                continue;
            }

            if (!MeetsNodeConditions(data))
            {
                rt.State = RunNodeState.Locked;
                continue;
            }

            rt.State = RunNodeState.Reachable;
        }

        RefreshNodeKnowledge();
    }

    public bool CanEnterNode(string nodeId)
    {
        if (!CurrentRun.NodeStates.TryGetValue(nodeId, out var rt))
            return false;
        return rt.State == RunNodeState.Reachable;
    }

    /// <summary>Reachable node and enough <see cref="RunResourceIds.Fuel"/> for this node type.</summary>
    public bool CanAffordNodeEntry(string nodeId)
    {
        if (!CanEnterNode(nodeId))
            return false;
        if (!CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
            return false;
        int cost = NodeEncounterConfig.GetFuelCostFor(data.Type);
        if (cost <= 0)
            return true;
        CurrentRun.Resources.TryGetValue(RunResourceIds.Fuel, out int fuel);
        return fuel >= cost;
    }

    /// <summary>True if any reachable, non-branch-locked node can still be entered (fuel check).</summary>
    public bool HasAnyAffordableReachableNode()
    {
        foreach (var kv in CurrentRun.NodeStates)
        {
            var id = kv.Key;
            var rt = kv.Value;
            if (rt.State != RunNodeState.Reachable)
                continue;
            if (CurrentRun.BranchLockedNodeIds.Contains(id))
                continue;
            if (CanAffordNodeEntry(id))
                return true;
        }

        return false;
    }

    public void EnterNode(string nodeId)
    {
        if (!CanEnterNode(nodeId))
            return;

        CurrentRun.CurrentNodeId = nodeId;
        var data = CurrentDefinition.Nodes[nodeId];
        CurrentRun.CurrentDepth = data.Depth;

        var rt = CurrentRun.NodeStates[nodeId];
        rt.WasVisited = true;

        LockSiblingBranches(nodeId);
        ReevaluateNodeStates();

        OnNodeEnteredHook?.Invoke(nodeId);
    }

    /// <summary>
    /// For each predecessor P of <paramref name="enteredNodeId"/>, locks every other direct child of P
    /// (same-fork siblings). Prevents taking parallel branches (e.g. N2→N4 then entering N1).
    /// </summary>
    private void LockSiblingBranches(string enteredNodeId)
    {
        if (!_predecessors.TryGetValue(enteredNodeId, out var preds) || preds.Count == 0)
            return;

        foreach (var predId in preds)
        {
            if (!CurrentDefinition.Nodes.TryGetValue(predId, out var parent))
                continue;

            foreach (var siblingId in parent.NextNodeIds)
            {
                if (siblingId == enteredNodeId)
                    continue;

                CurrentRun.BranchLockedNodeIds.Add(siblingId);
            }
        }
    }

    public void ResolveNode(string nodeId, NodeResolution resolution)
    {
        if (resolution == NodeResolution.Unresolved)
            return;
        if (CurrentRun.CurrentNodeId != nodeId)
            return;
        if (!CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
            return;

        var rt = CurrentRun.NodeStates[nodeId];
        rt.Resolution = resolution;

        ApplyResolutionEffects(data, resolution);

        if (resolution == NodeResolution.Failure)
            rt.State = RunNodeState.Failed;
        else
            rt.State = RunNodeState.Completed;

        if (!CurrentRun.VisitedNodeIds.Contains(nodeId))
            CurrentRun.VisitedNodeIds.Add(nodeId);

        CurrentRun.LastResolvedNodeId = nodeId;
        CurrentRun.CurrentNodeId = null;
        CurrentRun.CurrentDepth = data.Depth;

        ReevaluateNodeStates();

        OnNodeResolvedHook?.Invoke(nodeId, resolution);
    }

    public void ApplyResolutionEffects(RunNodeData data, NodeResolution resolution)
    {
        List<string>? flags = null;
        Dictionary<string, int>? res = null;

        switch (resolution)
        {
            case NodeResolution.Success:
                flags = data.GrantedFlagsOnSuccess;
                res = data.ResourceChangesOnSuccess;
                break;
            case NodeResolution.PartialSuccess:
                flags = data.GrantedFlagsOnPartial;
                res = data.ResourceChangesOnPartial;
                break;
            case NodeResolution.Failure:
                flags = data.GrantedFlagsOnFailure;
                res = data.ResourceChangesOnFailure;
                break;
            case NodeResolution.Skipped:
                break;
        }

        if (flags != null)
        {
            foreach (var f in flags)
                CurrentRun.Flags.Add(f);
        }

        if (res != null && res.Count > 0)
        {
            var parts = new List<string>();
            foreach (var kvp in res)
            {
                CurrentRun.Resources.TryGetValue(kvp.Key, out var v);
                int before = v;
                int after = Math.Max(0, before + kvp.Value);
                CurrentRun.Resources[kvp.Key] = after;
                parts.Add($"{kvp.Key} {before}→{after} ({(kvp.Value >= 0 ? "+" : "")}{kvp.Value})");
            }
            Godot.GD.Print($"[Run] Node '{data.Id}' {resolution}: {string.Join(", ", parts)}");
        }
    }

    public List<RunNodeData> GetReachableNodes() =>
        CurrentDefinition.Nodes.Values
            .Where(n => CurrentRun.NodeStates[n.Id].State == RunNodeState.Reachable)
            .ToList();

    public List<RunNodeData> GetVisibleNodes() =>
        CurrentDefinition.Nodes.Values
            .Where(n => CurrentRun.NodeStates[n.Id].State == RunNodeState.Visible)
            .ToList();

    public RunNodeRuntime GetNodeRuntime(string nodeId) =>
        CurrentRun.NodeStates[nodeId];
}
