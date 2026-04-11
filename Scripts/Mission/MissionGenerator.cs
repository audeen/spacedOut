using System;
using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>
/// Bridges the procedural LevelGenerator output with the mission
/// data structures consumed by MissionController, StarMapDisplay
/// and TacticalDisplay.
///
/// Call <see cref="PopulateMission"/> after a level has been generated
/// and before <see cref="MissionController.StartMission"/>.
/// </summary>
public static class MissionGenerator
{
    /// <summary>
    /// Populates GameState with contacts, waypoints, briefing and ship
    /// position derived from the current level.
    /// </summary>
    public static void PopulateMission(GameState state, LevelGenerator level)
    {
        var biome = BiomeDefinition.Get(level.CurrentBiomeId);
        float mapScale = 500f / biome.LevelRadius;

        state.Contacts.Clear();
        state.Route.Waypoints.Clear();
        state.ActiveEvents.Clear();
        state.Overlays.Clear();
        state.Mission = new MissionState();

        var shipMap = Map(level.SpawnPoint, mapScale);
        state.Ship.PositionX = shipMap.X;
        state.Ship.PositionY = shipMap.Y;

        PopulateMissionInfo(state, level.CurrentBiomeId, level.CurrentSeed);
        PopulateWaypoints(state, level, mapScale);
        PopulateContacts(state, level, mapScale);

        GD.Print($"[MissionGen] {state.Route.Waypoints.Count} Waypoints, " +
                 $"{state.Contacts.Count} Kontakte generiert.");
    }

    // ── coordinate helper ───────────────────────────────────────────

    private static Vector2 Map(Vector3 pos, float scale) =>
        new(pos.X * scale + 500f, pos.Z * scale + 500f);

    // ── mission identity ────────────────────────────────────────────

    private static void PopulateMissionInfo(GameState state, string biomeId, int seed)
    {
        switch (biomeId)
        {
            case "asteroid_field":
                state.Mission.MissionId = $"asteroid_survey_{seed}";
                state.Mission.MissionTitle = "Erkundung Asteroidenfeld";
                state.Mission.BriefingText =
                    "Ein unchartiertes Asteroidenfeld wurde in diesem Sektor identifiziert. " +
                    "Ihr Auftrag: Durchqueren Sie das Feld, scannen Sie den zentralen " +
                    "Asteroiden-Komplex und erreichen Sie den Ausgangspunkt. " +
                    "Sensorinterferenzen durch metallische Asteroiden sind möglich.";
                break;

            case "wreck_zone":
                state.Mission.MissionId = $"wreck_salvage_{seed}";
                state.Mission.MissionTitle = "Bergung in Wrackzone";
                state.Mission.BriefingText =
                    "Ein schwaches Notsignal wurde aus einer Trümmerzone empfangen. " +
                    "Lokalisieren Sie das Hauptwrack, nähern Sie sich und " +
                    "sichern Sie die Bergung. Das Gebiet enthält dichten " +
                    "Trümmerregen – Schilde bereithalten.";
                break;

            case "station_periphery":
                state.Mission.MissionId = $"station_approach_{seed}";
                state.Mission.MissionTitle = "Andockmanöver Stationsperipherie";
                state.Mission.BriefingText =
                    "Eine Versorgungsstation in diesem Sektor erwartet Ihre Ankunft. " +
                    "Navigieren Sie durch die Peripherie-Strukturen zum Stationskern " +
                    "und führen Sie ein Andockmanöver durch. " +
                    "Frachtverkehr im Umfeld ist zu erwarten.";
                break;
        }
    }

    // ── waypoints ───────────────────────────────────────────────────

