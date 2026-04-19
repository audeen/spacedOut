using System;
using System.Collections.Generic;
using System.Text.Json;
using SpacedOut.Poi;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class EngineerCommandHandler
{
    private readonly ICommandContext _ctx;

    public EngineerCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.SetEnergyDistribution => HandleSetEnergyDistribution(data),
            CommandNames.StartRepair => HandleStartRepair(data),
            CommandNames.TriggerEmergencyShutdown => HandleEmergencyShutdown(data),
            CommandNames.RaiseSystemWarning => HandleRaiseWarning(role, data),
            CommandNames.CoolantPulse => HandleCoolantPulse(data),
            CommandNames.OverchargeSystem => HandleOvercharge(data),
            CommandNames.ActivateTractor => HandleActivateTractor(data),
            CommandNames.ExtractResource => HandleExtractResource(data),
            _ => false,
        };
    }

    private bool HandleSetEnergyDistribution(JsonElement data)
    {
        int drive = data.GetProperty("drive").GetInt32();
        int shields = data.GetProperty("shields").GetInt32();
        int sensors = data.GetProperty("sensors").GetInt32();
        int weapons = data.GetProperty("weapons").GetInt32();

        var dist = new EnergyDistribution
        {
            Drive = drive, Shields = shields, Sensors = sensors, Weapons = weapons
        };
        if (!dist.IsValid()) return false;

        _ctx.State.Ship.Energy = dist;
        _ctx.AddLog("Engineer", $"Energie: Antrieb {drive} / Schilde {shields} / Sensorik {sensors} / Waffen {weapons}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleStartRepair(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _ctx.State.Ship.Systems[systemId];
        if (system.Status == SystemStatus.Operational) return false;
        if (system.IsRepairing) return false;

        foreach (var s in _ctx.State.Ship.Systems.Values)
            s.IsRepairing = false;

        system.IsRepairing = true;
        system.RepairProgress = 0;
        _ctx.AddLog("Engineer", $"Reparatur gestartet: {systemId}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleEmergencyShutdown(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _ctx.State.Ship.Systems[systemId];
        system.Status = SystemStatus.Offline;
        system.Heat = 0;
        system.IsRepairing = false;
        _ctx.AddLog("Engineer", $"Notabschaltung: {systemId}");
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

    private bool HandleCoolantPulse(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _ctx.State.Ship.Systems[systemId];
        if (system.Status == SystemStatus.Offline) return false;
        if (system.CoolantCooldown > 0) return false;

        system.Heat = Math.Max(0, system.Heat - 20f);
        system.CoolantCooldown = ShipSystem.CoolantCooldownTime;
        _ctx.AddLog("Engineer", $"Kühlpuls: {systemId} (Heat: {system.Heat:F0})");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleOvercharge(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _ctx.State.Ship.Systems[systemId];
        if (system.Status == SystemStatus.Offline) return false;

        _ctx.AddLog("Engineer", $"Overcharge: {systemId}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleActivateTractor(JsonElement data)
    {
        return StartPoiExtraction(data, tractor: true);
    }

    private bool HandleExtractResource(JsonElement data)
    {
        return StartPoiExtraction(data, tractor: false);
    }

    private bool StartPoiExtraction(JsonElement data, bool tractor)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (string.IsNullOrEmpty(contact.PoiType)) return false;

        var bp = PoiBlueprintCatalog.GetOrNull(contact.PoiType);
        if (bp == null || !bp.RequiresExtraction) return false;
        if (tractor != bp.UsesTractorBeam) return false;

        bool readyForExtract = bp.RequiresDrill
            ? contact.PoiPhase == PoiPhase.Opened
            : contact.PoiPhase == PoiPhase.Analyzed;
        if (!readyForExtract) return false;

        float dx = contact.PositionX - _ctx.State.Ship.PositionX;
        float dy = contact.PositionY - _ctx.State.Ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        // Align with MissionController.TickPoiExtraction outer bound (ExtractRange * 1.3f).
        if (dist > bp.ExtractRange * 1.2f) return false;

        StopAnyExtraction();

        contact.PoiExtracting = true;
        contact.PoiPhase = PoiPhase.Extracting;
        contact.PoiProgress = 0;
        string tool = tractor ? "Traktorstrahl" : "Extraktion";
        _ctx.AddLog("Engineer", $"{tool} aktiviert: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private void StopAnyExtraction()
    {
        foreach (var c in _ctx.State.Contacts)
        {
            if (c.PoiExtracting)
            {
                c.PoiExtracting = false;
                if (c.PoiPhase == PoiPhase.Extracting)
                {
                    c.PoiPhase = c.PoiProgress > 0 ? PoiPhase.Opened : PoiPhase.Analyzed;
                    c.PoiProgress = 0;
                }
            }
        }
    }
}
