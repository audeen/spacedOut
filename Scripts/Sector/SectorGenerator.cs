using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.Poi;
using SpacedOut.Run;
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

    public SectorData Generate(int seed, string biomeId, RunNodeType? nodeType = null,
        float radiusMultiplier = 1f, List<AgentSpawnProfile>? agentOverrides = null,
        IReadOnlyList<MissionMarkerPlacement>? missionMarkers = null)
    {
        _rng = new Random(seed);
        _instanceCounter = 0;

        var biome = BiomeDefinition.Get(biomeId);
        float effectiveRadius = biome.LevelRadius * Math.Max(radiusMultiplier, 0.1f);
        float scaleFactor = effectiveRadius / biome.LevelRadius;

        var data = new SectorData
        {
            Seed = seed,
            BiomeId = biomeId,
            LevelRadius = effectiveRadius,
            NodeType = nodeType,
        };

        CalculateLayout(data, biome);
        PlaceLandmarks(data, biome);

        var clusters = GenerateClusterCenters(data, biome, scaleFactor);
        data.ClusterCenters.AddRange(clusters);

        PlaceMidScale(data, biome, clusters, scaleFactor);
        PlaceSmallInClusters(data, biome, clusters, scaleFactor);
        PlaceScatter(data, biome, scaleFactor);
        PlaceMarkers(data, biome, scaleFactor, missionMarkers);
        if (GameFeatures.ResourceZonesEnabled)
        {
            GenerateResourceZones(data, biome, scaleFactor);
            PopulateResourceZoneFill(data, biome);
        }
        PlaceDynamicContacts(data, biome, seed, agentOverrides);

        return data;
    }

    // ── Layout ──────────────────────────────────────────────────────

    private void CalculateLayout(SectorData data, BiomeDefinition biome)
    {
        float r = data.LevelRadius;

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

    private List<Vector3> GenerateClusterCenters(SectorData data, BiomeDefinition biome,
        float scaleFactor = 1f)
    {
        float sf = MathF.Sqrt(scaleFactor);
        int countMin = (int)(biome.ClusterCountMin * sf);
        int countMax = (int)(biome.ClusterCountMax * sf);
        int count = _rng.Next(countMin, countMax + 1);
        float r = data.LevelRadius;
        float spacing = biome.ClusterSpacing * scaleFactor;
        float safeRadius = biome.SpawnSafeRadius * scaleFactor;
        float corridorWidth = biome.CorridorWidth * scaleFactor;
        var centers = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float angle = NextFloat() * Mathf.Tau;
                float dist = (NextFloat() * 0.55f + 0.2f) * r;
                var c = new Vector3(
                    MathF.Cos(angle) * dist,
                    (NextFloat() - 0.5f) * r * 0.22f,
                    MathF.Sin(angle) * dist);

                if (c.DistanceTo(data.SpawnPoint) < safeRadius * 1.5f) continue;
                if (IsInCorridor(c, data.SpawnPoint, data.LandmarkPosition, corridorWidth * 0.8f)) continue;
                if (!AllFarEnough(c, centers, spacing)) continue;

                centers.Add(c);
                found = true;
                break;
            }

            if (!found)
            {
                float a = NextFloat() * Mathf.Tau;
                float d = NextFloat() * r * 0.6f;
                centers.Add(new Vector3(
                    MathF.Cos(a) * d,
                    (NextFloat() - 0.5f) * r * 0.15f,
                    MathF.Sin(a) * d));
            }
        }

        return centers;
    }

    // ── Mid-scale objects ───────────────────────────────────────────

    private void PlaceMidScale(SectorData data, BiomeDefinition biome, List<Vector3> clusters,
        float scaleFactor = 1f)
    {
        float sf = MathF.Sqrt(scaleFactor);
        int total = _rng.Next((int)(biome.MidScaleMin * sf), (int)(biome.MidScaleMax * sf) + 1);
        if (clusters.Count == 0) return;

        float clusterRadius = biome.ClusterRadius * scaleFactor;
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

                var pos = clusters[c] + RandomInSphere(clusterRadius);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TryPlaceEntity(data, def, pos, scale, biome);
            }
        }
    }

    // ── Small objects in clusters ───────────────────────────────────

    private void PlaceSmallInClusters(SectorData data, BiomeDefinition biome, List<Vector3> clusters,
        float scaleFactor = 1f)
    {
        float sf = MathF.Sqrt(scaleFactor);
        int total = _rng.Next((int)(biome.SmallMin * sf), (int)(biome.SmallMax * sf) + 1);
        int inClusters = (int)(total * 0.65f);
        if (clusters.Count == 0) return;

        float clusterRadius = biome.ClusterRadius * scaleFactor;
        int perCluster = inClusters / clusters.Count;

        for (int c = 0; c < clusters.Count; c++)
        {
            for (int i = 0; i < perCluster; i++)
            {
                string assetId = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
                var def = AssetLibrary.GetById(assetId);
                if (def == null) continue;

                var pos = clusters[c] + RandomInSphere(clusterRadius * 1.3f);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TryPlaceEntity(data, def, pos, scale, biome);
            }
        }
    }

    // ── Scatter ─────────────────────────────────────────────────────

    private void PlaceScatter(SectorData data, BiomeDefinition biome, float scaleFactor = 1f)
    {
        float sf = MathF.Sqrt(scaleFactor);
        int count = _rng.Next((int)(biome.ScatterMin * sf), (int)(biome.ScatterMax * sf) + 1);
        for (int i = 0; i < count; i++)
        {
            string assetId = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
            var def = AssetLibrary.GetById(assetId);
            if (def == null) continue;

            var pos = RandomInSphere(data.LevelRadius * 0.8f);
            float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
            TryPlaceEntity(data, def, pos, scale, biome);
        }
    }

    // ── Markers (POI, exit, encounter, etc.) ────────────────────────

    private void PlaceMarkers(SectorData data, BiomeDefinition biome, float scaleFactor = 1f,
        IReadOnlyList<MissionMarkerPlacement>? missionMarkers = null)
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

        // Encounter hotspot is only data.EncounterPosition (CalculateLayout). Optional encounter_marker
        // contacts can be placed via mission scripts / MissionMarkerPlacement — not auto-spawned here.

        PlaceGuaranteedMissionMarkers(data, biome, missionMarkers);

        var excludedFromRandom = new HashSet<string>();
        if (missionMarkers != null)
        {
            foreach (var m in missionMarkers)
                excludedFromRandom.Add(m.AssetId);
        }

        string[] randomPool = biome.MarkerAssets;
        if (excludedFromRandom.Count > 0)
        {
            var filtered = biome.MarkerAssets.Where(a => !excludedFromRandom.Contains(a)).ToArray();
            if (filtered.Length > 0)
                randomPool = filtered;
        }

        // POI / resource / loot markers (random pool — no story-critical assets)
        float sf = MathF.Sqrt(scaleFactor);
        int maxMarkers = (int)MathF.Max(0f, biome.MarkerCount * sf);
        int markerCount = maxMarkers <= 0 ? 0 : _rng.Next(0, maxMarkers + 1);
        for (int i = 0; i < markerCount; i++)
        {
            string assetId = randomPool[_rng.Next(randomPool.Length)];
            var def = AssetLibrary.GetById(assetId);
            if (def == null) continue;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var pos = RandomInSphere(data.LevelRadius * 0.75f);
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

    private void PlaceGuaranteedMissionMarkers(SectorData data, BiomeDefinition biome,
        IReadOnlyList<MissionMarkerPlacement>? placements)
    {
        if (placements == null || placements.Count == 0) return;

        foreach (var p in placements)
        {
            if (string.IsNullOrEmpty(p.AssetId) || string.IsNullOrEmpty(p.ContactId)) continue;
            var def = AssetLibrary.GetById(p.AssetId);
            if (def == null) continue;

            Vector3 pos = p.Rule switch
            {
                MarkerPlacementRule.AlongSpawnToLandmark => data.SpawnPoint.Lerp(
                    data.LandmarkPosition, Math.Clamp(p.TAlongPath, 0f, 1f))
                    + new Vector3(0f, (NextFloat() - 0.5f) * data.LevelRadius * 0.04f, 0f),
                _ => data.SpawnPoint,
            };

            if (!CanPlace(pos, def.Radius, def.MinSpacing, data.Entities, data.SpawnPoint, biome.SpawnSafeRadius))
            {
                float push = 0f;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    push += data.LevelRadius * 0.02f;
                    var tryPos = pos + (data.LandmarkPosition - data.SpawnPoint).Normalized() * push;
                    if (CanPlace(tryPos, def.Radius, def.MinSpacing, data.Entities, data.SpawnPoint,
                            biome.SpawnSafeRadius))
                    {
                        pos = tryPos;
                        break;
                    }
                }
            }

            var entity = CreateEntity(def, pos, 1f, Vector3.Zero, data.BiomeId);
            entity.Id = p.ContactId;
            entity.MapPresence = MapPresence.Point;
            entity.IsMissionRelevant = true;
            entity.Tags = new[] { "mission", "story" };
            ApplyMarkerDefaults(entity, def);
            data.Entities.Add(entity);
        }
    }

    // ── Resource zones (Phase 2 data, generated here for consistency) ──

    private void GenerateResourceZones(SectorData data, BiomeDefinition biome, float scaleFactor = 1f)
    {
        float sf = MathF.Sqrt(scaleFactor);
        float r = data.LevelRadius;

        foreach (var template in biome.ResourceZoneTemplates)
        {
            int count = _rng.Next(
                (int)(template.CountMin * sf),
                (int)(template.CountMax * sf) + 1);
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    float angle = NextFloat() * Mathf.Tau;
                    float dist = (NextFloat() * 0.5f + 0.15f) * r;
                    var center = new Vector3(
                        MathF.Cos(angle) * dist,
                        (NextFloat() - 0.5f) * r * 0.1f,
                        MathF.Sin(angle) * dist);

                    if (center.DistanceTo(data.SpawnPoint) < biome.SpawnSafeRadius * 2f) continue;

                    bool tooClose = data.ResourceZones.Any(z =>
                        center.DistanceTo(z.Center) < z.Radius + template.RadiusMin);
                    if (tooClose) continue;

                    float radius = Mathf.Lerp(template.RadiusMin, template.RadiusMax, NextFloat());
                    float density = Mathf.Lerp(template.DensityMin, template.DensityMax, NextFloat());
                    float amount = radius * density * 100f;

                    string zoneId = $"zone_{template.ResourceType}_{i}";
                    data.ResourceZones.Add(new ResourceZone
                    {
                        Id = zoneId,
                        ResourceType = template.ResourceType,
                        Center = center,
                        Radius = radius,
                        Density = density,
                        MapColor = template.MapColor,
                        TotalAmount = amount,
                        RemainingAmount = amount,
                    });
                    TrySpawnResourceSignalForZone(data, biome, center, radius, zoneId);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// One tactical "Ressourcensignal" point contact per resource zone (not from the random marker pool).
    /// </summary>
    private void TrySpawnResourceSignalForZone(
        SectorData data, BiomeDefinition biome, Vector3 zoneCenter, float zoneRadius, string zoneId)
    {
        var def = AssetLibrary.GetById("resource_node");
        if (def == null) return;

        float placeRadius = MathF.Max(zoneRadius * 0.35f, 8f);
        for (int attempt = 0; attempt < 25; attempt++)
        {
            var offset = placeRadius > 0.5f ? RandomInSphere(placeRadius) : Vector3.Zero;
            var pos = zoneCenter + offset;
            if (!CanPlace(pos, def.Radius, def.MinSpacing, data.Entities, data.SpawnPoint, biome.SpawnSafeRadius))
                continue;

            var entity = CreateEntity(def, pos, 1f, Vector3.Zero, data.BiomeId);
            entity.Id = $"rsig_{zoneId}";
            entity.MapPresence = MapPresence.Point;
            entity.IsMissionRelevant = true;
            entity.Tags = new[] { "resource_zone_signal", zoneId };
            ApplyMarkerDefaults(entity, def);
            data.Entities.Add(entity);
            return;
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

    // ── Dynamic contacts (agents) ─────────────────────────────────

    private void PlaceDynamicContacts(SectorData data, BiomeDefinition biome, int seed,
        List<AgentSpawnProfile>? agentOverrides = null)
    {
        var profiles = agentOverrides ?? AgentSpawnConfig.GetProfiles(data.BiomeId, data.NodeType);
        int agentIndex = 0;

        foreach (var profile in profiles)
        {
            if (!AgentDefinition.TryGet(profile.AgentType, out var def)) continue;

            int count = _rng.Next(profile.CountMin, profile.CountMax + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos;
                Vector3 anchorPos;
                Vector3 destinationPos;

                float lr = data.LevelRadius;
                if (profile.SpawnNearLandmark)
                {
                    float offset = lr * 0.1f;
                    float a = NextFloat() * Mathf.Tau;
                    spawnPos = data.LandmarkPosition + new Vector3(
                        MathF.Cos(a) * offset, 0f, MathF.Sin(a) * offset);
                    anchorPos = data.LandmarkPosition;
                }
                else
                {
                    float dist = lr * profile.SpawnRadiusFactor;
                    float a = NextFloat() * Mathf.Tau;
                    spawnPos = new Vector3(
                        MathF.Cos(a) * dist,
                        (NextFloat() - 0.5f) * lr * 0.06f,
                        MathF.Sin(a) * dist);
                    anchorPos = spawnPos;
                }

                if (profile.InitialMode == AgentBehaviorMode.Transit)
                {
                    float entryAngle = NextFloat() * Mathf.Tau;
                    float exitAngle = entryAngle + MathF.PI + (NextFloat() - 0.5f) * 0.8f;
                    float edgeDist = lr * 0.9f;
                    spawnPos = new Vector3(
                        MathF.Cos(entryAngle) * edgeDist,
                        (NextFloat() - 0.5f) * lr * 0.06f,
                        MathF.Sin(entryAngle) * edgeDist);
                    destinationPos = new Vector3(
                        MathF.Cos(exitAngle) * edgeDist,
                        (NextFloat() - 0.5f) * lr * 0.06f,
                        MathF.Sin(exitAngle) * edgeDist);
                    anchorPos = spawnPos;
                }
                else
                {
                    destinationPos = data.ExitPoint;
                }

                var entityType = def.ContactType == ContactType.Hostile
                    ? SectorEntityType.HostileShip
                    : SectorEntityType.NeutralShip;

                string id = $"agent_{profile.AgentType}_{agentIndex:D2}";
                agentIndex++;

                var dir = (destinationPos - spawnPos).Normalized();
                var initialVelocity = dir * def.BaseSpeed;
                if (profile.InitialMode is AgentBehaviorMode.Patrol or AgentBehaviorMode.Guard)
                    initialVelocity = new Vector3(def.BaseSpeed * 0.6f, 0f, 0f);

                data.Entities.Add(new SectorEntity
                {
                    Id = id,
                    Type = entityType,
                    AssetId = "encounter_marker",
                    WorldPosition = spawnPos,
                    Scale = 1f,
                    Radius = 3f,
                    Tags = new[] { "contact", "mobile", "agent" },
                    MapPresence = MapPresence.Point,
                    DisplayName = def.DisplayName,
                    ContactType = def.ContactType,
                    ThreatLevel = def.ThreatLevel,
                    Discovery = DiscoveryState.Hidden,
                    IsMovable = true,
                    Velocity = initialVelocity,
                    AgentTypeId = profile.AgentType,
                    InitialBehaviorMode = profile.InitialMode,
                    AnchorPosition = anchorPos,
                    DestinationPosition = destinationPos,
                });
            }
        }
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

    private void ApplyPoiMarkerVariantDefaults(SectorEntity entity, AssetDefinition def)
    {
        entity.ContactType = ContactType.Anomaly;
        entity.ThreatLevel = 1;

        string blueprintId = ExtractBlueprintIdFromTags(def.Tags);
        var bp = !string.IsNullOrEmpty(blueprintId)
            ? PoiBlueprintCatalog.GetOrNull(blueprintId) : null;

        if (bp != null)
        {
            entity.PoiType = bp.Id;
            entity.DisplayName = bp.DisplayName;

            if (bp.RewardProfiles.Length > 0)
                entity.PoiRewardProfile = RollRewardProfile(bp);
            if (bp.TrapChance > 0 && NextFloat() < bp.TrapChance)
            {
                entity.PoiHasTrap = true;
                var trapProfile = bp.RewardProfiles.FirstOrDefault(p => p.IsTrap);
                if (trapProfile != null)
                    entity.PoiRewardProfile = trapProfile.ProfileId;
            }
        }
        else
        {
            entity.DisplayName = def.DisplayName;
        }

        entity.PreRevealed = false;
        entity.Discovery = DiscoveryState.Hidden;
    }

    private string RollRewardProfile(PoiBlueprint bp)
    {
        var nonTrap = bp.RewardProfiles.Where(p => !p.IsTrap).ToArray();
        if (nonTrap.Length == 0) return "";
        float total = nonTrap.Sum(p => p.Weight);
        float roll = NextFloat() * total;
        float accum = 0;
        foreach (var p in nonTrap)
        {
            accum += p.Weight;
            if (roll <= accum) return p.ProfileId;
        }
        return nonTrap[^1].ProfileId;
    }

    private static string ExtractBlueprintIdFromTags(string[] tags)
    {
        const string prefix = "poi_blueprint:";
        foreach (var t in tags)
        {
            if (t.StartsWith(prefix))
                return t[prefix.Length..];
        }
        return "";
    }

    private void ApplyMarkerDefaults(SectorEntity entity, AssetDefinition def)
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
                entity.PreRevealed = false;
                entity.Discovery = DiscoveryState.Hidden;
                break;
            case AssetCategory.LootMarker:
                entity.DisplayName = "Ladungsrest";
                entity.ContactType = ContactType.Unknown;
                entity.ThreatLevel = 1;
                entity.PreRevealed = false;
                entity.Discovery = DiscoveryState.Hidden;
                break;
            case AssetCategory.PoiMarker:
                ApplyPoiMarkerVariantDefaults(entity, def);
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