    private static void PopulateWaypoints(
        GameState state, LevelGenerator level, float mapScale)
    {
        var spawnMap = Map(level.SpawnPoint, mapScale);
        var exitMap = Map(level.ExitPoint, mapScale);

        var landmark = level.SpawnedObjects.FirstOrDefault(o => o.IsLandmark);
        var landmarkMap = landmark != null
            ? Map(landmark.Position, mapScale)
            : (spawnMap + exitMap) / 2f;
        var landmark3D = landmark?.Position ?? Vector3.Zero;

        // 1 – Start
        state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_start", X = spawnMap.X, Y = spawnMap.Y,
            Label = "Start", IsReached = true,
        });

        // Collect POI/marker objects along the route for intermediate waypoints
        var markers = level.SpawnedObjects
            .Where(o => o.Category >= AssetCategory.ResourceNode
                        && o.Category < AssetCategory.EncounterMarker)
            .ToList();

        // 2 – First approach: POI closest to the 1/3 point between spawn and landmark
        var thirdPos3D = level.SpawnPoint + (landmark3D - level.SpawnPoint) * 0.33f;
        var approach1 = markers
            .OrderBy(o => o.Position.DistanceTo(thirdPos3D))
            .FirstOrDefault();

        if (approach1 != null)
        {
            var pm = Map(approach1.Position, mapScale);
            state.Route.Waypoints.Add(new Waypoint
            {
                Id = "wp_approach_1", X = pm.X, Y = pm.Y,
                Label = "Sondierung",
            });
        }

        // 3 – Second approach: POI closest to the 2/3 point
        var twoThirdPos3D = level.SpawnPoint + (landmark3D - level.SpawnPoint) * 0.66f;
        var approach2 = markers
            .Where(o => o != approach1)
            .OrderBy(o => o.Position.DistanceTo(twoThirdPos3D))
            .FirstOrDefault();

        if (approach2 != null)
        {
            var pm = Map(approach2.Position, mapScale);
            state.Route.Waypoints.Add(new Waypoint
            {
                Id = "wp_approach_2", X = pm.X, Y = pm.Y,
                Label = "Annäherung",
            });
        }

        // 4 – Target (landmark)
        state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_target", X = landmarkMap.X, Y = landmarkMap.Y,
            Label = level.CurrentBiomeId switch
            {
                "asteroid_field" => "Asteroiden-Komplex",
                "wreck_zone" => "Hauptwrack",
                "station_periphery" => "Stationskern",
                _ => "Zielgebiet",
            },
        });

        // 5 – Exit
        state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_exit", X = exitMap.X, Y = exitMap.Y,
            Label = "Ausgang",
        });

        state.Route.CurrentWaypointIndex = 1;
    }

    // ── contacts ────────────────────────────────────────────────────

    private static void PopulateContacts(
        GameState state, LevelGenerator level, float mapScale)
    {
        // Primary target – the landmark
        var landmark = level.SpawnedObjects.FirstOrDefault(o => o.IsLandmark);
        if (landmark != null)
        {
            var p = Map(landmark.Position, mapScale);
            state.Contacts.Add(new Contact
            {
                Id = "primary_target",
                Type = ContactType.Unknown,
                DisplayName = level.CurrentBiomeId switch
                {
                    "asteroid_field" => "Massiver Asteroiden-Komplex",
                    "wreck_zone" => "Schwaches Notsignal",
                    "station_periphery" => "Stationssignal",
                    _ => "Unbekanntes Objekt",
                },
                PositionX = p.X, PositionY = p.Y,
                ThreatLevel = 0, ScanProgress = 10,
                IsVisibleOnMainScreen = true,
            });
        }

        // Cluster anomalies – all sizable mid-scale objects (up to 3)
        int clusterIdx = 0;
        foreach (var obj in level.SpawnedObjects
            .Where(o => !o.IsLandmark && o.ObjectRadius >= 8f)
            .Take(3))
        {
            var p = Map(obj.Position, mapScale);
            state.Contacts.Add(new Contact
            {
                Id = $"cluster_anomaly_{clusterIdx}",
                Type = ContactType.Anomaly,
                DisplayName = level.CurrentBiomeId switch
                {
                    "asteroid_field" => "Dichtes Asteroidenfeld",
                    "wreck_zone" => "Trümmerfeld",
                    "station_periphery" => "Frachtverkehr",
                    _ => "Anomalie",
                },
                PositionX = p.X, PositionY = p.Y,
                ThreatLevel = 2, ScanProgress = 40,
                IsVisibleOnMainScreen = true,
            });
            clusterIdx++;
        }

        // Encounter marker → store position for TriggerUnknownContact
        var encounter = level.SpawnedObjects
            .FirstOrDefault(o => o.Category == AssetCategory.EncounterMarker);
        if (encounter != null)
        {
            var ep = Map(encounter.Position, mapScale);
            state.Mission.EncounterSpawnX = ep.X;
            state.Mission.EncounterSpawnY = ep.Y;
        }

        // Additional contacts from markers / beacons / POIs
        int idx = 0;
        foreach (var obj in level.SpawnedObjects)
        {
            if (idx >= 8) break;

            var p = Map(obj.Position, mapScale);
            Contact? c = obj.Category switch
            {
                AssetCategory.Beacon => new Contact
                {
                    Id = $"beacon_{idx}", Type = ContactType.Neutral,
                    DisplayName = "Signalboje",
                    PositionX = p.X, PositionY = p.Y,
                    ScanProgress = 60, IsVisibleOnMainScreen = true,
                },
                AssetCategory.ResourceNode => new Contact
                {
                    Id = $"resource_{idx}", Type = ContactType.Anomaly,
                    DisplayName = "Ressourcensignal",
                    PositionX = p.X, PositionY = p.Y,
                    ThreatLevel = 1, ScanProgress = 30,
                },
                AssetCategory.LootMarker => new Contact
                {
                    Id = $"loot_{idx}", Type = ContactType.Unknown,
                    DisplayName = "Ladungsrest",
                    PositionX = p.X, PositionY = p.Y,
                    ThreatLevel = 1, ScanProgress = 20,
                },
                AssetCategory.PoiMarker => new Contact
                {
                    Id = $"poi_{idx}", Type = ContactType.Anomaly,
                    DisplayName = "Unidentifizierte Anomalie",
                    PositionX = p.X, PositionY = p.Y,
                    ThreatLevel = 2, ScanProgress = 15,
                },
                AssetCategory.UtilityNode => new Contact
                {
                    Id = $"utility_{idx}", Type = ContactType.Neutral,
                    DisplayName = "Versorgungsknoten",
                    PositionX = p.X, PositionY = p.Y,
                    ScanProgress = 50, IsVisibleOnMainScreen = true,
                },
                _ => null,
            };

            if (c != null)
            {
                state.Contacts.Add(c);
                idx++;
            }
        }
    }
}
