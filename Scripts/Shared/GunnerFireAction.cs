using System;
using SpacedOut.Poi;
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
        var (svx, svy) = ShipCalculations.GetShipVelocityXY(state);
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

        state.CombatFx.PendingShots.Add(new ShotEvent
        {
            ShooterId = "player",
            TargetId = contact.Id,
            Visual = gunner.Mode == WeaponMode.Precision
                ? WeaponVisualKind.LaserBeam
                : WeaponVisualKind.KineticTracer,
            Hit = hit,
            TimestampSec = state.Mission.ElapsedTime,
        });

        void AddGunnerLog(string message)
        {
            state.AddMissionLogEntry(new MissionLogEntry
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
            contact.IsTargetingPlayer = false;
            gunner.SelectedTargetId = null;
            gunner.TargetLockProgress = 0;

            int lootSeed = HashCode.Combine(
                contact.Id,
                (int)(state.Mission.ElapsedTime * 1000d) & 0x7FFFFFFF);
            var lootRng = new Random(lootSeed);

            if (TryConvertHostileToLootWreck(state, contact, lootRng))
            {
                AddGunnerLog($"{contact.DisplayName} zerstört — Wrack bergbar.");
                SetFeedback($"Vernichtung — {contact.DisplayName}");
            }
            else
            {
                contact.ApplyCombatDestruction();
                AddGunnerLog($"{contact.DisplayName} zerstört!");
                SetFeedback($"Vernichtung — {contact.DisplayName}");
            }
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

    /// <summary>
    /// Hostile agent ship destroyed → neutral wreck with POI so Tactical can run salvage (or false = caller sets IsDestroyed).
    /// </summary>
    private static bool TryConvertHostileToLootWreck(GameState state, Contact contact, Random rng)
    {
        if (GameFeatures.DestroyedHostileLootChance <= 0f) return false;
        if (contact.Type != ContactType.Hostile) return false;
        if (!string.IsNullOrEmpty(contact.PoiType)) return false;
        if (contact.Agent == null) return false;
        if (rng.NextDouble() >= GameFeatures.DestroyedHostileLootChance) return false;

        bool useArgos = rng.NextDouble() < 0.5;
        string blueprintId = useArgos ? "argos_blackbox" : "drifting_pod";
        var bp = PoiBlueprintCatalog.GetOrNull(blueprintId);
        if (bp == null) return false;

        string profile = bp.RewardProfiles.Length > 0
            ? PoiRewardRoller.RollRewardProfile(bp, rng)
            : "";

        string oldName = contact.DisplayName;
        contact.Type = ContactType.Neutral;
        contact.HitPoints = 0;
        contact.MaxHitPoints = 0;
        contact.IsDestroyed = false;
        contact.Agent = null;
        contact.PoiType = bp.Id;
        contact.PoiRewardProfile = profile;
        contact.PoiPhase = PoiPhase.None;
        contact.PoiProgress = 0;
        contact.PoiAnalyzing = false;
        contact.PoiDrilling = false;
        contact.PoiExtracting = false;
        contact.PoiInstabilityTimer = 0;
        contact.PoiTrapRevealed = false;
        contact.VelocityX = 0;
        contact.VelocityY = 0;
        contact.VelocityZ = 0;
        contact.IsDesignated = false;
        contact.HasWeakness = false;
        contact.IsAnalyzing = false;
        contact.WeaknessAnalysisProgress = 0;
        contact.IsTargetingPlayer = false;
        contact.AttackCooldown = 0;
        contact.DisplayName = $"Wrack — {oldName}";
        contact.ThreatLevel = 0;

        state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = state.Mission.ElapsedTime,
            Source = "Tactical",
            Message = $"Ziel ausgeschaltet — bergbare Signatur: {contact.DisplayName}",
            WebToast = MissionLogWebToast.Toast,
        });

        return true;
    }
}
