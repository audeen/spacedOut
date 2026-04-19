using System;
using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.Run;

/// <summary>Builds a procedural <see cref="RunDefinition"/> from a campaign seed.</summary>
public class RunGenerator
{
    // === Tuning =============================================================
    // All upper bounds are exclusive (Random.Next(min, max) convention).
    private const int ActCountMin = 2, ActCountMax = 4;             // 2 or 3 acts
    private const int ColumnsPerActMin = 3, ColumnsPerActMax = 5;   // 3 or 4 columns
    private const int NodesPerColumnMin = 2, NodesPerColumnMax = 4; // 2 or 3 nodes per column
    private const int MaxForwardEdgesPerNode = 2;                   // neighbors in the next column

    // Act-exit convergence: bias toward Station templates so exit hubs feel like
    // safe anchors (green ring on the map). If none is found in a handful of rolls
    // we fall back to any generic.
    private const int ActExitStationRollRetries = 5;

    /// <param name="skipTutorial">When true, the START node is seeded with a procedural
    /// mission from <see cref="MissionTemplateCatalog.GenericPool"/> instead of the
    /// scripted tutorial. Enables post-first-run loops where the tutorial is already done.</param>
    /// <param name="director">
    /// Optional <see cref="IRunDirector"/>. When supplied, the generator delegates template picks
    /// to <see cref="IRunDirector.WeightTemplate"/>, calls <see cref="IRunDirector.PostprocessNode"/>
    /// after appending each procedural node, and finishes with <see cref="IRunDirector.EnforceActGuarantees"/>.
    /// Pass <c>null</c> to keep the legacy uniform behaviour.
    /// </param>
    public RunDefinition GenerateRun(int seed, bool skipTutorial = false, IRunDirector? director = null)
    {
        var rng = new Random(seed);
        var def = new RunDefinition
        {
            Id = $"run_gen_{unchecked((uint)seed):x8}",
            StartNodeId = "START",
        };

        var directorRunState = new RunStateData { CampaignSeed = seed };
        var directorCtx = new RunDirectorContext(def, directorRunState, rng);

        int actCount = rng.Next(ActCountMin, ActCountMax);
        int depthCounter = 0;

        void TouchDepth(RunNodeData n)
        {
            n.Depth = depthCounter++;
        }

        RunNodeData start;
        if (skipTutorial)
        {
            var pool = MissionTemplateCatalog.GenericPool;
            var startTpl = director?.WeightTemplate(directorCtx, act: 0, depth: 0, pool, NodeIntent.Breather)
                           ?? pool[rng.Next(0, pool.Count)];
            var startType = MissionTemplateCatalog.MapToRunNodeType(startTpl.Type);
            start = new RunNodeData
            {
                Id = "START",
                Title = startTpl.Title,
                Type = startType,
                RiskRating = startTpl.Risk,
                LayoutX = 0.5f,
                LayoutY = 0.02f,
                NextNodeIds = new List<string>(),
                AssignedMissionId = startTpl.Id,
            };
        }
        else
        {
            start = new RunNodeData
            {
                Id = "START",
                Title = "Blindsprung",
                Type = RunNodeType.Start,
                RiskRating = 0,
                LayoutX = 0.5f,
                LayoutY = 0.02f,
                NextNodeIds = new List<string>(),
                AssignedMissionId = "tutorial_blindsprung",
            };
        }
        TouchDepth(start);
        def.Nodes["START"] = start;
        director?.PostprocessNode(directorCtx, start);

        string entryId = "START";

        float bandStep = 0.78f / actCount;

        for (int act = 0; act < actCount; act++)
        {
            string storyId = $"A{act}ST";
            float yBand = 0.06f + act * bandStep;

            var lastLayerIds = BuildActGenerics(
                def, rng, act, entryId, TouchDepth, yBand, bandStep, director, directorCtx);

            foreach (var id in lastLayerIds)
            {
                if (!def.Nodes.TryGetValue(id, out var node))
                    continue;
                if (!node.NextNodeIds.Contains(storyId))
                    node.NextNodeIds.Add(storyId);
            }

            // Procedural-only: the act-exit convergence is always a station-biased
            // generic. GameFeatures.StoryMissionsEnabled is intentionally not read
            // here — story_act_* templates remain dormant until a post-MVP re-enable.
            var exitTpl = PickActExitTemplate(rng, director, directorCtx, act, depthCounter);
            var exitRunType = MissionTemplateCatalog.MapToRunNodeType(exitTpl.Type);
            var convergenceNode = new RunNodeData
            {
                Id = storyId,
                Title = exitTpl.Title,
                Type = exitRunType,
                RiskRating = exitTpl.Risk,
                LayoutX = 0.5f,
                LayoutY = Math.Min(0.94f, yBand + bandStep - 0.02f),
                NextNodeIds = new List<string>(),
                AssignedMissionId = exitTpl.Id,
            };

            def.Nodes[storyId] = convergenceNode;
            TouchDepth(convergenceNode);
            director?.PostprocessNode(directorCtx, convergenceNode);

            entryId = storyId;
        }

        const string endId = "END";
        def.Nodes[entryId].NextNodeIds.Add(endId);
        def.Nodes[endId] = new RunNodeData
        {
            Id = endId,
            Title = "Sektor verlassen",
            Type = RunNodeType.End,
            RiskRating = 0,
            LayoutX = 0.5f,
            LayoutY = 0.96f,
            NextNodeIds = new List<string>(),
        };
        TouchDepth(def.Nodes[endId]);

        director?.EnforceActGuarantees(directorCtx, def);

        return def;
    }

