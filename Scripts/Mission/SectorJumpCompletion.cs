using System;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>
/// Shared rules for automatic sector completion at the jump point and for the Captain
/// <c>LeaveSector</c> command — must stay in sync with <see cref="MissionController.CheckEndConditions"/>.
/// </summary>
public static class SectorJumpCompletion
{
    public const float ExitProximityM = 50f;

    public static bool IsReady(GameState state)
    {
        if (!state.MissionStarted || state.Mission.Phase == MissionPhase.Ended)
            return false;

        float Dist(Contact c)
        {
            float dx = state.Ship.PositionX - c.PositionX;
            float dy = state.Ship.PositionY - c.PositionY;
            float dz = state.Ship.PositionZ - c.PositionZ;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        if (state.Mission.ProceduralSectorMission)
        {
            var exit = state.Contacts.Find(c => c.Id == "sector_exit");
            return exit != null && state.Mission.JumpCoordinatesUnlocked && Dist(exit) < ExitProximityM;
        }

        if (state.Mission.ScriptLocksExitUntilScan)
        {
            var relay = state.Contacts.Find(c => c.Id == "primary_target");
            var exit = state.Contacts.Find(c => c.Id == "sector_exit");
            return relay != null && relay.ScanProgress >= 100 && exit != null
                && state.Mission.JumpCoordinatesUnlocked && Dist(exit) < ExitProximityM;
        }

        var recoveryEvent = state.ActiveEvents.Find(e => e.Id == "recovery_window");
        if (recoveryEvent != null)
        {
            var primaryTarget = state.Contacts.Find(c => c.Id == "primary_target");
            return primaryTarget != null && primaryTarget.ScanProgress >= 100
                && Dist(primaryTarget) < ExitProximityM;
        }

        return false;
    }
}
