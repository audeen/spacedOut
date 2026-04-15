using System;
using SpacedOut.State;

namespace SpacedOut.Shared;

/// <summary>
/// Shared gunner shot resolution (manual Fire command and server-side autofire).
/// </summary>
public static class GunnerFireAction
{
    /// <summary>
    /// Attempts one shot. Returns true if a round was fired (hit or miss); false if preconditions failed.
    /// </summary>
    public static bool TryExecuteFire(GameState state)
    {
        var gunner = state.Gunner;
        if (string.IsNullOrEmpty(gunner.SelectedTargetId)) return false;
        if (gunner.TargetLockProgress < 100f) return false;
        if (gunner.FireCooldown > 0) return false;
        if (gunner.Tool != ToolMode.Combat) return false;

        var weaponSystem = state.Ship.Systems[SystemId.Weapons];
        if (weaponSystem.Status == SystemStatus.Offline) return false;
        if (weaponSystem.Heat >= ShipSystem.CriticalHeatThreshold) return false;

        var contact = state.Contacts.Find(c => c.Id == gunner.SelectedTargetId);
        if (contact == null || contact.IsDestroyed) return false;

        var ship = state.Ship;
        float dx = contact.PositionX - ship.PositionX;
        float dy = contact.PositionY - ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        var (svx, svy) = ShipCalculations.GetShipVelocityXY(ship, state.Route);
        float lateral = CombatAccuracy.ComputeLateralRelativeSpeed(
            svx, svy, contact.VelocityX, contact.VelocityY, dx, dy, dist);

        float pHit = CombatAccuracy.ComputeGunnerHitChance(
            dist, lateral, gunner.Mode, contact.IsDesignated, contact.HasWeakness,
            state.Engagement, ship.FlightMode);
        bool forceHit = state.Debug.GodMode;
        bool hit = CombatAccuracy.RollHit(pHit, forceHit);

        float heatPerShot = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionHeatPerShot
            : GunnerState.BarrageHeatPerShot;
        weaponSystem.Heat = Math.Clamp(weaponSystem.Heat + heatPerShot, 0, ShipSystem.MaxHeat);

        float fireInterval = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionFireInterval
            : GunnerState.BarrageFireInterval;
        gunner.FireCooldown = fireInterval;

        gunner.LastShotFeedbackSeq++;
        void SetFeedback(string text) => gunner.LastShotFeedbackText = text;

        void AddGunnerLog(string message)
        {
            state.Mission.Log.Add(new MissionLogEntry
            {
                Timestamp = state.Mission.ElapsedTime,
                Source = "Gunner",
                Message = message
            });
        }

        if (!hit)
        {
            AddGunnerLog($"Daneben: {contact.DisplayName} (Heat: {weaponSystem.Heat:F0}°)");
            SetFeedback($"Daneben — {contact.DisplayName}");
            return true;
        }

        float rawDamage = CalculateDamage(state, contact, dist);
        float shieldAbs = contact.Agent?.ShieldAbsorption ?? 0f;
        float damage = rawDamage * (1f - shieldAbs);
        contact.HitPoints = Math.Max(0, contact.HitPoints - damage);

        AddGunnerLog($"Treffer auf {contact.DisplayName}: {damage:F0} Schaden (Heat: {weaponSystem.Heat:F0}°)");

        if (contact.HitPoints <= 0)
        {
            contact.IsDestroyed = true;
            contact.IsTargetingPlayer = false;
            gunner.SelectedTargetId = null;
            gunner.TargetLockProgress = 0;
            AddGunnerLog($"{contact.DisplayName} zerstört!");
            SetFeedback($"Vernichtung — {contact.DisplayName}");
        }
        else
            SetFeedback($"Treffer — {damage:F0} Schaden");

        return true;
    }

    private static float CalculateDamage(GameState state, Contact contact, float dist)
    {
        var gunner = state.Gunner;
        var ship = state.Ship;

        float baseDamage = gunner.Mode == WeaponMode.Precision
            ? GunnerState.PrecisionBaseDamage
            : GunnerState.BarrageBaseDamage;

        float energyFactor = ship.Energy.Weapons / 25f;
        float statusMult = ship.Systems[SystemId.Weapons].GetHeatEfficiencyMultiplier();

        float distanceFactor = gunner.Mode == WeaponMode.Precision
            ? Math.Clamp(300f / (dist + 50f), 0.72f, 1.12f)
            : Math.Clamp(150f / (dist + 30f), 0.65f, 1.15f);

        float designationBonus = contact.IsDesignated ? 1.25f : 1f;
        float weaknessBonus = contact.HasWeakness ? 1.5f : 1f;

        float engagementMult = state.Engagement switch
        {
            EngagementRule.Aggressive => 1.2f,
            EngagementRule.Defensive => 0.8f,
            _ => 1f
        };

        return baseDamage * energyFactor * statusMult * distanceFactor
            * designationBonus * weaknessBonus * engagementMult;
    }
}
