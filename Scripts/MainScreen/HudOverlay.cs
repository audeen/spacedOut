using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Campaign;
using SpacedOut.LevelGen;
using SpacedOut.Shared;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

public partial class HudOverlay : Control
{
    [Signal] public delegate void DebugModeChangedEventHandler(bool enabled);
    [Signal] public delegate void DebugCommandEventHandler(string command);

    private GameState? _state;
    private bool _debugPanelVisible;
    private MissionPhase _lastPhase = MissionPhase.Briefing;
    private float _phaseBannerTimer;
    private string _phaseBannerText = "";

    private Label _phaseLabel = null!;
    private Label _timerLabel = null!;
    private Label _hullBar = null!;
    private Label _flightModeLabel = null!;
    private Label _speedLabel = null!;
    private VBoxContainer _overlayContainer = null!;
    private VBoxContainer _eventContainer = null!;
    private VBoxContainer _contactsContainer = null!;
    private Label _statusLine = null!;
    private PanelContainer _debugPanel = null!;
    private VBoxContainer _debugButtons = null!;
    private Label _missionEndLabel = null!;
    private Label _phaseBannerLabel = null!;

    private StarMapDisplay _starMapDisplay = null!;
    private TacticalDisplay _tacticalDisplay = null!;
    private SectorMapDisplay _sectorMapDisplay = null!;

    private PanelContainer? _campaignInfoPanel;
    private Label? _campaignFuelLabel;
    private Label? _campaignScrapLabel;
    private Label? _campaignSectorLabel;
    private Label? _sectorMapPromptLabel;

    private ColorRect _energyDriveBar = null!;
    private ColorRect _energyShieldsBar = null!;
    private ColorRect _energySensorsBar = null!;
    private Label _energyDriveLabel = null!;
    private Label _energyShieldsLabel = null!;
    private Label _energySensorsLabel = null!;

    private Label? _levelGenInfoLabel;

    public void Initialize(GameState state)
    {
        _state = state;
        BuildHud();
    }

