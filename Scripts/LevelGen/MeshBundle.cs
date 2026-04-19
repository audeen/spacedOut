using System;

namespace SpacedOut.LevelGen;

/// <summary>
/// Configuration for a GLB/scene file that bundles multiple mesh variants
/// (e.g. a single .glb holding 100 asteroid children). A bundle can feed
/// multiple <see cref="AssetDefinition"/> pools at once — every pool sees
/// the same set of children (shared pool), but applies its own per-pool
/// <see cref="MeshBundlePoolAssignment.VisualScale"/> on top of the
/// asset's base <c>VisualScale</c>.
/// </summary>
public class MeshBundle
{
    /// <summary>Stable identifier (debug / logging only).</summary>
    public string Id { get; init; } = "";

    /// <summary>Resource path of the bundle scene (e.g. a .glb).</summary>
    public string ScenePath { get; init; } = "";

    /// <summary>Pools that should draw from this bundle's children.</summary>
    public MeshBundlePoolAssignment[] PoolAssignments { get; init; } =
        Array.Empty<MeshBundlePoolAssignment>();

    /// <summary>
    /// When non-empty, only top-level children whose name is listed here are
    /// used. Empty => all top-level <c>Node3D</c> children qualify.
    /// </summary>
    public string[] IncludeChildren { get; init; } = Array.Empty<string>();

    /// <summary>Names of top-level children to exclude from the pool.</summary>
    public string[] ExcludeChildren { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Mapping from a bundle to one <see cref="AssetDefinition"/> pool, with
/// a per-pool visual scale. The bundle's children appear in every assigned
/// pool; only the scale differs.
/// </summary>
public class MeshBundlePoolAssignment
{
    /// <summary>Target asset id in <see cref="AssetLibrary"/> (e.g. "asteroid_small").</summary>
    public string TargetAssetId { get; init; } = "";

    /// <summary>
    /// Multiplicative scale applied on top of <see cref="AssetDefinition.VisualScale"/>.
    /// Lets the same mesh appear as a small (0.25), medium (0.75) or large (1.8)
    /// asteroid depending on which pool picked it.
    /// </summary>
    public float VisualScale { get; init; } = 1f;
}

/// <summary>
/// A picked variant: either a whole scene (<see cref="ChildName"/> == null)
/// or a specific child node of a bundle scene.
/// </summary>
public readonly record struct ResolvedMeshRef(
    string ScenePath,
    string? ChildName,
    float VisualScale);
