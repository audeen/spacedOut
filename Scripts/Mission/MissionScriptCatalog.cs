using System.Collections.Generic;
using SpacedOut.Agents;
using SpacedOut.State;

namespace SpacedOut.Mission;

public static class MissionScriptCatalog
{
    private static readonly Dictionary<string, MissionScript> Scripts = new()
    {
        ["tutorial_blindsprung"] = CreateTutorialBlindsprung(),
    };

    public static MissionScript? GetOrNull(string id) =>
        Scripts.TryGetValue(id, out var s) ? s : null;

    private static MissionScript CreateTutorialBlindsprung() => new()
    {
        Id = "tutorial_blindsprung",
        BiomeId = "asteroid_field",
        LevelRadiusMultiplier = 5f,

        UseOnlyScriptTriggers = true,
        PauseActiveEventTimers = false,
        FailMissionWhenRecoveryWindowExpires = false,
        DisableBiomeLandmark = true,

        Initial = new InitialConditions
        {
            HullIntegrity = 95f,
            ProbeCharges = 3,
            Systems = new Dictionary<SystemId, SystemOverride>
            {
                [SystemId.Drive] = new() { Heat = 65f, Status = SystemStatus.Degraded },
                [SystemId.Sensors] = new() { Heat = 30f, Status = SystemStatus.Degraded },
                [SystemId.Shields] = new() { Heat = 40f },
            },
        },

        PrimaryObjective = new PrimaryObjective
        {
            AssetId = "station_relay",
            PoiBlueprintId = "navigation_relay",
            DefaultName = "Unbekanntes Signal",
            ClassifiedName = "Navigationsrelais",
            Placement = MarkerPlacementRule.SectorCenter,
            HiddenUntilDiscovered = true,
            HideExitUntilScanned = true,
        },

        MissionMarkers = new List<MissionMarkerPlacement>
        {
            new()
            {
                AssetId = "beacon",
                ContactId = "story_beacon",
                Rule = MarkerPlacementRule.AlongSpawnToLandmark,
                TAlongPath = 0.4f,
            },
        },

        AgentOverrides = new List<AgentSpawnProfile>
        {
            new()
            {
                AgentType = "trader_ship", CountMin = 1, CountMax = 1,
                InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
            },
        },

        DeferredAgentSpawns = new List<DeferredAgentSpawn>
        {
            new()
            {
                AgentType = "pirate_raider",
                TriggerId = "raider_spawn",
                Origin = SpawnOrigin.EdgeRandom,
                AnchorRef = TriggerRef.MapCenter,
                InitialMode = AgentBehaviorMode.Patrol,
            },
            new()
            {
                AgentType = "pirate_corsair",
                TriggerId = "corsair_spawn",
                Origin = SpawnOrigin.NearLandmark,
                AnchorRef = TriggerRef.Landmark,
                InitialMode = AgentBehaviorMode.Guard,
            },
        },

        ProximityTriggers = new List<ProximityTrigger>
        {
            new()
            {
                Ref = TriggerRef.NearestBeacon, Radius = 80f,
                EventId = "beacon_data", Once = true,
            },
            new()
            {
                Ref = TriggerRef.MapCenter, Radius = 300f,
                EventId = "raider_spawn",
                LogEntry = "[System] Kontakt! Schnelles Objekt auf Abfangkurs.",
                Once = true,
            },
            new()
            {
                Ref = TriggerRef.MapCenter, Radius = 250f,
                EventId = "sensor_shimmer", DecisionId = "interference_choice", Once = true,
            },
            new()
            {
                Ref = TriggerRef.Encounter, Radius = 200f,
                EventId = "unknown_approach",
                Once = true,
            },
            new()
            {
                Ref = TriggerRef.Landmark, Radius = 250f,
                EventId = "corsair_spawn",
                LogEntry = "[System] Weiterer Kontakt nahe dem Relais. Bewaffnet.",
                Once = true,
            },
            new()
            {
                Ref = TriggerRef.Landmark, Radius = 200f,
                EventId = "shield_stress", Once = true,
            },
            new()
            {
                Ref = TriggerRef.Landmark, Radius = 100f,
                EventId = "recovery_window", Once = true,
            },
        },

        TimeTriggers = new List<TimeTrigger>(),

        Classifications = new Dictionary<string, ContactClassification>
        {
            ["story_beacon"] = new()
            {
                Name = "Notfall-Signalboje",
                Type = ContactType.Neutral,
                Log = "Boje sendet automatisch: \u201e...Konvoi \u00fcberfallen... Frachter ARGOS verloren... Piraten operieren nahe dem Relais... Warnung an alle Schiffe...\u201c",
            },
            ["primary_target"] = new()
            {
                Name = "Navigationsrelais",
                Type = ContactType.Neutral,
                Log = "Relais aktiv. Tiefe Navigationsspeicher verschl\u00fcsselt — Tiefenscan und Extraktion durch den Maschinenraum erforderlich, bevor Sprungdaten bereitstehen.",
            },
            ["pirate_raider"] = new()
            {
                Log = "Piratenj\u00e4ger identifiziert. Zugeh\u00f6rigkeit: unbekannter Verband.",
            },
            ["pirate_corsair"] = new()
            {
                Log = "Piraten-Korsair. Schwer bewaffnet. Bewacht das Relais.",
            },
            ["trader_ship"] = new()
            {
                Name = "Handelsschiff",
                Log = "Passiert den Sektor ohne erkennbare Absicht.",
            },
            ["unknown_contact"] = new()
            {
                Name = "Frachtschlepper ARGOS-2",
                Log = "\u00dcberreste des Konvois.",
            },
        },

        Events = new Dictionary<string, ScriptedEvent>
        {
            ["beacon_data"] = new()
            {
                Title = "Datenfragment",
                Description = "Die Signalboje sendet ein automatisches Notsignal mit Datenfragmenten.",
                Duration = 60f,
                DecisionId = "beacon_choice",
                LogEntry = "[System] Boje sendet automatische Nachricht: \u201e...Konvoi \u00fcberfallen... Frachter ARGOS verloren... Piraten operieren nahe dem Relais... Warnung an alle Schiffe...\u201c",
                ShowOnMainScreen = false,
            },
        },

        Decisions = new Dictionary<string, ScriptedDecision>
        {
            ["beacon_choice"] = new()
            {
                Title = "Datenfragment der Boje",
                Description = "Die Boje enth\u00e4lt fragmentierte Daten. Auswerten kostet Zeit, k\u00f6nnte aber n\u00fctzlich sein.",
                Options = new List<DecisionOption>
                {
                    new() { Id = "evaluate", Label = "Daten auswerten", Description = "Verz\u00f6gerung, aber m\u00f6glicher Bonus" },
                    new() { Id = "acknowledge", Label = "Zur Kenntnis nehmen", Description = "Weiterfahren ohne Umweg" },
                },
            },
            ["interference_choice"] = new()
            {
                Title = "Interferenzzone",
                Description = "Starke elektromagnetische Interferenz voraus. Wie durchqueren?",
                Options = new List<DecisionOption>
                {
                    new() { Id = "slow", Label = "Langsam durchfahren", Description = "Evasive-Modus empfohlen" },
                    new() { Id = "detour", Label = "Umfliegen", Description = "L\u00e4ngerer Weg, weniger Risiko" },
                    new() { Id = "full_speed", Label = "Volle Kraft", Description = "Risiko f\u00fcr weitere Systemst\u00f6rungen" },
                },
            },
        },
    };
}
