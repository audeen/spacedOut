using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

/// <summary>
/// Renders a miniature tactical radar display on the main bridge HUD.
/// Draws sector entities from SectorData and ship/contacts from GameState.
/// Includes nearfield sonar for ambient objects.
/// </summary>
public partial class TacticalDisplay : Control
{
    private static readonly Color RingColor = new(0.0f, 0.78f, 0.9f);
    private static readonly Color CrosshairColor = new(0.0f, 0.78f, 0.9f, 0.1f);
    private static readonly Color ShipColor = new(0.0f, 0.83f, 0.91f);
    private static readonly Color SweepColor = new(0.0f, 0.78f, 0.9f);
    private static readonly Color ScanArcColor = new(0.0f, 0.83f, 0.91f, 0.6f);
    private static readonly Color AltHighColor = new(1.0f, 0.55f, 0.1f, 0.9f);
    private static readonly Color AltLowColor = new(0.3f, 0.55f, 1.0f, 0.9f);
    private static readonly Color FogColor = new(0.03f, 0.05f, 0.12f, 0.55f);
    private static readonly Color ProbeRingColor = new(0.2f, 0.9f, 0.4f, 0.4f);
    private static readonly Color VelocityPipColor = new(0.9f, 0.85f, 0.2f, 0.6f);
    private static readonly Color NearfieldColor = new(0.25f, 0.4f, 0.55f, 0.35f);
    private static readonly Color ZoneBorderColor = new(1f, 1f, 1f, 0.2f);
    private static readonly Color PinHighlightColor = new(0.0f, 0.85f, 0.95f, 0.6f);

    private const float NearfieldRange = 200f;

    private GameState? _state;
    private SectorData? _sector;
    private HashSet<string> _pinnedIds = new();
    private float _sweepAngle;
    private float _animPulse;

    public void UpdateState(GameState state)
    {
        _state = state;
    }

    public void UpdateSector(SectorData? sector)
    {
        _sector = sector;
    }

    public void UpdatePinnedIds(HashSet<string> ids)
    {
        _pinnedIds = ids;
    }

