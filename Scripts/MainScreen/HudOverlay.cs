using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Meta;
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
    [Signal] public delegate void RunScanPressedEventHandler(string nodeId);
    [Signal] public delegate void NewRunRequestedEventHandler(string perkId);
    [Signal] public delegate void ReturnToMainMenuRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();
    [Signal] public delegate void ProfileRequestedEventHandler();
    [Signal] public delegate void ProfileClosedEventHandler();

    private GameState? _state;
    private SectorData? _sector;

    private ShipStatusPanel _shipPanel = null!;
    private MissionInfoPanel _missionPanel = null!;
    private MissionLogPanel _missionLogPanel = null!;
    private HudDebugPanel _debugPanel = null!;
    private RunPanel _runPanel = null!;
    private MainMenuOverlay _mainMenu = null!;
    private RunEndOverlay _runEndOverlay = null!;
    private ProfilePanel _profilePanel = null!;
    private MetaProgressService? _metaProgress;

    private StarMapDisplay _starMapDisplay = null!;
    private TacticalDisplay _tacticalDisplay = null!;
    private RunMapDisplay _runMapDisplay = null!;

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

        _missionLogPanel = new MissionLogPanel(this);
        _missionLogPanel.Build();

        BuildStationDisplays();

        _runPanel = new RunPanel(this, _runMapDisplay,
            () => EmitSignal(SignalName.RunEnterPressed),
            res => EmitSignal(SignalName.RunResolvePressed, res),
            nodeId => EmitSignal(SignalName.RunScanPressed, nodeId));
        _runPanel.Build();

        _debugPanel = new HudDebugPanel(this,
            cmd => EmitSignal(SignalName.DebugCommand, cmd),
            _state!.Debug);
        _debugPanel.Build();

        _runEndOverlay = new RunEndOverlay(this,
            onNewRun: () => EmitSignal(SignalName.NewRunRequested, ""),
            onReturnToMenu: () => EmitSignal(SignalName.ReturnToMainMenuRequested));
        _runEndOverlay.Build();

        _mainMenu = new MainMenuOverlay(this,
            onNewRun: perkId => EmitSignal(SignalName.NewRunRequested, perkId ?? ""),
            onQuit: () => EmitSignal(SignalName.QuitRequested),
            onProfile: () => EmitSignal(SignalName.ProfileRequested),
            profileGetter: () => _metaProgress?.Profile);
        _mainMenu.Build();

        _profilePanel = new ProfilePanel(this,
            serviceGetter: () => _metaProgress,
            onClose: () => EmitSignal(SignalName.ProfileClosed),
            onChanged: () => _mainMenu.Refresh());
        _profilePanel.Build();
    }

    /// <summary>M7: wires the persistent meta service so the menu/profile UI can read Sternenstaub
    /// and unlock state. Must be called after <see cref="Initialize"/>.</summary>
    public void SetMetaProgress(MetaProgressService meta)
    {
        _metaProgress = meta;
        _mainMenu?.Refresh();
        _profilePanel?.Refresh();
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
        _missionLogPanel.Update(state);
        UpdateStationDisplays(state);
        _debugPanel.UpdateGodmodeStatus();
        _debugPanel.UpdateSkyboxStatus();
        _debugPanel.UpdateSectorContactsDebugList(state);

        if (state.RunActive && run != null)
            UpdateRunUi(run);
    }

    private void UpdateStationDisplays(GameState state)
    {
        bool profileActive = state.ShowProfile;
        bool menuActive = state.ShowMainMenu && !profileActive;
        bool runEnded = state.RunOutcome != RunOutcome.Ongoing && !profileActive;

        if (profileActive) _profilePanel.Show(); else _profilePanel.Hide();
        if (menuActive) _mainMenu.Show(); else _mainMenu.Hide();
        if (profileActive) _runEndOverlay.Hide(); else _runEndOverlay.UpdateOutcome(state);

        // Whenever any full-screen overlay is up, suppress all other panels/maps.
        bool suppressAll = profileActive || menuActive || runEnded;

        bool runMapActive = !suppressAll && state.ShowRunMapOnMainScreen && state.RunActive;

        var pinnedIds = new System.Collections.Generic.HashSet<string>(
            state.PinnedEntities.Select(p => p.EntityId));

        _starMapDisplay.Visible = !suppressAll && state.ShowStarMapOnMainScreen && !runMapActive;
        if (_starMapDisplay.Visible)
        {
            _starMapDisplay.UpdateState(state);
            _starMapDisplay.UpdatePinnedIds(pinnedIds);
        }

        _tacticalDisplay.Visible = !suppressAll && state.ShowTacticalOnMainScreen && !runMapActive;
        if (_tacticalDisplay.Visible)
        {
            _tacticalDisplay.UpdateState(state);
            _tacticalDisplay.UpdatePinnedIds(pinnedIds);
        }

        _runMapDisplay.Visible = runMapActive;
        _runPanel.UpdateVisibility(runMapActive);
    }

    public void ShowMainMenu()
    {
        if (_state != null)
            _state.ShowMainMenu = true;
        _mainMenu?.Show();
    }

    public void HideMainMenu()
    {
        if (_state != null)
            _state.ShowMainMenu = false;
        _mainMenu?.Hide();
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

    public void UpdateRunUi(RunController run)
    {
        _runPanel.UpdateRun(run);
        _debugPanel.UpdateDirectorInfo(run);
    }

    public void UpdateLevelGenInfo(LevelGenerator? gen) =>
        _debugPanel.UpdateLevelGenInfo(gen);
}
