using System;
using Godot;

namespace SpacedOut.Sky;

/// <summary>
/// Palette + intensity knobs for a procedural sector skybox.
/// Lives on <see cref="LevelGen.BiomeDefinition"/> and is consumed by
/// <see cref="SpaceSkyController"/> to drive the space_sky shader uniforms.
/// </summary>
public class SkyboxProfile
{
    // ── Nebula ───────────────────────────────────────────────────────
    public Color NebulaColorA   { get; init; } = new(0.55f, 0.25f, 0.75f);
    public Color NebulaColorB   { get; init; } = new(0.12f, 0.28f, 0.65f);
    public float NebulaIntensity { get; init; } = 1.0f;
    public float NebulaScale    { get; init; } = 1.5f;
    public float NebulaContrast { get; init; } = 2.0f;

    // ── Stars ────────────────────────────────────────────────────────
    public float StarDensity    { get; init; } = 1.0f;
    public float StarBrightness { get; init; } = 1.0f;
    public float StarTwinkle    { get; init; } = 0.15f;

    // ── Galaxy band ──────────────────────────────────────────────────
    public Color GalaxyColor    { get; init; } = new(0.72f, 0.78f, 1.0f);
    public float GalaxyIntensity { get; init; } = 0.6f;
    public float GalaxyWidth    { get; init; } = 0.16f;

    // ── Suns (0‒3) ───────────────────────────────────────────────────
    public SunSpec[] Suns { get; init; } = Array.Empty<SunSpec>();

    // ── Planets ──────────────────────────────────────────────────────
    public int PlanetCountMin { get; init; } = 0;
    public int PlanetCountMax { get; init; } = 2;
    public PlanetPalette[] PlanetPalettes { get; init; } = Array.Empty<PlanetPalette>();

    /// <summary>
    /// Optional path to a hand-authored Sky resource (.tres with
    /// PanoramaSkyMaterial or a Cubemap-backed Sky). When non-null and the
    /// file exists, the controller uses it instead of the procedural shader.
    /// Planet billboards are still spawned alongside curated skies.
    /// </summary>
    public string? OverrideSkyResourcePath { get; init; }
}

public class SunSpec
{
    public Color Color     { get; init; } = new(1.0f, 0.88f, 0.62f);
    public float Size      { get; init; } = 0.015f;
    public float Intensity { get; init; } = 1.0f;
}

public class PlanetPalette
{
    public Color SurfaceA        { get; init; } = new(0.55f, 0.42f, 0.30f);
    public Color SurfaceB        { get; init; } = new(0.28f, 0.20f, 0.15f);
    public Color Atmosphere      { get; init; } = new(0.45f, 0.65f, 1.00f);
    public float AtmosphereStrength { get; init; } = 0.6f;
    public float AngularSizeMin  { get; init; } = 0.08f; // radians (~4.6°)
    public float AngularSizeMax  { get; init; } = 0.14f;   // (~8.0°)
}
