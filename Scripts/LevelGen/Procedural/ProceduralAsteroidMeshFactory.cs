using System.Collections.Generic;
using Godot;

namespace SpacedOut.LevelGen.Procedural;

/// <summary>
/// Baut deterministische Asteroid-Meshes aus einer subdivided Icosphere
/// (3 Subdivisions → 642 Vertices / 1280 Faces). Pro Vertex wird ein
/// 3-Oktaven FBM-Displacement angewendet, Normalen aus der resultierenden
/// Geometrie rekonstruiert und die normalisierte Displacement-Höhe als
/// <c>COLOR.r</c> mitgeliefert (wird im Rock-Shader als Cavity-AO genutzt).
///
/// Das Ergebnis ist ein leichtgewichtiges <see cref="ArrayMesh"/>, das über
/// <see cref="ProceduralMeshRegistry"/> zentral gecached und via
/// <see cref="InstancedAsteroidPool"/> per MultiMesh gerendert wird.
/// </summary>
public static class ProceduralAsteroidMeshFactory
{
    // ── Bake-Parameter ─────────────────────────────────────────────
    // Subdiv 4 wäre 2562 V / 5120 F. Bei 12 Varianten = 30.7k unique Tris —
    // immer noch winzig, aber deutlich bessere Krater-Auflösung.
    private const int Subdivisions = 4;
    private const float PrimaryAmplitude = 0.42f;       // Grundsilhouette
    private const float PrimaryFrequency = 1.05f;
    private const float DetailAmplitude = 0.10f;        // kleine Bergrücken
    private const float DetailFrequency = 3.2f;
    private const int NoiseOctaves = 3;
    private const float NoiseLacunarity = 2.1f;
    private const float NoiseGain = 0.5f;
    private const float StretchMin = 0.72f;
    private const float StretchMax = 1.32f;

    // Zwei Krater-Layer mit unterschiedlicher Größe und Dichte.
    private const float Crater1Frequency = 2.2f;
    private const float Crater1Probability = 0.55f;
    private const float Crater1DepthMax = 0.17f;
    private const float Crater1Sharpness = 2.4f;

    private const float Crater2Frequency = 5.0f;
    private const float Crater2Probability = 0.45f;
    private const float Crater2DepthMax = 0.07f;
    private const float Crater2Sharpness = 3.2f;

    /// <summary>
    /// Erzeugt ein ArrayMesh für die angegebene Variante. Seed wird aus
    /// <paramref name="variantIndex"/> per FNV-Hash abgeleitet, sodass die
    /// gleichen Varianten beim nächsten Boot identisch aussehen.
    /// </summary>
    public static ArrayMesh Build(int variantIndex)
    {
        uint seed = StableHash(variantIndex);
        var rng = new DeterministicRng(seed);

        // Separate Offsets pro Noise-Layer, damit sie nicht miteinander phasieren.
        Vector3 primaryOffset = RandomOffset(rng);
        Vector3 detailOffset = RandomOffset(rng);
        Vector3 crater1Offset = RandomOffset(rng);
        Vector3 crater2Offset = RandomOffset(rng);

        Vector3 stretch = new(
            rng.NextFloat(StretchMin, StretchMax),
            rng.NextFloat(StretchMin, StretchMax),
            rng.NextFloat(StretchMin, StretchMax));

        // Zufällige Basis-Rotation der Stretchachsen, damit die Ellipsoid-
        // Silhouette nicht nur entlang der Weltachsen liegt.
        Basis stretchRot = new(
            new Vector3(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)).Normalized(),
            rng.NextFloat(0f, Mathf.Tau));

        BuildIcosphere(Subdivisions, out var vertices, out var indices);