    public override void _Process(double delta)
    {
        _sweepAngle = (_sweepAngle + (float)delta * 0.5f) % (MathF.PI * 2f);
        _animPulse = (_animPulse + (float)delta * 2f) % (MathF.PI * 2f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;

        var rect = GetRect();
        float w = rect.Size.X;
        float h = rect.Size.Y;

        DisplayChrome.DrawPanelChrome(this, w, h, "TAKTISCH", "TAC");

        float centerX = w * 0.5f;
        float centerY = 30f + (h - 40f) * 0.5f;
        float maxRadius = MathF.Min(w - 30, h - 50) * 0.42f;
        float sensorRange = CalculateSensorRange();
        float radarScale = maxRadius / MathF.Max(sensorRange, 1f);

        DrawFogOfWar(centerX, centerY, maxRadius, sensorRange, radarScale);
        DrawSweep(centerX, centerY, maxRadius);
        DrawRangeRings(centerX, centerY, maxRadius, sensorRange);
        DrawCrosshairs(centerX, centerY, maxRadius);
        DrawResourceZones(centerX, centerY, radarScale);
        DrawNearfieldSonar(centerX, centerY, radarScale, sensorRange);
        DrawProbes(centerX, centerY, radarScale);
        DrawShipMarker(centerX, centerY);
        DrawSectorContacts(centerX, centerY, radarScale, sensorRange);
    }

    // ── Sector entities from SectorData ─────────────────────────────

    private void DrawSectorContacts(float cx, float cy, float radarScale, float sensorRange)
    {
        if (_sector == null || _state == null) return;
        float shipAlt = _state.Ship.PositionZ;
        var shipWorld = CoordinateMapper.MapToWorld(_state.Ship.PositionX, _state.Ship.PositionY, _state.Ship.PositionZ, _sector.LevelRadius);

        foreach (var entity in _sector.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;
            if (entity.Discovery == DiscoveryState.Hidden && !entity.PreRevealed) continue;

            var mapPos = CoordinateMapper.WorldToMap3D(entity.WorldPosition, _sector.LevelRadius);
            float dx = mapPos.X - _state.Ship.PositionX;
            float dy = mapPos.Y - _state.Ship.PositionY;
            float dz = mapPos.Z - shipAlt;

            if (entity.Discovery == DiscoveryState.Detected && !entity.PreRevealed && !entity.RadarShowDetectedInFullRange && !entity.IsMovable)
            {
                float dist2d = MathF.Sqrt(dx * dx + dy * dy);
                if (dist2d > sensorRange / 3f) continue;
            }

            var basePos = new Vector2(cx + dx * radarScale, cy + dy * radarScale);

            var color = ThemeColors.GetContactColor(entity.ContactType);
            bool isDetectedOnly = entity.Discovery == DiscoveryState.Detected;
            float dotAlpha = isDetectedOnly ? 0.5f : 1f;
            bool showIdentifiedName = entity.Discovery == DiscoveryState.Scanned || entity.PreRevealed;

            if (MathF.Abs(dz) > 5f)
                DrawAltStem(basePos, dz, dotAlpha);

            DrawCircle(basePos, isDetectedOnly ? 3f : 4f, new Color(color, dotAlpha));

            if (entity.ScanProgress > 0 && entity.ScanProgress < 100)
            {
                float arc = (entity.ScanProgress / 100f) * MathF.PI * 2f;
                DrawArc(basePos, 9f, -MathF.PI / 2f, -MathF.PI / 2f + arc, 16,
                    ScanArcColor, 1.5f);
            }

            string labelText = showIdentifiedName && !string.IsNullOrEmpty(entity.DisplayName)
                ? entity.DisplayName
                : "???";
            DrawString(ThemeDB.FallbackFont, basePos + new Vector2(0, -14),
                labelText, HorizontalAlignment.Center, -1, 9,
                new Color(1, 1, 1, 0.7f * dotAlpha));

            if (entity.ThreatLevel > 0 && entity.Discovery == DiscoveryState.Scanned)
            {
                var threatColor = entity.ThreatLevel >= 5 ? ThemeColors.ContactHostile :
                    entity.ThreatLevel >= 3 ? ThemeColors.ContactUnknown : ThemeColors.ContactNeutral;
                DrawString(ThemeDB.FallbackFont, basePos + new Vector2(6, 3),
                    $"T{entity.ThreatLevel}", HorizontalAlignment.Left, -1, 8, threatColor);
            }

            float dist3d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            DrawString(ThemeDB.FallbackFont, basePos + new Vector2(0, 16),
                $"{dist3d:F0}", HorizontalAlignment.Center, -1, 7,
                new Color(1, 1, 1, 0.3f * dotAlpha));

            if (_pinnedIds.Contains(entity.Id))
            {
                float pulse = 8f + MathF.Sin(_animPulse) * 2f;
                DrawArc(basePos, pulse, 0, MathF.PI * 2f, 24, PinHighlightColor, 1.5f);
            }

            if (entity.IsMovable)
                DrawVelocityPipFromEntity(basePos, entity, radarScale);
        }
    }

    // ── Nearfield sonar for ambient objects ──────────────────────────

    private void DrawNearfieldSonar(float cx, float cy, float radarScale, float sensorRange)
    {
        if (_sector == null || _state == null) return;

        float nearfieldMapRange = NearfieldRange * (CoordinateMapper.WorldToMap3D(
            new Vector3(NearfieldRange, 0, 0), _sector.LevelRadius).X - 500f);
        if (nearfieldMapRange <= 0) nearfieldMapRange = NearfieldRange;

        var shipWorld = CoordinateMapper.MapToWorld(
            _state.Ship.PositionX, _state.Ship.PositionY, _state.Ship.PositionZ, _sector.LevelRadius);

        foreach (var entity in _sector.Entities)
        {
            if (entity.MapPresence != MapPresence.NearfieldOnly) continue;

            float worldDist = entity.WorldPosition.DistanceTo(shipWorld);
            if (worldDist > NearfieldRange) continue;

            var mapPos = CoordinateMapper.WorldToMap3D(entity.WorldPosition, _sector.LevelRadius);
            float dx = mapPos.X - _state.Ship.PositionX;
            float dy = mapPos.Y - _state.Ship.PositionY;
            var screenPos = new Vector2(cx + dx * radarScale, cy + dy * radarScale);

            float proximity = 1f - (worldDist / NearfieldRange);
            float pulse = 0.5f + MathF.Sin(_animPulse + worldDist * 0.01f) * 0.3f;
            float alpha = proximity * pulse * 0.6f;

            float dotSize = 1.5f + proximity * 1.5f;
            DrawCircle(screenPos, dotSize, new Color(NearfieldColor, alpha));
        }
    }

    // ── Resource zones on radar ─────────────────────────────────────

    private void DrawResourceZones(float cx, float cy, float radarScale)
    {
        if (!GameFeatures.ResourceZonesEnabled || _sector == null || _state == null) return;

        foreach (var zone in _sector.ResourceZones)
        {
            if (zone.Discovery == DiscoveryState.Hidden) continue;

            var mapPos = CoordinateMapper.WorldToMap3D(zone.Center, _sector.LevelRadius);
            float dx = mapPos.X - _state.Ship.PositionX;
            float dy = mapPos.Y - _state.Ship.PositionY;
            var screenPos = new Vector2(cx + dx * radarScale, cy + dy * radarScale);
            float mapRadius = zone.Radius * (500f / _sector.LevelRadius);
            float screenRadius = mapRadius * radarScale;

            var fillColor = new Color(zone.MapColor, 0.06f * zone.Density);
            DrawCircle(screenPos, screenRadius, fillColor);
            DrawArc(screenPos, screenRadius, 0, MathF.PI * 2f, 24,
                new Color(zone.MapColor, 0.2f), 1f);

            if (zone.Discovery == DiscoveryState.Scanned)
            {
                DrawString(ThemeDB.FallbackFont, screenPos,
                    zone.ResourceType.ToString(), HorizontalAlignment.Center, -1, 8,
                    new Color(zone.MapColor, 0.5f));
            }
        }
    }

    // ── Drawing helpers ─────────────────────────────────────────────

    private void DrawAltStem(Vector2 basePos, float dz, float dotAlpha)
    {
        float stemLen = Math.Clamp(MathF.Abs(dz) * 0.4f, 25f, 80f);
        float stemDir = dz > 0 ? -1f : 1f;
        var stemStart = basePos + new Vector2(0, stemDir * 5f);
        var stemEnd = basePos + new Vector2(0, stemDir * stemLen);
        var col = dz > 0 ? AltHighColor : AltLowColor;
        var colDim = new Color(col, col.A * 0.55f * dotAlpha);

        DrawLine(stemStart, stemEnd, colDim, 2f);
        DrawLine(stemEnd - new Vector2(6f, 0), stemEnd + new Vector2(6f, 0),
            new Color(col, col.A * dotAlpha), 2.5f);
        DrawLine(basePos + new Vector2(-4, 0), basePos + new Vector2(4, 0), colDim, 1.5f);

        string altLabel = dz >= 0 ? $"+{dz:F0}" : $"{dz:F0}";
        DrawString(ThemeDB.FallbackFont, stemEnd + new Vector2(9, 4),
            altLabel, HorizontalAlignment.Left, -1, 9, new Color(col, col.A * dotAlpha));
    }

    private void DrawVelocityPipFromEntity(Vector2 contactPos, SectorEntity e, float radarScale)
    {
        float vx = e.Velocity.X;
        float vz = e.Velocity.Z;
        if (MathF.Abs(vx) < 0.1f && MathF.Abs(vz) < 0.1f) return;

        float mapScale = (500f / (_sector?.LevelRadius ?? 1f)) * radarScale;
        var velDir = new Vector2(vx, vz);
        float speed = velDir.Length();
        velDir /= speed;
        float lineLen = MathF.Min(speed * 15f * mapScale, 60f);
        DrawLine(contactPos, contactPos + velDir * lineLen,
            new Color(VelocityPipColor, 0.3f), 1f);
    }

    private void DrawFogOfWar(float cx, float cy, float maxRadius, float sensorRange, float radarScale)
    {
        float sensorScreenRadius = sensorRange * radarScale;
        for (int ring = 0; ring < 12; ring++)
        {
            float r = sensorScreenRadius + ring * 8f;
            if (r > maxRadius + 40f) break;
            float alpha = MathF.Min(ring * 0.05f, 0.55f);
            DrawArc(new Vector2(cx, cy), r, 0, MathF.PI * 2f, 48,
                new Color(FogColor, alpha), 8f);
        }
    }

    private void DrawSweep(float cx, float cy, float radius)
    {
        const int sweepSteps = 16;
        const float sweepLen = 0.5f;

        for (int i = 0; i < sweepSteps; i++)
        {
            float angle = _sweepAngle - (i / (float)sweepSteps) * sweepLen;
            float alpha = (1f - i / (float)sweepSteps) * 0.07f;
            var end = new Vector2(cx + MathF.Cos(angle) * radius, cy + MathF.Sin(angle) * radius);
            DrawLine(new Vector2(cx, cy), end, new Color(SweepColor, alpha), 1.5f);
        }
    }

    private void DrawRangeRings(float cx, float cy, float maxRadius, float sensorRange)
    {
        for (int r = 1; r <= 3; r++)
        {
            float radius = (sensorRange / 3f) * r * (maxRadius / sensorRange);
            float alpha = 0.06f + r * 0.03f;
            DrawArc(new Vector2(cx, cy), radius, 0, MathF.PI * 2f, 48,
                new Color(RingColor, alpha), 1f);
            int rangeLabel = (int)(sensorRange / 3f * r);
            DrawString(ThemeDB.FallbackFont, new Vector2(cx + radius + 3, cy - 2),
                rangeLabel.ToString(), HorizontalAlignment.Left, -1, 8,
                new Color(RingColor, 0.3f));
        }
    }

    private void DrawCrosshairs(float cx, float cy, float maxRadius)
    {
        DrawLine(new Vector2(cx - maxRadius, cy), new Vector2(cx + maxRadius, cy), CrosshairColor, 1f);
        DrawLine(new Vector2(cx, cy - maxRadius), new Vector2(cx, cy + maxRadius), CrosshairColor, 1f);
        float diag = maxRadius * 0.707f;
        var diagColor = new Color(CrosshairColor, 0.05f);
        DrawLine(new Vector2(cx - diag, cy - diag), new Vector2(cx + diag, cy + diag), diagColor, 1f);
        DrawLine(new Vector2(cx + diag, cy - diag), new Vector2(cx - diag, cy + diag), diagColor, 1f);
    }

    private void DrawProbes(float cx, float cy, float radarScale)
    {
        if (_state == null) return;
        foreach (var probe in _state.ContactsState.ActiveProbes)
        {
            float dx = probe.X - _state.Ship.PositionX;
            float dy = probe.Y - _state.Ship.PositionY;
            var pos = new Vector2(cx + dx * radarScale, cy + dy * radarScale);
            float r = probe.RevealRadius * radarScale;
            float pulse = 1f + MathF.Sin(_animPulse * 1.5f) * 0.15f;
            DrawArc(pos, r * pulse, 0, MathF.PI * 2f, 32,
                new Color(ProbeRingColor, 0.3f + MathF.Sin(_animPulse) * 0.1f), 1.5f);
            DrawCircle(pos, 2f, ProbeRingColor);
        }
    }

    private void DrawShipMarker(float cx, float cy)
    {
        DrawCircle(new Vector2(cx, cy), 3f, ShipColor);
        DrawArc(new Vector2(cx, cy), 7f, 0, MathF.PI * 2f, 16,
            new Color(ShipColor, 0.3f), 1f);
    }

    private float CalculateSensorRange()
    {
        if (_state == null) return 500f;
        return MathF.Max(ShipCalculations.CalculateSensorRange(_state.Ship), 50f);
    }
}
