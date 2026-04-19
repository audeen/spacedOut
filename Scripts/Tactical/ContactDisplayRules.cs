using SpacedOut.State;

namespace SpacedOut.Tactical;

/// <summary>Player-facing identity (matches tactical ??? until identification).</summary>
public static class ContactDisplayRules
{
    public static bool IsIdentityRevealedForPlayer(Contact c) =>
        c.PreRevealed || c.ScanProgress >= 100f;

    public static string GetDisplayNameForUi(Contact c) =>
        IsIdentityRevealedForPlayer(c)
            ? (string.IsNullOrEmpty(c.DisplayName) ? c.Id : c.DisplayName)
            : "???";
}
