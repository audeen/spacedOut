using System;
using System.Collections.Generic;
using SpacedOut.Agents;
using SpacedOut.Mission;

namespace SpacedOut.Run;

/// <summary>
/// Run-scoped storyteller hook. Implementations decide which mission templates flesh out the
/// procedural map (macro pass) and may reactively rewrite future, unseen nodes (reactive pass)
/// while the player progresses.
/// </summary>
/// <remarks>
/// Macro callbacks fire from <see cref="RunGenerator.GenerateRun"/>. Reactive callbacks fire from
/// <see cref="RunController"/> via <c>OnNodeEnteredHook</c> / <c>OnNodeResolvedHook</c> (wired by
/// <see cref="SpacedOut.Orchestration.RunOrchestrator"/>). Reactive edits MUST stay outside the
/// player's scan horizon (<see cref="RunController.MaxScanDepthAhead"/>); the controller exposes
/// <see cref="RunController.IsWithinScanRange"/> for this check.
/// </remarks>
public interface IRunDirector
{
    /// <summary>
    /// Macro: pick one template from <paramref name="pool"/> for an upcoming generic node.
    /// Implementations may apply weights based on <see cref="NodeIntent"/>, hostile streaks etc.
    /// Returning <c>null</c> falls back to the generator's default uniform pick.
    /// </summary>
    /// <param name="ctx">Director context (run state, RNG, current depth, pacing).</param>
    /// <param name="act">0-based act index this node belongs to.</param>
    /// <param name="depth">Depth counter the node will receive.</param>
    /// <param name="pool">Candidate pool (typically <see cref="MissionTemplateCatalog.GenericPool"/>).</param>
    /// <param name="hint">Optional intent the generator wants honored (e.g. station bias on act exits).</param>
    MissionTemplate? WeightTemplate(
        RunDirectorContext ctx,
        int act,
        int depth,
        IReadOnlyList<MissionTemplate> pool,
        NodeIntent hint);

    /// <summary>
    /// Macro: invoked after a fresh node is appended. Lets the director patch fields (Risk, Tags,
    /// title…) before the run starts. <paramref name="data"/> may be mutated in place.
    /// </summary>
    void PostprocessNode(RunDirectorContext ctx, RunNodeData data);

    /// <summary>
    /// Macro: final pass after the whole map is built. Use to enforce act-level guarantees
    /// (e.g. at least one Station per act, risk curve clamping).
    /// </summary>
    void EnforceActGuarantees(RunDirectorContext ctx, RunDefinition definition);

    /// <summary>
    /// Reactive: called from <see cref="RunController.OnNodeEnteredHook"/>. Update pacing counters.
    /// </summary>
    void OnNodeEntered(RunDirectorContext ctx, string nodeId);

    /// <summary>
    /// Reactive: called from <see cref="RunController.OnNodeResolvedHook"/>. Update pacing
    /// counters and call <see cref="AdjustUpcomingNodes"/> as needed.
    /// </summary>
    void OnNodeResolved(RunDirectorContext ctx, string nodeId, NodeResolution resolution);

    /// <summary>
    /// Reactive: rewrite future, out-of-scan nodes based on the current pacing state.
    /// Implementations MUST skip nodes inside the scan horizon
    /// (<see cref="RunController.IsWithinScanRange"/>).
    /// </summary>
    /// <returns>List of node ids that were modified (for logging / broadcast).</returns>
    IReadOnlyList<string> AdjustUpcomingNodes(RunDirectorContext ctx);

    /// <summary>
    /// Director-Hook für Event-Auswahl. Wird von <see cref="SpacedOut.Orchestration.MissionOrchestrator"/>
    /// aufgerufen, statt direkt <see cref="NodeEventCatalog.PickForNode"/> zu nutzen. Implementierung
    /// gewichtet Kandidaten gegen <see cref="PacingState.ThreatPool"/> + Intent + Streaks.
    /// </summary>
    /// <param name="ctx">Director context (run state, RNG, pacing).</param>
    /// <param name="node">Node das gerade betreten wird.</param>
    /// <param name="trigger">Pre- oder InSector — die Kandidatenliste ist bereits gefiltert.</param>
    /// <param name="candidates">Eligible Events (bereits durch Catalog gefiltert auf Trigger/Risk/Type).</param>
    /// <returns>Event zum Feuern oder <c>null</c> für "kein Event diesmal".</returns>
    NodeEvent? PickEvent(
        RunDirectorContext ctx,
        RunNodeData node,
        NodeEventTrigger trigger,
        IReadOnlyList<NodeEvent> candidates);

