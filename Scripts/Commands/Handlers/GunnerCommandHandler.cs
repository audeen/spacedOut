using System;
using System.Text.Json;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class GunnerCommandHandler
{
    private readonly ICommandContext _ctx;

    public GunnerCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.SelectTarget => HandleSelectTarget(data),
            CommandNames.Fire => HandleFire(),
            CommandNames.CeaseFire => HandleCeaseFire(),
            CommandNames.SetWeaponMode => HandleSetWeaponMode(data),
            CommandNames.SetDefensiveMode => HandleSetDefensiveMode(data),
            _ => false,
        };
    }

    private bool HandleSelectTarget(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned) return false;
        if (contact.IsDestroyed) return false;

        var gunner = _ctx.State.Gunner;
        if (gunner.SelectedTargetId == contactId) return true;

        gunner.SelectedTargetId = contactId;
        gunner.TargetLockProgress = 0;
        _ctx.AddLog("Gunner", $"Ziel erfasst: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleFire()
    {
        var gunner = _ctx.State.Gunner;
        if (string.IsNullOrEmpty(gunner.SelectedTargetId)) return false;
        if (gunner.TargetLockProgress < 100f) return false;
        if (gunner.FireCooldown > 0) return false;

        var weaponSystem = _ctx.State.Ship.Systems[SystemId.Weapons];
        if (weaponSystem.Status == SystemStatus.Offline) return false;
        if (weaponSystem.Heat >= ShipSystem.CriticalHeatThreshold) return false;

        var contact = _ctx.State.Contacts.Find(c => c.Id == gunner.SelectedTargetId);
        if (contact == null || contact.IsDestroyed) return false;

        float damage = CalculateDamage(contact);
        contact.HitPoints = Math.Max(0, contact.HitPoints - damage);

        float heatPerShot = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionHeatPerShot
            : GunnerState.BarrageHeatPerShot;
        weaponSystem.Heat = Math.Clamp(weaponSystem.Heat + heatPerShot, 0, ShipSystem.MaxHeat);

        float fireInterval = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionFireInterval
            : GunnerState.BarrageFireInterval;
        gunner.FireCooldown = fireInterval;

        _ctx.AddLog("Gunner", $"Feuer auf {contact.DisplayName}: {damage:F0} Schaden (Heat: {weaponSystem.Heat:F0}°)");

        if (contact.HitPoints <= 0)
        {
            contact.IsDestroyed = true;
            contact.IsTargetingPlayer = false;
            gunner.SelectedTargetId = null;
            gunner.TargetLockProgress = 0;
            _ctx.AddLog("Gunner", $"{contact.DisplayName} zerstört!");
        }

        _ctx.EmitStateChanged();
        return true;
    }

    private float CalculateDamage(Contact contact)
    {
        var gunner = _ctx.State.Gunner;
        var ship = _ctx.State.Ship;

        float baseDamage = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionBaseDamage
            : GunnerState.BarrageBaseDamage;

        float energyFactor = ship.Energy.Weapons / 25f;
        float statusMult = ship.Systems[SystemId.Weapons].GetHeatEfficiencyMultiplier();

        float dx = contact.PositionX - ship.PositionX;
        float dy = contact.PositionY - ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float distanceFactor = gunner.Mode == WeaponMode.Precision
            ? Math.Clamp(300f / (dist + 50f), 0.3f, 1.5f)
            : Math.Clamp(150f / (dist + 30f), 0.2f, 1.8f);

        float flightModeMult = ship.FlightMode switch
        {
            FlightMode.Approach => 1.2f,
            FlightMode.Cruise => 1f,
            FlightMode.Evasive => 0.6f,
            FlightMode.Hold => 1.1f,
            _ => 1f,
        };

        float designationBonus = contact.IsDesignated ? 1.25f : 1f;
        float weaknessBonus = contact.HasWeakness ? 1.5f : 1f;

        float engagementMult = _ctx.State.Engagement switch
        {
            EngagementRule.Aggressive => 1.2f,
            EngagementRule.Defensive => 0.8f,
            _ => 1f,
        };

        return baseDamage * energyFactor * statusMult * distanceFactor
            * flightModeMult * designationBonus * weaknessBonus * engagementMult;
    }

    private bool HandleCeaseFire()
    {
        var gunner = _ctx.State.Gunner;
        gunner.SelectedTargetId = null;
        gunner.TargetLockProgress = 0;
        _ctx.AddLog("Gunner", "Feuer eingestellt");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetWeaponMode(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "";
        if (!Enum.TryParse<WeaponMode>(modeStr, true, out var mode)) return false;

        _ctx.State.Gunner.Mode = mode;
        _ctx.State.Gunner.TargetLockProgress = 0;
        _ctx.AddLog("Gunner", $"Waffenmodus: {mode}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetDefensiveMode(JsonElement data)
    {
        bool enabled = data.GetProperty("enabled").GetBoolean();
        _ctx.State.Gunner.IsDefensiveMode = enabled;
        _ctx.AddLog("Gunner", enabled ? "Defensivfeuer aktiviert" : "Defensivfeuer deaktiviert");
        _ctx.EmitStateChanged();
        return true;
    }
}
