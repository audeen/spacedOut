using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Shared;
using SpacedOut.State;
using SpacedOut.Tactical;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Overlays, events, contacts, status line, phase banner, and mission-end display.</summary>
public class MissionInfoPanel
{
    private VBoxContainer _overlayContainer = null!;
    private VBoxContainer _pinnedContainer = null!;
    private VBoxContainer _eventContainer = null!;
    private VBoxContainer _contactsContainer = null!;
    private Label _statusLine = null!;
    private Label _exitStatusLine = null!;
    private Label _harvestLine = null!;
    private Label _dockStatusLine = null!;
    private Label _phaseBannerLabel = null!;
    private Label _missionEndLabel = null!;

    private readonly Control _parent;
    private MissionPhase _lastPhase = MissionPhase.Briefing;
    private float _phaseBannerTimer;

    public MissionInfoPanel(Control parent)
    {
        _parent = parent;
    }

    public void Build()
    {
        var overlayPanel = UI.CreatePanel(TC.PanelBg);
        overlayPanel.Position = new Vector2(20, 100);
        overlayPanel.Size = new Vector2(380, 420);
        overlayPanel.Visible = false;
        overlayPanel.Name = "OverlayPanel";
        _parent.AddChild(overlayPanel);

        var overlayTitle = UI.CreateLabel("CREW OVERLAYS", 14, TC.DimWhite);
        overlayTitle.Position = new Vector2(10, 5);
        overlayPanel.AddChild(overlayTitle);

        var overlayColumn = new VBoxContainer
        {
            Position = new Vector2(10, 28),
            Size = new Vector2(360, 380),
        };
        overlayColumn.AddThemeConstantOverride("separation", 8);
        overlayPanel.AddChild(overlayColumn);

        _overlayContainer = new VBoxContainer();
        _overlayContainer.AddThemeConstantOverride("separation", 4);
        overlayColumn.AddChild(_overlayContainer);

        _pinnedContainer = new VBoxContainer();
        _pinnedContainer.AddThemeConstantOverride("separation", 6);
        overlayColumn.AddChild(_pinnedContainer);

        var eventPanel = UI.CreatePanel(TC.PanelBg);
        eventPanel.AnchorLeft = 1; eventPanel.AnchorRight = 1;
        eventPanel.Position = new Vector2(-320, 100);
        eventPanel.Size = new Vector2(300, 250);
        eventPanel.Visible = false;
        eventPanel.Name = "EventPanel";
        _parent.AddChild(eventPanel);

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
        _parent.AddChild(contactsPanel);

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
        _parent.AddChild(statusPanel);

        _statusLine = UI.CreateLabel("Warte auf Crew-Verbindungen...", 14, TC.DimWhite);
        _statusLine.Position = new Vector2(10, 6);
        statusPanel.AddChild(_statusLine);

        // M1b: Exit-Status + Sektor-Ertrag (replaces the earlier procedural-objective panel).
        var sectorPanel = UI.CreatePanel(TC.PanelBg);
        sectorPanel.AnchorTop = 1; sectorPanel.AnchorBottom = 1;
        sectorPanel.Position = new Vector2(20, -155);
        sectorPanel.Size = new Vector2(500, 100);
        sectorPanel.Visible = false;
        sectorPanel.Name = "SectorPanel";
        _parent.AddChild(sectorPanel);

        _exitStatusLine = UI.CreateLabel("", 14, TC.DimWhite);
        _exitStatusLine.Position = new Vector2(10, 6);
        sectorPanel.AddChild(_exitStatusLine);

        _harvestLine = UI.CreateLabel("", 13, TC.Green);
        _harvestLine.Position = new Vector2(10, 32);
        sectorPanel.AddChild(_harvestLine);

        _dockStatusLine = UI.CreateLabel("", 13, TC.Cyan);
        _dockStatusLine.Position = new Vector2(10, 58);
        _dockStatusLine.Visible = false;
        sectorPanel.AddChild(_dockStatusLine);

        _phaseBannerLabel = UI.CreateLabel("", 40, TC.Cyan);
        _phaseBannerLabel.AnchorLeft = 0.5f; _phaseBannerLabel.AnchorRight = 0.5f;
        _phaseBannerLabel.AnchorTop = 0.4f; _phaseBannerLabel.AnchorBottom = 0.4f;
        _phaseBannerLabel.Position = new Vector2(-250, -30);
        _phaseBannerLabel.Size = new Vector2(500, 60);
        _phaseBannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _phaseBannerLabel.Visible = false;
        _parent.AddChild(_phaseBannerLabel);

        _missionEndLabel = UI.CreateLabel("", 32, TC.White);
        _missionEndLabel.AnchorLeft = 0.5f; _missionEndLabel.AnchorRight = 0.5f;
        _missionEndLabel.AnchorTop = 0.5f; _missionEndLabel.AnchorBottom = 0.5f;
        _missionEndLabel.Position = new Vector2(-300, -40);
        _missionEndLabel.Size = new Vector2(600, 80);
        _missionEndLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _missionEndLabel.Visible = false;
        _parent.AddChild(_missionEndLabel);
    }