    private void BuildHud()
    {
        AnchorsPreset = (int)LayoutPreset.FullRect;

        var topBar = UI.CreatePanel(TC.PanelBg);
        topBar.Position = new Vector2(20, 10);
        topBar.Size = new Vector2(800, 50);
        AddChild(topBar);

        var topHbox = new HBoxContainer { Position = new Vector2(15, 8) };
        topBar.AddChild(topHbox);

        _phaseLabel = UI.CreateLabel("PHASE: ---", 20, TC.Cyan);
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
        AddChild(hullPanel);

        _hullBar = UI.CreateLabel("HULL: 100%", 22, TC.Green);
        _hullBar.Position = new Vector2(15, 10);
        hullPanel.AddChild(_hullBar);

        // Energy minibar (below top bar)
        BuildEnergyMinibar();

        var overlayPanel = UI.CreatePanel(TC.PanelBg);
        overlayPanel.Position = new Vector2(20, 100);
        overlayPanel.Size = new Vector2(380, 300);
        overlayPanel.Visible = false;
        overlayPanel.Name = "OverlayPanel";
        AddChild(overlayPanel);

        var overlayTitle = UI.CreateLabel("CREW OVERLAYS", 14, TC.DimWhite);
        overlayTitle.Position = new Vector2(10, 5);
        overlayPanel.AddChild(overlayTitle);

        _overlayContainer = new VBoxContainer { Position = new Vector2(10, 30) };
        _overlayContainer.AddThemeConstantOverride("separation", 4);
        overlayPanel.AddChild(_overlayContainer);

        var eventPanel = UI.CreatePanel(TC.PanelBg);
        eventPanel.AnchorLeft = 1; eventPanel.AnchorRight = 1;
        eventPanel.Position = new Vector2(-320, 100);
        eventPanel.Size = new Vector2(300, 250);
        eventPanel.Visible = false;
        eventPanel.Name = "EventPanel";
        AddChild(eventPanel);

        var eventTitle = UI.CreateLabel("AKTIVE EREIGNISSE", 14, TC.DimWhite);
        eventTitle.Position = new Vector2(10, 5);
        eventPanel.AddChild(eventTitle);

        _eventContainer = new VBoxContainer { Position = new Vector2(10, 30) };
        _eventContainer.AddThemeConstantOverride("separation", 4);
        eventPanel.AddChild(_eventContainer);

        var contactsPanel = UI.CreatePanel(TC.PanelBg);
        contactsPanel.AnchorLeft = 1; contactsPanel.AnchorRight = 1;
        contactsPanel.AnchorTop = 1; contactsPanel.AnchorBottom = 1;
        contactsPanel.Position = new Vector2(-320, -180);
        contactsPanel.Size = new Vector2(300, 160);
        contactsPanel.Visible = false;
        contactsPanel.Name = "ContactsPanel";
        AddChild(contactsPanel);

        var contactsTitle = UI.CreateLabel("KONTAKTE", 14, TC.DimWhite);
        contactsTitle.Position = new Vector2(10, 5);
        contactsPanel.AddChild(contactsTitle);

        _contactsContainer = new VBoxContainer { Position = new Vector2(10, 30) };
        _contactsContainer.AddThemeConstantOverride("separation", 2);
        contactsPanel.AddChild(_contactsContainer);

        var statusPanel = UI.CreatePanel(TC.PanelBg);
        statusPanel.AnchorTop = 1; statusPanel.AnchorBottom = 1;
        statusPanel.Position = new Vector2(20, -50);
        statusPanel.Size = new Vector2(500, 35);
        AddChild(statusPanel);

        _statusLine = UI.CreateLabel("Warte auf Crew-Verbindungen...", 14, TC.DimWhite);
        _statusLine.Position = new Vector2(10, 6);
        statusPanel.AddChild(_statusLine);

        _phaseBannerLabel = UI.CreateLabel("", 40, TC.Cyan);
        _phaseBannerLabel.AnchorLeft = 0.5f; _phaseBannerLabel.AnchorRight = 0.5f;
        _phaseBannerLabel.AnchorTop = 0.4f; _phaseBannerLabel.AnchorBottom = 0.4f;
        _phaseBannerLabel.Position = new Vector2(-250, -30);
        _phaseBannerLabel.Size = new Vector2(500, 60);
        _phaseBannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _phaseBannerLabel.Visible = false;
        AddChild(_phaseBannerLabel);

        _missionEndLabel = UI.CreateLabel("", 32, TC.White);
        _missionEndLabel.AnchorLeft = 0.5f; _missionEndLabel.AnchorRight = 0.5f;
        _missionEndLabel.AnchorTop = 0.5f; _missionEndLabel.AnchorBottom = 0.5f;
        _missionEndLabel.Position = new Vector2(-300, -40);
        _missionEndLabel.Size = new Vector2(600, 80);
        _missionEndLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _missionEndLabel.Visible = false;
        AddChild(_missionEndLabel);

        BuildStationDisplays();
        BuildCampaignUi();
        BuildDebugPanel();
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

        _sectorMapDisplay = new SectorMapDisplay
        {
            Name = "SectorMapDisplay",
            AnchorLeft = 0.5f, AnchorTop = 0.5f,
            AnchorRight = 0.5f, AnchorBottom = 0.5f,
            Position = new Vector2(-450, -220),
            Size = new Vector2(900, 440),
            Visible = false,
        };
        AddChild(_sectorMapDisplay);
    }

