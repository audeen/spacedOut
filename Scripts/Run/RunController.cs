using System;
using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.Run;

/// <summary>Coordinates run graph progression: reveal rules, OR convergence, flags, resources.</summary>
public class RunController
{
    private readonly Dictionary<string, List<string>> _predecessors = new();

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
                [RunResourceIds.Hull] = 10,
                [RunResourceIds.SpareParts] = 2,
                [RunResourceIds.ScienceData] = 0,
                [RunResourceIds.Ammo] = 1,
            },
            VisitedNodeIds = new List<string>(),
            BranchLockedNodeIds = new HashSet<string>(),
            NodeStates = new Dictionary<string, RunNodeRuntime>(),
        };

        foreach (var id in definition.Nodes.Keys)
        {
            CurrentRun.NodeStates[id] = new RunNodeRuntime
            {
                NodeId = id,
                State = RunNodeState.Hidden,
                Resolution = NodeResolution.Unresolved,
                WasVisited = false,
                Knowledge = NodeKnowledgeState.Unknown,
            };
        }

        ReevaluateNodeStates();
    }

    /// <summary>Future: Funk/Radar/Anomalie — aktuell deaktiviert (kein Effekt).</summary>
    public bool CanScanNode(string nodeId) => false;

    /// <summary>Future: Funk/Radar/Anomalie — aktuell No-op.</summary>
    public void ScanNode(string nodeId) { }

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
    /// Fog-of-war: <see cref="NodeKnowledgeState"/> für strukturell sichtbare Knoten.
    /// </summary>
    public void RefreshNodeKnowledge()
    {
        var revealed = ComputeRevealedNodeIds();
        var identified = new HashSet<string> { CurrentDefinition.StartNodeId };

        foreach (var id in CurrentRun.VisitedNodeIds)
            identified.Add(id);

        foreach (var kvp in CurrentRun.NodeStates)
        {
            if (kvp.Value.WasVisited)
                identified.Add(kvp.Key);
        }

        if (!string.IsNullOrEmpty(CurrentRun.CurrentNodeId))
        {
            foreach (var n in NeighborIds(CurrentRun.CurrentNodeId))
                identified.Add(n);
        }
        else if (!string.IsNullOrEmpty(CurrentRun.LastResolvedNodeId))
        {
            foreach (var n in NeighborIds(CurrentRun.LastResolvedNodeId))
                identified.Add(n);
        }

        foreach (var id in CurrentDefinition.Nodes.Keys)
        {
            var rt = CurrentRun.NodeStates[id];
            if (rt.State == RunNodeState.Hidden)
                continue;

            if (identified.Contains(id))
            {
                rt.Knowledge = NodeKnowledgeState.Identified;
                continue;
            }

            if (NeighborIds(id).Any(identified.Contains))
                rt.Knowledge = NodeKnowledgeState.Detected;
            else
                rt.Knowledge = NodeKnowledgeState.Unknown;
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

    /// <summary>
    /// Revealed: start node, its outgoing neighbors (initial frontier), and successors of any completed node.
    /// </summary>
    private HashSet<string> ComputeRevealedNodeIds()
    {
        var revealed = new HashSet<string> { CurrentDefinition.StartNodeId };
        if (CurrentDefinition.Nodes.TryGetValue(CurrentDefinition.StartNodeId, out var start))
        {
            foreach (var n in start.NextNodeIds)
                revealed.Add(n);
        }

        foreach (var kvp in CurrentRun.NodeStates)
        {
            if (kvp.Value.State != RunNodeState.Completed)
                continue;
            if (!CurrentDefinition.Nodes.TryGetValue(kvp.Key, out var data))
                continue;
            foreach (var next in data.NextNodeIds)
                revealed.Add(next);
        }

        return revealed;
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
        var revealed = ComputeRevealedNodeIds();

        foreach (var id in CurrentDefinition.Nodes.Keys)
        {
            var rt = CurrentRun.NodeStates[id];
            var data = CurrentDefinition.Nodes[id];

            if (rt.State is RunNodeState.Completed or RunNodeState.Failed)
                continue;

            if (!revealed.Contains(id))
            {
                rt.State = RunNodeState.Hidden;
                continue;
            }

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

        if (res != null)
        {
            foreach (var kvp in res)
            {
                CurrentRun.Resources.TryGetValue(kvp.Key, out var v);
                CurrentRun.Resources[kvp.Key] = Math.Max(0, v + kvp.Value);
            }
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
