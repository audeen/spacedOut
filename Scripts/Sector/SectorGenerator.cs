using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.State;

namespace SpacedOut.Sector;

/// <summary>
/// Pure-data sector generation: Seed + BiomeId -> SectorData.
/// No Godot nodes are created here; the LevelGenerator consumes
/// the resulting SectorData to build the 3D scene.
/// </summary>
public class SectorGenerator
{
    private Random _rng = new();
    private int _instanceCounter;

    public SectorData Generate(int seed, string biomeId)
    {
        _rng = new Random(seed);
        _instanceCounter = 0;

        var biome = BiomeDefinition.Get(biomeId);
        var data = new SectorData
        {
            Seed = seed,
            BiomeId = biomeId,
            LevelRadius = biome.LevelRadius,
        };

        CalculateLayout(data, biome);
        PlaceLandmarks(data, biome);

        var clusters = GenerateClusterCenters(data, biome);
        data.ClusterCenters.AddRange(clusters);

        PlaceMidScale(data, biome, clusters);
        PlaceSmallInClusters(data, biome, clusters);
        PlaceScatter(data, biome);
        PlaceMarkers(data, biome);
        GenerateResourceZones(data, biome);
        PopulateResourceZoneFill(data, biome);
        PlaceDynamicContacts(data, biome, seed);

        return data;
    }

    // ── Layout ──────────────────────────────────────────────────────

    private void CalculateLayout(SectorData data, BiomeDefinition biome)
    {
        float r = biome.LevelRadius;

        float spawnAngle = NextFloat() * Mathf.Tau;
        data.SpawnPoint = new Vector3(
            MathF.Cos(spawnAngle) * r * 0.85f,
            (NextFloat() - 0.5f) * r * 0.12f,
            MathF.Sin(spawnAngle) * r * 0.85f);

        float exitAngle = spawnAngle + MathF.PI + (NextFloat() - 0.5f) * 0.7f;
        data.ExitPoint = new Vector3(
            MathF.Cos(exitAngle) * r * 0.8f,
            (NextFloat() - 0.5f) * r * 0.12f,
            MathF.Sin(exitAngle) * r * 0.8f);

        float lmDist = r * biome.LandmarkCenterOffset;
        float lmAngle = NextFloat() * Mathf.Tau;
        data.LandmarkPosition = new Vector3(
            MathF.Cos(lmAngle) * lmDist,
            (NextFloat() - 0.5f) * r * 0.08f,
            MathF.Sin(lmAngle) * lmDist);

        float midAngle = (spawnAngle + exitAngle) / 2f;
        float encAngle = midAngle + MathF.PI / 2f + (NextFloat() - 0.5f) * 0.4f;
        if (_rng.Next(2) == 0) encAngle += MathF.PI;
        data.EncounterPosition = new Vector3(
            MathF.Cos(encAngle) * r * 0.9f,
            (NextFloat() - 0.5f) * r * 0.1f,
            MathF.Sin(encAngle) * r * 0.9f);
    }

    // ── Landmarks ───────────────────────────────────────────────────

    private void PlaceLandmarks(SectorData data, BiomeDefinition biome)
    {
        for (int i = 0; i < biome.LandmarkCount; i++)
        {
            string assetId = biome.LandmarkAssets[_rng.Next(biome.LandmarkAssets.Length)];
            var def = AssetLibrary.GetById(assetId);
            if (def == null) continue;

            float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
            var entity = CreateEntity(def, data.LandmarkPosition, scale, RandomRotation(), data.BiomeId);
            entity.MapPresence = MapPresence.Point;
            entity.IsLandmark = true;
            entity.DisplayName = def.DisplayName;
            entity.PreRevealed = true;
            entity.Discovery = DiscoveryState.Scanned;
            data.Entities.Add(entity);
        }
    }

    // ── Cluster centers ─────────────────────────────────────────────

