using System;
using SpacedOut.State;

namespace SpacedOut.Shared;

/// <summary>
/// Hit chance and single-roll resolution for gunner and hostile weapons.
/// Distance and lateral relative speed are primary factors; values are tuned for arcade feel (clamped band).
/// </summary>
public static class CombatAccuracy
{
    public const float MinHitChance = 0.05f;
    public const float MaxHitChance = 0.95f;

    /// <summary>Laterale Relativgeschwindigkeit (XY) senkrecht zur Schusslinie Schiff→Ziel.</summary>
    public static float ComputeLateralRelativeSpeed(
        float shooterVx, float shooterVy,
        float targetVx, float targetVy,
        float toTargetX, float toTargetY, float dist)
    {
        if (dist < 0.01f) dist = 0.01f;
        float rx = toTargetX / dist;
        float ry = toTargetY / dist;
        float vrx = targetVx - shooterVx;
        float vry = targetVy - shooterVy;
        return MathF.Abs(vrx * (-ry) + vry * rx);
    }

    public static float ComputeGunnerHitChance(
        float dist,
        float lateralRelSpeed,
        WeaponMode mode,
        bool isDesignated,
        bool hasWeakness,
        EngagementRule engagement,
        FlightMode shooterFlightMode)
    {
        float optimal = mode == WeaponMode.Precision ? 240f : 140f;
        float rangeSpread = mode == WeaponMode.Precision ? 380f : 280f;
        float distShape = 1f - Math.Clamp(MathF.Abs(dist - optimal) / rangeSpread, 0f, 0.55f);

        float lateralDampen = 1f / (1f + lateralRelSpeed * 0.065f);

        float baseP = mode == WeaponMode.Precision ? 0.78f : 0.62f;
        float p = baseP * (0.45f + 0.55f * distShape) * lateralDampen;

        if (isDesignated) p *= 1.12f;
        if (hasWeakness) p *= 1.08f;

        p *= engagement switch
        {
            EngagementRule.Aggressive => 1.06f,
            EngagementRule.Defensive => 0.92f,
            _ => 1f
        };

        p *= shooterFlightMode switch
        {
            FlightMode.Evasive => 0.9f,
            FlightMode.Approach => 1.04f,
            FlightMode.Hold => 1.05f,
            _ => 1f
        };

        return Math.Clamp(p, MinHitChance, MaxHitChance);
    }

    /// <param name="enemyWeaponAccuracy">0–1 skill from agent definition; fallback uses threat.</param>
    public static float ComputeEnemyHitChance(
        float dist,
        float lateralRelSpeed,
        float effectiveRange,
        float enemyWeaponAccuracy,
        int threatFallback,
        FlightMode playerFlightMode,
        float playerSpeed,
        EngagementRule engagement)
    {
        float rangeT = effectiveRange > 0.01f ? Math.Clamp(dist / effectiveRange, 0f, 1f) : 0f;
        float edgePenalty = 1f - 0.22f * rangeT * rangeT;

        float lateralDampen = 1f / (1f + lateralRelSpeed * 0.055f);

        float skill = enemyWeaponAccuracy > 0f
            ? enemyWeaponAccuracy
            : Math.Clamp(0.42f + threatFallback * 0.06f, 0.35f, 0.82f);

        float p = skill * edgePenalty * lateralDampen;

        p *= 1f / (1f + playerSpeed * 0.045f);

        p *= playerFlightMode switch
        {
            FlightMode.Evasive => 0.68f,
            FlightMode.Approach => 0.92f,
            FlightMode.Cruise => 1f,
            FlightMode.Hold => 1.02f,
            _ => 1f
        };

        p *= engagement switch
        {
            EngagementRule.Defensive => 0.88f,
            EngagementRule.Aggressive => 1.05f,
            _ => 1f
        };

        return Math.Clamp(p, MinHitChance, MaxHitChance);
    }

    public static bool RollHit(float hitChance, bool forceHit)
    {
        if (forceHit) return true;
        return Random.Shared.NextSingle() < hitChance;
    }
}
