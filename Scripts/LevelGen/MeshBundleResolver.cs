using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.LevelGen.Procedural;

namespace SpacedOut.LevelGen;

/// <summary>
/// Resolves <see cref="MeshBundle"/> configurations against actual GLB/scene
/// contents. Loads each bundle's <see cref="PackedScene"/> exactly once into
/// an offscreen template, enumerates its top-level <c>Node3D</c> children, and
/// hands out fresh duplicates of single children to the spawn pipeline.
///
/// Cached for the lifetime of the process; call <see cref="Clear"/> (not
/// currently wired) to force a reload after hot-swapping assets.
/// </summary>
public static class MeshBundleResolver
{
    private sealed class BundleCache
    {
        public Node3D? Template;
        public string[] ChildNames = Array.Empty<string>();
        public bool Loaded;
    }

    private static readonly Dictionary<string, BundleCache> CacheByScenePath = new();

    /// <summary>
    /// Builds the list of variants a given asset pool should see from all
    /// bundles that target it. Each bundle contributes one
    /// <see cref="ResolvedMeshRef"/> per included child — shared across every
    /// pool the bundle is assigned to, distinguished only by VisualScale.
    /// </summary>
    public static ResolvedMeshRef[] BuildVariants(string assetId)
    {
        if (string.IsNullOrEmpty(assetId))
            return Array.Empty<ResolvedMeshRef>();

        var result = new List<ResolvedMeshRef>();

        foreach (var bundle in MeshBundleCatalog.BundlesFor(assetId))
        {
            var cache = GetOrLoad(bundle);
            if (cache.ChildNames.Length == 0) continue;

            float scale = 1f;
            foreach (var p in bundle.PoolAssignments)
            {
                if (p.TargetAssetId == assetId)
                {
                    scale = p.VisualScale;
                    break;
                }
            }

            foreach (var name in cache.ChildNames)
                result.Add(new ResolvedMeshRef(bundle.ScenePath, name, scale));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Returns a fresh duplicate of the named child out of the cached bundle
    /// template. Sub-resources (meshes, materials) are shared by Godot's
    /// resource system, so repeated duplicates are cheap.
    ///
    /// The returned node's Transform is reset to identity: inside the bundle
    /// the children may sit at arbitrary positions (e.g. spread across the
    /// original pack), but when used as a pool variant the spawning code
    /// places them via their parent. Keeping the template offset would
    /// teleport the mesh away from the spawn point.
    ///
    /// Returns null if the bundle or child is missing.
    /// </summary>
    public static Node3D? DuplicateChild(string bundleScenePath, string childName)
    {
        if (string.IsNullOrEmpty(bundleScenePath) || string.IsNullOrEmpty(childName))
            return null;

        if (!CacheByScenePath.TryGetValue(bundleScenePath, out var cache) || cache.Template == null)
            return null;

        var original = cache.Template.GetNodeOrNull<Node3D>(childName);
        if (original == null) return null;

        if (original.Duplicate() is not Node3D dup) return null;

        dup.Transform = Transform3D.Identity;
        dup.Visible = true;
        return dup;
    }

    /// <summary>Drops all cached templates. Safe to call; next access reloads.</summary>
    public static void Clear()
    {
        foreach (var cache in CacheByScenePath.Values)
        {
            if (cache.Template != null && GodotObject.IsInstanceValid(cache.Template))
                cache.Template.QueueFree();
        }
        CacheByScenePath.Clear();
    }

    // ── internals ───────────────────────────────────────────────────

    private static BundleCache GetOrLoad(MeshBundle bundle)
    {
        if (CacheByScenePath.TryGetValue(bundle.ScenePath, out var cached))
            return cached;

        var cache = new BundleCache();
        CacheByScenePath[bundle.ScenePath] = cache;

        // Prozedurale Bundles (z.B. "proc://asteroid") werden nicht von
        // ResourceLoader abgebildet — ihre Kinder kommen aus der
        // ProceduralMeshRegistry. Wir füllen den Cache synthetisch.
        if (ProceduralMeshRegistry.IsProceduralPath(bundle.ScenePath))
        {
            cache.Template = null;
            cache.ChildNames = FilterChildNames(
                ProceduralMeshRegistry.GetChildNames(bundle.ScenePath),
                bundle);
            cache.Loaded = true;

            GD.Print($"[MeshBundleResolver] procedural bundle '{bundle.Id}' → " +
                     $"{cache.ChildNames.Length} variant(s).");
            return cache;
        }

        if (!ResourceLoader.Exists(bundle.ScenePath))
        {
            GD.PushWarning($"[MeshBundleResolver] bundle scene missing: {bundle.ScenePath}");
            cache.Loaded = true;
            return cache;
        }

        var packed = GD.Load<PackedScene>(bundle.ScenePath);
        if (packed == null)
        {
            GD.PushWarning($"[MeshBundleResolver] failed to load PackedScene: {bundle.ScenePath}");
            cache.Loaded = true;
            return cache;
        }

        var template = packed.Instantiate<Node3D>();
        if (template == null)
        {
            GD.PushWarning($"[MeshBundleResolver] bundle root is not Node3D: {bundle.ScenePath}");
            cache.Loaded = true;
            return cache;
        }

        cache.Template = template;
        cache.ChildNames = EnumerateChildren(template, bundle);
        cache.Loaded = true;

        GD.Print($"[MeshBundleResolver] loaded '{bundle.Id}' from {bundle.ScenePath} " +
                 $"→ {cache.ChildNames.Length} variant(s).");

        return cache;
    }

    private static string[] EnumerateChildren(Node3D template, MeshBundle bundle)
    {
        var names = new List<string>();
        foreach (var child in template.GetChildren())
        {
            if (child is not Node3D node3D) continue;
            names.Add(node3D.Name.ToString());
        }

        return FilterChildNames(names, bundle);
    }

    private static string[] FilterChildNames(IEnumerable<string> source, MeshBundle bundle)
    {
        var includeSet = bundle.IncludeChildren.Length > 0
            ? new HashSet<string>(bundle.IncludeChildren)
            : null;
        var excludeSet = bundle.ExcludeChildren.Length > 0
            ? new HashSet<string>(bundle.ExcludeChildren)
            : null;

        var filtered = new List<string>();
        foreach (var name in source)
        {
            if (includeSet != null && !includeSet.Contains(name)) continue;
            if (excludeSet != null && excludeSet.Contains(name)) continue;
            filtered.Add(name);
        }

        filtered.Sort(StringComparer.Ordinal);
        return filtered.ToArray();
    }
}
