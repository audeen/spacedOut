using System;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;
using SpacedOut.Poi;
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

    }

    // ── Contacts from SectorData entities ───────────────────────────

    private static void PopulateContactsFromSector(GameState state, SectorData sector)
    {
        // Begegnungspunkt für MissionController: Layout-EncounterPosition, oder missionsgesetzter Marker
        var encEntity = sector.Entities.FirstOrDefault(e =>
            e.Type == SectorEntityType.EncounterMarker);
        Vector3 encWorld = encEntity != null ? encEntity.WorldPosition : sector.EncounterPosition;
        var encMap = CoordinateMapper.WorldToMap3D(encWorld, sector.LevelRadius);
        state.Mission.EncounterSpawnX = encMap.X;
        state.Mission.EncounterSpawnY = encMap.Y;

        foreach (var entity in sector.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;

            var p = CoordinateMapper.WorldToMap3D(entity.WorldPosition, sector.LevelRadius);
            var contact = new Contact
            {
                Id = entity.Id,
                AssetId = entity.AssetId,
                Type = entity.ContactType,
                DisplayName = entity.DisplayName,
                PositionX = p.X,
                PositionY = p.Y,
                PositionZ = p.Z,
                ThreatLevel = entity.ThreatLevel,
                ScanProgress = entity.ScanProgress,
                Discovery = entity.Discovery,
                PreRevealed = entity.PreRevealed,
                RadarShowDetectedInFullRange = entity.RadarShowDetectedInFullRange || entity.IsMovable,
                VelocityX = entity.Velocity.X,
                VelocityY = entity.Velocity.Z,
                VelocityZ = entity.Velocity.Y,
            };

            if (entity.PreRevealed)
            {
                contact.ReleasedToNav = true;
                contact.IsVisibleOnMainScreen = true;
            }

            if (!string.IsNullOrEmpty(entity.PoiType))
            {
                contact.PoiType = entity.PoiType;
                contact.PoiRewardProfile = entity.PoiRewardProfile;
                if (entity.PoiHasTrap)
                    contact.PoiRewardProfile = entity.PoiRewardProfile;
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

            if (!string.IsNullOrEmpty(entity.AgentTypeId) &&
                AgentDefinition.TryGet(entity.AgentTypeId, out var agentDef))
            {
                contact.HitPoints = agentDef.HitPoints;
                contact.MaxHitPoints = agentDef.HitPoints;
                contact.AttackDamage = agentDef.AttackDamage;
                contact.AttackInterval = agentDef.AttackInterval;
                contact.AttackRange = agentDef.AttackRange;

                var anchorMap = CoordinateMapper.WorldToMap3D(entity.AnchorPosition, sector.LevelRadius);
                var destMap = CoordinateMapper.WorldToMap3D(entity.DestinationPosition, sector.LevelRadius);

                contact.Agent = new AgentState
                {
                    AgentType = entity.AgentTypeId,
                    Mode = entity.InitialBehaviorMode,
                    AnchorX = anchorMap.X,
                    AnchorY = anchorMap.Y,
                    AnchorZ = anchorMap.Z,
                    DestinationX = destMap.X,
                    DestinationY = destMap.Y,
                    DetectionRadius = agentDef.DetectionRadius,
                    FleeThreshold = agentDef.FleeThreshold,
                    BaseSpeed = agentDef.BaseSpeed,
                    WeaponAccuracy = agentDef.WeaponAccuracy,
                    ShieldAbsorption = agentDef.ShieldAbsorption,
                    OrbitAngle = contact.PositionX * 0.1f,
                    PhaseOffset = contact.PositionY * 0.07f,
                };
            }

            state.Contacts.Add(contact);
        }
    }

    // ── Resource zones in map-space for web clients ─────────────────

    private static void PopulateResourceZones(GameState state, SectorData sector)
    {
        state.ResourceZones.Clear();
        if (!GameFeatures.ResourceZonesEnabled)
            return;

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