    public void Update(GameState state, float delta)
    {
        if (state.Mission.UseStructuredMissionPhases
            && state.Mission.Phase != _lastPhase
            && state.Mission.Phase != MissionPhase.Briefing)
        {
            _lastPhase = state.Mission.Phase;
            ShowPhaseBanner(HudOverlay.TranslatePhase(state.Mission.Phase));
        }
        UpdatePhaseBanner(delta);
        UpdateOverlays(state);
        UpdatePinnedContactCards(state);
        RefreshCrewOverlayPanelVisibility(state);
        UpdateEvents(state);
        UpdateContacts(state);
        UpdateStatusLine(state);
        UpdateSectorPanel(state);
        UpdateMissionEnd(state);
    }

    /// <summary>
    /// M1b Sektor-Panel: Zeigt Exit-Status (versteckt/aufgedeckt/in Reichweite) und
    /// den Ertrag aus POI-Rewards relativ zum Mission-Start.
    /// </summary>
    private void UpdateSectorPanel(GameState state)
    {
        var panel = _parent.GetNode<PanelContainer>("SectorPanel");
        var m = state.Mission;
        bool active = state.MissionStarted && m.Phase != MissionPhase.Ended;
        panel.Visible = active;
        if (!active) return;

        UpdateExitStatusLine(state);
        UpdateHarvestLine(state);
        UpdateDockStatusLine(state);
    }

    private void UpdateDockStatusLine(GameState state)
    {
        var m = state.Mission;
        if (m.Dock == null)
        {
            _dockStatusLine.Visible = false;
            return;
        }

        _dockStatusLine.Visible = true;
        var d = m.Dock;
        if (m.Docked)
        {
            _dockStatusLine.Text =
                $"Angedockt — Preise: F{d.FuelPrice} · P{d.PartsPrice} · D{d.DataPrice} · Rep 1P={d.HullPerPart}H";
            _dockStatusLine.AddThemeColorOverride("font_color", TC.Green);
        }
        else
        {
            float dist = m.DockDistance;
            int sl = state.Ship.SpeedLevel;
            string distStr = dist >= 0 ? $"{dist:F0} m" : "—";
            string hint;
            if (dist < 0)
                hint = "Dock in diesem Sektor — Andockmast anfliegen.";
            else if (dist > 60f)
                hint = $"Andockmast: {distStr} · Speed {sl} — näher heran (≤60 m) und Speed ≤2.";
            else if (sl > 2)
                hint = $"Andockmast: {distStr} · Speed {sl} — zu schnell, Geschwindigkeit auf ≤2 reduzieren.";
            else
                hint = $"Andockmast: {distStr} · Speed {sl} — Andocken läuft...";
            _dockStatusLine.Text = hint;
            _dockStatusLine.AddThemeColorOverride("font_color", TC.Cyan);
        }
    }

