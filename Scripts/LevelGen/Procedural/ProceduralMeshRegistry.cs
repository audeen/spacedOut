using System;
using System.Collections.Generic;
using Godot;

namespace SpacedOut.LevelGen.Procedural;

/// <summary>
/// Zentraler Cache für prozedural erzeugte Meshes. Wird über Pseudo-Pfade
/// der Form <c>proc://asteroid/v0..v11</c> angesprochen. Die bestehende
/// Bundle-/MultiMesh-Pipeline identifiziert Varianten über
/// <c>(scenePath, childName)</c>; für prozedurale Assets nutzen wir
/// <c>scenePath = "proc://asteroid"</c> und <c>childName = "v&lt;index&gt;"</c>.
///
/// Lazy-Init beim ersten Zugriff. Thread-agnostisch — Godot's Spawn-Pipeline
/// läuft auf dem Hauptthread.
/// </summary>
public static class ProceduralMeshRegistry
{
    public const string SchemePrefix = "proc://";
    public const string AsteroidScenePath = "proc://asteroid";

    /// <summary>Anzahl Asteroid-Varianten, die das Bundle dem Pool anbietet.</summary>
    public const int AsteroidVariantCount = 12;

    private sealed class Entry
    {
        public ArrayMesh Mesh = null!;
    }

    private static readonly Dictionary<string, Entry> Entries = new();
    private static ShaderMaterial? _asteroidMaterial;
    private static bool _asteroidsInitialized;

    /// <summary>True, wenn <paramref name="scenePath"/> das prozedurale Schema nutzt.</summary>
    public static bool IsProceduralPath(string? scenePath) =>
        !string.IsNullOrEmpty(scenePath) && scenePath!.StartsWith(SchemePrefix);

    /// <summary>
    /// Liefert die Child-Namen (<c>v0..v11</c>) für ein prozedurales Bundle.
    /// Für unbekannte Scene-Paths leer.
    /// </summary>
    public static string[] GetChildNames(string scenePath)
    {
        EnsureInitialized(scenePath);

        if (scenePath == AsteroidScenePath)
        {
            var names = new string[AsteroidVariantCount];
            for (int i = 0; i < AsteroidVariantCount; i++)
                names[i] = $"v{i}";
            return names;
        }

        return Array.Empty<string>();
    }

    /// <summary>Mesh für Variant <c>v&lt;index&gt;</c>. Null, wenn Pfad/Kind unbekannt.</summary>
    public static ArrayMesh? GetMesh(string scenePath, string childName)
    {
        EnsureInitialized(scenePath);
        return Entries.TryGetValue(Key(scenePath, childName), out var e) ? e.Mesh : null;
    }

    /// <summary>Gemeinsames Shader-Material für alle Asteroid-Varianten.</summary>
    public static ShaderMaterial? GetMaterial(string scenePath)
    {
        EnsureInitialized(scenePath);
        return scenePath == AsteroidScenePath ? _asteroidMaterial : null;
    }

    // ── Init ────────────────────────────────────────────────────────

    private static void EnsureInitialized(string scenePath)
    {
        if (scenePath == AsteroidScenePath && !_asteroidsInitialized)
            InitializeAsteroids();
    }

    private static void InitializeAsteroids()
    {
        _asteroidsInitialized = true;

        var shader = GD.Load<Shader>("res://Assets/shaders/asteroid_rock.gdshader");
        if (shader == null)
        {
            GD.PushWarning("[ProceduralMeshRegistry] Shader 'asteroid_rock.gdshader' konnte nicht geladen werden.");
        }

        _asteroidMaterial = new ShaderMaterial { Shader = shader };

        int totalTris = 0;
        for (int i = 0; i < AsteroidVariantCount; i++)
        {
            var mesh = ProceduralAsteroidMeshFactory.Build(i);
            Entries[Key(AsteroidScenePath, $"v{i}")] = new Entry { Mesh = mesh };

            if (mesh.GetSurfaceCount() > 0)
            {
                var arrays = mesh.SurfaceGetArrays(0);
                if (arrays[(int)Mesh.ArrayType.Index].AsInt32Array() is { } idx)
                    totalTris += idx.Length / 3;
            }
        }

        GD.Print($"[ProceduralMeshRegistry] {AsteroidVariantCount} Asteroid-Varianten gebaut " +
                 $"({totalTris} Tris gesamt).");
    }

    private static string Key(string scenePath, string childName) => $"{scenePath}#{childName}";
}
