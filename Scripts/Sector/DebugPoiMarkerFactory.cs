using System;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Poi;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Sector;

/// <summary>Builds a <see cref="SectorEntity"/> for debug POI spawns (marker defaults aligned with sector generation).</summary>
public static class DebugPoiMarkerFactory
{
    private static int _seq;

    public static SectorEntity CreateNearShip(
        AssetDefinition def,
        float shipMapX, float shipMapY, float shipMapZ,
        float levelRadius)
    {
        if (def.Category != AssetCategory.PoiMarker)
            throw new System.ArgumentException($"Expected PoiMarker, got {def.Category}", nameof(def));

        Vector3 shipWorld = CoordinateMapper.MapToWorld(shipMapX, shipMapY, shipMapZ, levelRadius);
        float angle = GD.Randf() * Mathf.Tau;
        float dist = (float)GD.RandRange(80.0, 200.0);
        Vector3 pos = shipWorld + new Vector3((float)Mathf.Cos(angle) * dist, 0f, (float)Mathf.Sin(angle) * dist);
        return CreateAtWorldPosition(def, pos, idPrefix: "debug_poi");
    }

    /// <summary>Build a POI marker entity at a precomputed map-space position. Caller is responsible for any clamp/anchor logic.</summary>
    public static SectorEntity CreateAtMapPosition(
        AssetDefinition def,
        float mapX, float mapY, float mapZ,
        float levelRadius,
        string idPrefix = "runtime_poi")
    {
        if (def.Category != AssetCategory.PoiMarker)
            throw new System.ArgumentException($"Expected PoiMarker, got {def.Category}", nameof(def));

        Vector3 worldPos = CoordinateMapper.MapToWorld(mapX, mapY, mapZ, levelRadius);
        return CreateAtWorldPosition(def, worldPos, idPrefix);
    }

    private static SectorEntity CreateAtWorldPosition(AssetDefinition def, Vector3 worldPos, string idPrefix)
    {
        float scale = def.MinScale >= def.MaxScale
            ? def.MinScale
            : (float)GD.RandRange(def.MinScale, def.MaxScale);

        Vector3 rot = Vector3.Zero;
        if (def.MeshYawRandomize)
            rot = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f);

        string id = $"{idPrefix}_{++_seq}";

        MapPresence mapPresence = def.DefaultMapPresence != MapPresence.None
            ? def.DefaultMapPresence
            : MapPresence.Point;

        var entity = new SectorEntity
        {
            Id = id,
            Type = SectorEntityType.PoiMarker,
            AssetId = def.Id,
            WorldPosition = worldPos,
            Rotation = rot,
            Scale = scale,
            Radius = def.Radius * scale,
            Tags = def.Tags,
            MapPresence = mapPresence,
            IsLandmark = def.IsLandmark,
            DisplayName = def.DisplayName,
        };

        ApplyPoiMarkerVariantDefaults(entity, def);
        return entity;
    }

    private static void ApplyPoiMarkerVariantDefaults(SectorEntity entity, AssetDefinition def)
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
            {
                var rng = new Random(HashCode.Combine(entity.Id, GD.Randi()));
                entity.PoiRewardProfile = PoiRewardRoller.RollRewardProfile(bp, rng);
            }
        }
        else
        {
            entity.DisplayName = def.DisplayName;
        }

        entity.PreRevealed = false;
        entity.Discovery = DiscoveryState.Hidden;
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
}
