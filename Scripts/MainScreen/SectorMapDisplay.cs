using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Campaign;
using SpacedOut.Shared;
using TC = SpacedOut.Shared.ThemeColors;

namespace SpacedOut.MainScreen;

/// <summary>
/// Renders the FTL/Slay-the-Spire style sector node map on the bridge HUD.
/// Drawn via Godot's _Draw() using the campaign state data.
/// </summary>
public partial class SectorMapDisplay : Control
{
    private static readonly Color BgColor = new(0.03f, 0.05f, 0.10f, 0.92f);
    private static readonly Color BorderColor = new(0.0f, 0.6f, 0.75f, 0.5f);
    private static readonly Color TitleColor = new(0.0f, 0.85f, 0.95f, 0.8f);
    private static readonly Color LabelDimColor = new(0.5f, 0.5f, 0.55f, 0.7f);
    private static readonly Color EdgeColor = new(0.2f, 0.3f, 0.5f, 0.4f);
    private static readonly Color EdgeActiveColor = new(0.0f, 0.83f, 0.91f, 0.6f);

    private CampaignState? _campaign;
    private float _animPulse;

    public void UpdateCampaign(CampaignState campaign)
    {
        _campaign = campaign;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _campaign == null) return;
        _animPulse = (_animPulse + (float)delta * 2.5f) % (MathF.PI * 2f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_campaign == null) return;
        var sector = _campaign.CurrentSector;
        if (sector == null) return;

        var rect = GetRect();
        float w = rect.Size.X;
        float h = rect.Size.Y;

        DrawRect(new Rect2(0, 0, w, h), BgColor);
        DrawRect(new Rect2(0, 0, w, h), BorderColor, false, 1.5f);

        DrawString(ThemeDB.FallbackFont, new Vector2(10, 18),
            $"SEKTORKARTE · {sector.DisplayName}",
            HorizontalAlignment.Left, -1, 12, TitleColor);

        string infoText = $"Hull:{_campaign.Ship.HullIntegrity:F0}%  " +
                          $"Fuel:{_campaign.Ship.Fuel}  " +
                          $"Scrap:{_campaign.Ship.Scrap}";
        DrawString(ThemeDB.FallbackFont, new Vector2(w - 10, 18),
            infoText, HorizontalAlignment.Right, -1, 10, LabelDimColor);

        float mapMarginX = 40f;
        float mapMarginTop = 30f;
        float mapMarginBottom = 20f;
        float mapW = w - mapMarginX * 2;
        float mapH = h - mapMarginTop - mapMarginBottom;

