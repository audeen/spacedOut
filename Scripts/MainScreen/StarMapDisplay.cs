using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

/// <summary>
/// Renders a miniature sector star map on the main bridge HUD,
/// mirroring the navigator's view with ship, waypoints, route, and contacts.
/// </summary>
public partial class StarMapDisplay : Control
{
    private const float MapSize = 1000f;

    private static readonly Color BgColor = new(0.03f, 0.05f, 0.10f, 0.88f);
    private static readonly Color GridColor = new(0.15f, 0.23f, 0.45f, 0.18f);
    private static readonly Color GridLabelColor = new(0.23f, 0.32f, 0.55f, 0.35f);
    private static readonly Color RouteColor = new(0.0f, 0.78f, 0.9f, 0.4f);
    private static readonly Color ShipColor = new(0.0f, 0.83f, 0.91f);
    private static readonly Color WpColor = new(0.0f, 0.83f, 0.91f, 0.85f);
    private static readonly Color WpReachedColor = new(0.0f, 0.83f, 0.91f, 0.25f);
    private static readonly Color SensorRingColor = new(0.0f, 0.78f, 0.9f, 0.08f);
    private static readonly Color BorderColor = new(0.0f, 0.6f, 0.75f, 0.5f);
    private static readonly Color TitleColor = new(0.0f, 0.85f, 0.95f, 0.8f);
    private static readonly Color LabelDimColor = new(0.5f, 0.5f, 0.55f, 0.7f);

    private GameState? _state;
    private float _animPulse;

