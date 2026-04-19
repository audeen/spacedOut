using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

/// <summary>
/// Renders a miniature sector star map on the main bridge HUD.
/// Draws entities from SectorData (single source of truth) and ship/route from GameState.
/// </summary>
public partial class StarMapDisplay : Control
{
    private const float MapSize = 1000f;

    private static readonly Color GridColor = new(0.15f, 0.23f, 0.45f, 0.18f);
    private static readonly Color GridLabelColor = new(0.23f, 0.32f, 0.55f, 0.35f);
    private static readonly Color RouteColor = new(0.0f, 0.78f, 0.9f, 0.4f);
    private static readonly Color ShipColor = new(0.0f, 0.83f, 0.91f);
    private static readonly Color WpColor = new(0.0f, 0.83f, 0.91f, 0.85f);
    private static readonly Color WpReachedColor = new(0.0f, 0.83f, 0.91f, 0.25f);
    private static readonly Color SensorRingColor = new(0.0f, 0.78f, 0.9f, 0.12f);
    private static readonly Color AltHighColor = new(1.0f, 0.55f, 0.1f, 0.9f);
    private static readonly Color AltLowColor = new(0.3f, 0.55f, 1.0f, 0.9f);
    private static readonly Color FogColor = new(0.03f, 0.05f, 0.12f, 0.45f);
    private static readonly Color VelocityPipColor = new(0.9f, 0.85f, 0.2f, 0.6f);
    private static readonly Color ZoneProbedColor = new(1f, 1f, 1f, 0.12f);
    private static readonly Color PinHighlightColor = new(0.0f, 0.85f, 0.95f, 0.6f);
    private const float MapCenter = 500f;

    private GameState? _state;
    private SectorData? _sector;
    private HashSet<string> _pinnedIds = new();
    private float _animPulse;

    public void UpdateState(GameState state)
    {
        _state = state;
        QueueRedraw();
    }

    public void UpdateSector(SectorData? sector)
    {
        _sector = sector;
        QueueRedraw();
    }

    public void UpdatePinnedIds(HashSet<string> ids)
    {
        _pinnedIds = ids;
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

        DisplayChrome.DrawPanelChrome(this, w, h, "SEKTORKARTE", "NAV");

        DrawGrid(ox, oy, mapArea, scale);
        DrawFogOfWar(ox, oy, scale, mapArea);
        DrawResourceZones(ox, oy, scale);
        DrawRouteLines(ox, oy, scale);
        DrawSectorEntities(ox, oy, scale);
        DrawWaypoints(ox, oy, scale);
        DrawSensorRange(ox, oy, scale);
        DrawShip(ox, oy, scale);
    }

    // ── Sector entities from SectorData ─────────────────────────────

    private void DrawSectorEntities(float ox, float oy, float scale)
    {
        if (_sector == null || _state == null) return;

        float shipAlt = _state.Ship.PositionZ;

        foreach (var entity in _sector.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;
            if (entity.IsDestroyed) continue;
            if (entity.Discovery == DiscoveryState.Hidden && !entity.PreRevealed) continue;

            var mapPos = CoordinateMapper.WorldToMap3D(entity.WorldPosition, _sector.LevelRadius);
            var screenPos = new Vector2(ox + mapPos.X * scale, oy + mapPos.Y * scale);
            var color = ThemeColors.GetContactColor(entity.ContactType);
            float altDiff = mapPos.Z - shipAlt;

            bool isDetectedOnly = entity.Discovery == DiscoveryState.Detected;
            float baseAlpha = isDetectedOnly ? 0.5f : 1f;
            bool showIdentifiedName = entity.Discovery == DiscoveryState.Scanned || entity.PreRevealed;

            DrawAltitudeStem(screenPos, altDiff);
            DrawCircle(screenPos, isDetectedOnly ? 3f : 4f, new Color(color, baseAlpha));

            string labelText = showIdentifiedName && !string.IsNullOrEmpty(entity.DisplayName)
                ? entity.DisplayName
                : "???";
            DrawString(ThemeDB.FallbackFont, screenPos + new Vector2(0, -10),
                labelText, HorizontalAlignment.Center, -1, 9,
                new Color(color, 0.75f));

            if (entity.ScanProgress > 0 && entity.ScanProgress < 100)
            {
                float arc = (entity.ScanProgress / 100f) * MathF.PI * 2f;
                DrawArc(screenPos, 8f, -MathF.PI / 2f, -MathF.PI / 2f + arc, 16,
                    new Color(ShipColor, 0.6f), 1.5f);
            }

            if (_pinnedIds.Contains(entity.Id))
            {
                float pulse = 8f + MathF.Sin(_animPulse) * 2f;
                DrawArc(screenPos, pulse, 0, MathF.PI * 2f, 24, PinHighlightColor, 1.5f);
            }

            if (entity.IsMovable)
                DrawVelocityPipFromEntity(screenPos, entity, scale);
        }
    }

    // ── Resource zones ──────────────────────────────────────────────

