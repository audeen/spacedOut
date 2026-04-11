using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using SpacedOut.Campaign;
using SpacedOut.Commands;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.Network;
using SpacedOut.Player;
using SpacedOut.State;

namespace SpacedOut;

public partial class GameManager : Node3D
{
    private GameState _gameState = new();
    private GameServer _server = null!;
    private CommandProcessor _commandProcessor = null!;
    private MissionController _missionController = null!;
    private CampaignManager _campaignManager = null!;
    private MainScreen.HudOverlay _hud = null!;
    private LevelGenerator? _levelGenerator;
    private Camera3D? _bridgeCamera;
    private FlyCamera? _flyCamera;
    private Node3D? _spaceBackground;
    private bool _flyMode;
    private float _levelRadius = 400f;

    private float _stateUpdateTimer;
    private const float StateUpdateInterval = 0.1f;

    private int _port = 8080;
    private bool _debugMode = true;

    private Dictionary<string, Action>? _debugCommands;

    public override void _Ready()
    {
        GD.Print("=== SpacedOut - Brückenspiel MVP ===");

        _server = new GameServer();
        AddChild(_server);

        _commandProcessor = new CommandProcessor();
        AddChild(_commandProcessor);

        _missionController = new MissionController();
        AddChild(_missionController);

        _campaignManager = new CampaignManager();
        AddChild(_campaignManager);

        _commandProcessor.Initialize(_gameState, _server);
        _missionController.Initialize(_gameState);

        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.CommandReceived += OnCommandReceived;
        _server.RoleSelected += OnRoleSelected;

        _commandProcessor.StateChanged += OnStateChanged;
        _commandProcessor.NodeSelected += OnNodeSelected;
        _missionController.PhaseChanged += OnPhaseChanged;
        _missionController.EventTriggered += OnEventTriggered;
        _missionController.MissionEnded += OnMissionEnded;

        _campaignManager.NodeCompleted += OnCampaignNodeCompleted;
        _campaignManager.SectorCompleted += OnCampaignSectorCompleted;
        _campaignManager.CampaignEnded += OnCampaignEnded;
        _campaignManager.MapUpdated += OnCampaignMapUpdated;

        SetupMainScreen();
        SetupLevelGenerator();
        InitDebugCommands();
        _server.StartServer(_port);
    }

    private void SetupMainScreen()
    {
        var env = GetNode<WorldEnvironment>("WorldEnvironment");
        var camera = GetNode<Camera3D>("Camera3D");

        var hudLayer = GetNode<CanvasLayer>("HUD");
        _hud = hudLayer.GetNode<MainScreen.HudOverlay>("HudOverlay");
        _hud.Initialize(_gameState);
        _hud.DebugModeChanged += OnDebugModeChanged;
        _hud.DebugCommand += OnDebugCommand;
    }

    private void SetupLevelGenerator()
    {
        _levelGenerator = GetNodeOrNull<LevelGenerator>("LevelGenerator");
        _bridgeCamera = GetNodeOrNull<Camera3D>("Camera3D");
        _flyCamera = _levelGenerator?.GetNodeOrNull<FlyCamera>("FlyCamera");
        _spaceBackground = GetNodeOrNull<Node3D>("SpaceBackground");

        if (_levelGenerator != null)
        {
            _campaignManager.Initialize(_gameState, _levelGenerator);
            StartCampaign();
        }
    }

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

    private void RegenerateBiome(string biomeId)
    {
        if (_levelGenerator == null) return;
        _levelGenerator.GenerateLevel(_levelGenerator.CurrentSeed, biomeId);
        _levelRadius = BiomeDefinition.Get(biomeId).LevelRadius;
        MissionGenerator.PopulateMission(_gameState, _levelGenerator);
        if (_flyMode && _flyCamera != null)
            _flyCamera.Teleport(_levelGenerator.SpawnPoint, Vector3.Zero);
        else
            SnapBridgeCameraToShip();
        _hud?.UpdateLevelGenInfo(_levelGenerator);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_gameState.MissionStarted && !_gameState.IsPaused)
        {
            _missionController.Update(dt);
        }

        UpdateBridgeCamera(dt);

        _stateUpdateTimer += dt;
        if (_stateUpdateTimer >= StateUpdateInterval)
        {
            _stateUpdateTimer = 0;
            BroadcastStateUpdates();
        }

        _hud?.UpdateDisplay(_gameState);

