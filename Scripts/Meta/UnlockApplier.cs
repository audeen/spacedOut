using SpacedOut.Run;

namespace SpacedOut.Meta;

/// <summary>
/// Applies the selected loadout perk (M7) to a freshly-initialised <see cref="RunStateData"/>.
/// Called by <see cref="Orchestration.RunOrchestrator.StartRun"/> right after the
/// run controller resets resources.
/// </summary>
public static class UnlockApplier
{
    /// <summary>Bumps a resource bucket by <paramref name="delta"/>, creating the entry if missing.</summary>
    private static void Add(RunStateData run, string id, int delta)
    {
        run.Resources.TryGetValue(id, out int v);
        run.Resources[id] = v + delta;
    }

    /// <summary>
    /// Resolves the active perk (if any) and mutates <paramref name="run"/>.
    /// Unknown ids and ids the player hasn't actually purchased are silently ignored
    /// — debug tooling may still pass them, but the perk only fires when the player owns it.
    /// </summary>
    public static void ApplyToRunStart(RunStateData run, MetaProfile profile, string? activePerkId)
    {
        if (string.IsNullOrEmpty(activePerkId)) return;

        // Hard-gate: only honour purchased perks. Debug-forced runs can call SetSelectedPerk
        // beforehand if they want bypass.
        if (!profile.UnlockedIds.Contains(activePerkId)) return;

        switch (activePerkId)
        {
            case "perk_salvage":
                Add(run, RunResourceIds.SpareParts, 2);
                break;
            case "perk_haul":
                Add(run, RunResourceIds.Fuel, 3);
                break;
            case "perk_research":
                Add(run, RunResourceIds.ScienceData, 2);
                run.PerkFlags.Add("free_first_scan");
                break;
            case "perk_trader":
                Add(run, RunResourceIds.Credits, 50);
                break;
            case "perk_armor":
                run.CurrentHull = 110f;
                run.MaxHullOverride = 110f;
                break;
        }
    }
}