    /// <summary>
    /// Director-Hook für Generic-NPC-Spawn-Profile. Wird vom <see cref="SpacedOut.Orchestration.MissionOrchestrator"/>
    /// nach <see cref="AgentSpawnConfig.GetProfiles"/> aufgerufen. Director darf CountMin/Max anheben oder
    /// senken (innerhalb <see cref="PacingState.ThreatPool"/>) und neue Profile hinzufügen.
    /// </summary>
    /// <param name="ctx">Director context.</param>
    /// <param name="node">Node-Daten (Type, Risk, Biome).</param>
    /// <param name="biomeId">Biome-Id des Sektors.</param>
    /// <param name="baseProfiles">Basis-Profile aus <see cref="AgentSpawnConfig.GetProfiles"/>.</param>
    /// <returns>Angepasste Profil-Liste (kann <paramref name="baseProfiles"/> selbst sein).</returns>
    List<AgentSpawnProfile> AdjustSpawnProfiles(
        RunDirectorContext ctx,
        RunNodeData node,
        string biomeId,
        List<AgentSpawnProfile> baseProfiles);

    /// <summary>
    /// In-Sector-Heartbeat: wird periodisch vom <see cref="MissionController"/> (über
    /// <c>OnHeartbeatHook</c>) aufgerufen. Implementierung darf am Map-Rand Reinforcement-Wellen
    /// spawnen (per <see cref="MissionController.QueueDeferredSpawns"/> +
    /// <see cref="MissionController.FireRuntimeTriggerNow"/>), respektiert dabei ThreatPool und
    /// per-Sektor-Cap.
    /// </summary>
    /// <param name="ctx">Director context.</param>
    /// <param name="mission">Aktiver MissionController des Sektors.</param>
    /// <param name="node">Run-Knoten, der den Sektor erzeugt hat.</param>
    /// <param name="deltaSec">Zeit seit letztem Heartbeat-Tick (für Diagnose).</param>
    void TickInSector(
        RunDirectorContext ctx,
        MissionController mission,
        RunNodeData node,
        float deltaSec);

    /// <summary>
    /// Reaktiver In-Sector-Hook: wird gefeuert, sobald ein Hostile-Kontakt im aktiven Sektor
    /// zerstört wurde. Implementierung darf eine Sofort-Welle als Eskalation auslösen
    /// (separater Cooldown, gemeinsamer per-Sektor-Cap).
    /// </summary>
    /// <param name="ctx">Director context.</param>
    /// <param name="mission">Aktiver MissionController des Sektors.</param>
    /// <param name="node">Run-Knoten, der den Sektor erzeugt hat.</param>
    /// <param name="contactId">Id des zerstörten Hostile-Kontakts (Diagnose).</param>
    void OnHostileDestroyed(
        RunDirectorContext ctx,
        MissionController mission,
        RunNodeData node,
        string contactId);
}

/// <summary>
/// Snapshot the director receives on every callback. Bundles run/definition references plus the
/// shared RNG so all director decisions stay deterministic per <see cref="RunStateData.CampaignSeed"/>.
/// </summary>
public sealed class RunDirectorContext
{
    public RunDirectorContext(
        RunDefinition definition,
        RunStateData run,
        Random rng,
        Func<string, bool>? isWithinScanRange = null)
    {
        Definition = definition;
        Run = run;
        Rng = rng;
        IsWithinScanRange = isWithinScanRange ?? (_ => false);
    }

    public RunDefinition Definition { get; }
    public RunStateData Run { get; }
    public Random Rng { get; }

    /// <summary>Bridge to <see cref="RunController.IsWithinScanRange"/>; <c>true</c> = node is locked for the director.</summary>
    public Func<string, bool> IsWithinScanRange { get; }

    public PacingState Pacing => Run.Pacing;

    /// <summary>Convenience helper: <c>true</c> when the node may be modified by the director.</summary>
    public bool CanRewrite(string nodeId) => !IsWithinScanRange(nodeId);
}