        DrawEdges(sector, mapMarginX, mapMarginTop, mapW, mapH);
        DrawNodes(sector, mapMarginX, mapMarginTop, mapW, mapH);
    }

    private void DrawEdges(SectorDefinition sector,
        float ox, float oy, float mapW, float mapH)
    {
        foreach (var edge in sector.Edges)
        {
            var fromNode = sector.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var toNode = sector.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fromNode == null || toNode == null) continue;
            if (!fromNode.IsRevealed || !toNode.IsRevealed) continue;

            var from = NodeScreenPos(fromNode, sector, ox, oy, mapW, mapH);
            var to = NodeScreenPos(toNode, sector, ox, oy, mapW, mapH);

            bool isOnPath = fromNode.Status is NodeStatus.Completed or NodeStatus.Current
                            && toNode.Status is NodeStatus.Completed or NodeStatus.Current;
            bool isAvailable = fromNode.Status == NodeStatus.Current
                               && toNode.Status == NodeStatus.Available;

            Color color;
            float width;
            if (isOnPath)
            {
                color = EdgeActiveColor;
                width = 2.5f;
            }
            else if (isAvailable)
            {
                float pulse = 0.4f + MathF.Sin(_animPulse) * 0.2f;
                color = new Color(TC.Cyan, pulse);
                width = 2f;
            }
            else
            {
                color = EdgeColor;
                width = 1f;
            }

            DrawLine(from, to, color, width);
        }
    }

    private void DrawNodes(SectorDefinition sector,
        float ox, float oy, float mapW, float mapH)
    {
        foreach (var node in sector.Nodes)
        {
            if (!node.IsRevealed) continue;

            var pos = NodeScreenPos(node, sector, ox, oy, mapW, mapH);
            float radius = 10f;

            Color fillColor;
            Color borderCol;

            switch (node.Status)
            {
                case NodeStatus.Completed:
                    fillColor = new Color(TC.Green, 0.3f);
                    borderCol = new Color(TC.Green, 0.6f);
                    break;
                case NodeStatus.Current:
                    float pulse = 0.6f + MathF.Sin(_animPulse) * 0.4f;
                    fillColor = new Color(TC.Cyan, 0.4f);
                    borderCol = new Color(TC.Cyan, pulse);
                    radius = 13f;
                    break;
                case NodeStatus.Available:
                    fillColor = new Color(TC.Cyan, 0.15f);
                    borderCol = new Color(TC.Cyan, 0.5f);
                    break;
                case NodeStatus.Skipped:
                    fillColor = new Color(TC.Dim, 0.1f);
                    borderCol = new Color(TC.Dim, 0.3f);
                    break;
                default:
                    fillColor = new Color(TC.Dim, 0.08f);
                    borderCol = new Color(TC.Dim, 0.2f);
                    break;
            }

            DrawCircle(pos, radius, fillColor);
            DrawArc(pos, radius, 0, MathF.PI * 2f, 24, borderCol, 2f);

            var typeColor = GetNodeTypeColor(node.Type);
            float innerRadius = radius * 0.45f;
            DrawCircle(pos, innerRadius, typeColor);

            if (node.Status is NodeStatus.Current or NodeStatus.Available
                             or NodeStatus.Completed)
            {
                string label = node.Label.Length > 14
                    ? node.Label[..12] + ".."
                    : node.Label;
                DrawString(ThemeDB.FallbackFont,
                    pos + new Vector2(0, -radius - 4), label,
                    HorizontalAlignment.Center, -1, 9,
                    new Color(TC.White, 0.8f));
            }

            string icon = GetNodeTypeSymbol(node.Type);
            DrawString(ThemeDB.FallbackFont,
                pos + new Vector2(0, radius + 12), icon,
                HorizontalAlignment.Center, -1, 8, typeColor);
        }
    }

    private Vector2 NodeScreenPos(MapNode node, SectorDefinition sector,
        float ox, float oy, float mapW, float mapH)
    {
        int totalLayers = sector.LayerCount;
        int nodesInLayer = sector.Nodes.Count(n => n.Layer == node.Layer && n.IsRevealed);
        int indexInLayer = sector.Nodes
            .Where(n => n.Layer == node.Layer && n.IsRevealed)
            .OrderBy(n => n.SlotIndex)
            .ToList()
            .IndexOf(node);

        float x = totalLayers > 1
            ? ox + (float)node.Layer / (totalLayers - 1) * mapW
            : ox + mapW / 2f;

        float y;
        if (nodesInLayer <= 1)
        {
            y = oy + mapH / 2f;
        }
        else
        {
            float spacing = mapH / (nodesInLayer + 1);
            y = oy + (indexInLayer + 1) * spacing;
        }

        return new Vector2(x, y);
    }

    private static Color GetNodeTypeColor(NodeType type) => type switch
    {
        NodeType.Start => TC.Cyan,
        NodeType.Navigation => TC.Blue,
        NodeType.ScanAnomaly => TC.Purple,
        NodeType.DebrisField => TC.Orange,
        NodeType.Encounter => TC.Red,
        NodeType.DistressSignal => TC.Yellow,
        NodeType.Station => TC.Green,
        NodeType.EliteEncounter => new Color(1f, 0.2f, 0.4f),
        NodeType.Boss => new Color(1f, 0.85f, 0.1f),
        _ => TC.Dim,
    };

    private static string GetNodeTypeSymbol(NodeType type) => type switch
    {
        NodeType.Start => "START",
        NodeType.Navigation => "NAV",
        NodeType.ScanAnomaly => "SCAN",
        NodeType.DebrisField => "DEBRIS",
        NodeType.Encounter => "CONTACT",
        NodeType.DistressSignal => "SOS",
        NodeType.Station => "DOCK",
        NodeType.EliteEncounter => "ELITE",
        NodeType.Boss => "BOSS",
        _ => "???",
    };
}
