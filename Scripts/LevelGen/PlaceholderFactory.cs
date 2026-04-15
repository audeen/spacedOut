using Godot;

namespace SpacedOut.LevelGen;

public static class PlaceholderFactory
{
    public static SpawnedObject Create(
        AssetDefinition asset, float scale, string biome, string instanceId)
    {
        var obj = new SpawnedObject
        {
            InstanceId = instanceId,
            AssetId = asset.Id,
            Category = asset.Category,
            BiomeType = biome,
            ObjectRadius = asset.Radius * scale,
            Tags = asset.Tags,
            IsLandmark = asset.IsLandmark,
            RealScenePath = asset.ScenePath,
            Name = $"{asset.Id}_{instanceId}",
        };

        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = CreateMesh(asset, scale);
        meshInstance.MaterialOverride = CreateMaterial(asset);
        obj.AddChild(meshInstance);

        return obj;
    }

    private static Mesh CreateMesh(AssetDefinition asset, float scale)
    {
        float r = asset.Radius * scale;

        return asset.Shape switch
        {
            PlaceholderShape.Sphere => new SphereMesh
            {
                Radius = r,
                Height = r * 2f,
                RadialSegments = asset.IsLandmark ? 24 : 12,
                Rings = asset.IsLandmark ? 12 : 6,
            },
            PlaceholderShape.Box => new BoxMesh
            {
                Size = new Vector3(r * 2f, r * 1.4f, r * 1.8f),
            },
            PlaceholderShape.Capsule => new CapsuleMesh
            {
                Radius = r,
                Height = r * 3f,
            },
            PlaceholderShape.Cylinder => new CylinderMesh
            {
                TopRadius = r * 0.85f,
                BottomRadius = r,
                Height = r * 2.5f,
            },
            _ => new SphereMesh { Radius = r, Height = r * 2f },
        };
    }

    private static StandardMaterial3D CreateMaterial(AssetDefinition asset)
    {
        bool isMetal = asset.Category is >= AssetCategory.WreckMain
                                         and <= AssetCategory.UtilityNode;

        return new StandardMaterial3D
        {
            AlbedoColor = asset.DebugColor,
            Roughness = isMetal ? 0.5f : 0.85f,
            Metallic = isMetal ? 0.45f : 0.05f,
        };
    }
}