    private void DrawResourceZones(float ox, float oy, float scale)
    {
        if (!GameFeatures.ResourceZonesEnabled || _sector == null) return;

        foreach (var zone in _sector.ResourceZones)
        {
            if (zone.Discovery == DiscoveryState.Hidden) continue;

            var mapPos = CoordinateMapper.WorldToMap3D(zone.Center, _sector.LevelRadius);
            var screenPos = new Vector2(ox + mapPos.X * scale, oy + mapPos.Y * scale);
            float screenRadius = zone.Radius * (MapCenter / _sector.LevelRadius) * scale;

            var fillColor = new Color(zone.MapColor, 0.08f * zone.Density);
            var borderColor = new Color(zone.MapColor, 0.25f);

            DrawCircle(screenPos, screenRadius, fillColor);
            DrawArc(screenPos, screenRadius, 0, MathF.PI * 2f, 32, borderColor, 1.5f);

            if (zone.Discovery == DiscoveryState.Scanned)
            {
                string label = zone.ResourceType.ToString();
                DrawString(ThemeDB.FallbackFont, screenPos + new Vector2(0, -screenRadius - 4),
                    label, HorizontalAlignment.Center, -1, 9,
                    new Color(zone.MapColor, 0.7f));
            }
        }
    }

    // ── Drawing helpers ─────────────────────────────────────────────

    private void DrawVelocityPipFromEntity(Vector2 contactPos, SectorEntity e, float scale)
    {
        float vx = e.Velocity.X;
        float vz = e.Velocity.Z;
        if (MathF.Abs(vx) < 0.1f && MathF.Abs(vz) < 0.1f) return;

        float mapScale = MapCenter / (_sector?.LevelRadius ?? 1f) * scale;
        var velDir = new Vector2(vx, vz);
        float speed = velDir.Length();
        velDir /= speed;

        float lineLen = MathF.Min(speed * 15f * mapScale, 50f);
        DrawLine(contactPos, contactPos + velDir * lineLen,
            new Color(VelocityPipColor, 0.3f), 1f);
    }

    private void DrawFogOfWar(float ox, float oy, float scale, float mapArea)
    {
        if (_state == null) return;
        float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship);
        float sensorRadius = sensorRange * scale;
        var shipScreen = new Vector2(ox + _state.Ship.PositionX * scale, oy + _state.Ship.PositionY * scale);

        for (int ring = 0; ring < 20; ring++)
        {
            float r = sensorRadius + ring * 12f;
            if (r > mapArea * 1.5f) break;
            float alpha = MathF.Min(ring * 0.03f, 0.45f);
            DrawArc(shipScreen, r, 0, MathF.PI * 2f, 48,
                new Color(FogColor, alpha), 12f);
        }
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

    private void DrawWaypoints(float ox, float oy, float scale)
    {
        if (_state == null) return;
        float shipAlt = _state.Ship.PositionZ;

        foreach (var wp in _state.Route.Waypoints)
        {
            var pos = new Vector2(ox + wp.X * scale, oy + wp.Y * scale);
            var color = wp.IsReached ? WpReachedColor : WpColor;
            float sz = 5f;
            float altDiff = wp.Z - shipAlt;

            if (!wp.IsReached)
                DrawAltitudeStem(pos, altDiff);

            var wpRect = new Rect2(pos.X - sz, pos.Y - sz, sz * 2, sz * 2);
            DrawRect(wpRect, color, false, 1.5f);

            if (!wp.IsReached)
                DrawRect(wpRect, new Color(color, 0.1f));

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
        float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship);
        float sensorRadius = sensorRange * scale;
        DrawArc(shipScreen, sensorRadius, 0, MathF.PI * 2f, 48, SensorRingColor, 1.5f);
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

        float alt = _state.Ship.PositionZ - MapCenter;
        string altText = $"ALT {(alt >= 0 ? "+" : "")}{alt:F0}";
        DrawString(ThemeDB.FallbackFont, shipScreen + new Vector2(0, sz + 12),
            "SCHIFF", HorizontalAlignment.Center, -1, 9, ShipColor);
        DrawString(ThemeDB.FallbackFont, shipScreen + new Vector2(0, sz + 22),
            altText, HorizontalAlignment.Center, -1, 8,
            new Color(ShipColor, 0.7f));
    }

    private void DrawAltitudeStem(Vector2 pos, float altDiff)
    {
        if (MathF.Abs(altDiff) < 5f) return;

        float stemLen = Math.Clamp(MathF.Abs(altDiff) * 0.4f, 25f, 80f);
        float stemDir = altDiff > 0 ? -1f : 1f;
        var stemStart = pos + new Vector2(0, stemDir * 5f);
        var stemEnd = pos + new Vector2(0, stemDir * stemLen);
        var col = altDiff > 0 ? AltHighColor : AltLowColor;
        var colDim = new Color(col, col.A * 0.55f);

        DrawLine(stemStart, stemEnd, colDim, 2f);
        DrawLine(stemEnd - new Vector2(6f, 0), stemEnd + new Vector2(6f, 0), col, 2.5f);
        DrawLine(pos + new Vector2(-4, 0), pos + new Vector2(4, 0), colDim, 1.5f);

        string label = altDiff >= 0 ? $"+{altDiff:F0}" : $"{altDiff:F0}";
        DrawString(ThemeDB.FallbackFont, stemEnd + new Vector2(9, 4), label,
            HorizontalAlignment.Left, -1, 9, col);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLen)
    {
        var dir = to - from;
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
