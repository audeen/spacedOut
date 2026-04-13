using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Run;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.MainScreen;

public partial class HudOverlay : Control
{
    [Signal] public delegate void DebugModeChangedEventHandler(bool enabled);
    [Signal] public delegate void DebugCommandEventHandler(string command);
    [Signal] public delegate void RunMapNodeClickedEventHandler(string nodeId);
    [Signal] public delegate void RunEnterPressedEventHandler();
    [Signal] public delegate void RunResolvePressedEventHandler(int resolution);

    private GameState? _state;
    private SectorData? _sector;

    private ShipStatusPanel _shipPanel = null!;
    private MissionInfoPanel _missionPanel = null!;
    private HudDebugPanel _debugPanel = null!;
    private RunPanel _runPanel = null!;

    private StarMapDisplay _starMapDisplay = null!;
    private TacticalDisplay _tacticalDisplay = null!;
    private RunMapDisplay _runMapDisplay = null!;
    private PinnedInfoPanel _pinnedPanel = null!;

    public void Initialize(GameState state)
    {
        _state = state;
        BuildHud();
    }

    public void UpdateSector(SectorData? sector)
    {
        _sector = sector;
        _starMapDisplay.UpdateSector(sector);
        _tacticalDisplay.UpdateSector(sector);
    }

    private void BuildHud()
    {
        AnchorsPreset = (int)LayoutPreset.FullRect;

        _shipPanel = new ShipStatusPanel(this);
        _shipPanel.Build();

        _missionPanel = new MissionInfoPanel(this);
        _missionPanel.Build();

        BuildStationDisplays();

        _runPanel = new RunPanel(this, _runMapDisplay,
            () => EmitSignal(SignalName.RunEnterPressed),
            res => EmitSignal(SignalName.RunResolvePressed, res));
        _runPanel.Build();

        _pinnedPanel = new PinnedInfoPanel
        {
            Name = "PinnedInfoPanel",
            AnchorLeft = 1f, AnchorTop = 1f,
            AnchorRight = 1f, AnchorBottom = 1f,
            Position = new Vector2(-240, -210),
            Size = new Vector2(220, 200),
        };
        AddChild(_pinnedPanel);

        _debugPanel = new HudDebugPanel(this,
            cmd => EmitSignal(SignalName.DebugCommand, cmd),
            _state!.Debug);
        _debugPanel.Build();
    }

    private void BuildStationDisplays()
    {
        _starMapDisplay = new StarMapDisplay
        {
            Name = "StarMapDisplay",
            AnchorLeft = 0f, AnchorTop = 0.5f,
            AnchorRight = 0f, AnchorBottom = 0.5f,
            Position = new Vector2(20, -200),
            Size = new Vector2(400, 400),
            Visible = false,
        };
        AddChild(_starMapDisplay);

        _tacticalDisplay = new TacticalDisplay
        {
            Name = "TacticalDisplay",
            AnchorLeft = 1f, AnchorTop = 0.5f,
            AnchorRight = 1f, AnchorBottom = 0.5f,
            Position = new Vector2(-420, -200),
            Size = new Vector2(400, 400),
            Visible = false,
        };
        AddChild(_tacticalDisplay);

        _runMapDisplay = new RunMapDisplay
        {
            Name = "RunMapDisplay",
            AnchorLeft = 0.5f, AnchorTop = 0.5f,
            AnchorRight = 0.5f, AnchorBottom = 0.5f,
            Position = new Vector2(-450, -220),
            Size = new Vector2(900, 440),
            Visible = false,
        };
        _runMapDisplay.RunNodeClicked += nodeId =>
            EmitSignal(SignalName.RunMapNodeClicked, nodeId);
        AddChild(_runMapDisplay);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F12)
        {
            _debugPanel.Toggle();
            EmitSignal(SignalName.DebugModeChanged, _debugPanel.IsVisible);
        }
    }

    public void UpdateDisplay(GameState state, RunController? run = null)
    {
        if (state == null) return;
        _state = state;

        float delta = (float)GetProcessDeltaTime();
        _shipPanel.Update(state);
        _missionPanel.Update(state, delta);
        UpdateStationDisplays(state);
        _debugPanel.UpdateGodmodeStatus();

        if (state.RunActive && run != null)
            UpdateRunUi(run);
    }

    private void UpdateStationDisplays(GameState state)
    {
        bool runMapActive = state.ShowRunMapOnMainScreen && state.RunActive;

        var pinnedIds = new System.Collections.Generic.HashSet<string>(
            state.PinnedEntities.Select(p => p.EntityId));

        _starMapDisplay.Visible = state.ShowStarMapOnMainScreen && !runMapActive;
        if (_starMapDisplay.Visible)
        {
            _starMapDisplay.UpdateState(state);
            _starMapDisplay.UpdatePinnedIds(pinnedIds);
        }

        _tacticalDisplay.Visible = state.ShowTacticalOnMainScreen && !runMapActive;
        if (_tacticalDisplay.Visible)
        {
            _tacticalDisplay.UpdateState(state);
            _tacticalDisplay.UpdatePinnedIds(pinnedIds);
        }

        _pinnedPanel.UpdateState(state, _sector);

        _runMapDisplay.Visible = runMapActive;
        _runPanel.UpdateVisibility(runMapActive);
    }

    public static string TranslatePhase(MissionPhase phase) => phase switch
    {
        MissionPhase.Briefing => "BRIEFING",
        MissionPhase.Anflug => "ANFLUG",
        MissionPhase.Stoerung => "STÖRUNG",
        MissionPhase.Krisenfenster => "KRISENFENSTER",
        MissionPhase.Abschluss => "ABSCHLUSS",
        MissionPhase.Operational => "EINSATZ",
        MissionPhase.Ended => "BEENDET",
        _ => "---"
    };

    public void SetRunMapSelection(string nodeId) =>
        _runPanel.SelectedNodeId = nodeId;

    public string? GetRunMapSelection() =>
        _runPanel.SelectedNodeId;

    public void UpdateRunUi(RunController run) =>
        _runPanel.UpdateRun(run);

    public void UpdateLevelGenInfo(LevelGenerator? gen) =>
        _debugPanel.UpdateLevelGenInfo(gen);
}
