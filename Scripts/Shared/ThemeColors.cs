using Godot;

namespace SpacedOut.Shared;

public static class ThemeColors
{
    public static readonly Color Cyan = new(0.0f, 0.85f, 0.95f);
    public static readonly Color Green = new(0.2f, 0.9f, 0.3f);
    public static readonly Color Yellow = new(0.95f, 0.85f, 0.2f);
    public static readonly Color Red = new(0.95f, 0.2f, 0.2f);
    public static readonly Color Blue = new(0.3f, 0.5f, 0.95f);
    public static readonly Color Orange = new(0.9f, 0.55f, 0.15f);
    public static readonly Color White = new(0.9f, 0.9f, 0.95f);
    public static readonly Color DimWhite = new(0.5f, 0.5f, 0.55f);
    public static readonly Color Purple = new(0.7f, 0.3f, 0.9f);
    public static readonly Color Dim = new(0.35f, 0.38f, 0.45f);
    public static readonly Color PanelBg = new(0.05f, 0.06f, 0.12f, 0.85f);

    public static readonly Color ContactFriendly = new(0.18f, 0.9f, 0.35f);
    public static readonly Color ContactHostile = new(0.91f, 0.19f, 0.19f);
    public static readonly Color ContactUnknown = new(0.91f, 0.82f, 0.13f);
    public static readonly Color ContactAnomaly = new(0.25f, 0.5f, 0.94f);
    public static readonly Color ContactNeutral = new(0.38f, 0.41f, 0.53f);

    public static Color GetContactColor(State.ContactType type) => type switch
    {
        State.ContactType.Friendly => ContactFriendly,
        State.ContactType.Hostile => ContactHostile,
        State.ContactType.Unknown => ContactUnknown,
        State.ContactType.Anomaly => ContactAnomaly,
        _ => ContactNeutral,
    };
}
