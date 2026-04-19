shader_type sky;

// ─────────────────────────────────────────────────────────────────────────────
// space_sky.gdshader
//
// Fully procedural space skybox inspired by wwwtyro/space-3d.
// Every feature is seed-driven so the same SectorData.Seed reproduces the same
// backdrop. Uniforms are set at runtime by Scripts/Sky/SpaceSkyController.cs.
//
// Layers (in draw order):
//   1) Nebula   – fBm with light domain warp, two-tone tint from biome palette.
//   2) Galaxy   – great-circle band around a seeded normal (early-out at 0).
//   3) Stars    – 2 voronoi-style layers + cheap twinkle.
//   4) Suns     – up to 3 directional glares with halo.
//
// Performance notes:
//   * Sky is set to Quality mode in SpaceSkyController so the cubemap is baked
//     exactly once per sector change; normal frames cost one cubemap fetch.
//   * Shader must be fully static (no TIME usage) or Godot will keep re-baking.
//     "Twinkle" here is therefore a per-star brightness jitter, not animated.
//   * hash33() is sinless to avoid 81+ sin() calls per pixel.
//   * fbm is unrolled into fbm2..fbm5 so drivers don't trip over the dynamic
//     octave-loop inside.
// ─────────────────────────────────────────────────────────────────────────────

uniform float seed = 0.0;

// ── Stars ──
uniform float star_density    = 1.0;
uniform float star_brightness = 1.0;
uniform float star_twinkle    = 0.15;

// ── Nebula ──
uniform vec3  nebula_color_a : source_color = vec3(0.55, 0.25, 0.75);
uniform vec3  nebula_color_b : source_color = vec3(0.12, 0.28, 0.65);
uniform float nebula_intensity = 1.0;
uniform float nebula_scale     = 1.5;
uniform float nebula_contrast  = 2.0;

// ── Galaxy band ──
uniform vec3  galaxy_normal    = vec3(0.2, 0.95, 0.25);
uniform vec3  galaxy_color : source_color = vec3(0.72, 0.78, 1.0);
uniform float galaxy_intensity = 0.6;
uniform float galaxy_width     = 0.16;

// ── Suns (intensity 0 disables the sun) ──
uniform vec3  sun1_direction = vec3(0.3, 0.2, -0.9);
uniform vec3  sun1_color : source_color = vec3(1.0, 0.88, 0.62);
uniform float sun1_size      = 0.015;
uniform float sun1_intensity = 0.0;

uniform vec3  sun2_direction = vec3(-0.5, 0.4, 0.7);
uniform vec3  sun2_color : source_color = vec3(0.55, 0.7, 1.0);
uniform float sun2_size      = 0.012;
uniform float sun2_intensity = 0.0;

uniform vec3  sun3_direction = vec3(0.8, -0.2, 0.5);
uniform vec3  sun3_color : source_color = vec3(1.0, 0.45, 0.3);
uniform float sun3_size      = 0.018;
uniform float sun3_intensity = 0.0;

// ─────────────────────────────────────────────────────────────────────────────
// Hash / noise helpers
// ─────────────────────────────────────────────────────────────────────────────

float hash31(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

// Sinless 3D->3D hash. Avoids per-pixel sin() in star_layer.
vec3 hash33(vec3 p) {
	p = fract(p * vec3(443.897, 441.423, 0.0973));
	p += dot(p, p.yxz + 19.19);
	return fract((p.xxy + p.yxx) * p.zyx);
}

float gnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);

	float n000 = hash31(i + vec3(0.0, 0.0, 0.0));
	float n100 = hash31(i + vec3(1.0, 0.0, 0.0));
	float n010 = hash31(i + vec3(0.0, 1.0, 0.0));
	float n110 = hash31(i + vec3(1.0, 1.0, 0.0));
	float n001 = hash31(i + vec3(0.0, 0.0, 1.0));
	float n101 = hash31(i + vec3(1.0, 0.0, 1.0));
	float n011 = hash31(i + vec3(0.0, 1.0, 1.0));
	float n111 = hash31(i + vec3(1.0, 1.0, 1.0));

	return mix(
		mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y),
		mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y),
		u.z);
}

// Unrolled fBm variants. Drivers reliably collapse these vs. a dynamic-octave
// loop with a runtime break.
float fbm2(vec3 p) {
	return 0.50 * gnoise(p) + 0.25 * gnoise(p * 2.0);
}

float fbm3(vec3 p) {
	return 0.50 * gnoise(p)
	     + 0.25 * gnoise(p * 2.0)
	     + 0.125 * gnoise(p * 4.0);
}

float fbm4(vec3 p) {
	return 0.500  * gnoise(p)
	     + 0.250  * gnoise(p * 2.0)
	     + 0.125  * gnoise(p * 4.0)
	     + 0.0625 * gnoise(p * 8.0);
}

float fbm5(vec3 p) {
	return 0.50000 * gnoise(p)
	     + 0.25000 * gnoise(p * 2.0)
	     + 0.12500 * gnoise(p * 4.0)
	     + 0.06250 * gnoise(p * 8.0)
	     + 0.03125 * gnoise(p * 16.0);
}

