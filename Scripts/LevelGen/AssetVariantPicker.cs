using Godot;
using SpacedOut.LevelGen.Procedural;

namespace SpacedOut.LevelGen;

/// <summary>
/// Deterministic selection of a single scene path from a pool, keyed by a
/// stable string (usually the SectorEntity.Id combined with the sector seed).
/// Same input keys always produce the same pick and yaw, so reloads/regenerations
/// with the same seed look identical.
/// </summary>
public static class AssetVariantPicker
{
    /// <summary>
    /// Picks one scene path from the pool. Returns null when the pool is
    /// empty or no entry exists as a resource on disk.
    /// </summary>
    public static string? PickScenePath(string[] pool, int seed, string entityId)
    {
        if (pool == null || pool.Length == 0) return null;

        uint hash = StableHash(seed, entityId);
        int index = (int)(hash % (uint)pool.Length);

        // Scan forward from the chosen index so a missing variant doesn't
        // fall back to the primitive — it just lands on the next existing one.
        for (int i = 0; i < pool.Length; i++)
        {
            int idx = (index + i) % pool.Length;
            var path = pool[idx];
            if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Picks a variant ref from the combined pool of an asset: plain
    /// <see cref="AssetDefinition.ScenePaths"/> plus any
    /// <see cref="MeshBundleCatalog"/> bundle entries targeting this asset.
    /// Returns null when neither source yields a usable variant.
    /// </summary>
    public static ResolvedMeshRef? PickRef(AssetDefinition asset, int seed, string entityId)
    {
        if (asset == null) return null;

        var bundleVariants = MeshBundleResolver.BuildVariants(asset.Id);
        var plain = asset.ScenePaths ?? System.Array.Empty<string>();

        int total = plain.Length + bundleVariants.Length;
        if (total == 0) return null;

        uint hash = StableHash(seed, entityId);
        int start = (int)(hash % (uint)total);

        for (int i = 0; i < total; i++)
        {
            int idx = (start + i) % total;

            if (idx < plain.Length)
            {
                var path = plain[idx];
                if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
                    return new ResolvedMeshRef(path, null, 1f);
            }
            else
            {
                var v = bundleVariants[idx - plain.Length];
                if (string.IsNullOrEmpty(v.ScenePath)) continue;

                // Prozedurale Bundles existieren nicht auf Disk; sie werden
                // erst beim MultiMesh-Upload aus der Registry aufgelöst.
                if (ProceduralMeshRegistry.IsProceduralPath(v.ScenePath) ||
                    ResourceLoader.Exists(v.ScenePath))
                {
                    return v;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Deterministic yaw in radians (0..2π) derived from the same key space.
    /// </summary>
    public static float PickYaw(int seed, string entityId)
    {
        uint hash = StableHash(seed ^ unchecked((int)0x9E3779B1), entityId);
        return (hash / (float)uint.MaxValue) * Mathf.Tau;
    }

    /// <summary>FNV-1a 32-bit hash combining seed and a string, stable across runs.</summary>
    private static uint StableHash(int seed, string key)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            foreach (char c in key)
                h = (h ^ c) * 16777619u;
            return h;
        }
    }
}
