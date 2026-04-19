# Skyboxes

Curated sky assets that can replace the procedural `space_sky.gdshader` for
specific sectors (story beats, hub scenes, tutorial, etc.). The procedural sky
is seed-driven and handles random sectors on its own – this folder is only for
**hand-picked** sectors that should look *exactly* the same every run.

## How curated skies are wired

Each biome (or later: each specific sector) can set
`SkyboxProfile.OverrideSkyResourcePath` to a `.tres` file below this folder.
`SpaceSkyController.ApplySectorSky` loads it via
`GD.Load<Sky>(path)` and assigns it to `WorldEnvironment.Environment.Sky`.

Planet billboards are still spawned even with curated skies; leave
`PlanetCountMax = 0` on the biome's `SkyboxProfile` if the cubemap already
depicts planets.

## Supported resource formats

| Godot resource                        | Source texture             | Typical size      |
| ------------------------------------- | -------------------------- | ----------------- |
| `Sky` + `PanoramaSkyMaterial`         | Equirectangular HDR (EXR)  | 4096 × 2048       |
| `Sky` + `PanoramaSkyMaterial`         | Equirectangular JPG / PNG  | 4096 × 2048       |
| `Sky` + `ShaderMaterial` + `Cubemap`  | 6 face textures            | 6 × 1024 × 1024   |

### Import settings (Project → Import)

- **HDR (`.hdr`, `.exr`)**
  - Compress → *Disabled* or *VRAM Uncompressed* (HDR detail)
  - Mipmaps → *On*
  - sRGB → *Disabled* (source is linear)
- **LDR (`.png`, `.jpg`)**
  - Compress → *VRAM Compressed*
  - Mipmaps → *On*
  - sRGB → *Enabled* (most space textures are authored in sRGB)
- **Cubemap faces**
  - Import 6 textures, create a `Cubemap` resource and assign the 6 faces in
    order `+X, -X, +Y, -Y, +Z, -Z`. Face orientation follows the Godot
    convention (see [Godot cubemap docs]).

## Sources

1. **Self-generated procedural (preferred for variety)**
   - Go to <https://tools.wwwtyro.net/space-3d/> – generate a sector, click
     *Save*, choose *Cubemap*, and download all six faces.
   - Alternatively click *Panoramic* to get one equirectangular image.
   - License: [CC BY-SA 3.0](https://github.com/wwwtyro/space-3d) – credit
     *Rye Terrell / space-3d* in-game credits if you ship them verbatim.

2. **Spacescape** (desktop tool, Windows/Linux)
   - <http://alexcpeterson.com/spacescape/>
   - Great for artisanal control (layered star fields, billboards, noise
     nebulae). Exports 6 cube faces directly as PNG.
   - License: MIT – attribution optional, always welcome.

3. **NASA Deep Star Maps / ESO**
   - <https://svs.gsfc.nasa.gov/4851> – 8k/16k/32k equirectangular Milky-Way
     panoramas in both visible and infrared.
   - <https://www.eso.org/public/images/eso0932a/> – ESO gigapixel Milky Way.
   - License: public domain / CC BY 4.0 (check per-image).

4. **Blender**
   - Model a scene with emission shaders + a big sphere camera → render
     *Equirectangular* to EXR. Good for hand-tuned story skies.

## Suggested folder layout

```
Assets/skyboxes/
├── README.md                  (this file)
├── hub_dockyard.tres          (Sky resource; wired from the hub biome)
├── hub_dockyard.exr           (source texture)
├── boss_finale/
│   ├── boss_finale.tres
│   ├── px.png ... nz.png      (six cube faces)
│   └── cubemap.tres
```

Keep the source texture and the Godot `.tres` in the same directory so
designers can find the matching raw asset.

[Godot cubemap docs]: https://docs.godotengine.org/en/stable/classes/class_cubemap.html
