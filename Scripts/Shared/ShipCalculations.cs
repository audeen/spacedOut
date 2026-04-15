using System;
using SpacedOut.State;

namespace SpacedOut.Shared;

public static class ShipCalculations
{
    /// <summary>Instantaneous map-space velocity (XY) toward the active waypoint; matches UpdateShipMovement.</summary>
    public static (float Vx, float Vy) GetShipVelocityXY(ShipState ship, RouteState route)
    {
        if (ship.FlightMode == FlightMode.Hold)
            return (0f, 0f);

        Waypoint? target = null;
        foreach (var w in route.Waypoints)
        {
            if (!w.IsReached)
            {
                target = w;
                break;
            }
        }

        if (target == null)
            return (0f, 0f);

        float dx = target.X - ship.PositionX;
        float dy = target.Y - ship.PositionY;
        float dz = target.Z - ship.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist < 0.001f)
            return (0f, 0f);

        float speed = CalculateShipSpeed(ship);
        return (dx / dist * speed, dy / dist * speed);
    }

    public static float GetStatusMultiplier(SystemStatus status) => status switch
    {
        SystemStatus.Operational => 1f,
        SystemStatus.Degraded => 0.7f,
        SystemStatus.Damaged => 0.4f,
        SystemStatus.Offline => 0f,
        _ => 1f
    };

    public static float GetDriveStatusMultiplier(SystemStatus status) => status switch
    {
        SystemStatus.Operational => 1f,
        SystemStatus.Degraded => 0.6f,
        SystemStatus.Damaged => 0.3f,
        SystemStatus.Offline => 0f,
        _ => 1f
    };

    public static float GetScanStatusMultiplier(SystemStatus status) => status switch
    {
        SystemStatus.Operational => 1f,
        SystemStatus.Degraded => 0.5f,
        SystemStatus.Damaged => 0.2f,
        SystemStatus.Offline => 0f,
        _ => 1f
    };

    public static float CalculateSensorRange(ShipState ship, bool activeSensors = false)
    {
        const float baseRange = 500f;
        float energyMultiplier = ship.Energy.Sensors / 25f;
        var sensorStatus = ship.Systems[SystemId.Sensors].Status;
        float statusMultiplier = GetStatusMultiplier(sensorStatus);
        float heatMult = ship.Systems[SystemId.Sensors].GetHeatEfficiencyMultiplier();
        float activeMult = activeSensors ? 1.5f : 1f;
        return baseRange * energyMultiplier * statusMultiplier * heatMult * activeMult;
    }

    public static float CalculateShipSpeed(ShipState ship)
    {
        float driveEnergy = ship.Energy.Drive / 25f;
        float statusMult = GetDriveStatusMultiplier(
            ship.Systems[SystemId.Drive].Status);
        float heatMult = ship.Systems[SystemId.Drive].GetHeatEfficiencyMultiplier();

        float modeSpeedMult = ship.FlightMode switch
        {
            FlightMode.Cruise => 1f,
            FlightMode.Approach => 0.4f,
            FlightMode.Evasive => 0.7f,
            FlightMode.Hold => 0f,
            _ => 1f
        };

        float baseSpeed = ship.SpeedLevel * (ShipState.MaxSpeed / ShipState.MaxSpeedLevel);
        return baseSpeed * driveEnergy * statusMult * heatMult * modeSpeedMult;
    }

    public static float CalculateShieldAbsorption(ShipState ship)
    {
        float shieldEnergy = ship.Energy.Shields;
        var shieldStatus = ship.Systems[SystemId.Shields].Status;
        if (shieldStatus == SystemStatus.Offline) return 0f;

        float statusMult = GetStatusMultiplier(shieldStatus);
        float heatMult = ship.Systems[SystemId.Shields].GetHeatEfficiencyMultiplier();
        return Math.Clamp(shieldEnergy / 50f * statusMult * heatMult, 0f, 0.6f);
    }

    public static float CalculateTargetLockSpeed(ShipState ship)
    {
        float weaponEnergy = ship.Energy.Weapons / 25f;
        float statusMult = GetStatusMultiplier(ship.Systems[SystemId.Weapons].Status);
        float heatMult = ship.Systems[SystemId.Weapons].GetHeatEfficiencyMultiplier();
        return weaponEnergy * statusMult * heatMult;
    }
}
