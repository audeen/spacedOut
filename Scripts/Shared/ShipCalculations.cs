using System;
using SpacedOut.State;

namespace SpacedOut.Shared;

public static class ShipCalculations
{
    /// <summary>Instantaneous map-space velocity (XY); matches UpdateShipMovement (target tracking or waypoint).</summary>
    public static (float Vx, float Vy) GetShipVelocityXY(GameState state)
    {
        var ship = state.Ship;
        if (ship.FlightMode == FlightMode.Hold)
            return (0f, 0f);

        var tt = state.Navigation.TargetTracking;
        var contact = FindValidTrackingContact(state, tt);
        if (tt.Mode != TargetTrackingMode.None && contact != null)
        {
            if (TryGetTrackingSteerTarget(tt, ship, contact, out var tx, out var ty, out var tz, out var shouldMove)
                && shouldMove)
            {
                float dx = tx - ship.PositionX;
                float dy = ty - ship.PositionY;
                float dz = tz - ship.PositionZ;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist < 0.001f)
                    return (0f, 0f);
                float speed = CalculateShipSpeed(ship);
                return (dx / dist * speed, dy / dist * speed);
            }

            return (0f, 0f);
        }

        Waypoint? wp = null;
        foreach (var w in state.Route.Waypoints)
        {
            if (!w.IsReached)
            {
                wp = w;
                break;
            }
        }

        if (wp == null)
            return (0f, 0f);

        float wdx = wp.X - ship.PositionX;
        float wdy = wp.Y - ship.PositionY;
        float wdz = wp.Z - ship.PositionZ;
        float wdist = MathF.Sqrt(wdx * wdx + wdy * wdy + wdz * wdz);
        if (wdist < 0.001f)
            return (0f, 0f);

        float spd = CalculateShipSpeed(ship);
        return (wdx / wdist * spd, wdy / wdist * spd);
    }

    public static Contact? FindValidTrackingContact(GameState state, TargetTrackingState tt)
    {
        if (tt.Mode == TargetTrackingMode.None || string.IsNullOrEmpty(tt.TrackedContactId))
            return null;
        var c = state.Contacts.Find(x => x.Id == tt.TrackedContactId);
        if (c == null || c.IsDestroyed || c.Discovery != DiscoveryState.Scanned
            || !state.IsContactAvailableToCaptainNav(c))
            return null;
        return c;
    }

    public static void AdvanceOrbitAngle(TargetTrackingState tt, ShipState ship, float delta)
    {
        if (tt.Mode != TargetTrackingMode.Orbit)
            return;
        float speed = CalculateShipSpeed(ship);
        float r = Math.Max(Math.Clamp(tt.Range, TargetTrackingState.MinRange, TargetTrackingState.MaxRange), 10f);
        float angular = speed / r;
        if (!tt.OrbitClockwise)
            angular = -angular;
        tt.OrbitAngle += angular * delta;
    }

    /// <summary>Steer target for the current tracking mode. Orbit uses <see cref="TargetTrackingState.OrbitAngle"/> (advance separately).</summary>
    public static bool TryGetTrackingSteerTarget(
        TargetTrackingState tt,
        ShipState ship,
        Contact contact,
        out float tx,
        out float ty,
        out float tz,
        out bool shouldMove)
    {
        tx = ty = tz = 0f;
        shouldMove = true;
        float cx = contact.PositionX;
        float cy = contact.PositionY;
        float cz = contact.PositionZ;
        float r = Math.Clamp(tt.Range, TargetTrackingState.MinRange, TargetTrackingState.MaxRange);

        switch (tt.Mode)
        {
            case TargetTrackingMode.Follow:
                tx = cx;
                ty = cy;
                tz = cz;
                float fdx = tx - ship.PositionX;
                float fdy = ty - ship.PositionY;
                float fdz = tz - ship.PositionZ;
                float fdist = MathF.Sqrt(fdx * fdx + fdy * fdy + fdz * fdz);
                shouldMove = fdist > 0.5f;
                return true;

            case TargetTrackingMode.Orbit:
                tx = cx + MathF.Cos(tt.OrbitAngle) * r;
                ty = cy + MathF.Sin(tt.OrbitAngle) * r;
                tz = cz;
                float odx = tx - ship.PositionX;
                float ody = ty - ship.PositionY;
                float odz = tz - ship.PositionZ;
                float odist = MathF.Sqrt(odx * odx + ody * ody + odz * odz);
                shouldMove = odist > 0.5f;
                return true;

            case TargetTrackingMode.KeepAtRange:
                float sdx = ship.PositionX - cx;
                float sdy = ship.PositionY - cy;
                float sdz = ship.PositionZ - cz;
                float sdist = MathF.Sqrt(sdx * sdx + sdy * sdy + sdz * sdz);
                float db = TargetTrackingState.KeepAtRangeDeadband;
                if (sdist < 0.001f)
                {
                    shouldMove = false;
                    return true;
                }

                if (sdist > r + db)
                {
                    tx = cx;
                    ty = cy;
                    tz = cz;
                    return true;
                }

                if (sdist < r - db)
                {
                    float inv = 1f / sdist;
                    tx = ship.PositionX + sdx * inv * 1000f;
                    ty = ship.PositionY + sdy * inv * 1000f;
                    tz = ship.PositionZ + sdz * inv * 1000f;
                    return true;
                }

                shouldMove = false;
                return true;

            default:
                return false;
        }
    }

    public static void StepShipToward(ShipState ship, float tx, float ty, float tz, float delta)
    {
        float dx = tx - ship.PositionX;
        float dy = ty - ship.PositionY;
        float dz = tz - ship.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist < 0.001f)
            return;
        float speed = CalculateShipSpeed(ship);
        float moveX = dx / dist * speed * delta;
        float moveY = dy / dist * speed * delta;
        float moveZ = dz / dist * speed * delta;
        if (MathF.Abs(moveX) > MathF.Abs(dx)) moveX = dx;
        if (MathF.Abs(moveY) > MathF.Abs(dy)) moveY = dy;
        if (MathF.Abs(moveZ) > MathF.Abs(dz)) moveZ = dz;
        ship.PositionX += moveX;
        ship.PositionY += moveY;
        ship.PositionZ += moveZ;
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

    /// <summary>
    /// Sensor range as if sensors were Operational (no <see cref="GetStatusMultiplier"/> for Sensors).
    /// Used for tactical UI to show the lost annulus under interference vs. effective range.
    /// </summary>
    public static float CalculateSensorRangeIgnoringSystemStatus(ShipState ship, bool activeSensors = false)
    {
        const float baseRange = 500f;
        float energyMultiplier = ship.Energy.Sensors / 25f;
        float heatMult = ship.Systems[SystemId.Sensors].GetHeatEfficiencyMultiplier();
        float activeMult = activeSensors ? 1.5f : 1f;
        return baseRange * energyMultiplier * heatMult * activeMult;
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
