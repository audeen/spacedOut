using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

/// <summary>
/// Compact HUD panel showing pinned contacts (up to 3) in the lower-right corner
/// of the main bridge screen. Classic space-game target info style.
/// </summary>
public partial class PinnedInfoPanel : Control
{
    private static readonly Color CardBg = new(0.04f, 0.06f, 0.12f, 0.85f);
    private static readonly Color CardBorder = new(0.0f, 0.78f, 0.9f, 0.45f);
    private static readonly Color LabelColor = new(0.75f, 0.78f, 0.85f, 0.9f);
    private static readonly Color DimColor = new(0.5f, 0.5f, 0.55f, 0.7f);
    private static readonly Color DistColor = new(0.0f, 0.85f, 0.95f, 0.8f);

    private const float CardWidth = 220f;
    private const float CardHeight = 60f;
    private const float CardSpacing = 6f;

    private GameState? _state;
    private SectorData? _sector;
    private float _animPulse;

    public void UpdateState(GameState state, SectorData? sector)
    {
        _state = state;
        _sector = sector;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _animPulse = (_animPulse + (float)delta * 2.5f) % (MathF.PI * 2f);
        if (_state != null && _state.PinnedEntities.Count > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;
        var pins = _state.PinnedEntities;
        if (pins.Count == 0) return;

        float y = 0;
        foreach (var pin in pins)
        {
            DrawPinCard(0, y, pin);
            y += CardHeight + CardSpacing;
        }
    }

    private void DrawPinCard(float x, float y, PinnedEntity pin)
    {
        if (_state == null) return;

        var contact = _state.Contacts.Find(c => c.Id == pin.EntityId);
        var entity = _sector?.Entities.FirstOrDefault(e => e.Id == pin.EntityId);

        var rect = new Rect2(x, y, CardWidth, CardHeight);
        DrawRect(rect, CardBg);

        float pulse = 0.45f + MathF.Sin(_animPulse) * 0.08f;
        DrawRect(rect, new Color(CardBorder, pulse), false, 1.5f);

        var typeColor = contact != null
            ? ThemeColors.GetContactColor(contact.Type)
            : ThemeColors.ContactUnknown;

        DrawCircle(new Vector2(x + 14, y + 18), 5f, typeColor);

        string name = pin.Label;
        if (string.IsNullOrEmpty(name) && contact != null)
            name = contact.DisplayName;
        DrawString(ThemeDB.FallbackFont, new Vector2(x + 26, y + 22),
            name, HorizontalAlignment.Left, (int)(CardWidth - 32), 12, LabelColor);

        DrawString(ThemeDB.FallbackFont, new Vector2(x + 26, y + 38),
            pin.Detail, HorizontalAlignment.Left, (int)(CardWidth - 32), 10, DimColor);

        float dist = CalculateDistance(pin.EntityId);
        if (dist >= 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(x + CardWidth - 8, y + 52),
                $"{dist:F0}", HorizontalAlignment.Right, -1, 10, DistColor);
        }
    }

    private float CalculateDistance(string entityId)
    {
        if (_state == null) return -1;

        var contact = _state.Contacts.Find(c => c.Id == entityId);
        if (contact != null)
        {
            float dx = contact.PositionX - _state.Ship.PositionX;
            float dy = contact.PositionY - _state.Ship.PositionY;
            float dz = contact.PositionZ - _state.Ship.PositionZ;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        return -1;
    }
}
