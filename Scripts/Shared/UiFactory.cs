using Godot;

namespace SpacedOut.Shared;

public static class UiFactory
{
    public static PanelContainer CreatePanel(Color bgColor)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            BorderColor = new Color(0.15f, 0.2f, 0.35f, 0.6f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    public static PanelContainer CreateDebugPanel(Color bgColor, Color borderColor)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    public static Label CreateLabel(string text, int fontSize, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    public static Control CreateSpacer(float width)
    {
        return new Control { CustomMinimumSize = new Vector2(width, 0) };
    }
}
