shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

// ─────────────────────────────────────────────────────────────────────────────
// asteroid_rock.gdshader
//
// PBR-Felsshader für prozedural erzeugte Asteroiden.
//
// Warum es NICHT mit der Kamera rotiert:
//   • v_local (Object-Space-Position) wird im Vertex-Shader eingefroren —
//     Noise-Lookup unabhängig von Kamera.
//   • v_world_normal (World-Space-Normale) wird im Vertex-Shader aus
//     MODEL_MATRIX * NORMAL berechnet — hemisphärischer Fill ist damit
//     ebenfalls kamera-unabhängig.
//   • Kein view-space Rim/Fresnel mehr; der gibt sonst einen hellen
//     Silhouettenstreifen, der immer "zur Kamera" zeigt und wie ein
//     mitdrehendes Gesicht wirkt.
//
// Vertex-Colors vom Mesh-Bake:
//   COLOR.r = Primary-Displacement-Höhe   → Cavity-AO
//   COLOR.g = Krater-Maske                → Kraterinnenflächen dunkler
//   COLOR.b = Detail-Displacement-Höhe    → Kamm-Highlights
// ─────────────────────────────────────────────────────────────────────────────

uniform vec3  color_deep      : source_color = vec3(0.06, 0.05, 0.04);
uniform vec3  color_low       : source_color = vec3(0.16, 0.13, 0.11);
uniform vec3  color_mid       : source_color = vec3(0.32, 0.28, 0.23);
uniform vec3  color_high      : source_color = vec3(0.58, 0.52, 0.44);
uniform float macro_scale     = 0.75;
uniform float meso_scale      = 2.2;
uniform float meso_weight     = 0.30;
uniform float color_contrast_low  = 0.30;
uniform float color_contrast_high = 0.78;
uniform float roughness_low   = 0.72;
uniform float roughness_high  = 0.98;
uniform float ao_strength     = 0.95;
uniform float crater_shade    = 0.65;
uniform float ridge_highlight = 0.45;

// ── Hemisphärischer Welt-Fill ───────────────────────────────────────
// Simuliert diffuses Umgebungslicht: "Sky"-Farbe auf der Schattenseite,
// "Ground"-Farbe auf der Sonnenseite, interpoliert über
// dot(world_normal, fill_up_direction). Komplett kamera-unabhängig —
// der helle Bereich bleibt an derselben WELT-Seite des Asteroiden,
// egal wie die Kamera steht.
//
// fill_up_direction ist default die ANTI-Sonne (Sonne in Main.tscn kommt
// aus basis.z ≈ (0.53, -0.56, 0.64); negiert = Schattenseiten-Richtung).
// So wird die Schattenseite mit Sky-Fill aufgehellt, während die
// Sonnenseite fast nichts extra bekommt und der Direktionslicht-Shading
// dort dominiert.
uniform vec3  fill_sky_color    : source_color = vec3(0.28, 0.22, 0.18);
uniform vec3  fill_ground_color : source_color = vec3(0.08, 0.06, 0.05);
uniform vec3  fill_up_direction = vec3(-0.53, 0.56, -0.64);
uniform float fill_strength     = 0.6;

varying vec3 v_local;
varying vec3 v_world_normal;

// ── Noise-Helfer ────────────────────────────────────────────────────
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

float fbm2(vec3 p) {
	return 0.70 * gnoise(p) + 0.30 * gnoise(p * 2.1);
}

void vertex() {
	// Object-Space-Position für Noise-Lookup.
	v_local = VERTEX;
	// World-Space-Normale für hemisphärischen Fill. Für uniforme Skalierung
	// ist MODEL_MATRIX * NORMAL korrekt; nicht-uniforme Skalen bräuchten
	// transpose(inverse(MODEL_MATRIX)), aber Asteroiden nutzen uniform Scale.
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	// Procedural-Mipmap: hochfrequente Oktave ausblenden, wenn sie unter
	// Nyquist rutscht (verhindert Alias-Flicker bei Kamerabewegung).
	float pixel_footprint = max(length(dFdx(v_local)), length(dFdy(v_local)));
	float macro_fade = 1.0 - smoothstep(0.6, 1.4, pixel_footprint * macro_scale);
	float meso_fade  = 1.0 - smoothstep(0.4, 1.1, pixel_footprint * meso_scale);

	float macro = mix(0.5, gnoise(v_local * macro_scale), macro_fade);
	float meso  = mix(0.5, fbm2(v_local * meso_scale),    meso_fade);

	float n = mix(macro, meso, meso_weight);
	float mixv = smoothstep(color_contrast_low, color_contrast_high, n);
	vec3 albedo = mix(color_low, color_mid, mixv);

	// Cavity aus Mesh-Bake.
	float cavity_t = (1.0 - COLOR.r) * ao_strength;
	albedo = mix(albedo, color_deep, cavity_t * 0.70);

	// Kraterinnen dunkler.
	albedo *= mix(1.0, 1.0 - crater_shade, COLOR.g);

	// Exponierte Kämme aufhellen.
	float ridge = COLOR.r * COLOR.b;
	albedo = mix(albedo, color_high, ridge * ridge_highlight);

	ALBEDO = albedo;
	ROUGHNESS = mix(roughness_high, roughness_low,
	                COLOR.r * (1.0 - COLOR.g * 0.6));
	METALLIC = 0.0;
	SPECULAR = 0.2;

	// ── Hemisphärischer Welt-Fill via EMISSION ─────────────────────
	// dot(world_normal, world_up) ∈ [-1, 1]; auf [0, 1] gemappt für
	// smoothen Übergang zwischen Sky- und Ground-Farbe.
	vec3 fill_up_n = normalize(fill_up_direction);
	float hemi = clamp(dot(v_world_normal, fill_up_n) * 0.5 + 0.5, 0.0, 1.0);
	vec3 fill = mix(fill_ground_color, fill_sky_color, hemi);
	EMISSION = albedo * fill * fill_strength;
}
