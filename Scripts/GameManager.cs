using System;
using Godot;
using SpacedOut.Commands;
using SpacedOut.Fx;
using SpacedOut.LevelGen;
using SpacedOut.Meta;
using SpacedOut.Mission;
using SpacedOut.Network;
using SpacedOut.Orchestration;
using SpacedOut.Player;
using SpacedOut.Run;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut;

public partial class GameManager : Node3D
{
    private GameState _gameState = new();
    private GameServer _server = null!;
    private CommandProcessor _commandProcessor = null!;
    private MissionController _missionController = null!;
    private MainScreen.HudOverlay _hud = null!;
    private LevelGenerator? _levelGenerator;
    private Camera3D? _bridgeCamera;
    private FlyCamera? _flyCamera;
    private Node3D? _spaceBackground;
    private bool _flyMode;

    private BroadcastService _broadcast = null!;
    private RunOrchestrator _runOrch = null!;
    private MissionOrchestrator _missionOrch = null!;
    private DebugCommandHandler _debugHandler = null!;
    private MetaProgressService _metaProgress = null!;
    private Sector3DMarkers? _markers;
    private Node? _shipScaleCompareRoot;
    private Node3D? _playerShipAnchor;
    private CombatFxSystem? _combatFx;

    private bool _debugMode = true;
    private int _port = 8080;

    public override void _Ready()
    {
        GD.Print("=== SpacedOut - Brückenspiel MVP ===");
        GD.Print(GameFeatures.ResourceZonesEnabled
            ? "[Features] Ressourcenzonen: aktiv"
            : "[Features] Ressourcenzonen: deaktiviert (GameFeatures.ResourceZonesEnabled = false)");
        GD.Print(GameFeatures.SmallAsteroidsEnabled
            ? "[Features] Kleine Asteroiden/Scatter: aktiv"
            : "[Features] Kleine Asteroiden/Scatter: deaktiviert (GameFeatures.SmallAsteroidsEnabled = false)");
        GD.Print(GameFeatures.SkyboxEnabled
            ? "[Features] Skybox: aktiv"
            : "[Features] Skybox: deaktiviert (GameFeatures.SkyboxEnabled = false)");

        _server = new GameServer();
        AddChild(_server);

        _commandProcessor = new CommandProcessor();
        AddChild(_commandProcessor);

        _missionController = new MissionController();
        AddChild(_missionController);

        _commandProcessor.Initialize(_gameState, _server);
        _missionController.Initialize(_gameState);
        _commandProcessor.SetMissionController(_missionController);

        _broadcast = new BroadcastService(_gameState, _server);
        _commandProcessor.SetBroadcastService(_broadcast);
        _runOrch = new RunOrchestrator(new RunController(), _gameState, _server, _broadcast);
        _missionOrch = new MissionOrchestrator(_missionController, _gameState, _broadcast);
        _commandProcessor.SetMissionOrchestrator(_missionOrch);

        _metaProgress = new MetaProgressService();
        _metaProgress.Load();
        _runOrch.SetMetaProgress(_metaProgress);
        _missionOrch.SetMetaProgress(_metaProgress);

        // Director-Wiring: RunOrchestrator instantiates the director on StartRun; bind it to
        // MissionOrchestrator so event picks and generic spawn profiles route through pacing.
        _runOrch.SetMissionOrchestrator(_missionOrch);

        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.CommandReceived += OnCommandReceived;
        _server.RoleSelected += OnRoleSelected;

        _commandProcessor.StateChanged += () => _broadcast.BroadcastStateUpdates();
        _commandProcessor.NodeSelected += nodeId => _runOrch.SelectRunNode(nodeId, _missionOrch);
        _commandProcessor.RunResolveRequested += (nodeId, res) => _runOrch.ResolveFromNetwork(nodeId, res, _hud);
        _commandProcessor.ScanRunNodeRequested += nodeId => _runOrch.ScanRunNode(nodeId);
        _commandProcessor.PreSectorDecisionResolved += (nodeId, eventId, skip) =>
            _missionOrch.ResolvePreSectorDecision(nodeId, eventId, skip, _runOrch);
        _missionController.PhaseChanged += phase => _broadcast.BroadcastPhaseChanged(phase);
        _missionController.EventTriggered += eventId => _broadcast.BroadcastEvent(eventId);
        _missionController.MissionEnded += OnMissionEnded;

        SetupMainScreen();
        SetupLevelGenerator();
        SetupCombatFx();

        _debugHandler = new DebugCommandHandler(
            _gameState, _missionController, _missionOrch, _runOrch,
            ToggleFlyCamera, OnBiomeChanged,
            refreshSkybox: () => _levelGenerator?.RefreshSkybox(),
            broadcastStateUpdates: () => _broadcast.BroadcastStateUpdates(),
            toggleShipScaleCompare: ToggleShipScaleCompare,
            metaProgress: _metaProgress);

        _gameState.ShowMainMenu = true;
        _hud?.ShowMainMenu();
        _server.StartServer(_port);
    }