        var colors = new Color[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 dir = vertices[i].Normalized();

            float primary = Fbm3(dir * PrimaryFrequency + primaryOffset);   // ~0..1
            float detail  = Fbm3(dir * DetailFrequency + detailOffset);     // ~0..1

            // Zwei Krater-Layer: groß+mittel. Threshold-basiert, damit nur
            // ein Teil der Oberfläche Einbuchtungen bekommt (sonst Flächen-
            // Rauschen statt Krater-Pockennarben).
            float c1Field = Fbm3(dir * Crater1Frequency + crater1Offset);
            float c2Field = Fbm3(dir * Crater2Frequency + crater2Offset);
            float c1Mask = CraterMask(c1Field, Crater1Probability);
            float c2Mask = CraterMask(c2Field, Crater2Probability);
            float c1 = Mathf.Pow(c1Mask, Crater1Sharpness) * Crater1DepthMax;
            float c2 = Mathf.Pow(c2Mask, Crater2Sharpness) * Crater2DepthMax;
            float craterDepth = c1 + c2;
            float craterMask = Mathf.Max(c1Mask, c2Mask * 0.7f);  // für Shading

            float displaced = 1f
                + PrimaryAmplitude * (primary - 0.5f)
                + DetailAmplitude  * (detail  - 0.5f)
                - craterDepth;

            // Anisotrope Streckung in rotiertem Frame.
            Vector3 stretched = stretchRot * (dir * displaced);
            stretched *= stretch;
            vertices[i] = stretchRot.Inverse() * stretched;

            // COLOR.r = Primary-Höhe (für Cavity-AO), COLOR.g = Krater-Intensität
            // (Shader kann Kraterränder dunkler setzen), COLOR.b = Detail-Höhe.
            colors[i] = new Color(
                Mathf.Clamp(primary, 0f, 1f),
                Mathf.Clamp(craterMask, 0f, 1f),
                Mathf.Clamp(detail, 0f, 1f),
                1f);
        }

