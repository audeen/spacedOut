using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Mission;
using SpacedOut.Run;
using TC = SpacedOut.Shared.ThemeColors;

namespace SpacedOut.MainScreen;

/// <summary>Draws the sector run graph (nodes + edges) from <see cref="RunController"/> state.</summary>
public partial class RunMapDisplay : Control
{
    private RunController? _run;
    private float _pulse;

    /// <summary>Node id to outline (selected on map before Enter).</summary>
    public string? HighlightNodeId { get; set; }

    public bool InteractionEnabled { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void UpdateRun(RunController run, string? highlightNodeId = null)
    {
        _run = run;
        HighlightNodeId = highlightNodeId;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _run == null) return;
        _pulse = (_pulse + (float)delta * 2.5f) % (MathF.PI * 2f);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!InteractionEnabled || _run == null) return;
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            var local = mb.Position;
            string? hit = HitTestNode(local);
            if (hit != null)
                EmitSignal(SignalName.RunNodeClicked, hit);
        }
    }

    [Signal] public delegate void RunNodeClickedEventHandler(string nodeId);

    private string? HitTestNode(Vector2 localPos)
    {
        if (_run == null) return null;
        var rect = GetRect();
        float ox = 40f;
        float oy = 28f;
        float mapW = rect.Size.X - 80f;
        float mapH = rect.Size.Y - 50f;
        const float r = 22f;

        foreach (var n in _run.CurrentDefinition.Nodes.Values)
        {
            var p = NodePos(n, ox, oy, mapW, mapH);
            if (localPos.DistanceTo(p) <= r)
                return n.Id;
        }

        return null;
    }

    private static Vector2 NodePos(RunNodeData n, float ox, float oy, float mapW, float mapH) =>
        new(ox + n.LayoutX * mapW, oy + n.LayoutY * mapH);

    public override void _Draw()
    {
        if (_run == null) return;

        var rect = GetRect();
        float w = rect.Size.X;
        float h = rect.Size.Y;

        var bg = new Color(0.03f, 0.05f, 0.10f, 0.92f);
        DrawRect(new Rect2(0, 0, w, h), bg);
        DrawRect(new Rect2(0, 0, w, h), new Color(TC.Cyan, 0.35f), false, 1.5f);

        DrawString(ThemeDB.FallbackFont, new Vector2(10, 18),
            $"RUN · {_run.CurrentDefinition.Id}",
            HorizontalAlignment.Left, -1, 12, new Color(TC.Cyan, 0.85f));

        float ox = 40f;
        float oy = 28f;
        float mapW = w - 80f;
        float mapH = h - 50f;

        var def = _run.CurrentDefinition;
        var states = _run.CurrentRun.NodeStates;

        DrawActBands(ox, oy, mapW, mapH, CountActs(def));

        foreach (var node in def.Nodes.Values)
        {
            foreach (var nextId in node.NextNodeIds)
            {
                if (!def.Nodes.TryGetValue(nextId, out var next)) continue;
                var a = NodePos(node, ox, oy, mapW, mapH);
                var b = NodePos(next, ox, oy, mapW, mapH);
                bool onPath = states[node.Id].State == RunNodeState.Completed &&
                               states[nextId].State is RunNodeState.Completed or RunNodeState.Reachable;
                bool avail = states[node.Id].State == RunNodeState.Completed &&
                             states[nextId].State == RunNodeState.Reachable;
                Color col;
                float width;
                if (onPath || avail)
                {
                    float pulse = avail ? 0.45f + MathF.Sin(_pulse) * 0.2f : 0.55f;
                    col = new Color(TC.Cyan, pulse);
                    width = avail ? 2.5f : 2f;
                }
                else
                {
                    col = new Color(0.2f, 0.3f, 0.5f, 0.35f);
                    width = 1f;
                }

                DrawLine(a, b, col, width);
            }
        }

        foreach (var node in def.Nodes.Values)
        {
            var p = NodePos(node, ox, oy, mapW, mapH);
            states.TryGetValue(node.Id, out var rt);
            var know = rt?.Knowledge ?? NodeKnowledgeState.Silhouette;

            switch (know)
            {
                case NodeKnowledgeState.Silhouette:
                    DrawSilhouetteNode(p, rt);
                    break;
                case NodeKnowledgeState.Sighted:
                    DrawInfoNode(p, node, rt, scanned: false);
                    break;
                case NodeKnowledgeState.Scanned:
                    DrawInfoNode(p, node, rt, scanned: true);
                    break;
            }
        }
    }

    /// <summary>Silhouette: grey border-only circle, tiny dot, no type/fuel info.</summary>
    private void DrawSilhouetteNode(Vector2 p, RunNodeRuntime? rt)
    {
        const float radius = 14f;
        var border = new Color(0.42f, 0.47f, 0.55f, 0.85f);
        var fill = new Color(0.10f, 0.12f, 0.17f, 0.85f);

        DrawCircle(p, radius, fill);
        DrawArc(p, radius, 0f, MathF.Tau, 32, border, 1.5f);

        if (_run != null && _run.CurrentRun.CurrentNodeId == rt?.NodeId)
            DrawArc(p, radius + 6f, 0f, MathF.Tau, 32, new Color(TC.Yellow, 0.9f), 2f);
        else if (HighlightNodeId == rt?.NodeId)
            DrawArc(p, radius + 5f, 0f, MathF.Tau, 32, new Color(TC.White, 0.55f), 1.5f);

        DrawString(ThemeDB.FallbackFont, p + new Vector2(-3, 4), "·",
            HorizontalAlignment.Left, -1, 12, new Color(0.8f, 0.85f, 0.9f, 0.8f));
    }

    /// <summary>Sighted + Scanned share the type-fill node body; Scanned adds a white accent ring + title.</summary>
    private void DrawInfoNode(Vector2 p, RunNodeData node, RunNodeRuntime? rt, bool scanned)
    {
        Color fill = TypeFill(node.Type);
        Color border = TC.DimWhite;
        float radius = 18f;

        switch (rt?.State)
        {
            case RunNodeState.Completed:
                fill = new Color(fill, 0.55f);
                border = TC.Green;
                break;
            case RunNodeState.Failed:
                fill = new Color(TC.Red, 0.45f);
                border = TC.Red;
                break;
            case RunNodeState.Reachable:
                border = TC.Cyan;
                radius = 20f;
                break;
            case RunNodeState.Locked:
                border = TC.Orange;
                fill = new Color(fill, 0.5f);
                break;
            case RunNodeState.Visible:
                border = TC.Dim;
                fill = new Color(fill, 0.42f);
                break;
        }

        DrawCircle(p, radius + 2f, new Color(border, 0.35f));
        DrawCircle(p, radius, fill);

        if (scanned)
        {
            // White accent ring marking permanent scan.
            DrawArc(p, radius + 1.5f, 0f, MathF.Tau, 40, new Color(TC.White, 0.85f), 1.5f);
        }

        if (_run != null && _run.CurrentRun.CurrentNodeId == node.Id)
            DrawArc(p, radius + 6f, 0f, MathF.Tau, 32, new Color(TC.Yellow, 0.9f), 2f);
        else if (HighlightNodeId == node.Id)
            DrawArc(p, radius + 5f, 0f, MathF.Tau, 32, new Color(TC.White, 0.55f), 1.5f);

        // Center glyph: Scanned shows the title, Sighted shows the type icon.
        string glyph = scanned ? ShortTitle(node.Title ?? node.Id, 6) : TypeGlyph(node.Type);
        int glyphSize = scanned ? 10 : 13;
        DrawString(ThemeDB.FallbackFont, p + new Vector2(-radius * 0.6f, 4), glyph,
            HorizontalAlignment.Left, -1, glyphSize, TC.White);

        // Fuel preview below the node.
        int fuel = NodeEncounterConfig.GetFuelCostFor(node.Type);
        string fuelLabel = fuel <= 0 ? "F:-" : $"F:{fuel}";
        DrawString(ThemeDB.FallbackFont, p + new Vector2(-10, radius + 12), fuelLabel,
            HorizontalAlignment.Left, -1, 9, new Color(TC.Cyan, 0.85f));
    }

    /// <summary>Renders soft Y-banded rectangles + SEKTOR N labels, matching <c>RunGenerator</c>'s act layout.</summary>
    private void DrawActBands(float ox, float oy, float mapW, float mapH, int actCount)
    {
        if (actCount <= 0) return;
        var font = ThemeDB.FallbackFont;
        float totalH = mapH;
        // Layout in RunGenerator: yBand = 0.06f + act * (0.78f / actCount); act spans ~0.2 in Y.
        float bandStep = 0.78f / actCount;
        for (int act = 0; act < actCount; act++)
        {
            float yStart = 0.06f + act * bandStep;
            float yEnd = Math.Min(0.94f, yStart + bandStep);
            float py = oy + yStart * totalH;
            float ph = (yEnd - yStart) * totalH;
            var tint = act % 2 == 0
                ? new Color(TC.Cyan.R, TC.Cyan.G, TC.Cyan.B, 0.05f)
                : new Color(TC.Blue.R, TC.Blue.G, TC.Blue.B, 0.05f);
            DrawRect(new Rect2(ox - 8f, py, mapW + 16f, ph), tint);

            DrawString(font, new Vector2(ox - 32f, py + 14f), $"SEKTOR {act + 1}",
                HorizontalAlignment.Left, -1, 10, new Color(TC.Cyan, 0.55f));
        }
    }

    /// <summary>Counts acts by scanning generated node IDs (A0ST, A1ST, ...).</summary>
    private static int CountActs(RunDefinition def)
    {
        int max = -1;
        foreach (var id in def.Nodes.Keys)
        {
            if (id.Length > 1 && id[0] == 'A' && char.IsDigit(id[1]))
            {
                int val = id[1] - '0';
                if (val > max) max = val;
            }
        }
        return max + 1;
    }

    private static string ShortTitle(string title, int maxChars)
    {
        if (title.Length <= maxChars) return title;
        return title.Substring(0, Math.Max(1, maxChars - 1)) + "…";
    }

    private static string TypeGlyph(RunNodeType t) => t switch
    {
        RunNodeType.Start => "▶",
        RunNodeType.Story => "◆",
        RunNodeType.Side => "◇",
        RunNodeType.Station => "⛽",
        RunNodeType.Hostile => "⊕",
        RunNodeType.Anomaly => "◉",
        RunNodeType.End => "★",
        _ => "?",
    };

    private static Color TypeFill(RunNodeType t) => t switch
    {
        RunNodeType.Story => new Color(0.95f, 0.95f, 1f),
        RunNodeType.Side => new Color(0.45f, 0.48f, 0.55f),
        RunNodeType.Station => new Color(0.2f, 0.75f, 0.45f),
        RunNodeType.Hostile => new Color(0.9f, 0.35f, 0.15f),
        RunNodeType.Anomaly => new Color(0.55f, 0.25f, 0.85f),
        RunNodeType.End => new Color(0.95f, 0.85f, 0.2f),
        RunNodeType.Start => new Color(0.3f, 0.65f, 0.95f),
        _ => TC.Dim,
    };
}