// ─────────────────────────────────────────────────────────────────────────────
// Star layer
// Sparsely distributes stars over a 3D cell grid on the unit sphere.
// `threshold` (0..1) is the probability that any given cell produces a star.
// ─────────────────────────────────────────────────────────────────────────────
float star_layer(vec3 dir, float cell_size, float threshold, float seed_offset) {
	vec3 p = dir * cell_size;
	vec3 id = floor(p);
	vec3 f = fract(p);
	float nearest = 1.0;
	vec3  hit_hash = vec3(0.0);

	for (int x = -1; x <= 1; x++) {
		for (int y = -1; y <= 1; y++) {
			for (int z = -1; z <= 1; z++) {
				vec3 n = vec3(float(x), float(y), float(z));
				vec3 h = hash33(id + n + vec3(seed_offset));
				vec3 cp = n + h;
				float d = length(cp - f);
				if (d < nearest) {
					nearest = d;
					hit_hash = h;
				}
			}
		}
	}
	if (hit_hash.x > threshold) return 0.0;
	float radius = 0.04 + hit_hash.y * 0.06;
	float core = 1.0 - smoothstep(0.0, radius, nearest);
	return core * (0.3 + hit_hash.z * 0.7);
}

vec3 stars(vec3 dir) {
	// Two layers cover dense + medium star sizes; the third "tiny" layer used
	// to add 27 hash33 calls per pixel for very subtle dots and is dropped.
	float s = 0.0;
	s += star_layer(dir,  80.0, 0.08, seed +  1.0) * 1.00;
	s += star_layer(dir, 160.0, 0.05, seed +  7.0) * 0.70;

	// Static per-direction brightness jitter. Replaces the old sin(TIME) twinkle
	// so the sky shader has no time dependency and Godot's Quality cubemap bake
	// stays cached until the next sector change.
	float jitter = hash31(dir * 120.0 + seed);
	float tw = 1.0 - star_twinkle + star_twinkle * jitter;

	return vec3(s * star_brightness * star_density * tw);
}

// ─────────────────────────────────────────────────────────────────────────────
// Nebula (fBm with light 2D domain warp, two-tone)
// ─────────────────────────────────────────────────────────────────────────────
vec3 nebula(vec3 dir) {
	if (nebula_intensity <= 0.0) return vec3(0.0);

	vec3 p = dir * nebula_scale + vec3(seed * 0.17);
	// 2D warp on XY only is roughly as visually convincing as the previous 3D
	// warp but uses 2 fbm2() instead of 3 fbm3() (saves 7 gnoise per pixel).
	vec2 warp = vec2(fbm2(p + 0.0), fbm2(p + 5.1));
	p.xy += (warp - 0.5) * 1.2;

	float n = fbm4(p);
	n = smoothstep(0.30, 0.95, n);
	n = pow(n, nebula_contrast);

	// Tone mix is driven directly by the main noise, eliminating a third fBm.
	vec3 color = mix(nebula_color_a, nebula_color_b, smoothstep(0.2, 0.8, n));
	return color * n * nebula_intensity;
}

// ─────────────────────────────────────────────────────────────────────────────
// Galaxy band
// ─────────────────────────────────────────────────────────────────────────────
vec3 galaxy(vec3 dir) {
	// Hard short-circuit: when galaxy is disabled (current biome default), we
	// skip 8 fbm octaves entirely instead of multiplying by 0 at the end.
	if (galaxy_intensity <= 0.0) return vec3(0.0);

	vec3 n = normalize(galaxy_normal);
	float along = abs(dot(dir, n));
	float band_mask = 1.0 - smoothstep(0.0, galaxy_width, along);
	if (band_mask <= 0.0) return vec3(0.0);

	vec3 p = dir * 3.0 + vec3(seed * 0.11);
	float dust = fbm3(p * 2.0 + 10.0);
	float glow = fbm4(p + 2.7);

	float intensity = band_mask * mix(0.25, 1.0, glow) * (1.0 - smoothstep(0.45, 0.7, dust) * 0.6);
	return galaxy_color * intensity * galaxy_intensity;
}

// ─────────────────────────────────────────────────────────────────────────────
// Sun
// ─────────────────────────────────────────────────────────────────────────────
vec3 sun_at(vec3 dir, vec3 sun_dir, vec3 sun_color, float sun_size, float intensity) {
	if (intensity <= 0.001) return vec3(0.0);
	vec3 sd = normalize(sun_dir);
	float angular = 1.0 - clamp(dot(dir, sd), -1.0, 1.0);
	float core = exp(-angular / max(sun_size, 0.0005));
	float halo = exp(-angular / (max(sun_size, 0.0005) * 18.0)) * 0.35;
	return sun_color * (core + halo) * intensity;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────────────────────
void sky() {
	vec3 dir = normalize(EYEDIR);

	vec3 col = vec3(0.0);
	col += nebula(dir);
	col += galaxy(dir);
	col += stars(dir);
	col += sun_at(dir, sun1_direction, sun1_color, sun1_size, sun1_intensity);
	col += sun_at(dir, sun2_direction, sun2_color, sun2_size, sun2_intensity);
	col += sun_at(dir, sun3_direction, sun3_color, sun3_size, sun3_intensity);

	COLOR = col;
}
