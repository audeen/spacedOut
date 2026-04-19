using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;

namespace SpacedOut.Debug;

/// <summary>
/// Debug: alle NPC-Schiffe und je ein Exemplar jedes <see cref="AssetLibrary"/>-Eintrags
/// nebeneinander — GLBs/Mesh-Bundles wie im Level, sonst dieselben Platzhalter wie
/// <see cref="PlaceholderFactory"/>.
/// </summary>
public partial class ShipScaleCompare : Node3D
{
    private const float Spacing = 300f;
    private const int AssetSeed = 42;

    public override void _Ready()
    {
        Name = "ShipScaleCompare";
        var holders = new List<Node3D>();

        foreach (var def in AgentDefinition.GetAll()
                     .Where(kvp =>
                         kvp.Value.ScenePaths is { Length: > 0 } paths &&
                         paths.Any(p => !string.IsNullOrEmpty(p) && ResourceLoader.Exists(p)))
                     .OrderBy(kvp => kvp.Key)
                     .Select(kvp => kvp.Value))
        {
            string? path = def.ScenePaths.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p) && ResourceLoader.Exists(p));
            if (path == null) continue;

            var packed = GD.Load<PackedScene>(path);
            if (packed == null) continue;

            var instance = packed.Instantiate<Node3D>();
            float s = def.VisualScale;
            instance.Scale = new Vector3(s, s, s);

            var holder = new Node3D { Name = $"Ship_{def.Id}" };
            holder.AddChild(instance);
            holder.Rotation = new Vector3(0f, def.VisualYawOffsetRadians, 0f);

            holder.AddChild(CreateLabel(
                $"{def.DisplayName}\n{def.Id}\n[Agent] scale {def.VisualScale:0.###}",
                140f));

            holders.Add(holder);
        }

        foreach (var asset in AssetLibrary.GetAll())
        {
            string biome = asset.AllowedBiomes.Length > 0
                ? asset.AllowedBiomes[0]
                : "asteroid_field";
            float scale = (asset.MinScale + asset.MaxScale) * 0.5f;

            var spawned = PlaceholderFactory.Create(
                asset, scale, biome, $"cmp_{asset.Id}", AssetSeed);
            spawned.Name = $"Asset_{asset.Id}";

            var holder = new Node3D { Name = $"Asset_{asset.Id}" };
            holder.AddChild(spawned);

            string meshKind = spawned.IsPlaceholder ? "Platzhalter" : "Mesh";
            string meshHint = spawned.IsPlaceholder
                ? asset.Shape.ToString()
                : (spawned.RealScenePath ?? "—");

            holder.AddChild(CreateLabel(
                $"{asset.DisplayName}\n{asset.Id}\n[{meshKind}] {meshHint}\nscale {scale:0.###}",
                Mathf.Max(120f, spawned.ObjectRadius * 0.55f + 80f)));

            holders.Add(holder);
        }

        int n = holders.Count;
        if (n == 0) return;

        float startX = -(n - 1) * Spacing * 0.5f;
        for (int i = 0; i < n; i++)
        {
            holders[i].Position = new Vector3(startX + i * Spacing, 0f, 0f);
            AddChild(holders[i]);
        }
    }

    private static Label3D CreateLabel(string text, float y)
    {
        return new Label3D
        {
            Text = text,
            Position = new Vector3(0f, y, 0f),
            // Größe im 3D-Raum: PixelSize ist der Haupthebel (World-Einheiten pro Font-Pixel).
            PixelSize = 0.09f,
            FontSize = 96,
            OutlineSize = 18,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Width = 2200f,
        };
    }
}
