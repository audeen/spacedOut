using System.Collections.Generic;
using SpacedOut.State;

namespace SpacedOut.Agents;

/// <summary>
/// Static registry of all agent archetypes with their combat stats,
/// movement parameters, and initial behavior modes.
/// </summary>
public class AgentDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public ContactType ContactType { get; init; } = ContactType.Unknown;
    public int ThreatLevel { get; init; }

    // Combat
    public float HitPoints { get; init; } = 100f;
    public float AttackDamage { get; init; }
    public float AttackInterval { get; init; } = 10f;
    public float AttackRange { get; init; } = 200f;
    /// <summary>Baseline enemy gunnery skill (0–1) for hit chance vs. the player.</summary>
    public float WeaponAccuracy { get; init; } = 0.55f;
    public float ShieldAbsorption { get; init; }

    // Movement
    public float BaseSpeed { get; init; } = 4f;
    public float PatrolOrbitRadius { get; init; } = 100f;
    public float AngularSpeed { get; init; } = 0.12f;

    // Behavior
    public AgentBehaviorMode InitialMode { get; init; } = AgentBehaviorMode.Patrol;
    public float DetectionRadius { get; init; } = 250f;
    public float DisengageRadius { get; init; } = 400f;
    public float FleeThreshold { get; init; } = 0.2f;
    public bool CanFlee { get; init; } = true;

    // ── Static registry ──────────────────────────────────────────────

    private static readonly Dictionary<string, AgentDefinition> Definitions = new()
    {
        ["pirate_raider"] = new AgentDefinition
        {
            Id = "pirate_raider",
            DisplayName = "Piraten-Jäger",
            ContactType = ContactType.Hostile,
            ThreatLevel = 4,
            HitPoints = 60f,
            AttackDamage = 4f,
            AttackInterval = 8f,
            AttackRange = 180f,
            WeaponAccuracy = 0.52f,
            ShieldAbsorption = 0f,
            BaseSpeed = 6f,
            PatrolOrbitRadius = 120f,
            AngularSpeed = 0.15f,
            InitialMode = AgentBehaviorMode.Patrol,
            DetectionRadius = 250f,
            DisengageRadius = 400f,
            FleeThreshold = 0.2f,
            CanFlee = true,
        },

        ["pirate_corsair"] = new AgentDefinition
        {
            Id = "pirate_corsair",
            DisplayName = "Piraten-Korsair",
            ContactType = ContactType.Hostile,
            ThreatLevel = 5,
            HitPoints = 120f,
            AttackDamage = 7f,
            AttackInterval = 12f,
            AttackRange = 220f,
            WeaponAccuracy = 0.62f,
            ShieldAbsorption = 0.15f,
            BaseSpeed = 3.5f,
            PatrolOrbitRadius = 50f,
            AngularSpeed = 0.1f,
            InitialMode = AgentBehaviorMode.Guard,
            DetectionRadius = 250f,
            DisengageRadius = 350f,
            FleeThreshold = 0f,
            CanFlee = false,
        },

        ["trader_ship"] = new AgentDefinition
        {
            Id = "trader_ship",
            DisplayName = "Handelsschiff",
            ContactType = ContactType.Neutral,
            ThreatLevel = 1,
            HitPoints = 80f,
            AttackDamage = 0f,
            ShieldAbsorption = 0f,
            BaseSpeed = 2.5f,
            PatrolOrbitRadius = 0f,
            InitialMode = AgentBehaviorMode.Transit,
            DetectionRadius = 0f,
            CanFlee = false,
        },

        ["cargo_hauler"] = new AgentDefinition
        {
            Id = "cargo_hauler",
            DisplayName = "Frachtschlepper",
            ContactType = ContactType.Neutral,
            ThreatLevel = 0,
            HitPoints = 150f,
            AttackDamage = 0f,
            ShieldAbsorption = 0f,
            BaseSpeed = 1.5f,
            PatrolOrbitRadius = 0f,
            InitialMode = AgentBehaviorMode.Transit,
            DetectionRadius = 0f,
            CanFlee = false,
        },
    };

    public static AgentDefinition Get(string id) => Definitions[id];

    public static bool TryGet(string id, out AgentDefinition definition) =>
        Definitions.TryGetValue(id, out definition!);

    public static IReadOnlyDictionary<string, AgentDefinition> GetAll() => Definitions;
}
