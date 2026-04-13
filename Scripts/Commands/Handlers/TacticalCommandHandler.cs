using System;
using System.Linq;
using System.Text.Json;
using Godot;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class TacticalCommandHandler
{
    private readonly ICommandContext _ctx;

    public TacticalCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.ScanContact => HandleScanContact(data),
            CommandNames.MarkContact => HandleMarkContact(data),
            CommandNames.SetThreatPriority => HandleSetThreatPriority(data),
            CommandNames.RaiseTacticalWarning => HandleRaiseWarning(role, data),
            CommandNames.ToggleTacticalOnMainScreen => HandleToggleTactical(),
            CommandNames.DeployProbe => HandleDeployProbe(data),
            CommandNames.ReleaseToNavigator => HandleReleaseToNavigator(data),
            CommandNames.DesignateTarget => HandleDesignateTarget(data),
            CommandNames.AnalyzeWeakness => HandleAnalyzeWeakness(data),
            CommandNames.SetSensorMode => HandleSetSensorMode(data),
            CommandNames.PinContact => HandlePinContact(data),
            CommandNames.UnpinContact => HandleUnpinContact(data),
            _ => false,
        };
    }

    private bool HandleScanContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.ScanProgress >= 100) return false;
        if (contact.Discovery == DiscoveryState.Hidden) return false;

        foreach (var c in _ctx.State.Contacts) c.IsScanning = false;

        contact.IsScanning = true;
        _ctx.AddLog("Tactical", $"Scan gestartet: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleMarkContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;

        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = StationRole.Tactical,
            Category = OverlayCategory.Marker,
            Priority = 2,
            Text = $"⚡ Kontakt: {contact.DisplayName} (Bedrohung: {contact.ThreatLevel:F0})",
            MarkerTargetId = contactId,
            DurationSeconds = 25f,
            RemainingTime = 25f,
            ApprovedByCaptain = true,
        };
        _ctx.State.Overlays.Add(overlay);
        contact.IsVisibleOnMainScreen = true;
        _ctx.AddLog("Tactical", $"Kontakt auf Hauptschirm markiert: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetThreatPriority(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        float threat = data.GetProperty("threat_level").GetSingle();
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;

        contact.ThreatLevel = Math.Clamp(threat, 0, 10);
        _ctx.AddLog("Tactical", $"Bedrohungsstufe {contact.DisplayName}: {threat:F0}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleToggleTactical()
    {
        _ctx.State.ShowTacticalOnMainScreen = !_ctx.State.ShowTacticalOnMainScreen;
        string status = _ctx.State.ShowTacticalOnMainScreen ? "eingeblendet" : "ausgeblendet";
        _ctx.AddLog("Tactical", $"Taktische Ansicht auf Hauptschirm {status}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleRaiseWarning(StationRole fromRole, JsonElement data)
    {
        string message = data.GetProperty("message").GetString() ?? "";
        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = fromRole,
            Category = OverlayCategory.Warning,
            Priority = 3,
            Text = message,
            DurationSeconds = 60f,
            RemainingTime = 60f,
        };
        _ctx.State.Overlays.Add(overlay);
        _ctx.AddLog(fromRole.ToString(), $"Warnung gesendet: {message}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleDeployProbe(JsonElement data)
    {
        var cs = _ctx.State.ContactsState;
        if (cs.ProbeCharges <= 0) return false;

        float x = data.GetProperty("x").GetSingle();
        float y = data.GetProperty("y").GetSingle();
        float z = data.TryGetProperty("z", out var zEl) ? zEl.GetSingle() : _ctx.State.Ship.PositionZ;

        cs.ProbeCharges--;
        var probe = new SensorProbe
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            X = x, Y = y, Z = z,
            RevealRadius = 150f,
            RemainingTime = 25f,
        };
        cs.ActiveProbes.Add(probe);

        float elapsed = _ctx.State.Mission.ElapsedTime;
        foreach (var contact in _ctx.State.Contacts)
        {
            if (contact.Discovery == DiscoveryState.Scanned) continue;

            float dx = contact.PositionX - x;
            float dy = contact.PositionY - y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > probe.RevealRadius) continue;

            contact.Discovery = DiscoveryState.Probed;
            contact.ProbeExpiry = elapsed + 25f;
            contact.SnapshotX = contact.PositionX;
            contact.SnapshotY = contact.PositionY;
            contact.SnapshotZ = contact.PositionZ;
        }

        RevealResourceZonesInProbeRange(x, y, probe.RevealRadius);

        _ctx.AddLog("Tactical", $"Sonde gesendet: ({x:F0}, {y:F0}) — {cs.ProbeCharges} Ladungen verbleibend");
        _ctx.EmitStateChanged();
        return true;
    }

    private void RevealResourceZonesInProbeRange(float probeMapX, float probeMapY, float probeRadius)
    {
        var sector = _ctx.CurrentSector;
        if (sector == null) return;

        foreach (var zone in sector.ResourceZones)
        {
            if (zone.Discovery != DiscoveryState.Hidden) continue;

            var zoneMapPos = CoordinateMapper.WorldToMap3D(zone.Center, sector.LevelRadius);
            float dx = zoneMapPos.X - probeMapX;
            float dy = zoneMapPos.Y - probeMapY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float zoneMapRadius = zone.Radius * (500f / sector.LevelRadius);
            if (dist < probeRadius + zoneMapRadius)
            {
                zone.Discovery = DiscoveryState.Probed;

                var mapZone = _ctx.State.ResourceZones.Find(z => z.Id == zone.Id);
                if (mapZone != null)
                    mapZone.Discovery = "Probed";

                _ctx.AddLog("Tactical", $"Ressourcenzone entdeckt: {zone.ResourceType}");
            }
        }
    }

    private bool HandleReleaseToNavigator(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned) return false;
        if (contact.PreRevealed) return false;

        contact.ReleasedToNav = !contact.ReleasedToNav;
        string status = contact.ReleasedToNav ? "freigegeben" : "gesperrt";
        _ctx.AddLog("Tactical", $"Kontakt für Kommandant {status}: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleDesignateTarget(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned) return false;

        if (contact.IsDesignated)
        {
            contact.IsDesignated = false;
            _ctx.AddLog("Tactical", $"Zielbezeichnung aufgehoben: {contact.DisplayName}");
            _ctx.EmitStateChanged();
            return true;
        }

        int currentDesignations = _ctx.State.Contacts.Count(c => c.IsDesignated);
        if (currentDesignations >= 2) return false;

        contact.IsDesignated = true;
        _ctx.AddLog("Tactical", $"Ziel designiert: {contact.DisplayName} (+25% Schaden für Gunner)");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleAnalyzeWeakness(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned) return false;
        if (contact.HasWeakness) return false;
        if (contact.WeaknessAnalysisProgress >= 100) return false;

        foreach (var c in _ctx.State.Contacts) c.IsAnalyzing = false;

        contact.IsAnalyzing = true;
        _ctx.AddLog("Tactical", $"Schwachstellenanalyse gestartet: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetSensorMode(JsonElement data)
    {
        string mode = data.GetProperty("mode").GetString() ?? "";
        bool active = mode.Equals("active", StringComparison.OrdinalIgnoreCase);
        _ctx.State.ContactsState.ActiveSensors = active;
        _ctx.AddLog("Tactical", active
            ? "Sensormodus: AKTIV (+50% Reichweite, Position wird verraten)"
            : "Sensormodus: PASSIV (Standardreichweite, verdeckt)");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandlePinContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned && !contact.PreRevealed) return false;

        var pins = _ctx.State.PinnedEntities;
        if (pins.Exists(p => p.EntityId == contactId)) return false;
        if (pins.Count >= GameState.MaxPins) return false;

        pins.Add(new PinnedEntity
        {
            EntityId = contactId,
            Label = contact.DisplayName,
            Detail = $"T{contact.ThreatLevel:F0} | {contact.Type}",
            PinnedAt = _ctx.State.Mission.ElapsedTime,
        });

        contact.IsVisibleOnMainScreen = true;
        _ctx.AddLog("Tactical", $"Kontakt gepinnt: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleUnpinContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        int removed = _ctx.State.PinnedEntities.RemoveAll(p => p.EntityId == contactId);
        if (removed == 0) return false;

        _ctx.AddLog("Tactical", $"Pin entfernt: {contactId}");
        _ctx.EmitStateChanged();
        return true;
    }
}
