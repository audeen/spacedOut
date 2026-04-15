using System;
using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.Run;

/// <summary>Builds a procedural <see cref="RunDefinition"/> from a campaign seed.</summary>
public class RunGenerator
{
    public RunDefinition GenerateRun(int seed)
    {
        var rng = new Random(seed);
        var def = new RunDefinition
        {
            Id = $"run_gen_{unchecked((uint)seed):x8}",
            StartNodeId = "START",
        };

        int actCount = rng.Next(2, 4);
        int depthCounter = 0;

        void TouchDepth(RunNodeData n)
        {
            n.Depth = depthCounter++;
        }

        var start = new RunNodeData
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
        TouchDepth(start);
        def.Nodes["START"] = start;

        string entryId = "START";

        for (int act = 0; act < actCount; act++)
        {
            string storyId = $"A{act}ST";
            int genCount = rng.Next(2, 5);
            float yBand = 0.06f + act * (0.78f / actCount);

            var lastLayerIds = BuildActGenerics(
                def, rng, act, entryId, genCount, TouchDepth, yBand);

            foreach (var id in lastLayerIds)
            {
                if (!def.Nodes.TryGetValue(id, out var node))
                    continue;
                if (!node.NextNodeIds.Contains(storyId))
                    node.NextNodeIds.Add(storyId);
            }

            var storyTemplate = MissionTemplateCatalog.GetOrNull($"story_act_{act + 1}")
                                ?? MissionTemplateCatalog.GetOrNull("story_act_1")
                                ?? throw new InvalidOperationException("Missing story template.");

            def.Nodes[storyId] = new RunNodeData
            {
                Id = storyId,
                Title = storyTemplate.Title,
                Type = RunNodeType.Story,
                RiskRating = storyTemplate.Risk,
                LayoutX = 0.5f,
                LayoutY = Math.Min(0.94f, yBand + 0.2f),
                NextNodeIds = new List<string>(),
                AssignedMissionId = storyTemplate.Id,
                StoryFunction = storyTemplate.StoryFunction,
            };
            TouchDepth(def.Nodes[storyId]);

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

        return def;
    }

    private static List<string> BuildActGenerics(
        RunDefinition def,
        Random rng,
        int act,
        string entryId,
        int genCount,
        Action<RunNodeData> touchDepth,
        float yBand)
    {
        var pool = MissionTemplateCatalog.GenericPool.ToList();
        int stations = 0;
        int hostiles = 0;

        MissionTemplate PickTemplate()
        {
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

        string MkId(string suffix) => $"A{act}_{suffix}";

        float SpreadX(int i, int n) => n <= 1 ? 0.5f : 0.15f + 0.7f * (i / (float)(n - 1));

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
            def.Nodes[id] = node;
            return node;
        }

        if (genCount == 2)
        {
            var t0 = PickTemplate();
            var t1 = PickTemplate();
            string g0 = MkId("G0");
            string g1 = MkId("G1");
            MakeGeneric(g0, t0, 0.38f, yBand + 0.04f);
            MakeGeneric(g1, t1, 0.62f, yBand + 0.1f);
            Wire(entryId, g0);
            Wire(g0, g1);
            return new List<string> { g1 };
        }

        if (genCount == 3)
        {
            var t0 = PickTemplate();
            var t1 = PickTemplate();
            var t2 = PickTemplate();
            string g0 = MkId("G0");
            string g1 = MkId("G1");
            string g2 = MkId("G2");
            MakeGeneric(g0, t0, SpreadX(0, 2), yBand + 0.03f);
            MakeGeneric(g1, t1, SpreadX(1, 2), yBand + 0.03f);
            MakeGeneric(g2, t2, 0.5f, yBand + 0.11f);
            Wire(entryId, g0);
            Wire(entryId, g1);
            Wire(g0, g2);
            Wire(g1, g2);
            return new List<string> { g2 };
        }

        // genCount == 4: parallel two-layer
        var a0 = PickTemplate();
        var a1 = PickTemplate();
        var b0 = PickTemplate();
        var b1 = PickTemplate();
        string p0 = MkId("P0");
        string p1 = MkId("P1");
        string q0 = MkId("Q0");
        string q1 = MkId("Q1");
        MakeGeneric(p0, a0, SpreadX(0, 2), yBand + 0.03f);
        MakeGeneric(p1, a1, SpreadX(1, 2), yBand + 0.03f);
        MakeGeneric(q0, b0, SpreadX(0, 2), yBand + 0.12f);
        MakeGeneric(q1, b1, SpreadX(1, 2), yBand + 0.12f);
        Wire(entryId, p0);
        Wire(entryId, p1);
        Wire(p0, q0);
        Wire(p1, q1);
        return new List<string> { q0, q1 };
    }
}
