namespace SpacedOut.State;

/// <summary>
/// Runtime debug toggles checked by game systems. All flags default to off.
/// Toggled via the F12 debug panel.
/// </summary>
public class DebugFlags
{
    // ── Godmode ──────────────────────────────────────────────────────
    public bool GodMode { get; set; }

    // Derived convenience — all true when GodMode is on, but can be toggled individually.
    public bool Invulnerable { get; set; }
    public bool InfiniteProbes { get; set; }
    public bool InstantScans { get; set; }
    public bool InstantLock { get; set; }
    public bool NoHeat { get; set; }
    public bool NoCooldowns { get; set; }
    public bool RevealContacts { get; set; }

    // ── General ──────────────────────────────────────────────────────
    public bool FreezeTime { get; set; }
    public bool ShowAllRunNodes { get; set; }

    /// <summary>Activate godmode: turns on every sub-flag at once.</summary>
    public void EnableGodMode()
    {
        GodMode = true;
        Invulnerable = true;
        InfiniteProbes = true;
        InstantScans = true;
        InstantLock = true;
        NoHeat = true;
        NoCooldowns = true;
        RevealContacts = true;
    }

    /// <summary>Deactivate godmode: resets every sub-flag.</summary>
    public void DisableGodMode()
    {
        GodMode = false;
        Invulnerable = false;
        InfiniteProbes = false;
        InstantScans = false;
        InstantLock = false;
        NoHeat = false;
        NoCooldowns = false;
        RevealContacts = false;
    }

    public void ToggleGodMode()
    {
        if (GodMode) DisableGodMode();
        else EnableGodMode();
    }
}
