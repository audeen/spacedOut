shader_type spatial;
render_mode unshaded, depth_draw_opaque, shadows_disabled, cull_disabled, fog_disabled;

// ─────────────────────────────────────────────────────────────────────────────
// planet_billboard.gdshader
//
// Applied to a unit QuadMesh that is parented to SpaceBackground (which
// follows the active camera). The quad is oriented so its local +Z points at
// the camera – the fragment shader rebuilds a sphere normal from the UV disc
// and lights it with a distant sun. The result looks like a crisp planet in
// the infinite sky.
// ─────────────────────────────────────────────────────────────────────────────

uniform vec3  surface_color_a   : source_color = vec3(0.55, 0.42, 0.30);
uniform vec3  surface_color_b   : source_color = vec3(0.28, 0.20, 0.15);
uniform vec3  atmosphere_color  : source_color = vec3(0.45, 0.65, 1.00);
uniform float atmosphere_strength = 0.6;
uniform vec3  sun_direction_world = vec3(0.4, 0.5, -0.8);
uniform float terminator_softness = 0.25;
uniform float ambient = 0.04;
uniform float detail_seed = 0.0;
uniform float detail_scale = 2.2;
uniform float detail_contrast = 1.6;

// ── Noise helpers (compact clone of the sky shader's) ──
float hash31(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

float gnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = hash31(i + vec3(0.0, 0.0, 0.0));
	float b = hash31(i + vec3(1.0, 0.0, 0.0));
	float c = hash31(i + vec3(0.0, 1.0, 0.0));
	float d = hash31(i + vec3(1.0, 1.0, 0.0));
	float e = hash31(i + vec3(0.0, 0.0, 1.0));
	float g = hash31(i + vec3(1.0, 0.0, 1.0));
	float h = hash31(i + vec3(0.0, 1.0, 1.0));
	float j = hash31(i + vec3(1.0, 1.0, 1.0));
	return mix(
		mix(mix(a, b, u.x), mix(c, d, u.x), u.y),
		mix(mix(e, g, u.x), mix(h, j, u.x), u.y),
		u.z);
}

// Unrolled 3-octave fBm (was 4 octaves with a runtime loop). Saves one gnoise
// evaluation per call and lets the driver fold the constants reliably.
float fbm3(vec3 p) {
	return 0.50  * gnoise(p)
	     + 0.25  * gnoise(p * 2.0)
	     + 0.125 * gnoise(p * 4.0);
}

void fragment() {
	vec2 cuv = (UV - vec2(0.5)) * 2.0;      // -1..1 across the quad
	float r2 = dot(cuv, cuv);
	if (r2 > 1.0) {
		discard;
	}

	float z = sqrt(1.0 - r2);
	vec3 n_local = vec3(cuv.x, cuv.y, z);   // quad-local sphere normal
	vec3 n_world = normalize((MODEL_MATRIX * vec4(n_local, 0.0)).xyz);

	// Surface pattern on the sphere. The previous version called fbm3() twice
	// for a "detail" layer; one call is enough at planet scale.
	vec3 sp = n_local * detail_scale + vec3(detail_seed);
	float band = fbm3(sp);
	float mixv = smoothstep(0.2, 0.8, pow(band, detail_contrast));
	vec3 surface = mix(surface_color_b, surface_color_a, mixv);

	// Lambertian lighting with soft terminator
	vec3 sun = normalize(sun_direction_world);
	float ndotl = dot(n_world, sun);
	float light = smoothstep(-terminator_softness, terminator_softness, ndotl);
	vec3 lit = surface * (ambient + (1.0 - ambient) * light);

	// Atmospheric rim glow on the lit limb
	float rim = pow(1.0 - z, 4.0) * atmosphere_strength;
	lit += atmosphere_color * rim * max(light, 0.15);

	ALBEDO = lit;
	ALPHA = 1.0;
}
