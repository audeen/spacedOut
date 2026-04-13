using System;
using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>
/// Bridges SectorData with GameState contacts consumed by MissionController
/// and the network clients. Reads from SectorData (single source of truth)
/// and writes into GameState.Contacts for WebClient compatibility.
/// </summary>
public static class MissionGenerator
{
    /// <summary>
    /// Populates GameState from SectorData (preferred path).
    /// </summary>
    public static void PopulateMission(GameState state, SectorData sector)
    {
        state.Contacts.Clear();
        state.Route.Waypoints.Clear();
        state.Route.CurrentWaypointIndex = 0;
        state.ActiveEvents.Clear();
        state.Overlays.Clear();
        state.Mission = new MissionState();

        var shipMap = CoordinateMapper.WorldToMap3D(sector.SpawnPoint, sector.LevelRadius);
        state.Ship.PositionX = shipMap.X;
        state.Ship.PositionY = shipMap.Y;
        state.Ship.PositionZ = shipMap.Z;

        state.ContactsState.ActiveProbes.Clear();
        state.ContactsState.ProbeCharges = 3;
        state.ContactsState.ProbeRechargeTimer = 0;

        PopulateMissionInfo(state, sector.BiomeId, sector.Seed);
        PopulateContactsFromSector(state, sector);
        PopulateResourceZones(state, sector);

        GD.Print($"[MissionGen] {state.Contacts.Count} Kontakte, {state.ResourceZones.Count} Zonen generiert.");
    }

    /// <summary>
    /// Legacy overload: reads from LevelGenerator (delegates to SectorData path if available).
    /// </summary>
    public static void PopulateMission(GameState state, LevelGenerator level)
    {
        if (level.CurrentSectorData != null)
        {
            PopulateMission(state, level.CurrentSectorData);
            return;
        }

        // Fallback for standalone mode without SectorData
        var biome = BiomeDefinition.Get(level.CurrentBiomeId);
        state.Contacts.Clear();
        state.Route.Waypoints.Clear();
        state.Route.CurrentWaypointIndex = 0;
        state.ActiveEvents.Clear();
        state.Overlays.Clear();
        state.Mission = new MissionState();

        var shipMap = CoordinateMapper.WorldToMap3D(level.SpawnPoint, biome.LevelRadius);
        state.Ship.PositionX = shipMap.X;
        state.Ship.PositionY = shipMap.Y;
        state.Ship.PositionZ = shipMap.Z;

        PopulateMissionInfo(state, level.CurrentBiomeId, level.CurrentSeed);
    }

    // ── Mission identity ────────────────────────────────────────────

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

    // ── Contacts from SectorData entities ───────────────────────────

    private static void PopulateContactsFromSector(GameState state, SectorData sector)
    {
        // Encounter marker position for MissionController
        var encEntity = sector.Entities.FirstOrDefault(e =>
            e.Type == SectorEntityType.EncounterMarker);
        if (encEntity != null)
        {
            var ep = CoordinateMapper.WorldToMap3D(encEntity.WorldPosition, sector.LevelRadius);
            state.Mission.EncounterSpawnX = ep.X;
            state.Mission.EncounterSpawnY = ep.Y;
        }

        foreach (var entity in sector.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;

            var p = CoordinateMapper.WorldToMap3D(entity.WorldPosition, sector.LevelRadius);
            var contact = new Contact
            {
                Id = entity.Id,
                Type = entity.ContactType,
                DisplayName = entity.DisplayName,
                PositionX = p.X,
                PositionY = p.Y,
                PositionZ = p.Z,
                ThreatLevel = entity.ThreatLevel,
                ScanProgress = entity.ScanProgress,
                Discovery = entity.Discovery,
                PreRevealed = entity.PreRevealed,
                VelocityX = entity.Velocity.X,
                VelocityY = entity.Velocity.Z,
                VelocityZ = entity.Velocity.Y,
            };

            if (entity.PreRevealed)
            {
                contact.ReleasedToNav = true;
                contact.IsVisibleOnMainScreen = true;
            }

            if (entity.IsLandmark)
            {
                contact.Id = "primary_target";
                contact.DisplayName = sector.BiomeId switch
                {
                    "asteroid_field" => "Massiver Asteroiden-Komplex",
                    "wreck_zone" => "Schwaches Notsignal",
                    "station_periphery" => "Stationssignal",
                    _ => entity.DisplayName,
                };
            }

            state.Contacts.Add(contact);
        }
    }

    // ── Resource zones in map-space for web clients ─────────────────

    private static void PopulateResourceZones(GameState state, SectorData sector)
    {
        state.ResourceZones.Clear();
        float mapScale = 500f / sector.LevelRadius;

        foreach (var zone in sector.ResourceZones)
        {
            var mapPos = CoordinateMapper.WorldToMap3D(zone.Center, sector.LevelRadius);
            var c = zone.MapColor;
            string hex = $"#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2}";

            state.ResourceZones.Add(new MapResourceZone
            {
                Id = zone.Id,
                ResourceType = zone.ResourceType.ToString(),
                X = mapPos.X,
                Y = mapPos.Y,
                MapRadius = zone.Radius * mapScale,
                Density = zone.Density,
                MapColorHex = hex,
                Discovery = zone.Discovery.ToString(),
            });
        }
    }
}
