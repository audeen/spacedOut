using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

/// <summary>
/// Renders a miniature tactical radar display on the main bridge HUD,
/// mirroring the tactical officer's view with sweep, contacts, and scan progress.
/// </summary>
public partial class TacticalDisplay : Control
{
    private static readonly Color BgColor = new(0.02f, 0.04f, 0.08f, 0.88f);
    private static readonly Color BorderColor = new(0.0f, 0.6f, 0.75f, 0.5f);
    private static readonly Color TitleColor = new(0.0f, 0.85f, 0.95f, 0.8f);
    private static readonly Color LabelDimColor = new(0.5f, 0.5f, 0.55f, 0.7f);
    private static readonly Color RingColor = new(0.0f, 0.78f, 0.9f);
    private static readonly Color CrosshairColor = new(0.0f, 0.78f, 0.9f, 0.1f);
    private static readonly Color ShipColor = new(0.0f, 0.83f, 0.91f);
    private static readonly Color SweepColor = new(0.0f, 0.78f, 0.9f);
    private static readonly Color ScanArcColor = new(0.0f, 0.83f, 0.91f, 0.6f);

    private GameState? _state;
    private float _sweepAngle;
    private float _animPulse;

    public void UpdateState(GameState state)
    {
        _state = state;
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

        DrawRect(new Rect2(0, 0, w, h), BgColor);
        DrawRect(new Rect2(0, 0, w, h), BorderColor, false, 1.5f);

        DrawString(ThemeDB.FallbackFont, new Vector2(10, 18), "TAKTISCH",
            HorizontalAlignment.Left, -1, 13, TitleColor);

        DrawString(ThemeDB.FallbackFont, new Vector2(w - 10, 18), "TAC",
            HorizontalAlignment.Right, -1, 11, LabelDimColor);

        float centerX = w * 0.5f;
        float centerY = 30f + (h - 40f) * 0.5f;
        float maxRadius = MathF.Min(w - 30, h - 50) * 0.42f;
        float sensorRange = CalculateSensorRange();
        float radarScale = maxRadius / MathF.Max(sensorRange, 1f);

        DrawSweep(centerX, centerY, maxRadius);
        DrawRangeRings(centerX, centerY, maxRadius, sensorRange);
        DrawCrosshairs(centerX, centerY, maxRadius);
        DrawShipMarker(centerX, centerY);
        DrawContacts(centerX, centerY, radarScale);
    }

    private void DrawSweep(float cx, float cy, float radius)
    {
        const int sweepSteps = 16;
        const float sweepLen = 0.5f;

        for (int i = 0; i < sweepSteps; i++)
        {
            float angle = _sweepAngle - (i / (float)sweepSteps) * sweepLen;
            float alpha = (1f - i / (float)sweepSteps) * 0.07f;

            var end = new Vector2(
                cx + MathF.Cos(angle) * radius,
                cy + MathF.Sin(angle) * radius
            );
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
            DrawString(ThemeDB.FallbackFont,
                new Vector2(cx + radius + 3, cy - 2),
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

    private void DrawShipMarker(float cx, float cy)
    {
        DrawCircle(new Vector2(cx, cy), 3f, ShipColor);
        DrawArc(new Vector2(cx, cy), 7f, 0, MathF.PI * 2f, 16,
            new Color(ShipColor, 0.3f), 1f);
    }

    private void DrawContacts(float cx, float cy, float radarScale)
    {
        if (_state == null) return;

        foreach (var c in _state.Contacts)
        {
            float dx = c.PositionX - _state.Ship.PositionX;
            float dy = c.PositionY - _state.Ship.PositionY;
            var contactPos = new Vector2(cx + dx * radarScale, cy + dy * radarScale);
            var color = ThemeColors.GetContactColor(c.Type);
            bool isScanning = c.IsScanning;

            if (c.ScanProgress > 0 && c.ScanProgress < 100)
            {
                float arc = (c.ScanProgress / 100f) * MathF.PI * 2f;
                DrawArc(contactPos, 9f, -MathF.PI / 2f, -MathF.PI / 2f + arc, 16,
                    ScanArcColor, 1.5f);

                if (isScanning)
                {
                    DrawArc(contactPos, 12f, -MathF.PI / 2f, -MathF.PI / 2f + arc, 16,
                        new Color(ScanArcColor, 0.2f), 3f);
                }
            }

            float dotSize = 4f;
            DrawCircle(contactPos, dotSize, color);

            if (isScanning)
            {
                float pulse = 5f + MathF.Sin(_animPulse) * 3f;
                DrawArc(contactPos, pulse + 4f, 0, MathF.PI * 2f, 20,
                    new Color(color, 0.35f), 1f);
            }

            if (!string.IsNullOrEmpty(c.DisplayName))
            {
                DrawString(ThemeDB.FallbackFont, contactPos + new Vector2(0, -14),
                    c.DisplayName, HorizontalAlignment.Center, -1, 9,
                    new Color(1, 1, 1, 0.7f));
            }

            if (c.ThreatLevel > 0)
            {
                var threatColor = c.ThreatLevel > 7 ? ThemeColors.ContactHostile :
                                  c.ThreatLevel > 4 ? ThemeColors.ContactUnknown : ThemeColors.ContactNeutral;
                DrawString(ThemeDB.FallbackFont, contactPos + new Vector2(dotSize + 3, 3),
                    $"T{c.ThreatLevel:F0}", HorizontalAlignment.Left, -1, 8, threatColor);
            }

            float dist = MathF.Sqrt(dx * dx + dy * dy);
            DrawString(ThemeDB.FallbackFont, contactPos + new Vector2(0, 16),
                $"{dist:F0}", HorizontalAlignment.Center, -1, 7,
                new Color(1, 1, 1, 0.3f));
        }
    }

    private float CalculateSensorRange()
    {
        if (_state == null) return 500f;
        return MathF.Max(ShipCalculations.CalculateSensorRange(_state.Ship), 50f);
    }
}
