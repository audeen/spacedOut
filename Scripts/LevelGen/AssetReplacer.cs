using Godot;

namespace SpacedOut.LevelGen;

/// <summary>
/// Walks the GeneratedLevel subtree and swaps placeholders for real
/// PackedScenes wherever ScenePath is set and the resource exists.
/// </summary>
public static class AssetReplacer
{
    public static int ReplaceAll(Node3D levelContainer)
    {
        int replaced = 0;

        foreach (var child in levelContainer.GetChildren())
        {
            if (child is not SpawnedObject obj) continue;
            if (!obj.IsPlaceholder) continue;
            if (string.IsNullOrEmpty(obj.RealScenePath)) continue;
            if (!ResourceLoader.Exists(obj.RealScenePath)) continue;

            var scene = GD.Load<PackedScene>(obj.RealScenePath);
            if (scene == null) continue;

            var instance = scene.Instantiate<Node3D>();

            // Remove every visual child (placeholder mesh + glow light)
            foreach (var visual in obj.GetChildren())
                visual.QueueFree();

            obj.AddChild(instance);
            obj.IsPlaceholder = false;
            replaced++;
        }

        GD.Print($"[AssetReplacer] {replaced} Platzhalter ersetzt.");
        return replaced;
    }

    /// <summary>
    /// Replace a single object by its <see cref="SpawnedObject.AssetId"/>.
    /// </summary>
    public static bool ReplaceSingle(Node3D levelContainer, string assetId, string scenePath)
    {
        foreach (var child in levelContainer.GetChildren())
        {
            if (child is not SpawnedObject obj) continue;
            if (obj.AssetId != assetId || !obj.IsPlaceholder) continue;
            if (!ResourceLoader.Exists(scenePath)) return false;

            var scene = GD.Load<PackedScene>(scenePath);
            if (scene == null) return false;

            foreach (var visual in obj.GetChildren())
                visual.QueueFree();

            obj.AddChild(scene.Instantiate<Node3D>());
            obj.IsPlaceholder = false;
            return true;
        }

        return false;
    }
}
