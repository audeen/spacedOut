using Godot;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Top bar: mission title / phase, timer, flight mode, speed, hull.</summary>
public class ShipStatusPanel
{
    private Label _phaseLabel = null!;
    private Label _timerLabel = null!;
    private Label _hullBar = null!;
    private Label _flightModeLabel = null!;
    private Label _speedLabel = null!;

    private readonly Control _parent;

    public ShipStatusPanel(Control parent)
    {
        _parent = parent;
    }

    public void Build()
    {
        var topBar = UI.CreatePanel(TC.PanelBg);
        topBar.Position = new Vector2(20, 10);
        topBar.Size = new Vector2(800, 50);
        _parent.AddChild(topBar);

        var topHbox = new HBoxContainer { Position = new Vector2(15, 8) };
        topBar.AddChild(topHbox);

        _phaseLabel = UI.CreateLabel("—", 20, TC.Cyan);
        topHbox.AddChild(_phaseLabel);
        topHbox.AddChild(UI.CreateSpacer(40));

        _timerLabel = UI.CreateLabel("00:00", 24, TC.White);
        topHbox.AddChild(_timerLabel);
        topHbox.AddChild(UI.CreateSpacer(40));

        _flightModeLabel = UI.CreateLabel("CRUISE", 18, TC.Green);
        topHbox.AddChild(_flightModeLabel);
        topHbox.AddChild(UI.CreateSpacer(20));

        _speedLabel = UI.CreateLabel("SPD: 2", 18, TC.DimWhite);
        topHbox.AddChild(_speedLabel);

        var hullPanel = UI.CreatePanel(TC.PanelBg);
        hullPanel.AnchorLeft = 1; hullPanel.AnchorRight = 1;
        hullPanel.Position = new Vector2(-220, 10);
        hullPanel.Size = new Vector2(200, 50);
        _parent.AddChild(hullPanel);

        _hullBar = UI.CreateLabel("HULL: 100%", 22, TC.Green);
        _hullBar.Position = new Vector2(15, 10);
        hullPanel.AddChild(_hullBar);
    }

    public void Update(GameState state)
    {
        if (state.Mission.Phase == MissionPhase.Ended)
            _phaseLabel.Text = "BEENDET";
        else if (state.Mission.UseStructuredMissionPhases)
            _phaseLabel.Text = $"PHASE: {HudOverlay.TranslatePhase(state.Mission.Phase)}";
        else
        {
            var title = state.Mission.MissionTitle;
            if (string.IsNullOrEmpty(title)) title = "EINSATZ";
            _phaseLabel.Text = title.Length > 40 ? title[..40] + "…" : title;
        }

        int mins = (int)(state.Mission.ElapsedTime / 60);
        int secs = (int)(state.Mission.ElapsedTime % 60);
        _timerLabel.Text = $"{mins:D2}:{secs:D2}";

        _flightModeLabel.Text = state.Ship.FlightMode.ToString().ToUpper();
        _flightModeLabel.AddThemeColorOverride("font_color",
            state.Ship.FlightMode == FlightMode.Evasive ? TC.Yellow : TC.Green);

        _speedLabel.Text = $"SPD: {state.Ship.SpeedLevel}";

        float hull = state.Ship.HullIntegrity;
        _hullBar.Text = $"HULL: {hull:F0}%";
        _hullBar.AddThemeColorOverride("font_color",
            hull > 60 ? TC.Green : hull > 30 ? TC.Yellow : TC.Red);
    }
}
