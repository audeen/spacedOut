using SpacedOut.State;

namespace SpacedOut.Tactical;

/// <summary>
/// Unlocks jump coordinates for the procedural sector exit (same state as a probe hit on
/// <c>sector_exit</c>). Does nothing when the mission script locks the exit until relay scan.
/// </summary>
public static class SectorExitCoordinateUnlock
{
    public static bool TryApplyIfEligible(MissionState mission, Contact contact)
    {
        if (contact.Id != "sector_exit" || mission.JumpCoordinatesUnlocked)
            return false;
        if (mission.ScriptLocksExitUntilScan)
            return false;

        mission.JumpCoordinatesUnlocked = true;
        contact.Discovery = DiscoveryState.Scanned;
        contact.PreRevealed = true;
        contact.ReleasedToNav = true;
        contact.IsVisibleOnMainScreen = true;
        return true;
    }

    /// <summary>
    /// Passive debug reveal (<see cref="DebugFlags.RevealContacts"/>): unlocks the exit even when
    /// <see cref="MissionState.ScriptLocksExitUntilScan"/> would block normal probes/sensors.
    /// </summary>
    public static void ApplyDebugPassiveReveal(MissionState mission, Contact contact)
    {
        if (contact.Id != "sector_exit" || mission.JumpCoordinatesUnlocked)
            return;
        mission.JumpCoordinatesUnlocked = true;
        contact.Discovery = DiscoveryState.Detected;
        contact.PreRevealed = true;
        contact.ReleasedToNav = true;
        contact.IsVisibleOnMainScreen = true;
    }
}
