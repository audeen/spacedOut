using Godot;

namespace SpacedOut.LevelGen;

public static class PlaceholderFactory
{
    public static SpawnedObject Create(
        AssetDefinition asset, float scale, string biome, string instanceId, int seed)
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
            Name = $"{asset.Id}_{instanceId}",
        };

        var variant = AssetVariantPicker.PickRef(asset, seed, instanceId);
        if (variant is { } v && TryAttachVariant(obj, asset, v, scale, seed, instanceId))
            return obj;

        AttachPrimitive(obj, asset, scale);
        return obj;
    }

    /// <summary>
    /// Picks a bundle variant for the asset and queues its world transform into
    /// the shared MultiMesh pool instead of spawning an individual node.
    /// Returns false when no bundle variant is available (primitive/plain-scene
    /// pools); the caller should fall back to <see cref="Create"/> in that case.
    /// </summary>
    public static bool TryAppendInstanced(
        AssetDefinition asset, float scale, int seed, string instanceId,
        Vector3 worldPosition, Vector3 rotation, InstancedAsteroidPool pool)
    {
        if (asset == null || pool == null) return false;

        var variant = AssetVariantPicker.PickRef(asset, seed, instanceId);
        if (variant is not { } v) return false;

        // Primitive-scene variants (plain ScenePaths, no bundle child) also go
        // through the single-node path because their hierarchy/materials aren't
        // guaranteed to match the MeshInstance3D layout the pool expects.
        if (v.ChildName is null) return false;

        float totalScale = scale * asset.VisualScale * v.VisualScale;

        var basis = Basis.FromEuler(rotation);
        if (asset.MeshYawRandomize)
        {
            float yaw = AssetVariantPicker.PickYaw(seed, instanceId);
            basis *= new Basis(Vector3.Up, yaw);
        }
        basis = basis.Scaled(new Vector3(totalScale, totalScale, totalScale));

        var worldTransform = new Transform3D(basis, worldPosition);
        return pool.TryAddInstance(v.ScenePath, v.ChildName, worldTransform);
    }

    private static bool TryAttachVariant(
        SpawnedObject obj, AssetDefinition asset, ResolvedMeshRef variant,
        float scale, int seed, string instanceId)
    {
        Node3D? instance;

        if (variant.ChildName is { } childName)
        {
            instance = MeshBundleResolver.DuplicateChild(variant.ScenePath, childName);
        }
        else
        {
            var packed = GD.Load<PackedScene>(variant.ScenePath);
            instance = packed?.Instantiate<Node3D>();
        }

        if (instance == null) return false;

        float totalScale = scale * asset.VisualScale * variant.VisualScale;
        instance.Scale = new Vector3(totalScale, totalScale, totalScale);

        if (asset.MeshYawRandomize)
            instance.Rotation = new Vector3(0f, AssetVariantPicker.PickYaw(seed, instanceId), 0f);

        obj.RealScenePath = variant.ChildName is null
            ? variant.ScenePath
            : $"{variant.ScenePath}#{variant.ChildName}";
        obj.IsPlaceholder = false;
        obj.AddChild(instance);
        return true;
    }

    private static void AttachPrimitive(SpawnedObject obj, AssetDefinition asset, float scale)
    {
        var meshInstance = new MeshInstance3D
        {
            Mesh = CreateMesh(asset, scale),
            MaterialOverride = CreateMaterial(asset),
        };
        obj.AddChild(meshInstance);
        obj.IsPlaceholder = true;
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
