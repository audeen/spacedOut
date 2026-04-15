using System.Text;
using Godot;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Bottom-left mission log (crew actions, system messages).</summary>
public class MissionLogPanel
{
    private readonly Control _parent;
    private ScrollContainer _scroll = null!;
    private RichTextLabel _rich = null!;
    private int _lastCount = -1;
    private string _lastMissionId = "";

    public MissionLogPanel(Control parent) => _parent = parent;

    public void Build()
    {
        var wrap = UI.CreatePanel(TC.PanelBg);
        wrap.AnchorLeft = 0;
        wrap.AnchorTop = 1;
        wrap.AnchorRight = 0;
        wrap.AnchorBottom = 1;
        wrap.Position = new Vector2(20, -270);
        wrap.Size = new Vector2(500, 185);
        wrap.Name = "MissionLogPanel";
        wrap.ClipContents = true;
        _parent.AddChild(wrap);

        var inset = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        inset.AddThemeConstantOverride("margin_left", 10);
        inset.AddThemeConstantOverride("margin_top", 6);
        inset.AddThemeConstantOverride("margin_right", 10);
        inset.AddThemeConstantOverride("margin_bottom", 8);
        wrap.AddChild(inset);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 6);
        inset.AddChild(column);

        var title = UI.CreateLabel("MISSIONSLOG", 12, TC.DimWhite);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(title);

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(_scroll);

        _rich = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = false,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _rich.AddThemeColorOverride("default_color", TC.DimWhite);
        _rich.AddThemeFontSizeOverride("normal_font_size", 13);
        _scroll.AddChild(_rich);
    }

    public void Update(GameState state)
    {
        if (state.Mission.MissionId != _lastMissionId)
        {
            _lastMissionId = state.Mission.MissionId;
            _lastCount = -1;
        }

        var log = state.Mission.Log;
        if (log.Count == _lastCount)
            return;
        _lastCount = log.Count;

        const int maxLines = 48;
        var start = System.Math.Max(0, log.Count - maxLines);
        var sb = new StringBuilder();
        for (int i = start; i < log.Count; i++)
        {
            var e = log[i];
            int t = (int)e.Timestamp;
            int mm = t / 60;
            int ss = t % 60;
            sb.Append('[');
            sb.Append(mm.ToString("D2"));
            sb.Append(':');
            sb.Append(ss.ToString("D2"));
            sb.Append("] ");
            sb.Append(e.Source ?? "");
            sb.Append(" · ");
            sb.Append(e.Message ?? "");
            sb.Append('\n');
        }

        if (log.Count == 0)
            sb.Append(state.MissionStarted ? "—" : "Warte auf Mission…");

        _rich.Text = sb.ToString().TrimEnd();

        _parent.GetTree().CreateTimer(0).Timeout += ScrollRichToEnd;
    }

    private void ScrollRichToEnd()
    {
        if (_scroll == null || !GodotObject.IsInstanceValid(_scroll))
            return;
        var bar = _scroll.GetVScrollBar();
        _scroll.ScrollVertical = (int)bar.MaxValue;
    }

}
