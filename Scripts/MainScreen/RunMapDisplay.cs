using System;
using Godot;
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
            if (_run.CurrentRun.NodeStates.TryGetValue(n.Id, out var st) &&
                st.State == RunNodeState.Hidden)
                continue;
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

        foreach (var node in def.Nodes.Values)
        {
            foreach (var nextId in node.NextNodeIds)
            {
                if (!def.Nodes.TryGetValue(nextId, out var next)) continue;
                if (states[node.Id].State == RunNodeState.Hidden ||
                    states[nextId].State == RunNodeState.Hidden)
                    continue;
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
            var know = rt?.Knowledge ?? NodeKnowledgeState.Unknown;
            Color fill = TypeFill(node.Type);
            Color border = TC.DimWhite;
            float radius = 18f;

            switch (rt?.State)
            {
                case RunNodeState.Hidden:
                    continue;
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

            if (know == NodeKnowledgeState.Unknown)
            {
                fill = new Color(0.22f, 0.24f, 0.28f, 0.95f);
                border = new Color(0.4f, 0.45f, 0.5f, 0.85f);
            }
            else if (know == NodeKnowledgeState.Detected)
            {
                fill = new Color(0.25f, 0.32f, 0.48f, 0.82f);
                border = new Color(0.45f, 0.55f, 0.75f, 0.9f);
            }

            DrawCircle(p, radius + 2f, new Color(border, 0.35f));
            DrawCircle(p, radius, fill);

            if (_run.CurrentRun.CurrentNodeId == node.Id)
            {
                DrawArc(p, radius + 6f, 0f, MathF.Tau, 32, new Color(TC.Yellow, 0.9f), 2f);
            }
            else if (HighlightNodeId == node.Id)
            {
                DrawArc(p, radius + 5f, 0f, MathF.Tau, 32, new Color(TC.White, 0.55f), 1.5f);
            }

            string label = MapLabel(node.Id, know);
            DrawString(ThemeDB.FallbackFont, p + new Vector2(-14, -6), label,
                HorizontalAlignment.Left, -1, 10, TC.White);
        }
    }

    private static string MapLabel(string nodeId, NodeKnowledgeState know) =>
        know switch
        {
            NodeKnowledgeState.Unknown => "·",
            NodeKnowledgeState.Detected => (nodeId.GetHashCode() & 1) == 0 ? "Signal" : "Echo",
            _ => nodeId,
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
