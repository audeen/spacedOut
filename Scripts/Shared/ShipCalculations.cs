using SpacedOut.State;

namespace SpacedOut.Shared;

public static class ShipCalculations
{
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

    public static float CalculateSensorRange(ShipState ship)
    {
        const float baseRange = 500f;
        float energyMultiplier = ship.Energy.Sensors / 33f;
        var sensorStatus = ship.Systems[SystemId.Sensors].Status;
        float statusMultiplier = GetStatusMultiplier(sensorStatus);
        return baseRange * energyMultiplier * statusMultiplier;
    }

    public static float CalculateShipSpeed(ShipState ship)
    {
        float driveEnergy = ship.Energy.Drive / 33f;
        float statusMult = GetDriveStatusMultiplier(
            ship.Systems[SystemId.Drive].Status);

        float modeSpeedMult = ship.FlightMode switch
        {
            FlightMode.Cruise => 1f,
            FlightMode.Approach => 0.4f,
            FlightMode.Evasive => 0.7f,
            FlightMode.Hold => 0f,
            _ => 1f
        };

        float baseSpeed = ship.SpeedLevel * (ShipState.MaxSpeed / ShipState.MaxSpeedLevel);
        return baseSpeed * driveEnergy * statusMult * modeSpeedMult;
    }
}