    private void UpdateExitStatusLine(GameState state)
    {
        var m = state.Mission;
        var exit = state.Contacts.Find(c => c.Id == "sector_exit");

        if (!m.JumpCoordinatesUnlocked || exit == null)
        {
            _exitStatusLine.Text = "Exit: versteckt — Sonden einsetzen, um den Sprungausgang aufzudecken.";
            _exitStatusLine.AddThemeColorOverride("font_color", TC.DimWhite);
            return;
        }

        float dx = exit.PositionX - state.Ship.PositionX;
        float dy = exit.PositionY - state.Ship.PositionY;
        float dz = exit.PositionZ - state.Ship.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist < 50f)
        {
            _exitStatusLine.Text = "Exit: in Reichweite — Sprung bereit.";
            _exitStatusLine.AddThemeColorOverride("font_color", TC.Green);
        }
        else
        {
            _exitStatusLine.Text = $"Exit: aufgedeckt ({dist:F0} m)";
            _exitStatusLine.AddThemeColorOverride("font_color", TC.Cyan);
        }
    }

    private static readonly (string Key, string Label)[] HarvestResourceKeys =
    {
        ("SpareParts",  "Parts"),
        ("ScienceData", "Data"),
        ("Fuel",        "Fuel"),
        ("Credits",     "Credits"),
    };

    private void UpdateHarvestLine(GameState state)
    {
        var run = state.ActiveRunState;
        var snap = state.Mission.MissionStartResourcesSnapshot;
        if (run == null)
        {
            _harvestLine.Text = "";
            _harvestLine.Visible = false;
            return;
        }

        var parts = new List<string>();
        foreach (var (key, label) in HarvestResourceKeys)
        {
            run.Resources.TryGetValue(key, out var now);
            snap.TryGetValue(key, out var before);
            int delta = now - before;
            if (delta > 0) parts.Add($"+{delta} {label}");
        }

        if (parts.Count == 0)
        {
            _harvestLine.Text = "Sektor-Ertrag: —";
            _harvestLine.AddThemeColorOverride("font_color", TC.DimWhite);
        }
        else
        {
            _harvestLine.Text = "Sektor-Ertrag: " + string.Join(" · ", parts);
            _harvestLine.AddThemeColorOverride("font_color", TC.Green);
        }
        _harvestLine.Visible = true;
    }

    private void ShowPhaseBanner(string text)
    {
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

    private void UpdateOverlays(GameState state)
    {
        foreach (var child in _overlayContainer.GetChildren())
            child.QueueFree();

        var approved = state.Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed).Take(3).ToList();
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

    private void RefreshCrewOverlayPanelVisibility(GameState state)
    {
        var panel = _parent.GetNode<PanelContainer>("OverlayPanel");
        int overlayCount = state.Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed).Count;
        panel.Visible = overlayCount > 0 || state.PinnedEntities.Count > 0;
    }

    private void UpdatePinnedContactCards(GameState state)
    {
        foreach (var child in _pinnedContainer.GetChildren())
            child.QueueFree();

        foreach (var pin in state.PinnedEntities)
        {
            var contact = state.Contacts.Find(c => c.Id == pin.EntityId);
            _pinnedContainer.AddChild(BuildPinCard(pin, contact, state));
        }
    }

    private static Control BuildPinCard(PinnedEntity pin, Contact? contact, GameState state)
    {
        var typeColor = contact != null
            ? ThemeColors.GetContactColor(contact.Type)
            : ThemeColors.ContactUnknown;

        var card = new PanelContainer { CustomMinimumSize = new Vector2(350, 58) };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.12f, 0.85f),
            BorderColor = new Color(TC.Cyan, 0.45f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var row = new HBoxContainer();
        card.AddChild(row);

        row.AddChild(new ColorRect
        {
            Color = typeColor,
            CustomMinimumSize = new Vector2(10, 10),
        });

        var textCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(textCol);

        string name = !string.IsNullOrEmpty(pin.Label)
            ? pin.Label
            : (contact != null ? ContactDisplayRules.GetDisplayNameForUi(contact) : pin.EntityId);
        textCol.AddChild(UI.CreateLabel(name, 12, new Color(0.75f, 0.78f, 0.85f, 0.9f)));
        textCol.AddChild(UI.CreateLabel(pin.Detail, 10, new Color(0.5f, 0.5f, 0.55f, 0.7f)));

        float dist = CalculatePinDistance(contact, state);
        if (dist >= 0)
        {
            var distLbl = UI.CreateLabel($"{dist:F0}", 10, TC.Cyan with { A = 0.8f });
            distLbl.HorizontalAlignment = HorizontalAlignment.Right;
            distLbl.CustomMinimumSize = new Vector2(40, 0);
            row.AddChild(distLbl);
        }

        return card;
    }

    private static float CalculatePinDistance(Contact? contact, GameState state)
    {
        if (contact == null) return -1;
        float dx = contact.PositionX - state.Ship.PositionX;
        float dy = contact.PositionY - state.Ship.PositionY;
        float dz = contact.PositionZ - state.Ship.PositionZ;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void UpdateEvents(GameState state)
    {
        var panel = _parent.GetNode<PanelContainer>("EventPanel");
        var active = state.ActiveEvents.FindAll(e => e.IsActive && e.ShowOnMainScreen);
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
        var panel = _parent.GetNode<PanelContainer>("ContactsPanel");
        var pinnedIds = new HashSet<string>(state.PinnedEntities.Select(p => p.EntityId));
        var visible = state.Contacts.FindAll(c =>
            c.IsVisibleOnMainScreen && !c.IsDestroyed && !pinnedIds.Contains(c.Id));
        panel.Visible = visible.Count > 0;

        foreach (var child in _contactsContainer.GetChildren())
            child.QueueFree();

        foreach (var contact in visible)
        {
            var color = ThemeColors.GetContactColor(contact.Type);
            string scanStr = contact.ScanProgress < 100 ? $" [{contact.ScanProgress:F0}%]" : "";
            var lbl = UI.CreateLabel($"● {ContactDisplayRules.GetDisplayNameForUi(contact)}{scanStr}", 13, color);
            _contactsContainer.AddChild(lbl);
        }
    }

    private void UpdateStatusLine(GameState state)
    {
        if (state.Mission.PreSectorEventActive)
        {
            string title = string.IsNullOrEmpty(state.Mission.PendingPreSectorEventTitle)
                ? "Unbekannt"
                : state.Mission.PendingPreSectorEventTitle;
            _statusLine.Text = $"Funkspruch: {title} — Captain muss entscheiden.";
            _statusLine.AddThemeColorOverride("font_color", TC.Yellow);
            return;
        }
        if (!state.MissionStarted && state.RunActive && state.ShowRunMapOnMainScreen)
        {
            _statusLine.Text = "Run-Karte: Knoten wählen, betreten, auflösen · F12 Debug";
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
            _statusLine.Text =
                $"Scan: {ContactDisplayRules.GetDisplayNameForUi(scanning)} ({scanning.ScanProgress:F0}%)";
            return;
        }

        float alt = state.Ship.PositionZ - 500f;
        string altStr = alt >= 0 ? $"+{alt:F0}" : $"{alt:F0}";
        _statusLine.Text = $"Position: ({state.Ship.PositionX:F0}, {state.Ship.PositionY:F0}) ALT {altStr}";
    }

    private void UpdateMissionEnd(GameState state)
    {
        if (state.Mission.Phase == MissionPhase.Ended && !_missionEndLabel.Visible)
        {
            _missionEndLabel.Visible = true;
            string primary = state.Mission.PrimaryObjective == ObjectiveStatus.Completed ? "ERREICHT" : "GESCHEITERT";
            string secondary = state.Mission.SecondaryObjective == ObjectiveStatus.Completed ? "ERREICHT" : "GESCHEITERT";
            _missionEndLabel.Text = $"MISSION BEENDET\nPrimärziel: {primary}\nSekundärziel: {secondary}";
            _missionEndLabel.AddThemeColorOverride("font_color",
                state.Mission.PrimaryObjective == ObjectiveStatus.Completed ? TC.Green : TC.Red);
        }
        else if (state.Mission.Phase != MissionPhase.Ended)
        {
            _missionEndLabel.Visible = false;
        }
    }
}
