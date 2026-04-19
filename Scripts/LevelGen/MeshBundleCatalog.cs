using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.LevelGen;

/// <summary>
/// Static registry of <see cref="MeshBundle"/> configurations. Bundles let a
/// single GLB feed multiple <see cref="AssetLibrary"/> pools — same children,
/// different per-pool visual scale. Extend <see cref="Bundles"/> to cover new
/// biomes (debris, wrecks, structures, ...).
/// </summary>
public static class MeshBundleCatalog
{
    private static readonly List<MeshBundle> Bundles = new()
    {
        // ── Asteroid field: GLB-Pack (aktiv) ──
        new MeshBundle
        {
            Id = "asteroid_field_basic",
            ScenePath = "res://Assets/models/asteroids/asteroid_pack_01/asteroid_pack_01.glb",
            PoolAssignments = new[]
            {
                new MeshBundlePoolAssignment { TargetAssetId = "asteroid_small",  VisualScale = 7.5f  },
                new MeshBundlePoolAssignment { TargetAssetId = "asteroid_medium", VisualScale = 22.5f },
                new MeshBundlePoolAssignment { TargetAssetId = "asteroid_large",  VisualScale = 70f   },
            },
        },

        // ── Deaktiviert: prozedurale Icosphere-Varianten (proc://asteroid) ──
        // Code bleibt (ProceduralMeshRegistry, Resolver, Pool-Hook). Wieder
        // aktivieren: diesen Block einkommentieren und asteroid_field_basic
        // oben auskommentieren.
        // new MeshBundle
        // {
        //     Id = "asteroid_field_procedural",
        //     ScenePath = "proc://asteroid",
        //     PoolAssignments = new[]
        //     {
        //         new MeshBundlePoolAssignment { TargetAssetId = "asteroid_small",  VisualScale = 7.5f  },
        //         new MeshBundlePoolAssignment { TargetAssetId = "asteroid_medium", VisualScale = 22.5f },
        //         new MeshBundlePoolAssignment { TargetAssetId = "asteroid_large",  VisualScale = 70f   },
        //     },
        // },

        // Future: debris_pack_01, wreck_pack_01, station_module_pack_01, …
    };

    /// <summary>All registered bundles.</summary>
    public static IReadOnlyList<MeshBundle> All => Bundles;

    /// <summary>
    /// Bundles that expose their children to the given asset pool. A bundle
    /// may appear in multiple pools (shared children, different VisualScale).
    /// </summary>
    public static IEnumerable<MeshBundle> BundlesFor(string assetId)
    {
        if (string.IsNullOrEmpty(assetId)) yield break;

        foreach (var b in Bundles)
        {
            if (b.PoolAssignments.Any(p => p.TargetAssetId == assetId))
                yield return b;
        }
    }
}