    private void BuildCampaignUi()
    {
        _campaignInfoPanel = UI.CreatePanel(TC.PanelBg);
        _campaignInfoPanel.Position = new Vector2(20, 100);
        _campaignInfoPanel.Size = new Vector2(260, 70);
        _campaignInfoPanel.Visible = false;
        _campaignInfoPanel.Name = "CampaignInfoPanel";
        AddChild(_campaignInfoPanel);

        var vbox = new VBoxContainer { Position = new Vector2(10, 8) };
        vbox.AddThemeConstantOverride("separation", 2);
        _campaignInfoPanel.AddChild(vbox);

        _campaignSectorLabel = UI.CreateLabel("Sektor: ---", 12, TC.Cyan);
        vbox.AddChild(_campaignSectorLabel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(hbox);

        _campaignFuelLabel = UI.CreateLabel("Fuel: --", 13, TC.Orange);
        hbox.AddChild(_campaignFuelLabel);

        _campaignScrapLabel = UI.CreateLabel("Scrap: --", 13, TC.Yellow);
        hbox.AddChild(_campaignScrapLabel);

        _sectorMapPromptLabel = UI.CreateLabel(
            "SEKTORKARTE — Knoten auf Client-Gerät auswählen", 16, TC.Cyan);
        _sectorMapPromptLabel.AnchorLeft = 0.5f; _sectorMapPromptLabel.AnchorRight = 0.5f;
        _sectorMapPromptLabel.AnchorTop = 0.15f; _sectorMapPromptLabel.AnchorBottom = 0.15f;
        _sectorMapPromptLabel.Position = new Vector2(-300, -15);
        _sectorMapPromptLabel.Size = new Vector2(600, 30);
        _sectorMapPromptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _sectorMapPromptLabel.Visible = false;
        AddChild(_sectorMapPromptLabel);
    }

    private void BuildEnergyMinibar()
    {
        var energyPanel = UI.CreatePanel(TC.PanelBg);
        energyPanel.Position = new Vector2(20, 65);
        energyPanel.Size = new Vector2(350, 30);
        AddChild(energyPanel);

        float barY = 6, barH = 12, labelY = 7;
        float x = 10;

        _energyDriveLabel = UI.CreateLabel("⚡34", 10, TC.DimWhite);
        _energyDriveLabel.Position = new Vector2(x, labelY);
        energyPanel.AddChild(_energyDriveLabel);

        _energyDriveBar = new ColorRect { Position = new Vector2(x + 30, barY), Size = new Vector2(68, barH), Color = TC.Cyan };
        energyPanel.AddChild(_energyDriveBar);

        x = 120;
        _energyShieldsLabel = UI.CreateLabel("🛡33", 10, TC.DimWhite);
        _energyShieldsLabel.Position = new Vector2(x, labelY);
        energyPanel.AddChild(_energyShieldsLabel);

        _energyShieldsBar = new ColorRect { Position = new Vector2(x + 30, barY), Size = new Vector2(66, barH), Color = TC.Blue };
        energyPanel.AddChild(_energyShieldsBar);

        x = 230;
        _energySensorsLabel = UI.CreateLabel("📡33", 10, TC.DimWhite);
        _energySensorsLabel.Position = new Vector2(x, labelY);
        energyPanel.AddChild(_energySensorsLabel);

        _energySensorsBar = new ColorRect { Position = new Vector2(x + 30, barY), Size = new Vector2(66, barH), Color = TC.Green };
        energyPanel.AddChild(_energySensorsBar);
    }

    private void BuildDebugPanel()
    {
        _debugPanel = UI.CreateDebugPanel(
            new Color(0.08f, 0.08f, 0.15f, 0.92f), TC.Cyan);
        _debugPanel.AnchorTop = 0; _debugPanel.AnchorBottom = 1;
        _debugPanel.AnchorLeft = 1; _debugPanel.AnchorRight = 1;
        _debugPanel.Position = new Vector2(-260, 10);
        _debugPanel.Size = new Vector2(250, 0);
        _debugPanel.Visible = false;
        AddChild(_debugPanel);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(234, 0) };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _debugPanel.AddChild(scroll);

        _debugButtons = new VBoxContainer();
        _debugButtons.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_debugButtons);

        var title = UI.CreateLabel("DEBUG", 16, TC.Cyan);
        _debugButtons.AddChild(title);

        AddDebugButton("Mission starten", "start_mission");
        AddDebugButton("Mission zurücksetzen", "reset_mission");
        AddDebugButton("Pause toggle", "toggle_pause");
        AddDebugSeparator();
        AddDebugButton("Event: Sensorstörung", "trigger_sensor_shimmer");
        AddDebugButton("Event: Schildstress", "trigger_shield_stress");
        AddDebugButton("Event: Unbekannter Kontakt", "trigger_unknown_contact");
        AddDebugButton("Event: Bergungsfenster", "trigger_recovery_window");
        AddDebugSeparator();
        AddDebugButton("Phase: Anflug", "phase_anflug");
        AddDebugButton("Phase: Störung", "phase_stoerung");
        AddDebugButton("Phase: Krisenfenster", "phase_krisenfenster");
        AddDebugButton("Phase: Abschluss", "phase_abschluss");
        AddDebugSeparator();
        AddDebugButton("Hull -20", "damage_hull");
        AddDebugButton("Alles reparieren", "repair_all");

        AddDebugSeparator();
        var lgTitle = UI.CreateLabel("LEVEL GENERATOR", 14, TC.Cyan);
        _debugButtons.AddChild(lgTitle);

        AddDebugButton("Fly-Kamera toggle", "toggle_fly_camera");
        AddDebugButton("Level regenerieren", "regen_level");
        AddDebugSeparator();
        AddDebugButton("Biom: Asteroidenfeld", "biome_asteroid");
        AddDebugButton("Biom: Wrackzone", "biome_wreck");
        AddDebugButton("Biom: Station", "biome_station");