        if (Input.IsActionJustPressed("ui_cancel"))
            TogglePause();
    }

    private void BroadcastStateUpdates()
    {
        foreach (var kvp in _server.Clients)
        {
            var client = kvp.Value;
            if (!client.IsConnected || client.Role == null) continue;

            var roleState = _gameState.GetStateForRole(client.Role.Value);
            var message = JsonSerializer.Serialize(new
            {
                type = "state_update",
                role = client.Role.Value.ToString(),
                elapsed_time = _gameState.Mission.ElapsedTime,
                mission_phase = _gameState.Mission.Phase.ToString(),
                mission_started = _gameState.MissionStarted,
                is_paused = _gameState.IsPaused,
                data = roleState
            });
            _server.SendToClient(kvp.Key, message);
        }
    }

    #region Bridge Camera

    private Vector3 MapToWorld(float mapX, float mapY)
    {
        float x = (mapX - 500f) * _levelRadius / 500f;
        float z = (mapY - 500f) * _levelRadius / 500f;
        return new Vector3(x, 2f, z);
    }

    private void SnapBridgeCameraToShip()
    {
        if (_bridgeCamera == null) return;
        var pos = MapToWorld(_gameState.Ship.PositionX, _gameState.Ship.PositionY);
        _bridgeCamera.GlobalPosition = pos;

        var unreached = _gameState.Route.Waypoints.FindAll(w => !w.IsReached);
        if (unreached.Count > 0)
        {
            var wpWorld = MapToWorld(unreached[0].X, unreached[0].Y);
            if (pos.DistanceTo(wpWorld) > 1f)
                _bridgeCamera.LookAt(wpWorld, Vector3.Up);
        }

        if (_spaceBackground != null)
            _spaceBackground.GlobalPosition = pos;
    }

    private void UpdateBridgeCamera(float delta)
    {
        if (_bridgeCamera == null || _flyMode) return;

        var targetPos = MapToWorld(_gameState.Ship.PositionX, _gameState.Ship.PositionY);
        float smoothPos = 1f - MathF.Exp(-8f * delta);
        _bridgeCamera.GlobalPosition = _bridgeCamera.GlobalPosition.Lerp(targetPos, smoothPos);

        var unreached = _gameState.Route.Waypoints.FindAll(w => !w.IsReached);
        if (unreached.Count > 0)
        {
            var wpWorld = MapToWorld(unreached[0].X, unreached[0].Y);
            if (_bridgeCamera.GlobalPosition.DistanceTo(wpWorld) > 5f)
            {
                var target = _bridgeCamera.GlobalTransform.LookingAt(wpWorld, Vector3.Up);
                float smoothRot = 1f - MathF.Exp(-3f * delta);
                _bridgeCamera.GlobalTransform = _bridgeCamera.GlobalTransform.InterpolateWith(
                    target, smoothRot);
            }
        }

        var activeCam = GetViewport().GetCamera3D();
        if (_spaceBackground != null && activeCam != null)
            _spaceBackground.GlobalPosition = activeCam.GlobalPosition;
    }

    #endregion

    #region Event Handlers

    private void OnClientConnected(string clientId)
    {
        GD.Print($"[GameManager] Client verbunden: {clientId}");
        var availableRoles = _server.GetAvailableRoles();
        var msg = JsonSerializer.Serialize(new
        {
            type = "welcome",
            client_id = clientId,
            available_roles = availableRoles.ConvertAll(r => r.ToString()),
            mission_started = _gameState.MissionStarted,
        });
        _server.SendToClient(clientId, msg);
    }

    private void OnClientDisconnected(string clientId)
    {
        GD.Print($"[GameManager] Client getrennt: {clientId}");
        BroadcastRoleStatus();
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
            var errorMsg = JsonSerializer.Serialize(new
            {
                type = "error",
                message = $"Rolle {role} ist bereits vergeben."
            });
            _server.SendToClient(clientId, errorMsg);
            return;
        }

        _server.AssignRole(clientId, role);
        BroadcastRoleStatus();

        // In campaign mode: send sector map, don't auto-start mission
        if (_gameState.CampaignActive)
        {
            BroadcastSectorMap();
            return;
        }

        // Legacy single-mission: auto-start when all roles filled (or debug)
        if (_debugMode || AllRolesFilled())
        {
            if (!_gameState.MissionStarted)
            {
                StartMission();
            }
        }
    }

    private bool AllRolesFilled()
    {
        return _server.IsRoleTaken(StationRole.Captain) &&
               _server.IsRoleTaken(StationRole.Navigator) &&
               _server.IsRoleTaken(StationRole.Engineer) &&
               _server.IsRoleTaken(StationRole.Tactical);
    }

    private void BroadcastRoleStatus()
    {
        var roles = new Dictionary<string, bool>
        {
            ["Captain"] = _server.IsRoleTaken(StationRole.Captain),
            ["Navigator"] = _server.IsRoleTaken(StationRole.Navigator),
            ["Engineer"] = _server.IsRoleTaken(StationRole.Engineer),
            ["Tactical"] = _server.IsRoleTaken(StationRole.Tactical),
        };
        var msg = JsonSerializer.Serialize(new
        {
            type = "role_status",
            roles,
            available = _server.GetAvailableRoles().ConvertAll(r => r.ToString()),
        });
        _server.BroadcastToAll(msg);
    }

    private void OnCommandReceived(string clientId, string messageJson)
    {
        _commandProcessor.ProcessCommand(clientId, messageJson);
    }

    private void OnNodeSelected(string nodeId)
    {
        SelectCampaignNode(nodeId);
    }

    private void OnStateChanged()
    {
        // Immediate state update on important changes
        BroadcastStateUpdates();
    }

    private void OnPhaseChanged(string phase)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "phase_changed",
            phase,
            elapsed_time = _gameState.Mission.ElapsedTime,
        });
        _server.BroadcastToAll(msg);
    }

    private void OnEventTriggered(string eventId)
    {
        var evt = _gameState.ActiveEvents.Find(e => e.Id == eventId);
        if (evt == null) return;

        var msg = JsonSerializer.Serialize(new
        {
            type = "event",
            event_id = eventId,
            title = evt.Title,
            description = evt.Description,
        });
        _server.BroadcastToAll(msg);
    }

    private void OnMissionEnded(string result)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "mission_ended",
            result,
            elapsed_time = _gameState.Mission.ElapsedTime,
            primary_objective = _gameState.Mission.PrimaryObjective.ToString(),
            secondary_objective = _gameState.Mission.SecondaryObjective.ToString(),
            hull_integrity = _gameState.Ship.HullIntegrity,
        });
        _server.BroadcastToAll(msg);

        if (_gameState.CampaignActive)
        {
            _campaignManager.OnMissionCompleted(result);
            _gameState.ShowSectorMapOnMainScreen = true;
            _hud?.UpdateCampaignInfo(_campaignManager.Campaign);
            _hud?.ShowSectorMapAfterMission();
        }
    }

    private void OnCampaignNodeCompleted(string nodeId, string result)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "campaign_node_completed",
            node_id = nodeId,
            result,
            ship = new
            {
                hull = _campaignManager.Campaign.Ship.HullIntegrity,
                fuel = _campaignManager.Campaign.Ship.Fuel,
                scrap = _campaignManager.Campaign.Ship.Scrap,
                morale = _campaignManager.Campaign.Ship.CrewMorale,
            },
        });
        _server.BroadcastToAll(msg);
        BroadcastSectorMap();
    }

    private void OnCampaignSectorCompleted(int sectorIndex)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "campaign_sector_completed",
            sector_index = sectorIndex,
        });
        _server.BroadcastToAll(msg);
        BroadcastSectorMap();
    }

    private void OnCampaignEnded(string result)
    {
        _gameState.CampaignActive = false;
        var msg = JsonSerializer.Serialize(new
        {
            type = "campaign_ended",
            result,
            nodes_completed = _campaignManager.Campaign.NodesCompleted,
        });
        _server.BroadcastToAll(msg);
    }

    private void OnCampaignMapUpdated()
    {
        BroadcastSectorMap();
    }

    private void BroadcastSectorMap()
    {
        var mapData = _campaignManager.GetSectorMapForClient();
        var msg = JsonSerializer.Serialize(new
        {
            type = "sector_map_update",
            data = mapData,
        });
        _server.BroadcastToAll(msg);
    }

    #endregion

    #region Game Control

    public void StartMission()
    {
        if (_gameState.MissionStarted) return;
        _missionController.StartMission();
        var msg = JsonSerializer.Serialize(new
        {
            type = "mission_started",
            briefing = _gameState.Mission.BriefingText,
        });
        _server.BroadcastToAll(msg);
        GD.Print("[GameManager] Mission gestartet!");
    }

    public void TogglePause()
    {
        _gameState.IsPaused = !_gameState.IsPaused;
        var msg = JsonSerializer.Serialize(new
        {
            type = _gameState.IsPaused ? "paused" : "resumed",
        });
        _server.BroadcastToAll(msg);
        GD.Print($"[GameManager] {(_gameState.IsPaused ? "Pausiert" : "Fortgesetzt")}");
    }

    #endregion

    #region Debug

    private void OnDebugModeChanged(bool enabled)
    {
        _debugMode = enabled;
    }

    private void InitDebugCommands()
    {
        _debugCommands = new Dictionary<string, Action>
        {
            ["start_mission"] = () => StartMission(),
            ["reset_mission"] = () =>
            {
                _missionController.ResetMission();
                if (_levelGenerator != null)
                    MissionGenerator.PopulateMission(_gameState, _levelGenerator);
                GD.Print("[Debug] Mission zurückgesetzt");
            },
            ["toggle_pause"] = TogglePause,

            ["trigger_sensor_shimmer"] = () => _missionController.TriggerEventManually("sensor_shimmer"),
            ["trigger_shield_stress"] = () => _missionController.TriggerEventManually("shield_stress"),
            ["trigger_unknown_contact"] = () => _missionController.TriggerEventManually("unknown_approach"),
            ["trigger_recovery_window"] = () => _missionController.TriggerEventManually("recovery_window"),

            ["phase_anflug"] = () => _missionController.SetPhaseManually(MissionPhase.Anflug),
            ["phase_stoerung"] = () => _missionController.SetPhaseManually(MissionPhase.Stoerung),
            ["phase_krisenfenster"] = () => _missionController.SetPhaseManually(MissionPhase.Krisenfenster),
            ["phase_abschluss"] = () => _missionController.SetPhaseManually(MissionPhase.Abschluss),

            ["damage_hull"] = () => _gameState.Ship.HullIntegrity = Math.Max(0, _gameState.Ship.HullIntegrity - 20),
            ["repair_all"] = () =>
            {
                foreach (var sys in _gameState.Ship.Systems.Values)
                {
                    sys.Status = SystemStatus.Operational;
                    sys.Heat = 0;
                    sys.IsRepairing = false;
                }
                _gameState.Ship.HullIntegrity = 100;
            },

            ["toggle_fly_camera"] = ToggleFlyCamera,
            ["regen_level"] = () =>
            {
                if (_levelGenerator == null) return;
                _levelGenerator.RegenerateWithNewSeed();
                _levelRadius = BiomeDefinition.Get(_levelGenerator.CurrentBiomeId).LevelRadius;
                MissionGenerator.PopulateMission(_gameState, _levelGenerator);
                if (_flyMode && _flyCamera != null)
                    _flyCamera.Teleport(_levelGenerator.SpawnPoint, Vector3.Zero);
                else
                    SnapBridgeCameraToShip();
                _hud?.UpdateLevelGenInfo(_levelGenerator);
            },

            ["biome_asteroid"] = () => RegenerateBiome("asteroid_field"),
            ["biome_wreck"] = () => RegenerateBiome("wreck_zone"),
            ["biome_station"] = () => RegenerateBiome("station_periphery"),

            ["campaign_start"] = () => StartCampaign(),
            ["campaign_select_node"] = AutoSelectNextNode,
        };
    }

    private void OnDebugCommand(string command)
    {
        _debugCommands ??= new();
        if (_debugCommands.TryGetValue(command, out var action))
            action();
    }

    #endregion

    #region Campaign

    public void StartCampaign(int? seed = null)
    {
        _gameState.CampaignActive = true;
        _gameState.ShowSectorMapOnMainScreen = true;
        _campaignManager.StartNewCampaign(seed);
        BroadcastSectorMap();
        _hud?.UpdateCampaignInfo(_campaignManager.Campaign);
        GD.Print("[GameManager] Kampagne gestartet – Sektorkarte angezeigt");
    }

    public void SelectCampaignNode(string nodeId)
    {
        if (!_gameState.CampaignActive) return;
        if (_campaignManager.IsInMission) return;

        if (_campaignManager.SelectNode(nodeId))
        {
            _gameState.ShowSectorMapOnMainScreen = false;
            _levelRadius = BiomeDefinition.Get(
                _levelGenerator!.CurrentBiomeId).LevelRadius;
            SnapBridgeCameraToShip();
            _hud?.UpdateLevelGenInfo(_levelGenerator);
            _hud?.UpdateCampaignInfo(_campaignManager.Campaign);

            _missionController.ApplyEncounterConfig(_campaignManager.ActiveEncounter);
            StartMission();
        }
    }

    private void AutoSelectNextNode()
    {
        var available = _campaignManager.GetAvailableNodes();
        if (available.Count > 0)
            SelectCampaignNode(available[0].Id);
    }

    #endregion

    public override void _ExitTree()
    {
        _server?.StopServer();
    }
}
