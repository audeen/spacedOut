using System;
using SpacedOut.Mission;
using SpacedOut.Orchestration;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.Commands;

/// <summary>
/// Shared operations available to all command handlers.
/// Implemented by <see cref="CommandProcessor"/> to bridge handler logic with Godot signals.
/// </summary>
public interface ICommandContext
{
    GameState State { get; }
    SectorData? CurrentSector { get; }
    /// <summary>Active mission controller. May be null if no mission is loaded (e.g. pre-sector events resolved from run map).</summary>
    MissionController? MissionController { get; }
    /// <summary>Active mission orchestrator. May be null in pre-sector context (no live sector to spawn into).</summary>
    MissionOrchestrator? Orchestrator { get; }
    void AddLog(string source, string message);
    void EmitStateChanged();

    Action<string>? OnNodeSelected { get; set; }
    Action<string, string>? OnRunResolveRequested { get; set; }
    Action<string>? OnScanRunNodeRequested { get; set; }
    /// <summary>Raised when a pre-sector decision needs the <see cref="Orchestration.MissionOrchestrator"/> to either skip the sector or build it.</summary>
    Action<string, string, bool>? OnPreSectorDecisionResolved { get; set; }

    /// <summary>CaptainNav nach getroffener Entscheidung: Prosatext + Ergebniszeile. <paramref name="cinematicResolution"/> true = Vollbild-Overlay (Pre-Sektor); false = Toast (In-Sektor u. a.).</summary>
    void NotifyDecisionResolved(string narrative, string effectsLine, string? decisionTitle, string? optionLabel, bool cinematicResolution);
}