    public void UpdateState(GameState state)
    {
        _state = state;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _animPulse = (_animPulse + (float)delta * 2f) % (MathF.PI * 2f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;

        var rect = GetRect();
        float w = rect.Size.X;
        float h = rect.Size.Y;
        float mapArea = MathF.Min(w - 20, h - 40);
        float scale = mapArea / MapSize;
        float ox = (w - mapArea) / 2f;
        float oy = 30f;

        DrawRect(new Rect2(0, 0, w, h), BgColor);
        DrawRect(new Rect2(0, 0, w, h), BorderColor, false, 1.5f);

        DrawString(ThemeDB.FallbackFont, new Vector2(10, 18), "SEKTORKARTE",
            HorizontalAlignment.Left, -1, 13, TitleColor);

        DrawString(ThemeDB.FallbackFont, new Vector2(w - 10, 18), "NAV",
            HorizontalAlignment.Right, -1, 11, LabelDimColor);

        DrawGrid(ox, oy, mapArea, scale);
        DrawRouteLines(ox, oy, scale);
        DrawContacts(ox, oy, scale);
        DrawWaypoints(ox, oy, scale);
        DrawSensorRange(ox, oy, scale);
        DrawShip(ox, oy, scale);
    }

    private void DrawGrid(float ox, float oy, float mapArea, float scale)
    {
        const float gridStep = 200f;
        for (float g = 0; g <= MapSize; g += gridStep)
        {
            float p = g * scale;
            DrawLine(new Vector2(ox + p, oy), new Vector2(ox + p, oy + mapArea), GridColor, 1f);
            DrawLine(new Vector2(ox, oy + p), new Vector2(ox + mapArea, oy + p), GridColor, 1f);

            if (g < MapSize)
            {
                DrawString(ThemeDB.FallbackFont, new Vector2(ox + p + 2, oy + 10),
                    g.ToString("F0"), HorizontalAlignment.Left, -1, 8, GridLabelColor);
            }
        }
    }

    private void DrawRouteLines(float ox, float oy, float scale)
    {
        if (_state == null) return;
        var unreached = _state.Route.Waypoints.FindAll(wp => !wp.IsReached);
        if (unreached.Count == 0) return;

        var from = new Vector2(ox + _state.Ship.PositionX * scale, oy + _state.Ship.PositionY * scale);
        foreach (var wp in unreached)
        {
            var to = new Vector2(ox + wp.X * scale, oy + wp.Y * scale);
            DrawDashedLine(from, to, RouteColor, 2f, 8f);
            from = to;
        }
    }

    private void DrawContacts(float ox, float oy, float scale)
    {
        if (_state == null) return;
        var visible = _state.Contacts.FindAll(c => c.ScanProgress > 20 || c.IsVisibleOnMainScreen);

        foreach (var c in visible)
        {
            var pos = new Vector2(ox + c.PositionX * scale, oy + c.PositionY * scale);
            var color = ThemeColors.GetContactColor(c.Type);

            DrawCircle(pos, 4f, color);

            if (!string.IsNullOrEmpty(c.DisplayName))
            {
                DrawString(ThemeDB.FallbackFont, pos + new Vector2(0, -10),
                    c.DisplayName, HorizontalAlignment.Center, -1, 9,
                    new Color(color, 0.75f));
            }

            if (c.IsScanning)
            {
                float pulse = 5f + MathF.Sin(_animPulse) * 3f;
                DrawArc(pos, pulse + 4f, 0, MathF.PI * 2, 24,
                    new Color(color, 0.35f), 1f);
            }

            if (c.ScanProgress > 0 && c.ScanProgress < 100)
            {
                float arc = (c.ScanProgress / 100f) * MathF.PI * 2f;
                DrawArc(pos, 8f, -MathF.PI / 2f, -MathF.PI / 2f + arc, 16,
                    new Color(ShipColor, 0.6f), 1.5f);
            }
        }
    }

    private void DrawWaypoints(float ox, float oy, float scale)
    {
        if (_state == null) return;

        foreach (var wp in _state.Route.Waypoints)
        {
            var pos = new Vector2(ox + wp.X * scale, oy + wp.Y * scale);
            var color = wp.IsReached ? WpReachedColor : WpColor;
            float sz = 5f;

            var wpRect = new Rect2(pos.X - sz, pos.Y - sz, sz * 2, sz * 2);
            DrawRect(wpRect, color, false, 1.5f);

            if (!wp.IsReached)
            {
                DrawRect(wpRect, new Color(color, 0.1f));
            }

            if (!string.IsNullOrEmpty(wp.Label))
            {
                DrawString(ThemeDB.FallbackFont, pos + new Vector2(0, -sz - 4),
                    wp.Label, HorizontalAlignment.Center, -1, 9, color);
            }
        }
    }

    private void DrawSensorRange(float ox, float oy, float scale)
    {
        if (_state == null) return;
        var shipScreen = new Vector2(ox + _state.Ship.PositionX * scale, oy + _state.Ship.PositionY * scale);
        float sensorRadius = 350f * scale;
        DrawArc(shipScreen, sensorRadius, 0, MathF.PI * 2f, 48, SensorRingColor, 1f);
    }

    private void DrawShip(float ox, float oy, float scale)
    {
        if (_state == null) return;
        var shipScreen = new Vector2(ox + _state.Ship.PositionX * scale, oy + _state.Ship.PositionY * scale);
        float sz = 6f;

        var tri = new Vector2[]
        {
            shipScreen + new Vector2(0, -sz),
            shipScreen + new Vector2(sz * 0.65f, sz * 0.65f),
            shipScreen + new Vector2(-sz * 0.65f, sz * 0.65f),
        };
        DrawColoredPolygon(tri, ShipColor);

        DrawString(ThemeDB.FallbackFont, shipScreen + new Vector2(0, sz + 12),
            "SCHIFF", HorizontalAlignment.Center, -1, 9, ShipColor);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLen)
    {
        var dir = (to - from);
        float length = dir.Length();
        if (length < 1f) return;
        dir /= length;

        float drawn = 0;
        bool drawing = true;
        while (drawn < length)
        {
            float segLen = MathF.Min(dashLen, length - drawn);
            if (drawing)
            {
                var segFrom = from + dir * drawn;
                var segTo = from + dir * (drawn + segLen);
                DrawLine(segFrom, segTo, color, width);
            }
            drawn += segLen;
            drawing = !drawing;
        }
    }

}
