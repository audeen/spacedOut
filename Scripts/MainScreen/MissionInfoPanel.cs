using System;
using System.Linq;
using Godot;
using SpacedOut.Shared;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Overlays, events, contacts, status line, phase banner, and mission-end display.</summary>
public class MissionInfoPanel
{
    private VBoxContainer _overlayContainer = null!;
    private VBoxContainer _eventContainer = null!;
    private VBoxContainer _contactsContainer = null!;
    private Label _statusLine = null!;
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
        overlayPanel.Size = new Vector2(380, 300);
        overlayPanel.Visible = false;
        overlayPanel.Name = "OverlayPanel";
        _parent.AddChild(overlayPanel);

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
        UpdateEvents(state);
        UpdateContacts(state);
        UpdateStatusLine(state);
        UpdateMissionEnd(state);
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
        var panel = _parent.GetNode<PanelContainer>("OverlayPanel");
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
        var panel = _parent.GetNode<PanelContainer>("EventPanel");
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
        var panel = _parent.GetNode<PanelContainer>("ContactsPanel");
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
            _statusLine.Text = $"Scan: {scanning.DisplayName} ({scanning.ScanProgress:F0}%)";
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