    private static MissionTemplate PickActExitTemplate(
        Random rng,
        IRunDirector? director,
        RunDirectorContext directorCtx,
        int act,
        int depth)
    {
        var pool = MissionTemplateCatalog.GenericPool;
        if (director != null)
        {
            var picked = director.WeightTemplate(directorCtx, act, depth, pool, NodeIntent.SafeHaven);
            if (picked != null)
                return picked;
        }
        for (int i = 0; i < ActExitStationRollRetries; i++)
        {
            var t = pool[rng.Next(0, pool.Count)];
            if (t.Type == MissionType.Station)
                return t;
        }
        return pool[rng.Next(0, pool.Count)];
    }

    /// <summary>
    /// Builds one act as an FTL-style column grid: 3-4 columns of 2-3 nodes each,
    /// with short-diagonal forward edges (at most <see cref="MaxForwardEdgesPerNode"/>
    /// per source, sorted by Y-proximity). All first-column nodes fan out from
    /// <paramref name="entryId"/>; returns the last-column ids for the caller to
    /// wire into <c>A{act}ST</c>.
    /// </summary>
    private static List<string> BuildActGenerics(
        RunDefinition def,
        Random rng,
        int act,
        string entryId,
        Action<RunNodeData> touchDepth,
        float yBand,
        float bandStep,
        IRunDirector? director,
        RunDirectorContext directorCtx)
    {
        var pool = MissionTemplateCatalog.GenericPool.ToList();
        int stations = 0;
        int hostiles = 0;

        MissionTemplate PickTemplate(int currentDepth)
        {
            // Director gets first dibs, but generator still enforces hard caps (1 Station / 2 Hostile per act).
            if (director != null)
            {
                var hint = NodeIntent.Free;
                var picked = director.WeightTemplate(directorCtx, act, currentDepth, pool, hint);
                if (picked != null
                    && !(picked.Type == MissionType.Station && stations >= 1)
                    && !(picked.Type == MissionType.Hostile && hostiles >= 2))
                {
                    if (picked.Type == MissionType.Station) stations++;
                    else if (picked.Type == MissionType.Hostile) hostiles++;
                    return picked;
                }
            }

            int tries = 0;
            while (tries++ < 64)
            {
                int idx = rng.Next(0, pool.Count);
                var t = pool[idx];
                if (t.Type == MissionType.Station && stations >= 1)
                    continue;
                if (t.Type == MissionType.Hostile && hostiles >= 2)
                    continue;
                if (t.Type == MissionType.Station)
                    stations++;
                else if (t.Type == MissionType.Hostile)
                    hostiles++;
                return t;
            }
            return pool[rng.Next(0, pool.Count)];
        }

        void Wire(string from, string to)
        {
            if (!def.Nodes[from].NextNodeIds.Contains(to))
                def.Nodes[from].NextNodeIds.Add(to);
        }

        RunNodeData MakeGeneric(string id, MissionTemplate t, float lx, float ly)
        {
            var runType = MissionTemplateCatalog.MapToRunNodeType(t.Type);
            var node = new RunNodeData
            {
                Id = id,
                Title = t.Title,
                Type = runType,
                RiskRating = t.Risk,
                LayoutX = lx,
                LayoutY = ly,
                NextNodeIds = new List<string>(),
                AssignedMissionId = t.Id,
            };
            touchDepth(node);
            // Procedural nodes keep ResourceChanges* empty; rewards flow through per-POI
            // GrantRewards during the sector (stations sell fuel at the dock, etc.).
            def.Nodes[id] = node;
            director?.PostprocessNode(directorCtx, node);
            return node;
        }

        int columns = rng.Next(ColumnsPerActMin, ColumnsPerActMax);
        var grid = new List<List<string>>(columns);

        for (int col = 0; col < columns; col++)
        {
            int nodesInCol = rng.Next(NodesPerColumnMin, NodesPerColumnMax);
            var ids = new List<string>(nodesInCol);

            float lx = 0.12f + 0.76f * (columns <= 1 ? 0.5f : col / (float)(columns - 1));

            for (int row = 0; row < nodesInCol; row++)
            {
                string id = $"A{act}C{col}R{row}";
                var tpl = PickTemplate(currentDepth: def.Nodes.Count);
                float yLocal = nodesInCol > 1 ? row / (float)(nodesInCol - 1) : 0.5f;
                float ly = yBand + 0.04f + yLocal * (bandStep - 0.08f);
                MakeGeneric(id, tpl, lx, ly);
                ids.Add(id);
            }
            grid.Add(ids);
        }

        // Entry -> all nodes in the first column (2-3 initial choices).
        foreach (var id in grid[0])
            Wire(entryId, id);

        // Forward edges col -> col+1: each source picks up to MaxForwardEdgesPerNode
        // targets sorted by |LayoutY(target) - LayoutY(source)| to keep diagonals short
        // and avoid long cross-act wires.
        for (int col = 0; col < columns - 1; col++)
        {
            var sources = grid[col];
            var targets = grid[col + 1];
            var incoming = targets.ToDictionary(id => id, _ => 0);

            foreach (var s in sources)
            {
                float sy = def.Nodes[s].LayoutY;
                var ordered = targets
                    .OrderBy(t => Math.Abs(def.Nodes[t].LayoutY - sy))
                    .ToList();

                int connect = Math.Min(MaxForwardEdgesPerNode, ordered.Count);
                if (connect <= 0) continue;

                // Always wire the nearest target.
                var first = ordered[0];
                Wire(s, first);
                incoming[first]++;

                if (connect >= 2 && ordered.Count >= 2)
                {
                    // Second pick: usually the next-nearest (65 %), otherwise the one
                    // after (30 %) or wrap back to the next in line. Guarded by bounds.
                    double roll = rng.NextDouble();
                    int secondIdx = roll < 0.65 ? 1 : (ordered.Count >= 3 ? 2 : 1);
                    var second = ordered[secondIdx];
                    if (second != first)
                    {
                        Wire(s, second);
                        incoming[second]++;
                    }
                    else if (ordered.Count >= 2)
                    {
                        Wire(s, ordered[1]);
                        incoming[ordered[1]]++;
                    }
                }
            }

            // Reachability fix-up: any target without an incoming edge gets attached
            // to its Y-nearest source so no column-N+1 node is stranded.
            foreach (var kv in incoming)
            {
                if (kv.Value > 0) continue;
                var t = kv.Key;
                float ty = def.Nodes[t].LayoutY;
                var nearest = sources
                    .OrderBy(s => Math.Abs(def.Nodes[s].LayoutY - ty))
                    .First();
                Wire(nearest, t);
            }
        }

        return grid[columns - 1];
    }
}