    private List<Vector3> GenerateClusterCenters(SectorData data, BiomeDefinition biome)
    {
        int count = _rng.Next(biome.ClusterCountMin, biome.ClusterCountMax + 1);
        var centers = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float angle = NextFloat() * Mathf.Tau;
                float dist = (NextFloat() * 0.55f + 0.2f) * biome.LevelRadius;
                var c = new Vector3(
                    MathF.Cos(angle) * dist,
                    (NextFloat() - 0.5f) * biome.LevelRadius * 0.22f,
                    MathF.Sin(angle) * dist);

                if (c.DistanceTo(data.SpawnPoint) < biome.SpawnSafeRadius * 1.5f) continue;
                if (IsInCorridor(c, data.SpawnPoint, data.LandmarkPosition, biome.CorridorWidth * 0.8f)) continue;
                if (!AllFarEnough(c, centers, biome.ClusterSpacing)) continue;

                centers.Add(c);
                found = true;
                break;
            }

            if (!found)
            {
                float a = NextFloat() * Mathf.Tau;
                float d = NextFloat() * biome.LevelRadius * 0.6f;
                centers.Add(new Vector3(
                    MathF.Cos(a) * d,
                    (NextFloat() - 0.5f) * biome.LevelRadius * 0.15f,
                    MathF.Sin(a) * d));
            }
        }

        return centers;
    }

    // ── Mid-scale objects ───────────────────────────────────────────

    private void PlaceMidScale(SectorData data, BiomeDefinition biome, List<Vector3> clusters)
    {
        int total = _rng.Next(biome.MidScaleMin, biome.MidScaleMax + 1);
        if (clusters.Count == 0) return;

        int perCluster = total / clusters.Count;
        int remainder = total % clusters.Count;

        for (int c = 0; c < clusters.Count; c++)
        {
            int n = perCluster + (c < remainder ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                string assetId = biome.MidScaleAssets[_rng.Next(biome.MidScaleAssets.Length)];
                var def = AssetLibrary.GetById(assetId);
                if (def == null) continue;

                var pos = clusters[c] + RandomInSphere(biome.ClusterRadius);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TryPlaceEntity(data, def, pos, scale, biome);
            }
        }
    }

    // ── Small objects in clusters ───────────────────────────────────

    private void PlaceSmallInClusters(SectorData data, BiomeDefinition biome, List<Vector3> clusters)
    {
        int total = _rng.Next(biome.SmallMin, biome.SmallMax + 1);
        int inClusters = (int)(total * 0.65f);
        if (clusters.Count == 0) return;

        int perCluster = inClusters / clusters.Count;

        for (int c = 0; c < clusters.Count; c++)
        {
            for (int i = 0; i < perCluster; i++)
            {
                string assetId = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
                var def = AssetLibrary.GetById(assetId);
                if (def == null) continue;

                var pos = clusters[c] + RandomInSphere(biome.ClusterRadius * 1.3f);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TryPlaceEntity(data, def, pos, scale, biome);
            }
        }
    }

    // ── Scatter ─────────────────────────────────────────────────────

    private void PlaceScatter(SectorData data, BiomeDefinition biome)
    {
        int count = _rng.Next(biome.ScatterMin, biome.ScatterMax + 1);
        for (int i = 0; i < count; i++)
        {
            string assetId = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
            var def = AssetLibrary.GetById(assetId);
            if (def == null) continue;

            var pos = RandomInSphere(biome.LevelRadius * 0.8f);
            float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
            TryPlaceEntity(data, def, pos, scale, biome);
        }
    }

    // ── Markers (POI, exit, encounter, etc.) ────────────────────────

    private void PlaceMarkers(SectorData data, BiomeDefinition biome)
    {
        // Exit marker
        var exitDef = AssetLibrary.GetById("exit_marker");
        if (exitDef != null)
        {
            var entity = CreateEntity(exitDef, data.ExitPoint, 1f, Vector3.Zero, data.BiomeId);
            entity.MapPresence = MapPresence.Point;
            entity.DisplayName = "Ausgang";
            entity.PreRevealed = true;
            entity.Discovery = DiscoveryState.Scanned;
            entity.ContactType = ContactType.Neutral;
            data.Entities.Add(entity);
        }

        // Encounter marker
        var encDef = AssetLibrary.GetById("encounter_marker");
        if (encDef != null)
        {
            var entity = CreateEntity(encDef, data.EncounterPosition, 1f, Vector3.Zero, data.BiomeId);
            entity.MapPresence = MapPresence.Point;
            entity.DisplayName = "Begegnungssignal";
            entity.Discovery = DiscoveryState.Hidden;
            data.Entities.Add(entity);
        }

        // POI / resource / loot markers
        for (int i = 0; i < biome.MarkerCount; i++)
        {
            string assetId = biome.MarkerAssets[_rng.Next(biome.MarkerAssets.Length)];
            var def = AssetLibrary.GetById(assetId);
            if (def == null) continue;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var pos = RandomInSphere(biome.LevelRadius * 0.75f);
                if (CanPlace(pos, def.Radius, def.MinSpacing, data.Entities, data.SpawnPoint, biome.SpawnSafeRadius))
                {
                    var entity = CreateEntity(def, pos, 1f, Vector3.Zero, data.BiomeId);
                    entity.MapPresence = MapPresence.Point;
                    entity.IsMissionRelevant = true;
                    ApplyMarkerDefaults(entity, def);
                    data.Entities.Add(entity);
                    break;
                }
            }
        }
    }

    // ── Resource zones (Phase 2 data, generated here for consistency) ──

    private void GenerateResourceZones(SectorData data, BiomeDefinition biome)
    {
        foreach (var template in biome.ResourceZoneTemplates)
        {
            int count = _rng.Next(template.CountMin, template.CountMax + 1);
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    float angle = NextFloat() * Mathf.Tau;
                    float dist = (NextFloat() * 0.5f + 0.15f) * biome.LevelRadius;
                    var center = new Vector3(
                        MathF.Cos(angle) * dist,
                        (NextFloat() - 0.5f) * biome.LevelRadius * 0.1f,
                        MathF.Sin(angle) * dist);

                    if (center.DistanceTo(data.SpawnPoint) < biome.SpawnSafeRadius * 2f) continue;

                    bool tooClose = data.ResourceZones.Any(z =>
                        center.DistanceTo(z.Center) < z.Radius + template.RadiusMin);
                    if (tooClose) continue;

                    float radius = Mathf.Lerp(template.RadiusMin, template.RadiusMax, NextFloat());
                    float density = Mathf.Lerp(template.DensityMin, template.DensityMax, NextFloat());
                    float amount = radius * density * 100f;

                    data.ResourceZones.Add(new ResourceZone
                    {
                        Id = $"zone_{template.ResourceType}_{i}",
                        ResourceType = template.ResourceType,
                        Center = center,
                        Radius = radius,
                        Density = density,
                        MapColor = template.MapColor,
                        TotalAmount = amount,
                        RemainingAmount = amount,
                    });
                    break;
                }
            }
        }
    }

    // ── Fill resource zones with actual objects ────────────────────
    
    private void PopulateResourceZoneFill(SectorData data, BiomeDefinition biome)
    {
        var templatesByType = biome.ResourceZoneTemplates.ToDictionary(t => t.ResourceType);

        foreach (var zone in data.ResourceZones)
        {
            if (!templatesByType.TryGetValue(zone.ResourceType, out var template))
                continue;
            if (template.FillAssets.Length == 0)
                continue;

            int fillCount = (int)(template.BaseFillCount * zone.Density);
            fillCount = Math.Max(fillCount, 3);

            for (int i = 0; i < fillCount; i++)
            {
                string assetId = template.FillAssets[_rng.Next(template.FillAssets.Length)];
                var def = AssetLibrary.GetById(assetId);
                if (def == null) continue;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var offset = RandomInSphere(zone.Radius * 0.9f);
                    var pos = zone.Center + offset;

                    float distFromCenter = pos.DistanceTo(zone.Center);
                    if (distFromCenter > zone.Radius) continue;

                    float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                    float r = def.Radius * scale;

                    if (pos.DistanceTo(data.SpawnPoint) < biome.SpawnSafeRadius)
                        continue;

                    bool overlap = false;
                    foreach (var e in data.Entities)
                    {
                        if (pos.DistanceTo(e.WorldPosition) < r + e.Radius + def.MinSpacing * 0.5f)
                        {
                            overlap = true;
                            break;
                        }
                    }
                    if (overlap) continue;

                    var entity = CreateEntity(def, pos, scale, RandomRotation(), data.BiomeId);
                    entity.Tags = new[] { "zone_fill", zone.Id };
                    data.Entities.Add(entity);
                    break;
                }
            }
        }
    }

    // ── Dynamic contacts (patrol drone etc.) ────────────────────────

    private void PlaceDynamicContacts(SectorData data, BiomeDefinition biome, int seed)
    {
        float droneSpawnDist = biome.LevelRadius * 0.4f;
        float angle = new Random(seed).NextSingle() * MathF.PI * 2f;
        var dronePos = data.SpawnPoint + new Vector3(
            MathF.Cos(angle) * droneSpawnDist,
            0f,
            MathF.Sin(angle) * droneSpawnDist);

        data.Entities.Add(new SectorEntity
        {
            Id = "patrol_drone",
            Type = SectorEntityType.PatrolDrone,
            AssetId = "encounter_marker",
            WorldPosition = dronePos,
            Scale = 1f,
            Radius = 3f,
            Tags = new[] { "contact", "mobile" },
            MapPresence = MapPresence.Point,
            DisplayName = "Bewegliches Objekt",
            ContactType = ContactType.Unknown,
            ThreatLevel = 4,
            Discovery = DiscoveryState.Hidden,
            IsMovable = true,
            Velocity = new Vector3(8f, 0f, 0f),
        });
    }

    // ── Entity creation helpers ─────────────────────────────────────

    private SectorEntity CreateEntity(AssetDefinition def, Vector3 pos, float scale, Vector3 rot, string biomeId)
    {
        var mapPresence = ResolveMapPresence(def);
        return new SectorEntity
        {
            Id = NextInstanceId(),
            Type = MapAssetCategoryToEntityType(def.Category),
            AssetId = def.Id,
            WorldPosition = pos,
            Rotation = rot,
            Scale = scale,
            Radius = def.Radius * scale,
            Tags = def.Tags,
            MapPresence = mapPresence,
            IsLandmark = def.IsLandmark,
            DisplayName = def.DisplayName,
        };
    }

    private bool TryPlaceEntity(SectorData data, AssetDefinition def, Vector3 pos, float scale, BiomeDefinition biome)
    {
        float r = def.Radius * scale;
        if (!CanPlace(pos, r, def.MinSpacing, data.Entities, data.SpawnPoint, biome.SpawnSafeRadius))
            return false;

        var entity = CreateEntity(def, pos, scale, RandomRotation(), data.BiomeId);
        data.Entities.Add(entity);
        return true;
    }

    private static void ApplyMarkerDefaults(SectorEntity entity, AssetDefinition def)
    {
        switch (def.Category)
        {
            case AssetCategory.Beacon:
                entity.DisplayName = "Signalboje";
                entity.ContactType = ContactType.Neutral;
                entity.PreRevealed = true;
                entity.Discovery = DiscoveryState.Scanned;
                break;
            case AssetCategory.ResourceNode:
                entity.DisplayName = "Ressourcensignal";
                entity.ContactType = ContactType.Anomaly;
                entity.ThreatLevel = 1;
                break;
            case AssetCategory.LootMarker:
                entity.DisplayName = "Ladungsrest";
                entity.ContactType = ContactType.Unknown;
                entity.ThreatLevel = 1;
                break;
            case AssetCategory.PoiMarker:
                entity.DisplayName = "Unidentifizierte Anomalie";
                entity.ContactType = ContactType.Anomaly;
                entity.ThreatLevel = 2;
                break;
            case AssetCategory.UtilityNode:
                entity.DisplayName = "Versorgungsknoten";
                entity.ContactType = ContactType.Neutral;
                entity.PreRevealed = true;
                entity.Discovery = DiscoveryState.Scanned;
                break;
        }
    }

    // ── Mapping helpers ─────────────────────────────────────────────

    private static MapPresence ResolveMapPresence(AssetDefinition def)
    {
        if (def.DefaultMapPresence != MapPresence.None)
            return def.DefaultMapPresence;

        if (def.IsLandmark)
            return MapPresence.Point;

        return def.Category switch
        {
            AssetCategory.AsteroidSmall or AssetCategory.DebrisCluster or AssetCategory.CargoCluster
                => MapPresence.NearfieldOnly,
            AssetCategory.AsteroidMedium or AssetCategory.WreckMedium or AssetCategory.StationModule
                => MapPresence.NearfieldOnly,
            AssetCategory.ResourceNode or AssetCategory.PoiMarker or AssetCategory.LootMarker
            or AssetCategory.Beacon or AssetCategory.UtilityNode or AssetCategory.ExitMarker
            or AssetCategory.EncounterMarker
                => MapPresence.Point,
            _ => MapPresence.None,
        };
    }

    private static SectorEntityType MapAssetCategoryToEntityType(AssetCategory cat) => cat switch
    {
        AssetCategory.AsteroidLarge => SectorEntityType.AsteroidLarge,
        AssetCategory.AsteroidMedium => SectorEntityType.AsteroidMedium,
        AssetCategory.AsteroidSmall => SectorEntityType.AsteroidSmall,
        AssetCategory.WreckMain => SectorEntityType.WreckMain,
        AssetCategory.WreckMedium => SectorEntityType.WreckMedium,
        AssetCategory.DebrisCluster => SectorEntityType.DebrisCluster,
        AssetCategory.StationCore => SectorEntityType.StationCore,
        AssetCategory.StationModule => SectorEntityType.StationModule,
        AssetCategory.CargoCluster => SectorEntityType.CargoCluster,
        AssetCategory.UtilityNode => SectorEntityType.UtilityNode,
        AssetCategory.ResourceNode => SectorEntityType.ResourceNode,
        AssetCategory.PoiMarker => SectorEntityType.PoiMarker,
        AssetCategory.LootMarker => SectorEntityType.LootMarker,
        AssetCategory.Beacon => SectorEntityType.Beacon,
        AssetCategory.EncounterMarker => SectorEntityType.EncounterMarker,
        AssetCategory.ExitMarker => SectorEntityType.ExitMarker,
        _ => SectorEntityType.AsteroidSmall,
    };

    // ── Placement validation ────────────────────────────────────────

    private static bool CanPlace(
        Vector3 position, float radius, float minSpacing,
        List<SectorEntity> existing, Vector3 spawnPoint, float spawnSafeRadius)
    {
        if (position.DistanceTo(spawnPoint) < spawnSafeRadius)
            return false;

        foreach (var e in existing)
        {
            float minDist = radius + e.Radius + minSpacing;
            if (position.DistanceTo(e.WorldPosition) < minDist)
                return false;
        }

        return true;
    }

    // ── Math utilities ──────────────────────────────────────────────

    private float NextFloat() => (float)_rng.NextDouble();
    private string NextInstanceId() => $"{_instanceCounter++:D4}";

    private Vector3 RandomRotation() => new(
        NextFloat() * Mathf.Tau,
        NextFloat() * Mathf.Tau,
        NextFloat() * Mathf.Tau);

    private Vector3 RandomInSphere(float radius)
    {
        float u = NextFloat();
        float v = NextFloat();
        float theta = Mathf.Tau * u;
        float phi = MathF.Acos(2f * v - 1f);
        float r = radius * MathF.Cbrt(NextFloat());

        return new Vector3(
            r * MathF.Sin(phi) * MathF.Cos(theta),
            r * MathF.Sin(phi) * MathF.Sin(theta) * 0.3f,
            r * MathF.Cos(phi));
    }

    private static bool IsInCorridor(Vector3 point, Vector3 start, Vector3 end, float width)
    {
        var axis = end - start;
        float axisLenSq = axis.LengthSquared();
        if (axisLenSq < 0.01f) return false;
        float t = Mathf.Clamp((point - start).Dot(axis) / axisLenSq, 0, 1);
        var closest = start + axis * t;
        return point.DistanceTo(closest) < width;
    }

    private static bool AllFarEnough(Vector3 p, List<Vector3> pts, float minDist) =>
        pts.TrueForAll(q => p.DistanceTo(q) >= minDist);
}