        var normals = RecomputeNormals(vertices, indices);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.ResourceName = $"ProcAsteroid_v{variantIndex}";
        return mesh;
    }

    private static Vector3 RandomOffset(DeterministicRng rng) => new(
        rng.NextFloat(-50f, 50f),
        rng.NextFloat(-50f, 50f),
        rng.NextFloat(-50f, 50f));

    private static float CraterMask(float field, float probability)
    {
        // field ~ 0..1. Nur Werte > (1-prob) ergeben Maske > 0; darüber
        // linear auf 0..1 abgebildet. Threshold = (1-prob) steuert, wie
        // viel der Oberfläche insgesamt Kraterfläche ist.
        float threshold = 1f - probability;
        if (field <= threshold) return 0f;
        return Mathf.Clamp((field - threshold) / probability, 0f, 1f);
    }

    // ── Icosphere ───────────────────────────────────────────────────

    private static void BuildIcosphere(int subdiv, out List<Vector3> vertices, out List<int> indices)
    {
        // Basis-Icosaeder (12 Vertices, 20 Faces).
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

        vertices = new List<Vector3>
        {
            new(-1f,  t,  0f), new( 1f,  t,  0f), new(-1f, -t,  0f), new( 1f, -t,  0f),
            new( 0f, -1f,  t), new( 0f,  1f,  t), new( 0f, -1f, -t), new( 0f,  1f, -t),
            new( t,  0f, -1f), new( t,  0f,  1f), new(-t,  0f, -1f), new(-t,  0f,  1f),
        };

        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = vertices[i].Normalized();

        indices = new List<int>
        {
            0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
            1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
            3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
            4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1,
        };

        // Loop-Subdivision: jede Kante bekommt einen neuen Mittelvertex
        // (auf die Einheitskugel projiziert), jede Face wird in 4 Faces
        // zerlegt. Cache verhindert Doppel-Vertices pro Kante.
        for (int level = 0; level < subdiv; level++)
        {
            var midCache = new Dictionary<long, int>();
            var newIndices = new List<int>(indices.Count * 4);

            for (int i = 0; i < indices.Count; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];
                int ab = GetMid(a, b, vertices, midCache);
                int bc = GetMid(b, c, vertices, midCache);
                int ca = GetMid(c, a, vertices, midCache);

                newIndices.Add(a);  newIndices.Add(ab); newIndices.Add(ca);
                newIndices.Add(b);  newIndices.Add(bc); newIndices.Add(ab);
                newIndices.Add(c);  newIndices.Add(ca); newIndices.Add(bc);
                newIndices.Add(ab); newIndices.Add(bc); newIndices.Add(ca);
            }

            indices = newIndices;
        }
    }

    private static int GetMid(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int existing)) return existing;

        Vector3 mid = ((vertices[a] + vertices[b]) * 0.5f).Normalized();
        int index = vertices.Count;
        vertices.Add(mid);
        cache[key] = index;
        return index;
    }

    // ── Normalen-Rebuild ────────────────────────────────────────────

    private static List<Vector3> RecomputeNormals(List<Vector3> vertices, List<int> indices)
    {
        var normals = new List<Vector3>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) normals.Add(Vector3.Zero);

        for (int i = 0; i < indices.Count; i += 3)
        {
            int ia = indices[i];
            int ib = indices[i + 1];
            int ic = indices[i + 2];
            Vector3 va = vertices[ia];
            Vector3 vb = vertices[ib];
            Vector3 vc = vertices[ic];
            Vector3 faceN = (vb - va).Cross(vc - va);
            // Kein Normalisieren: längerer Vektor = größere Fläche =>
            // größerer Beitrag, was weiche Smooth-Normals erzeugt.
            normals[ia] += faceN;
            normals[ib] += faceN;
            normals[ic] += faceN;
        }

        for (int i = 0; i < normals.Count; i++)
            normals[i] = normals[i].Normalized();

        return normals;
    }

    // ── Noise ───────────────────────────────────────────────────────

    private static float Fbm3(Vector3 p)
    {
        float amp = 0.5f;
        float freq = 1f;
        float sum = 0f;
        float norm = 0f;
        for (int o = 0; o < NoiseOctaves; o++)
        {
            sum += amp * ValueNoise(p * freq);
            norm += amp;
            freq *= NoiseLacunarity;
            amp *= NoiseGain;
        }
        return sum / norm;
    }

    private static float ValueNoise(Vector3 p)
    {
        Vector3 i = new(Mathf.Floor(p.X), Mathf.Floor(p.Y), Mathf.Floor(p.Z));
        Vector3 f = p - i;
        Vector3 u = f * f * (new Vector3(3f, 3f, 3f) - 2f * f);

        float a = Hash31(i + new Vector3(0, 0, 0));
        float b = Hash31(i + new Vector3(1, 0, 0));
        float c = Hash31(i + new Vector3(0, 1, 0));
        float d = Hash31(i + new Vector3(1, 1, 0));
        float e = Hash31(i + new Vector3(0, 0, 1));
        float g = Hash31(i + new Vector3(1, 0, 1));
        float h = Hash31(i + new Vector3(0, 1, 1));
        float j = Hash31(i + new Vector3(1, 1, 1));

        return Mathf.Lerp(
            Mathf.Lerp(Mathf.Lerp(a, b, u.X), Mathf.Lerp(c, d, u.X), u.Y),
            Mathf.Lerp(Mathf.Lerp(e, g, u.X), Mathf.Lerp(h, j, u.X), u.Y),
            u.Z);
    }

    private static float Hash31(Vector3 p)
    {
        Vector3 q = new(
            Fract(p.X * 0.1031f),
            Fract(p.Y * 0.1030f),
            Fract(p.Z * 0.0973f));
        float dot = q.X * (q.Y + 33.33f) + q.Y * (q.Z + 33.33f) + q.Z * (q.X + 33.33f);
        q += new Vector3(dot, dot, dot);
        return Fract((q.X + q.Y) * q.Z);
    }

    private static float Fract(float v) => v - Mathf.Floor(v);

    // ── Hash / RNG ──────────────────────────────────────────────────

    /// <summary>FNV-1a 32-bit, passt zur Konvention aus AssetVariantPicker.</summary>
    private static uint StableHash(int variantIndex)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)variantIndex) * 16777619u;
            h = (h ^ 0xA5F0C37Bu) * 16777619u;   // Namespace-Salt "asteroid"
            return h;
        }
    }

    private sealed class DeterministicRng
    {
        private uint _state;
        public DeterministicRng(uint seed) { _state = seed == 0 ? 1u : seed; }

        private uint NextUInt()
        {
            // xorshift32
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float NextFloat(float min, float max)
        {
            float unit = (NextUInt() & 0x00FFFFFF) / (float)0x01000000;
            return min + (max - min) * unit;
        }
    }
}
