using System;
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

    // Visual
    /// <summary>
    /// Pool of PackedScene paths for this ship archetype. When non-empty,
    /// <see cref="SpacedOut.LevelGen.Sector3DMarkers"/> picks one
    /// deterministically per contact id and attaches it as the 3D ship mesh.
    /// Empty pool => only the HUD blip is shown.
    /// </summary>
    public string[] ScenePaths { get; init; } = System.Array.Empty<string>();

    /// <summary>Uniform scale applied to the loaded ship scene.</summary>
    public float VisualScale { get; init; } = 1f;

    /// <summary>
    /// Extra Y rotation (radians) applied after aligning the mesh holder to velocity
    /// (<see cref="SpacedOut.LevelGen.Sector3DMarkers"/>). Use π when the model’s
    /// forward axis is +Z instead of Godot’s −Z.
    /// </summary>
    public float VisualYawOffsetRadians { get; init; } = 0f;

    /// <summary>
    /// Cosmetic weapon style shown when this agent fires on the player. Ignored for
    /// non-combat agents. Consumed by <see cref="SpacedOut.Fx.CombatFxSystem"/>.
    /// </summary>
    public WeaponVisualKind WeaponVisual { get; init; } = WeaponVisualKind.KineticTracer;

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
            ScenePaths = new[] { "res://Assets/models/ships/pirate_raider_01/pirate_raider_01.glb" },
            VisualScale = 0.075f,
            WeaponVisual = WeaponVisualKind.PlasmaBolt,
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
            ScenePaths = new[] { "res://Assets/models/ships/pirate_corsair_01/pirate_corsair_01.glb" },
            VisualScale = 0.1f,
            WeaponVisual = WeaponVisualKind.LaserBeam,
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
            ScenePaths = new[] { "res://Assets/models/ships/trader_ship_01/trader_ship_01.glb" },
            VisualScale = 0.05f,
            VisualYawOffsetRadians = MathF.PI,
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
            ScenePaths = new[] { "res://Assets/models/ships/cargo_hauler_01/cargo_hauler_01.glb" },
            VisualScale = 2f,
            VisualYawOffsetRadians = MathF.PI,
        },
    };

    public static AgentDefinition Get(string id) => Definitions[id];

    public static bool TryGet(string id, out AgentDefinition definition) =>
        Definitions.TryGetValue(id, out definition!);

    public static IReadOnlyDictionary<string, AgentDefinition> GetAll() => Definitions;
}