    private void SetupMainScreen()
    {
        var hudLayer = GetNode<CanvasLayer>("HUD");
        _hud = hudLayer.GetNode<MainScreen.HudOverlay>("HudOverlay");
        _hud.Initialize(_gameState);
        _hud.SetMetaProgress(_metaProgress);
        _hud.DebugModeChanged += enabled => _debugMode = enabled;
        _hud.DebugCommand += cmd => _debugHandler.Execute(cmd);
        _hud.RunMapNodeClicked += nodeId => _hud.SetRunMapSelection(nodeId);
        _hud.RunEnterPressed += () =>
        {
            var id = _hud.GetRunMapSelection();
            if (!string.IsNullOrEmpty(id))
                _runOrch.SelectRunNode(id, _missionOrch);
        };
        _hud.RunResolvePressed += resolution => _runOrch.ResolveFromHud(resolution, _hud);
        _hud.RunScanPressed += nodeId => _runOrch.ScanRunNode(nodeId);
        _hud.NewRunRequested += perkId =>
        {
            _hud.HideMainMenu();
            _gameState.ShowProfile = false;
            string? selected = string.IsNullOrEmpty(perkId) ? null : perkId;
            _runOrch.StartRun(missionController: _missionController, perkId: selected);
        };
        _hud.ReturnToMainMenuRequested += () =>
        {
            _gameState.RunOutcome = RunOutcome.Ongoing;
            _gameState.RunActive = false;
            _gameState.ShowProfile = false;
            _hud.ShowMainMenu();
        };
        _hud.ProfileRequested += () =>
        {
            _gameState.ShowProfile = true;
        };
        _hud.ProfileClosed += () =>
        {
            _gameState.ShowProfile = false;
        };
        _hud.QuitRequested += () => GetTree().Quit();
    }

    private void SetupLevelGenerator()
    {
        _levelGenerator = GetNodeOrNull<LevelGenerator>("LevelGenerator");
        _bridgeCamera = GetNodeOrNull<Camera3D>("Camera3D");
        _flyCamera = _levelGenerator?.GetNodeOrNull<FlyCamera>("FlyCamera");
        _spaceBackground = GetNodeOrNull<Node3D>("SpaceBackground");

        _missionOrch.SetLevelGenerator(_levelGenerator);

        if (_levelGenerator != null)
        {
            var markerContainer = _levelGenerator.GetNode<Node3D>("GeneratedLevel");
            _markers = new Sector3DMarkers(markerContainer);
            _missionOrch.SetMarkers(_markers);
        }
    }

    /// <summary>
    /// Creates the invisible player-ship anchor node (used as muzzle origin for
    /// gunner fire and impact target for enemy fire, since no 3D ship node exists)
    /// plus the <see cref="CombatFxSystem"/> that spawns short-lived projectile/beam FX.
    /// </summary>
    private void SetupCombatFx()
    {
        _playerShipAnchor = new Node3D { Name = "PlayerShipAnchor" };
        AddChild(_playerShipAnchor);

        _combatFx = new CombatFxSystem { Name = "CombatFx" };
        AddChild(_combatFx);
        _combatFx.Initialize(_gameState, _missionOrch, _playerShipAnchor);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_gameState.MissionStarted && !_gameState.IsPaused)
            _missionController.Update(dt);

        UpdateBridgeCamera(dt);
        _broadcast.Update(dt);
        _runOrch.SyncRunToState();
        _missionOrch.UpdateMarkers();

        if (_playerShipAnchor != null)
            _playerShipAnchor.GlobalPosition = MapToWorld(
                _gameState.Ship.PositionX, _gameState.Ship.PositionY, _gameState.Ship.PositionZ);

        _hud?.UpdateDisplay(_gameState, _runOrch.Controller);

        if (_missionOrch.CurrentSector != null)
        {
            _hud?.UpdateSector(_missionOrch.CurrentSector);
            _commandProcessor.SetSectorData(_missionOrch.CurrentSector);
        }

