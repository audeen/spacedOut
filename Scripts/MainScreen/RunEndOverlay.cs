using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using SpacedOut.Run;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Full-screen Victory/Defeat overlay shown when <see cref="RunStateSnapshot.Outcome"/>
/// leaves <see cref="RunOutcome.Ongoing"/>. Offers "Neuer Run" or "Hauptmen\u00fc".</summary>
public class RunEndOverlay
{
    private readonly Control _parent;
    private PanelContainer _panel = null!;
    private Label _titleLabel = null!;
    private Label _flavorLabel = null!;
    private Label _statsLabel = null!;
    private Label _stardustLabel = null!;
    private readonly Action _onNewRun;
    private readonly Action _onReturnToMenu;

    private RunOutcome _lastRenderedOutcome = RunOutcome.Ongoing;

    public RunEndOverlay(Control parent, Action onNewRun, Action onReturnToMenu)
    {
        _parent = parent;
        _onNewRun = onNewRun;
        _onReturnToMenu = onReturnToMenu;
    }

    public bool IsVisible => _panel?.Visible == true;

    public void Build()
    {
        _panel = UI.CreatePanel(new Color(0.02f, 0.03f, 0.06f, 0.95f));
        _panel.Name = "RunEndOverlay";
        _panel.AnchorLeft = 0f; _panel.AnchorTop = 0f;
        _panel.AnchorRight = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 0; _panel.OffsetTop = 0;
        _panel.OffsetRight = 0; _panel.OffsetBottom = 0;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _panel.Visible = false;
        _parent.AddChild(_panel);

        var center = new CenterContainer
        {
            AnchorLeft = 0f, AnchorTop = 0f,
            AnchorRight = 1f, AnchorBottom = 1f,
        };
        _panel.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        center.AddChild(box);

        _titleLabel = UI.CreateLabel("", 48, TC.White);
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_titleLabel);

        _flavorLabel = UI.CreateLabel("", 16, TC.DimWhite);
        _flavorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _flavorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _flavorLabel.CustomMinimumSize = new Vector2(620, 0);
        box.AddChild(_flavorLabel);

        _statsLabel = UI.CreateLabel("", 14, TC.White);
        _statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_statsLabel);

        _stardustLabel = UI.CreateLabel("", 18, TC.Yellow);
        _stardustLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_stardustLabel);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        var newRunBtn = new Button { Text = "Neuer Run", CustomMinimumSize = new Vector2(260, 56) };
        newRunBtn.AddThemeFontSizeOverride("font_size", 22);
        newRunBtn.Pressed += () => _onNewRun();
        box.AddChild(newRunBtn);

        var menuBtn = new Button { Text = "Hauptmen\u00fc", CustomMinimumSize = new Vector2(260, 44) };
        menuBtn.AddThemeFontSizeOverride("font_size", 18);
        menuBtn.Pressed += () => _onReturnToMenu();
        box.AddChild(menuBtn);
    }

    public void UpdateOutcome(GameState state)
    {
        var outcome = state.RunOutcome;
        bool show = outcome != RunOutcome.Ongoing;
        _panel.Visible = show;
        if (!show)
        {
            _lastRenderedOutcome = RunOutcome.Ongoing;
            return;
        }

        // Always refresh stardust line — values can change between renders even when the outcome is stable.
        int gain = state.LastRunStardustGain;
        _stardustLabel.Text = gain > 0
            ? $"\u2728 +{gain} Sternenstaub eingesammelt."
            : "Kein Sternenstaub diesen Run.";

        if (outcome == _lastRenderedOutcome) return;
        _lastRenderedOutcome = outcome;

        if (outcome == RunOutcome.Victory)
        {
            _titleLabel.Text = "RUN ABGESCHLOSSEN";
            _titleLabel.AddThemeColorOverride("font_color", TC.Green);
            _flavorLabel.Text = "Sprung aus dem Sektor gelungen. Der Run ist \u00fcberstanden.";
        }
        else if (state.Run.StrandedDefeat)
        {
            _titleLabel.Text = "GESTRANDET";
            _titleLabel.AddThemeColorOverride("font_color", TC.Yellow);
            _flavorLabel.Text = "Treibstoff aufgebraucht \u2014 das Schiff treibt f\u00fchrerlos durchs All.";
        }
        else
        {
            _titleLabel.Text = "SCHIFF ZERST\u00d6RT";
            _titleLabel.AddThemeColorOverride("font_color", TC.Red);
            _flavorLabel.Text = "H\u00fclle auf 0. Das Schiff ist verloren. Der Run endet.";
        }

        _statsLabel.Text = BuildStats(state);
    }

    public void Hide() => _panel.Visible = false;

    private static string BuildStats(GameState state)
    {
        var run = state.ActiveRunState;
        if (run == null) return "";
        var sb = new StringBuilder();
        sb.Append("Ressourcen bei Run-Ende: ");
        var parts = new List<string>();
        foreach (var kv in run.Resources)
            parts.Add($"{kv.Key} {kv.Value}");
        sb.Append(string.Join(" \u00b7 ", parts));
        return sb.ToString();
    }
}
