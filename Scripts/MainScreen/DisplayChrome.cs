using Godot;

namespace SpacedOut.MainScreen;

/// <summary>
/// Shared drawing helpers for bridge display panels (StarMapDisplay, TacticalDisplay).
/// </summary>
public static class DisplayChrome
{
    public static readonly Color BgColor = new(0.03f, 0.05f, 0.10f, 0.88f);
    public static readonly Color BorderColor = new(0.0f, 0.6f, 0.75f, 0.5f);
    public static readonly Color TitleColor = new(0.0f, 0.85f, 0.95f, 0.8f);
    public static readonly Color LabelDimColor = new(0.5f, 0.5f, 0.55f, 0.7f);

    public static void DrawPanelChrome(Control display, float width, float height,
                                        string title, string tag)
    {
        display.DrawRect(new Rect2(0, 0, width, height), BgColor);
        display.DrawRect(new Rect2(0, 0, width, height), BorderColor, false, 1.5f);

        display.DrawString(ThemeDB.FallbackFont, new Vector2(10, 18), title,
            HorizontalAlignment.Left, -1, 13, TitleColor);
        display.DrawString(ThemeDB.FallbackFont, new Vector2(width - 10, 18), tag,
            HorizontalAlignment.Right, -1, 11, LabelDimColor);
    }
}
