using System.Collections.Generic;

namespace SpacedOut.Run;

/// <summary>
/// What kind of beat the director wants the upcoming node to deliver. Maps loosely to
/// <see cref="MissionTemplate.Tags"/> so an <see cref="IRunDirector"/> can re-roll node templates
/// that carry the right vibe.
/// </summary>
public enum NodeIntent
{
    /// <summary>No explicit intent — leave whatever the macro generator picked.</summary>
    Free,
    /// <summary>Lower risk, salvage/anomaly to give the player a breath after a tense streak.</summary>
    Breather,
    /// <summary>Hostile or hazardous; raise tension.</summary>
    Pressure,
    /// <summary>Reward beat — extra resources / cache loot.</summary>
    Reward,
    /// <summary>Push a Station / safe-haven node into reach (e.g. low hull rescue path).</summary>
    SafeHaven,
}

/// <summary>
/// Director-owned, run-scoped pacing state. Lives on <see cref="RunStateData"/> so it can be
/// serialized into save snapshots once persistence lands. The <see cref="IRunDirector"/> updates
/// this on every node enter/resolve and reads it back when adjusting upcoming nodes.
/// </summary>
public class PacingState
{
    /// <summary>Consecutive Hostile-Knoten the player just walked through (resets on Side/Station/Anomaly).</summary>
    public int RecentHostileStreak { get; set; }

    /// <summary>Knoten seit dem letzten "Breather" (Side/Anomaly mit Risk &lt;= 1) oder Station.</summary>
    public int NodesSinceBreather { get; set; }

    /// <summary>Knoten seit der letzten Station.</summary>
    public int NodesSinceStation { get; set; }

    /// <summary>0..1 Tension-Schätzung; steigt mit Hostile-Streak und niedriger Hülle, fällt nach Breather/Station.</summary>
    public float TensionLevel { get; set; }

    /// <summary>Pro NodeId welche Beat-Richtung der Director sich für den Knoten gewünscht hat (Diagnose / UI).</summary>
    public Dictionary<string, NodeIntent> NodeIntent { get; set; } = new();

    /// <summary>
    /// Run-weiter Threat-Pool. Director gibt diese Punkte aus, wenn er Spawn-tragende Events oder
    /// extra NPC-Spawns platziert. Aufgeladen pro <c>OnNodeResolved</c>; verbraucht in
    /// <c>PickEvent</c>, <c>AdjustSpawnProfiles</c> und <see cref="SpacedOut.Mission.DecisionEffectResolver"/>.
    /// </summary>
    public float ThreatPool { get; set; }

    /// <summary>Aktuelle Obergrenze für <see cref="ThreatPool"/> (skaliert mit Akt).</summary>
    public float ThreatCapacity { get; set; }

    /// <summary>Letzter Pool-Drain (Diagnose / Debug-Panel). Format: <c>"event:in_ambush"</c>, <c>"spawn:pirate_raider"</c>.</summary>
    public string LastDrainReason { get; set; } = "";

    /// <summary>Punkte, die im letzten Drain abgezogen wurden.</summary>
    public float LastDrainAmount { get; set; }

    /// <summary>Letzter Pool-Refill (Diagnose). Format: <c>"+2.5 (Act 1, Tension 0.40)"</c>.</summary>
    public string LastRefillReason { get; set; } = "";

    /// <summary>Punkte, die im letzten Refill addiert wurden.</summary>
    public float LastRefillAmount { get; set; }

    // ── In-Sector Director-Wellen (per aktivem Sektor; reset in MissionOrchestrator beim Sektor-Eintritt) ──

    /// <summary>Anzahl Director-Wellen, die im aktuell aktiven Sektor bereits gespawnt wurden.</summary>
    public int InSectorWaveCount { get; set; }

    /// <summary>Mission-ElapsedTime (Sekunden) der letzten Welle. -1 = noch keine Welle dieses Sektors.</summary>
    public float LastWaveAtElapsed { get; set; } = -1f;

    /// <summary>Diagnose-Label der letzten Welle: <c>"heartbeat"</c>, <c>"on_kill"</c> oder leer.</summary>
    public string LastWaveReason { get; set; } = "";

    /// <summary>Mission-ElapsedTime, an der der nächste Director-Heartbeat fällig ist.</summary>
    public float NextHeartbeatAtElapsed { get; set; }
}
