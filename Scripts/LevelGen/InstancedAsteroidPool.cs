using System.Collections.Generic;
using Godot;
using SpacedOut.LevelGen.Procedural;

namespace SpacedOut.LevelGen;

/// <summary>
/// GPU-instanced renderer for asteroid bundle variants. For every
/// <c>(scenePath, childName)</c> combination we spawn one
/// <see cref="MultiMeshInstance3D"/> per sub-<c>MeshInstance3D</c> found inside
/// the bundle child and push per-instance <see cref="Transform3D"/>s into it.
///
/// This collapses hundreds of individual asteroid nodes into a handful of
/// draw calls – one per unique asteroid mesh variant rather than per spawn.
///
/// Usage flow (per sector build):
///   1. <see cref="Reset"/> to drop the previous sector's instances.
///   2. <see cref="TryAddInstance"/> once per asteroid to queue a transform.
///   3. <see cref="Commit"/> after all asteroids are queued to upload the
///      transforms to the GPU in one pass.
/// </summary>
public partial class InstancedAsteroidPool : Node3D
{
    private sealed class SubMeshSlot
    {
        public Mesh Mesh = null!;
        public Transform3D LocalOffset = Transform3D.Identity;
        public GeometryInstance3D.ShadowCastingSetting CastShadow =
            GeometryInstance3D.ShadowCastingSetting.On;
        public Material? MaterialOverride;
        public MultiMeshInstance3D Node = null!;
        public MultiMesh MultiMesh = null!;
    }

    private sealed class KeyEntry
    {
        public SubMeshSlot[] SubMeshes = System.Array.Empty<SubMeshSlot>();
        public readonly List<Transform3D> Transforms = new();
    }

    private readonly Dictionary<string, KeyEntry> _entries = new();

    public override void _Ready()
    {
        if (string.IsNullOrEmpty(Name))
            Name = "InstancedAsteroids";
    }

    /// <summary>
    /// Queues a world-space transform for a bundle variant. Returns false when
    /// the bundle / child is unknown or contains no mesh data, letting the
    /// caller fall back to a single-node spawn.
    /// </summary>
    public bool TryAddInstance(string scenePath, string childName, Transform3D worldTransform)
    {
        if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(childName))
            return false;

        var entry = GetOrCreateEntry(scenePath, childName);
        if (entry == null || entry.SubMeshes.Length == 0) return false;

        entry.Transforms.Add(worldTransform);
        return true;
    }

    /// <summary>
    /// Uploads the queued transforms to every MultiMesh. Call after all
    /// <see cref="TryAddInstance"/> calls for the sector build.
    /// </summary>
    public void Commit()
    {
        foreach (var entry in _entries.Values)
            Upload(entry);
    }

    /// <summary>
    /// Drops all queued transforms and zeroes out every MultiMesh instance
    /// count. The cached meshes and MultiMeshInstance3D nodes stay alive so
    /// subsequent sector builds avoid re-loading bundles.
    /// </summary>
    public void Reset()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Transforms.Clear();
            foreach (var slot in entry.SubMeshes)
            {
                if (slot.MultiMesh != null)
                    slot.MultiMesh.InstanceCount = 0;
            }
        }
    }

    private void Upload(KeyEntry entry)
    {
        int count = entry.Transforms.Count;
        foreach (var slot in entry.SubMeshes)
        {
            slot.MultiMesh.InstanceCount = count;
            for (int i = 0; i < count; i++)
            {
                slot.MultiMesh.SetInstanceTransform(i, entry.Transforms[i] * slot.LocalOffset);
            }
        }
    }

    private KeyEntry? GetOrCreateEntry(string scenePath, string childName)
    {
        string key = $"{scenePath}#{childName}";
        if (_entries.TryGetValue(key, out var existing)) return existing;

        var entry = new KeyEntry();
        _entries[key] = entry;

        var slots = new List<SubMeshSlot>();

        if (ProceduralMeshRegistry.IsProceduralPath(scenePath))
        {
            // Prozedurale Varianten haben genau ein Sub-Mesh und teilen sich
            // ein ShaderMaterial aus der Registry — kein Scene-Duplizieren.
            var mesh = ProceduralMeshRegistry.GetMesh(scenePath, childName);
            if (mesh == null)
            {
                GD.PushWarning($"[InstancedAsteroidPool] Procedural mesh fehlt: {key}");
                return entry;
            }

            slots.Add(new SubMeshSlot
            {
                Mesh = mesh,
                LocalOffset = Transform3D.Identity,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                MaterialOverride = ProceduralMeshRegistry.GetMaterial(scenePath),
            });
        }
        else
        {
            var template = MeshBundleResolver.DuplicateChild(scenePath, childName);
            if (template == null)
            {
                GD.PushWarning($"[InstancedAsteroidPool] Konnte Bundle-Kind nicht laden: {key}");
                return entry;
            }

            CollectMeshInstances(template, Transform3D.Identity, slots);
            template.QueueFree();
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.MultiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = slot.Mesh,
                UseCustomData = false,
                UseColors = false,
            };
            slot.Node = new MultiMeshInstance3D
            {
                Multimesh = slot.MultiMesh,
                Name = $"MM_{childName}_{i}",
                CastShadow = slot.CastShadow,
                MaterialOverride = slot.MaterialOverride,

                // Godot 4 wendet LOD + VisibilityRange pro MultiMeshInstance an,
                // nicht pro Instanz. Da unsere Asteroiden über den ganzen
                // Sektor verteilt sind, würde ein globaler LOD-Wechsel alle
                // Steine gleichzeitig auf grobe Stufen zwingen. Laut Godot-
                // Doku hält ein großer LodBias die höchste Detailstufe auch
                // auf Distanz (0 = niedrigste, 1 = Standard, größer = mehr
                // Detail). 128 sperrt effektiv auf LOD 0; VisibilityRange-
                // End = 0 deaktiviert das Distance-Fading komplett.
                LodBias = 128f,
                VisibilityRangeBegin = 0f,
                VisibilityRangeEnd = 0f,
            };
            AddChild(slot.Node);
        }

        entry.SubMeshes = slots.ToArray();
        return entry;
    }

    private static void CollectMeshInstances(Node3D node, Transform3D parentGlobal,
        List<SubMeshSlot> slots)
    {
        var global = parentGlobal * node.Transform;

        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            slots.Add(new SubMeshSlot
            {
                Mesh = mi.Mesh,
                LocalOffset = global,
                CastShadow = mi.CastShadow,
                MaterialOverride = mi.MaterialOverride,
            });
        }

        foreach (var child in node.GetChildren())
        {
            if (child is Node3D n3d)
                CollectMeshInstances(n3d, global, slots);
        }
    }
}
