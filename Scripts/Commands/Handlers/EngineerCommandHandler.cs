using System;
using System.Collections.Generic;
using System.Text.Json;
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
            CommandNames.ConvertSparesToAmmo => HandleConvertSparesToAmmo(),
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

    private bool HandleConvertSparesToAmmo()
    {
        return false;
    }
}
