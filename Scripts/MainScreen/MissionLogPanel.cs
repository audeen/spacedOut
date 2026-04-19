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
    private Label _commsHighlight = null!;
    private Label _decisionHighlight = null!;
    private int _lastCount = -1;
    private string _lastMissionId = "";

    public MissionLogPanel(Control parent) => _parent = parent;

    private static Color SourceAccent(string? src) => src switch
    {
        "System" => TC.Yellow,
        "CaptainNav" or "Captain" => TC.Purple,
        "Navigation" or "Navigator" => TC.Cyan,
        "Tactical" => TC.Orange,
        "Engineer" => TC.Green,
        "Gunner" => TC.Red,
        _ => TC.DimWhite,
    };

    public void Build()
    {
        var wrap = UI.CreatePanel(TC.PanelBg);
        wrap.AnchorLeft = 0;
        wrap.AnchorTop = 1;
        wrap.AnchorRight = 0;
        wrap.AnchorBottom = 1;
        wrap.Position = new Vector2(20, -300);
        wrap.Size = new Vector2(500, 220);
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
        column.AddThemeConstantOverride("separation", 4);
        inset.AddChild(column);

        _commsHighlight = UI.CreateLabel("", 11, TC.Purple);
        _commsHighlight.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _commsHighlight.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _commsHighlight.Visible = false;
        column.AddChild(_commsHighlight);

        _decisionHighlight = UI.CreateLabel("", 11, TC.Cyan);
        _decisionHighlight.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _decisionHighlight.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _decisionHighlight.Visible = false;
        column.AddChild(_decisionHighlight);

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

        var comms = state.Mission.LastCommsHighlight ?? "";
        var dec = state.Mission.LastDecisionHighlight ?? "";
        _commsHighlight.Visible = !string.IsNullOrEmpty(comms);
        _commsHighlight.Text = _commsHighlight.Visible ? $"Letzte Funk: {comms}" : "";
        _decisionHighlight.Visible = !string.IsNullOrEmpty(dec);
        _decisionHighlight.Text = _decisionHighlight.Visible ? $"Letzte Entscheidung: {dec}" : "";

        var log = state.Mission.Log;
        if (log.Count == _lastCount)
            return;
        _lastCount = log.Count;

        const int maxLines = 48;
        var start = System.Math.Max(0, log.Count - maxLines);

        _rich.Clear();

        if (log.Count == 0)
        {
            _rich.AppendText(state.MissionStarted ? "—" : "Warte auf Mission…");
            _parent.GetTree().CreateTimer(0).Timeout += ScrollRichToEnd;
            return;
        }

        for (int i = start; i < log.Count; i++)
        {
            var e = log[i];
            int t = (int)e.Timestamp;
            int mm = t / 60;
            int ss = t % 60;

            _rich.PushColor(TC.DimWhite);
            _rich.AppendText($"[{mm:D2}:{ss:D2}] ");
            _rich.Pop();

            _rich.PushColor(SourceAccent(e.Source));
            _rich.AppendText(e.Source ?? "");
            _rich.AppendText(" · ");
            _rich.Pop();

            _rich.PushColor(TC.White);
            _rich.AppendText(e.Message ?? "");
            _rich.Pop();
            _rich.AppendText("\n");
        }

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