        if (Input.IsActionJustPressed("ui_cancel"))
            TogglePause();
    }

    #region Bridge Camera

    private Vector3 MapToWorld(float mapX, float mapY, float mapZ) =>
        CoordinateMapper.MapToWorld(mapX, mapY, mapZ, _missionOrch.LevelRadius);

    private Vector3 MapToWorld(float mapX, float mapY) =>
        CoordinateMapper.MapToWorld(mapX, mapY, _missionOrch.LevelRadius);

    private void SnapBridgeCameraToShip()
    {
        if (_bridgeCamera == null) return;
        var pos = MapToWorld(_gameState.Ship.PositionX, _gameState.Ship.PositionY, _gameState.Ship.PositionZ);
        _bridgeCamera.GlobalPosition = pos;

        if (TryGetBridgeCameraLookTargetWorld(out var lookAtWorld)
            && pos.DistanceTo(lookAtWorld) > 1f)
        {
            _bridgeCamera.LookAt(lookAtWorld, Vector3.Up);
        }

        if (_spaceBackground != null)
            _spaceBackground.GlobalPosition = pos;
    }

    private void UpdateBridgeCamera(float delta)
    {
        if (_bridgeCamera == null || _flyMode) return;

        var targetPos = MapToWorld(_gameState.Ship.PositionX, _gameState.Ship.PositionY, _gameState.Ship.PositionZ);
        float smoothPos = 1f - MathF.Exp(-8f * delta);
        _bridgeCamera.GlobalPosition = _bridgeCamera.GlobalPosition.Lerp(targetPos, smoothPos);

        if (TryGetBridgeCameraLookTargetWorld(out var lookAtWorld)
            && _bridgeCamera.GlobalPosition.DistanceTo(lookAtWorld) > 5f)
        {
            var target = _bridgeCamera.GlobalTransform.LookingAt(lookAtWorld, Vector3.Up);
            float smoothRot = 1f - MathF.Exp(-3f * delta);
            _bridgeCamera.GlobalTransform = _bridgeCamera.GlobalTransform.InterpolateWith(
                target, smoothRot);
        }

        var activeCam = GetViewport().GetCamera3D();
        if (_spaceBackground != null && activeCam != null)
            _spaceBackground.GlobalPosition = activeCam.GlobalPosition;
    }

    /// <summary>
    /// Bridge camera look-at target in world space: target tracking, then first
    /// unreached waypoint; if all waypoints are reached, the last waypoint in the
    /// route (final destination); otherwise the sector origin (map center) at mission
    /// start when no route exists yet.
    /// </summary>
    private bool TryGetBridgeCameraLookTargetWorld(out Vector3 worldPos)
    {
        var ship = _gameState.Ship;
        var tt = _gameState.Navigation.TargetTracking;
        var contact = ShipCalculations.FindValidTrackingContact(_gameState, tt);
        if (tt.Mode != TargetTrackingMode.None && contact != null)
        {
            if (ShipCalculations.TryGetTrackingSteerTarget(tt, ship, contact,
                    out var tx, out var ty, out var tz, out var shouldMove)
                && shouldMove)
            {
                worldPos = MapToWorld(tx, ty, tz);
                return true;
            }

            worldPos = MapToWorld(contact.PositionX, contact.PositionY, contact.PositionZ);
            return true;
        }

        var unreached = _gameState.Route.Waypoints.FindAll(w => !w.IsReached);
        if (unreached.Count > 0)
        {
            var wp = unreached[0];
            worldPos = MapToWorld(wp.X, wp.Y, wp.Z);
            return true;
        }

        var route = _gameState.Route.Waypoints;
        if (route.Count > 0)
        {
            var last = route[^1];
            worldPos = MapToWorld(last.X, last.Y, last.Z);
            return true;
        }

        // Kein Nav-Ziel: Richtung Sektormitte (Kartenmittelpunkt), u. a. direkt beim Levelstart.
        worldPos = CoordinateMapper.SectorOriginWorld(_missionOrch.LevelRadius);
        return true;
    }

    #endregion

    #region Event Handlers

    private void OnClientConnected(string clientId)
    {
        GD.Print($"[GameManager] Client verbunden: {clientId}");
        _broadcast.SendWelcome(clientId);
        if (_gameState.RunActive)
            _runOrch.SendRunStateToClient(clientId);
    }

    private void OnClientDisconnected(string clientId)
    {
        GD.Print($"[GameManager] Client getrennt: {clientId}");
        _broadcast.BroadcastRoleStatus();
    }

    private void OnRoleSelected(string clientId, string roleStr)
    {
        if (!Enum.TryParse<StationRole>(roleStr, true, out var role))
        {
            GD.PrintErr($"[GameManager] Unbekannte Rolle: {roleStr}");
            return;
        }

        if (_server.IsRoleTaken(role))
        {
            _broadcast.SendError(clientId, $"Rolle {role} ist bereits vergeben.");
            return;
        }

        _server.AssignRole(clientId, role);
        _broadcast.BroadcastRoleStatus();

        if (_gameState.RunActive)
        {
            _runOrch.BroadcastRunState();
            return;
        }

        if ((_debugMode || AllRolesFilled()) && !_gameState.MissionStarted)
            _missionOrch.StartMission();
    }

    private bool AllRolesFilled() =>
        _server.IsRoleTaken(StationRole.CaptainNav) &&
        _server.IsRoleTaken(StationRole.Engineer) &&
        _server.IsRoleTaken(StationRole.Tactical) &&
        _server.IsRoleTaken(StationRole.Gunner);

    private void OnCommandReceived(string clientId, string messageJson)
    {
        if (!_commandProcessor.ProcessCommand(clientId, messageJson))
            _broadcast.SendError(clientId, "Befehl konnte nicht ausgeführt werden.");
    }

    private void OnMissionEnded(int resultInt)
    {
        var result = (MissionResult)resultInt;
        _broadcast.BroadcastMissionEnded(result);

        if (_gameState.RunActive)
            _runOrch.OnMissionEnded(result, _hud);
    }

    #endregion

    #region Camera Toggle

    private void ToggleFlyCamera()
    {
        if (_flyCamera == null || _bridgeCamera == null) return;
        _flyMode = !_flyMode;

        if (_flyMode)
        {
            if (_levelGenerator != null)
                _flyCamera.Teleport(_levelGenerator.SpawnPoint, Vector3.Zero);
            _flyCamera.Activate();
            GD.Print("[GameManager] Debug: Fly-Kamera aktiviert");
        }
        else
        {
            _flyCamera.Deactivate();
            _bridgeCamera.Current = true;
            GD.Print("[GameManager] Debug: Brückenkamera aktiviert");
        }
    }

    private void OnBiomeChanged(string _biomeId)
    {
        if (_flyMode && _flyCamera != null && _levelGenerator != null)
            _flyCamera.Teleport(_levelGenerator.SpawnPoint, Vector3.Zero);
        else
            SnapBridgeCameraToShip();
        _hud?.UpdateLevelGenInfo(_levelGenerator);
        _hud?.UpdateSector(_missionOrch.CurrentSector);
    }

    /// <summary>
    /// Spawns or removes the ship scale comparison scene under the generated level (FlyCam).
    /// </summary>
    private void ToggleShipScaleCompare()
    {
        if (_levelGenerator == null)
        {
            GD.PrintErr("[Debug] Schiff-Vergleich: kein LevelGenerator.");
            return;
        }

        var container = _levelGenerator.GetNode<Node3D>("GeneratedLevel");
        if (_shipScaleCompareRoot != null && GodotObject.IsInstanceValid(_shipScaleCompareRoot))
        {
            _shipScaleCompareRoot.QueueFree();
            _shipScaleCompareRoot = null;
            GD.Print("[Debug] Schiff-Vergleich ausgeblendet.");
            return;
        }

        _shipScaleCompareRoot = null;
        var scene = GD.Load<PackedScene>("res://Scenes/Debug/ShipScaleCompare.tscn");
        if (scene == null)
        {
            GD.PrintErr("[Debug] Schiff-Vergleich: ShipScaleCompare.tscn nicht gefunden.");
            return;
        }

        var root = scene.Instantiate<Node3D>();
        root.Position = _levelGenerator.SpawnPoint;
        container.AddChild(root);
        _shipScaleCompareRoot = root;
        GD.Print("[Debug] Schiff-Vergleich: NPC-Schiffe + je ein AssetLibrary-Eintrag (Meshes/Platzhalter) am Spawn (FlyCam). Erneut klicken zum Ausblenden.");
    }

    #endregion

    public void TogglePause()
    {
        _gameState.IsPaused = !_gameState.IsPaused;
        _broadcast.BroadcastPaused();
        GD.Print($"[GameManager] {(_gameState.IsPaused ? "Pausiert" : "Fortgesetzt")}");
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = 1f;
        _server?.StopServer();
    }
}