        AddDebugSeparator();
        var campTitle = UI.CreateLabel("KAMPAGNE", 14, TC.Cyan);
        _debugButtons.AddChild(campTitle);

        AddDebugButton("Kampagne starten", "campaign_start");
        AddDebugButton("Nächsten Knoten wählen", "campaign_select_node");

        _levelGenInfoLabel = UI.CreateLabel("Level: nicht generiert", 11, TC.DimWhite);
        _levelGenInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _levelGenInfoLabel.CustomMinimumSize = new Vector2(220, 0);
        _debugButtons.AddChild(_levelGenInfoLabel);
    }

    private void AddDebugButton(string text, string command)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(220, 30),
        };
        btn.Pressed += () => EmitSignal(SignalName.DebugCommand, command);
        _debugButtons.AddChild(btn);
    }

    private void AddDebugSeparator()
    {
        _debugButtons.AddChild(new HSeparator());
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F12)
        {
            _debugPanelVisible = !_debugPanelVisible;
            _debugPanel.Visible = _debugPanelVisible;
            EmitSignal(SignalName.DebugModeChanged, _debugPanelVisible);
        }
    }

    public void UpdateDisplay(GameState state)
    {
        if (state == null) return;
        _state = state;

        _phaseLabel.Text = $"PHASE: {TranslatePhase(state.Mission.Phase)}";
        int mins = (int)(state.Mission.ElapsedTime / 60);
        int secs = (int)(state.Mission.ElapsedTime % 60);
        _timerLabel.Text = $"{mins:D2}:{secs:D2}";

        // Phase banner
        if (state.Mission.Phase != _lastPhase && state.Mission.Phase != MissionPhase.Briefing)
        {
            _lastPhase = state.Mission.Phase;
            ShowPhaseBanner(TranslatePhase(state.Mission.Phase));
        }
        UpdatePhaseBanner((float)GetProcessDeltaTime());

        _flightModeLabel.Text = state.Ship.FlightMode.ToString().ToUpper();
        _flightModeLabel.AddThemeColorOverride("font_color",
            state.Ship.FlightMode == FlightMode.Evasive ? TC.Yellow : TC.Green);

        _speedLabel.Text = $"SPD: {state.Ship.SpeedLevel}";

        float hull = state.Ship.HullIntegrity;
        _hullBar.Text = $"HULL: {hull:F0}%";
        _hullBar.AddThemeColorOverride("font_color",
            hull > 60 ? TC.Green : hull > 30 ? TC.Yellow : TC.Red);

        UpdateEnergyMinibar(state);
        UpdateOverlays(state);
        UpdateEvents(state);
        UpdateContacts(state);
        UpdateStatusLine(state);
        UpdateMissionEnd(state);
        UpdateStationDisplays(state);
    }

    private void ShowPhaseBanner(string text)
    {
        _phaseBannerText = text;
        _phaseBannerTimer = 2.5f;
        _phaseBannerLabel.Text = text;
        _phaseBannerLabel.Visible = true;
        _phaseBannerLabel.Modulate = new Color(1, 1, 1, 0);
    }

    private void UpdatePhaseBanner(float delta)
    {
        if (_phaseBannerTimer <= 0) return;

        _phaseBannerTimer -= delta;

        float alpha;
        if (_phaseBannerTimer > 2.0f)
            alpha = 1f - (_phaseBannerTimer - 2.0f) / 0.5f;
        else if (_phaseBannerTimer > 0.5f)
            alpha = 1f;
        else
            alpha = _phaseBannerTimer / 0.5f;

        alpha = Math.Clamp(alpha, 0, 1);
        _phaseBannerLabel.Modulate = new Color(1, 1, 1, alpha);

        if (_phaseBannerTimer <= 0)
            _phaseBannerLabel.Visible = false;
    }

    private void UpdateEnergyMinibar(GameState state)
    {
        float maxBarWidth = 68f;
        float drivePct = state.Ship.Energy.Drive / (float)EnergyDistribution.TotalBudget;
        float shieldsPct = state.Ship.Energy.Shields / (float)EnergyDistribution.TotalBudget;
        float sensorsPct = state.Ship.Energy.Sensors / (float)EnergyDistribution.TotalBudget;

        _energyDriveBar.Size = new Vector2(maxBarWidth * drivePct, 12);
        _energyShieldsBar.Size = new Vector2(maxBarWidth * shieldsPct, 12);
        _energySensorsBar.Size = new Vector2(maxBarWidth * sensorsPct, 12);

        _energyDriveLabel.Text = $"⚡{state.Ship.Energy.Drive}";
        _energyShieldsLabel.Text = $"🛡{state.Ship.Energy.Shields}";
        _energySensorsLabel.Text = $"📡{state.Ship.Energy.Sensors}";

        _energyDriveBar.Color = state.Ship.Energy.Drive < 15 ? TC.Red :
                                 state.Ship.Energy.Drive < 25 ? TC.Yellow : TC.Cyan;
        _energyShieldsBar.Color = state.Ship.Energy.Shields < 15 ? TC.Red :
                                   state.Ship.Energy.Shields < 25 ? TC.Yellow : TC.Blue;
        _energySensorsBar.Color = state.Ship.Energy.Sensors < 15 ? TC.Red :
                                   state.Ship.Energy.Sensors < 25 ? TC.Yellow : TC.Green;
    }

    private void UpdateOverlays(GameState state)
    {
        var panel = GetNode<PanelContainer>("OverlayPanel");
        var approved = state.Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed).Take(3).ToList();

        panel.Visible = approved.Count > 0;

        foreach (var child in _overlayContainer.GetChildren())
            child.QueueFree();

        foreach (var overlay in approved)
        {
            var color = overlay.Category switch
            {
                OverlayCategory.Warning => TC.Red,
                OverlayCategory.Tactical => TC.Yellow,
                OverlayCategory.Marker => TC.Blue,
                _ => TC.White
            };
            var lbl = UI.CreateLabel($"[{overlay.SourceStation}] {overlay.Text}", 14, color);
            lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lbl.CustomMinimumSize = new Vector2(350, 0);
            _overlayContainer.AddChild(lbl);
        }
    }

    private void UpdateEvents(GameState state)
    {
        var panel = GetNode<PanelContainer>("EventPanel");
        var active = state.ActiveEvents.FindAll(e => e.IsActive);

        panel.Visible = active.Count > 0;

        foreach (var child in _eventContainer.GetChildren())
            child.QueueFree();

        foreach (var evt in active)
        {
            var titleLbl = UI.CreateLabel($"⚠ {evt.Title}", 15, TC.Yellow);
            _eventContainer.AddChild(titleLbl);

            if (evt.TimeRemaining > 0)
            {
                var timerLbl = UI.CreateLabel($"  {evt.TimeRemaining:F0}s", 13, TC.Orange);
                _eventContainer.AddChild(timerLbl);
            }

            var desc = UI.CreateLabel(evt.Description, 12, TC.DimWhite);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            desc.CustomMinimumSize = new Vector2(270, 0);
            _eventContainer.AddChild(desc);
        }
    }

    private void UpdateContacts(GameState state)
    {
        var panel = GetNode<PanelContainer>("ContactsPanel");
        var visible = state.Contacts.FindAll(c => c.IsVisibleOnMainScreen);

        panel.Visible = visible.Count > 0;

        foreach (var child in _contactsContainer.GetChildren())
            child.QueueFree();

        foreach (var contact in visible)
        {
            var color = ThemeColors.GetContactColor(contact.Type);
            string scanStr = contact.ScanProgress < 100 ? $" [{contact.ScanProgress:F0}%]" : "";
            var lbl = UI.CreateLabel($"● {contact.DisplayName}{scanStr}", 13, color);
            _contactsContainer.AddChild(lbl);
        }
    }

    private void UpdateStatusLine(GameState state)
    {
        if (!state.MissionStarted && state.CampaignActive && state.ShowSectorMapOnMainScreen)
        {
            _statusLine.Text = "Wähle nächsten Knoten auf dem Navigator-Client · F12 Debug";
            _statusLine.AddThemeColorOverride("font_color", TC.Cyan);
            return;
        }

        if (!state.MissionStarted)
        {
            _statusLine.Text = "Warte auf Mission-Start... (F12 für Debug-Panel)";
            _statusLine.AddThemeColorOverride("font_color", TC.DimWhite);
            return;
        }

        if (state.IsPaused)
        {
            _statusLine.Text = "⏸ PAUSIERT";
            _statusLine.AddThemeColorOverride("font_color", TC.Yellow);
            return;
        }

        _statusLine.AddThemeColorOverride("font_color", TC.DimWhite);

        var repairing = state.Ship.Systems.Values.FirstOrDefault(s => s.IsRepairing);
        if (repairing != null)
        {
            _statusLine.Text = $"Reparatur: {repairing.Id} ({repairing.RepairProgress:F0}%)";
            return;
        }

        var scanning = state.Contacts.FirstOrDefault(c => c.IsScanning);
        if (scanning != null)
        {
            _statusLine.Text = $"Scan: {scanning.DisplayName} ({scanning.ScanProgress:F0}%)";
            return;
        }

        _statusLine.Text = $"Position: ({state.Ship.PositionX:F0}, {state.Ship.PositionY:F0})";
    }

    private void UpdateMissionEnd(GameState state)
    {
        if (state.Mission.Phase == MissionPhase.Ended && !_missionEndLabel.Visible)
        {
            _missionEndLabel.Visible = true;
            string primary = state.Mission.PrimaryObjective == ObjectiveStatus.Completed ? "ERREICHT" : "GESCHEITERT";
            string secondary = state.Mission.SecondaryObjective == ObjectiveStatus.Completed ? "ERREICHT" : "GESCHEITERT";

            if (state.CampaignActive)
            {
                _missionEndLabel.Text = $"MISSION BEENDET\nPrimärziel: {primary}\nSekundärziel: {secondary}\n\nSektorkarte wird geladen...";
            }
            else
            {
                _missionEndLabel.Text = $"MISSION BEENDET\nPrimärziel: {primary}\nSekundärziel: {secondary}";
            }
            _missionEndLabel.AddThemeColorOverride("font_color",
                state.Mission.PrimaryObjective == ObjectiveStatus.Completed ? TC.Green : TC.Red);
        }
        else if (state.Mission.Phase != MissionPhase.Ended)
        {
            _missionEndLabel.Visible = false;
        }
    }

    private void UpdateStationDisplays(GameState state)
    {
        bool sectorMapActive = state.ShowSectorMapOnMainScreen && state.CampaignActive;

        _starMapDisplay.Visible = state.ShowStarMapOnMainScreen && !sectorMapActive;
        if (_starMapDisplay.Visible)
            _starMapDisplay.UpdateState(state);

        _tacticalDisplay.Visible = state.ShowTacticalOnMainScreen && !sectorMapActive;
        if (_tacticalDisplay.Visible)
            _tacticalDisplay.UpdateState(state);

        _sectorMapDisplay.Visible = sectorMapActive;
        _sectorMapPromptLabel!.Visible = sectorMapActive;
    }

    private static string TranslatePhase(MissionPhase phase) => phase switch
    {
        MissionPhase.Briefing => "BRIEFING",
        MissionPhase.Anflug => "ANFLUG",
        MissionPhase.Stoerung => "STÖRUNG",
        MissionPhase.Krisenfenster => "KRISENFENSTER",
        MissionPhase.Abschluss => "ABSCHLUSS",
        MissionPhase.Ended => "BEENDET",
        _ => "---"
    };

    public void UpdateCampaignInfo(CampaignState? campaign)
    {
        if (campaign == null) return;
        if (_campaignInfoPanel != null)
            _campaignInfoPanel.Visible = campaign.IsActive;
        if (_campaignSectorLabel != null)
        {
            var sector = campaign.CurrentSector;
            _campaignSectorLabel.Text = sector != null
                ? $"Sektor {sector.SectorIndex + 1}/{campaign.Sectors.Count}: {sector.DisplayName}"
                : "---";
        }
        if (_campaignFuelLabel != null)
            _campaignFuelLabel.Text = $"Fuel: {campaign.Ship.Fuel}";
        if (_campaignScrapLabel != null)
            _campaignScrapLabel.Text = $"Scrap: {campaign.Ship.Scrap}";

        _sectorMapDisplay?.UpdateCampaign(campaign);
    }

    public void ShowSectorMapAfterMission()
    {
        _missionEndLabel.Visible = false;
    }

    public void UpdateLevelGenInfo(LevelGenerator? gen)
    {
        if (_levelGenInfoLabel == null || gen == null) return;
        var biome = BiomeDefinition.Get(gen.CurrentBiomeId);
        _levelGenInfoLabel.Text =
            $"Seed: {gen.CurrentSeed}\n" +
            $"Biom: {biome.DisplayName}\n" +
            $"Objekte: {gen.SpawnedObjects.Count}\n" +
            $"Valide: {(gen.IsValid ? "Ja" : "Nein")}";
    }

}
