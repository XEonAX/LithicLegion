using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a Goldberg polyhedron (hexasphere) cell field into an equirectangular texture.
///
/// A Goldberg polyhedron is the spherical Voronoi diagram of an icosphere's vertices, so no
/// modelling is needed: the cell centres are just the icosphere points. For each texel we store
/// the angular distance to the nearest and second-nearest centre (d1 and d2), normalised by the
/// mean cell spacing.
///
/// R = (d2 - d1) / spacing, which is 0 exactly on a cell border and 1 at the cell centre.
/// That single smooth channel is everything HexForceField.shader needs.
///
/// WHY THE LINES ARE NOT BAKED HERE
/// --------------------------------
/// It is tempting to bake the visible hex pattern -- bright double lines on a black interior --
/// straight into this texture. Don't: it is both blurry and smear-prone.
///
///   * A line baked into a 1024x512 equirect map gets roughly 30 texels per cell at the equator
///     and far fewer near the poles, so the lines soften and break up. The lattice is only
///     crisp if the line is reconstructed per-pixel, which is why the shader derives it from R.
///   * Distortion driven by the screen-space gradient of a *baked* line image smears, because
///     the derivative of a hard step is a one-pixel spike. Here the gradient is analytic
///     (dR is smooth and known in closed form), so the refraction stays clean at any resolution.
///
/// The double line itself is one signed bump on R evaluated in the fragment shader; see
/// _HexLineOffset / _HexLineWidth. Feed this asset to _HexPattern -- not an authored
/// height/normal image -- or the two representations will disagree and the distortion smears.
/// </summary>
public static class BakeGoldbergPattern
{
    // Icosphere(subdiv) starts from the 12-vertex icosahedron and applies `subdiv` passes, so:
    // 0 -> 12, 1 -> 42, 2 -> 162, 3 -> 642 cells. 2 gives 162 (150 hexes + 12 pentagons).
    //
    // 2 -> 162 cells is the reference density. Do not "correct" this to 3 on the strength of an
    // autocorrelation or a peak count across the dome: the reference draws every seam as a DOUBLE
    // line (a lit bevel each side of a dark groove), so counting bright features counts each seam
    // twice and makes the lattice look twice as dense as it is. Counted as CELLS -- panel
    // interiors between dark grooves -- the reference runs ~10 across the front of a 579 px dome,
    // and 162 cells puts 11 across. 642 cells was tried at matched framing and is visibly too
    // busy by eye against the reference.
    const int DefaultSubdiv = 2;
    const int DefaultWidth = 1024;
    const int DefaultHeight = 512;
    const string DefaultPath = "Assets/Materials/GoldbergPattern.asset";

    // Keep in sync with the HexForceField.shader property defaults.
    const float DefaultLineOffset = 0.22f;
    const float DefaultLineWidth = 0.05f;
    const float DefaultFaceFalloff = 1.25f;
    const float DefaultFaceOpacity = 0.05f;

    // There is deliberately only ONE source for the lattice: Icosphere(subdiv). Baking from an
    // authored hexasphere mesh used to exist as a second path, so the lattice could be matched to
    // a Blender export instead. It is gone: it needed a baked-mesh asset that nothing else used,
    // and an icosphere gives the same 12 pentagons and the same 162 cells without one. Two sources
    // for one lattice is a way to bake a field that disagrees with the mesh you are looking at.
    [MenuItem("Tools/Shield/Bake Goldberg Pattern (equirect)")]
    static void BakeMenu() => Bake(DefaultPath, DefaultSubdiv, DefaultWidth, DefaultHeight, PreviewPath);

    /// <summary>Writes a PNG of the double-line mask exactly as the shader reconstructs it.</summary>
    static string PreviewPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath),
        "Logs/GoldbergPattern_preview.png");

    // Batch entry point:
    //   -executeMethod BakeGoldbergPattern.BakeCLI -bakeSubdiv 3 -bakeW 1024 -bakeH 512 \
    //     -bakeOut Assets/.../x.asset -bakePreview Logs/pattern.png -lineOffset 0.22 -lineWidth 0.09
    public static void BakeCLI()
    {
        string path = DefaultPath;
        string preview = PreviewPath;
        int subdiv = DefaultSubdiv, w = DefaultWidth, h = DefaultHeight;
        float lineOffset = DefaultLineOffset, lineWidth = DefaultLineWidth;

        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
        {
            switch (a[i])
            {
                case "-bakeOut": path = a[i + 1]; break;
                case "-bakeSubdiv": int.TryParse(a[i + 1], out subdiv); break;
                case "-bakeW": int.TryParse(a[i + 1], out w); break;
                case "-bakeH": int.TryParse(a[i + 1], out h); break;
                case "-bakePreview": preview = a[i + 1]; break;
                case "-lineOffset": float.TryParse(a[i + 1], out lineOffset); break;
                case "-lineWidth": float.TryParse(a[i + 1], out lineWidth); break;
            }
        }

        Bake(path, subdiv, w, h, preview, lineOffset, lineWidth);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    public static void Bake(string path, int subdiv, int width, int height,
                            string previewPath = null,
                            float lineOffset = DefaultLineOffset, float lineWidth = DefaultLineWidth)
    {
        BakeWithCentres(path, Icosphere(subdiv), width, height, "icosphere subdiv " + subdiv,
                        previewPath, lineOffset, lineWidth);
    }

    static void BakeWithCentres(string path, Vector3[] centres, int width, int height, string tag,
                                string previewPath, float lineOffset, float lineWidth)
    {
        int n = centres.Length;
        float spacing = MeanSpacing(centres);
        Debug.Log($"BAKE [{tag}] centres={n} ({n - 12} hexes + 12 pentagons) spacing={spacing:F5} rad, {width}x{height}");

        // R = d2 - d1 (border distance, 0 on a border and 1 at a cell centre) -- the channel the
        // shader reads. G and B keep d1 and d2 so the field can be re-interpreted without a
        // re-bake, at no extra cost since the format is already 16-bit RGBA.
        var px = new Color[width * height];
        Parallel.For(0, height, y =>
        {
            double v = (y + 0.5) / height;          // v = 0 at the bottom row -> north pole
            double theta = v * Math.PI;
            double st = Math.Sin(theta), ct = Math.Cos(theta);

            for (int x = 0; x < width; x++)
            {
                double u = (x + 0.5) / width;
                double phi = (u - 0.5) * 2.0 * Math.PI;
                double dx = st * Math.Cos(phi), dy = ct, dz = st * Math.Sin(phi);

                double d1 = double.MaxValue, d2 = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = centres[i];
                    double dp = dx * c.x + dy * c.y + dz * c.z;
                    double ang = Math.Acos(Math.Clamp(dp, -1.0, 1.0));
                    if (ang < d1) { d2 = d1; d1 = ang; }
                    else if (ang < d2) { d2 = ang; }
                }

                float a = (float)(d1 / spacing);
                float b = (float)(d2 / spacing);
                px[y * width + x] = new Color(b - a, a, b, 1f);
            }
        });

        // Update the existing asset IN PLACE whenever the size matches.
        //
        // The obvious DeleteAsset + CreateAsset is a trap: it drops the asset, Unity nulls every
        // material reference to it, and by the time the new asset lands the materials are already
        // pointing at nothing. The shader then samples black, R reads 0 everywhere, no hex lines
        // are produced, and the shield looks like nothing changed no matter what you tweak.
        // Re-baking must be safe to run at any time, so only build a new asset when we have to.
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && existing.width == width && existing.height == height)
        {
            existing.wrapModeU = TextureWrapMode.Repeat;
            existing.wrapModeV = TextureWrapMode.Clamp;
            existing.filterMode = FilterMode.Bilinear;
            existing.anisoLevel = 4;
            existing.SetPixels(px);
            existing.Apply(true);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            Debug.Log($"BAKED {path} (updated in place, GUID preserved)");
            if (!string.IsNullOrEmpty(previewPath))
                WritePreview(previewPath, px, width, height, lineOffset, lineWidth);
            return;
        }

        if (existing != null)
            Debug.LogWarning($"BakeGoldbergPattern: {path} is {existing.width}x{existing.height} but this bake "
                           + $"is {width}x{height}. Recreating the asset -- every material using _HexPattern "
                           + "will need it re-assigned.");

        var tex = new Texture2D(width, height, TextureFormat.RGBAHalf, true, true)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path),
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };
        tex.SetPixels(px);
        tex.Apply(true);

        AssetDatabase.CreateAsset(tex, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"BAKED {path}");

        if (!string.IsNullOrEmpty(previewPath))
            WritePreview(previewPath, px, width, height, lineOffset, lineWidth);
    }

    // =============================================================================================
    // Relief normal bake
    // =============================================================================================
    //
    // WHY THIS EXISTS
    // ---------------
    // The old shader synthesised a normal from a scalar: nxy = -dir * f(en), with `en` the
    // UNSIGNED distance to the nearest border. Because `dir` = normalize(grad en) flips at every
    // border, any single-lobed f gives one up-lobe and one down-lobe per crossing -- an S -- and
    // for a crossing that is nearly perpendicular to the border the offset runs along the
    // background line and all but vanishes, leaving a tick (H / N). No parameter fixes that.
    //
    // The relief we actually want is a DOUBLE RIDGE: one ridge on each flank of every border, at
    // |s| = ridgeOffset. Its surface gradient is
    //
    //     grad h = H'(en) * grad en,   H(u) = exp(-((u - r0)/w)^2),  H' = -2(u-r0)/w^2 * H
    //
    // which is NOT odd about the border: H' changes sign at en = r0 on each side, so the profile
    // along a crossing is -, +, -, + -- four lobes, a W -- at EVERY crossing angle. Only the
    // overall amplitude scales with how obliquely the line meets the border.
    //
    // This bakes that gradient as a tangent-space normal map in the SAME equirect mapping the
    // shader samples the pattern with (DirToEquirect), so the two can never disagree.
    //
    // The map is baked in the orthonormal frame (T, B, N) of that mapping, i.e. the equirect
    // metric is already folded in:
    //
    //     T = dP/du normalized,  B = dP/dv normalized,  N = P
    //     |dP/du| = 2*pi*sin(theta),   |dP/dv| = pi        (unit sphere, DirToEquirect)
    //     n = normalize( -(dh/du)/|dP/du|, -(dh/dv)/|dP/dv|, 1 )
    //
    // so the shader only has to rebuild (T, B, N) and normalise -- no metric term at runtime.
    // A vector field also mip-filters correctly, which a baked LINE image does not.

    const string DefaultNormalPath = "Assets/Materials/HexReliefNormal.asset";
    const float DefaultRidgeOffset = 0.18f;   // en at the ridge crest -- keep == _HexLineOffset
    const float DefaultRidgeWidth  = 0.10f;   // en half-width of each ridge (must be < offset,
                                              // or the two flanks merge across the border and the
                                              // four-lobe W collapses to a single staple)
    const float DefaultMaxTilt     = 0.70f;   // 99.5th percentile |n.xy| in the baked map
    // The relief has to be several texels wide to survive a bake: at 1024x512 one texel spans
    // 0.0061 rad in v, a ridge of width 0.11 en units is ~0.008 rad, i.e. 1.3 texels -- it
    // aliases into a dotted line. 2048x1024 with w=0.20 gives ~5 texels across the ridge.
    const int DefaultNormalWidth = 2048;
    const int DefaultNormalHeight = 1024;

    [MenuItem("Tools/Shield/Bake Hex Relief Normal (equirect)")]
    static void BakeNormalMenu()
        => BakeReliefNormal(DefaultNormalPath, DefaultSubdiv, DefaultNormalWidth, DefaultNormalHeight);

    /// <summary>Bakes the double-ridge relief's tangent-space normal, in equirect, from the icosphere.</summary>
    public static void BakeReliefNormal(string path, int subdiv, int width, int height,
                                        float ridgeOffset = DefaultRidgeOffset,
                                        float ridgeWidth = DefaultRidgeWidth,
                                        float maxTilt = DefaultMaxTilt)
    {
        BakeReliefNormalFromCentres(path, Icosphere(subdiv), width, height, ridgeOffset, ridgeWidth, maxTilt);
    }

    static void BakeReliefNormalFromCentres(string path, Vector3[] centres, int width, int height,
                                            float r0, float w, float maxTilt)
    {
        int n = centres.Length;
        float spacing = MeanSpacing(centres);
        Debug.Log($"BAKE NORMAL centres={n} spacing={spacing:F5} rad, ridgeOffset={r0} ridgeWidth={w}, {width}x{height}");

        // 1) Per texel: en, and the ANALYTIC surface gradient of en.
        //
        // Differentiating the baked en image with finite differences works but is fragile: the
        // ridge is only a couple of texels wide near the equator, and the 1/sin(theta) of the
        // equirect metric turns differencing noise near the poles into hot spots. The field is
        // available in closed form, so use it.
        //
        // d1 and d2 are angular distances to cell centres c1 and c2. For a direction d, the
        // surface (great-circle) gradient of the angular distance to a centre c is
        //
        //     grad acos(d.c) = -(c - d*(d.c)) / sin(acos(d.c)),     |grad| = 1
        //
        // so grad en = (grad d2 - grad d1)/spacing exactly, with no pole singularity.
        var tx = new float[width * height];   // tangential normal, along +u
        var ty = new float[width * height];   // tangential normal, along +v
        var enField = new float[width * height];

        Parallel.For(0, height, y =>
        {
            double v = (y + 0.5) / height;
            double theta = v * Math.PI;
            double st = Math.Sin(theta), ct = Math.Cos(theta);
            double sinPhi = Math.Sin(0), cosPhi = Math.Cos(0);

            for (int x = 0; x < width; x++)
            {
                double u = (x + 0.5) / width;
                double phi = (u - 0.5) * 2.0 * Math.PI;
                sinPhi = Math.Sin(phi); cosPhi = Math.Cos(phi);
                var dir = new Vector3((float)(st * cosPhi), (float)ct, (float)(st * sinPhi));

                int i1 = -1, i2 = -1;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    double ang = Math.Acos(Math.Clamp(Vector3.Dot(dir, centres[i]), -1.0, 1.0));
                    if (ang < d1) { d2 = d1; i2 = i1; d1 = ang; i1 = i; }
                    else if (ang < d2) { d2 = ang; i2 = i; }
                }

                int k = y * width + x;
                double en = (d2 - d1) / spacing;
                enField[k] = (float)en;

                float hp = (float)(-2.0 * (en - r0) / (w * w) * Math.Exp(-((en - r0) * (en - r0)) / (w * w)));
                if (Math.Abs(hp) < 1e-7f || i1 < 0 || i2 < 0) { tx[k] = 0f; ty[k] = 0f; continue; }

                Vector3 g1 = GradAngle(dir, centres[i1], d1);
                Vector3 g2 = GradAngle(dir, centres[i2], d2);
                Vector3 nin = (g2 - g1) * (hp / spacing);   // -H'(en) * grad en

                // Equirect unit tangents: T along +u (azimuth), B along +v (polar).
                var T = new Vector3((float)-sinPhi, 0f, (float)cosPhi);
                var B = new Vector3((float)(ct * cosPhi), (float)-st, (float)(ct * sinPhi));
                tx[k] = Vector3.Dot(nin, T);
                ty[k] = Vector3.Dot(nin, B);
            }
        });

        // 2) scale so the strongest tilt lands at maxTilt, then pack n = normalize(tx, ty, 1).
        var mags = new float[tx.Length];
        for (int i = 0; i < tx.Length; i++) mags[i] = Mathf.Sqrt(tx[i] * tx[i] + ty[i] * ty[i]);
        var sorted = (float[])mags.Clone();
        Array.Sort(sorted);
        float P(float q) => sorted[Mathf.Clamp((int)(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
        float p995 = P(0.995f);
        float scale = maxTilt / Mathf.Max(p995, 1e-9f);
        Debug.Log($"BAKE NORMAL |grad h| p50={P(0.5f):F4} p90={P(0.9f):F4} p99={P(0.99f):F4} "
                + $"p99.5={p995:F4} max={sorted[sorted.Length - 1]:F4} -> scale {scale:F5} (target tilt {maxTilt})");

        var px = new Color[width * height];
        for (int i = 0; i < px.Length; i++)
        {
            float nx = tx[i] * scale, ny = ty[i] * scale;
            float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
            px[i] = new Color(0.5f + 0.5f * nx * inv, 0.5f + 0.5f * ny * inv, 0.5f + 0.5f * inv, 1f);
        }

        WritePreviewPng(path, px, width, height);

        // RGBA32, linear, NOT the pattern's RGBAHalf: a tangent-space normal is an 8-bit
        // quantity in every engine, and half-float here made a 44 MB asset for no visible
        // gain. The pattern keeps RGBAHalf because it is a distance field that gets
        // differenced, where the extra precision is worth something.
        WriteTexture(path, px, width, height, "BAKED NORMAL", TextureFormat.RGBA32);
    }

    /// <summary>Surface gradient of the angular distance from <paramref name="dir"/> to a centre.</summary>
    static Vector3 GradAngle(Vector3 dir, Vector3 centre, double angle)
    {
        double s = Math.Sin(angle);
        if (s < 1e-7) return Vector3.zero;
        float cosA = (float)Math.Cos(angle);
        return -(centre - dir * cosA) / (float)s;
    }

    /// <summary>Writes the baked normal map next to the project (Logs/) so it can be eyeballed.</summary>
    static void WritePreviewPng(string assetPath, Color[] px, int width, int height)
    {
        try
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath) + "_preview.png";
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Logs");
            System.IO.Directory.CreateDirectory(dir);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.SetPixels(px);
            tex.Apply(false);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"PREVIEW {System.IO.Path.Combine(dir, name)}");
        }
        catch (Exception e) { Debug.LogWarning("WritePreviewPng: " + e.Message); }
    }

    /// <summary>Creates the texture, or updates it in place when the size matches (keeps the GUID).</summary>
    static void WriteTexture(string path, Color[] px, int width, int height, string tag,
                             TextureFormat format = TextureFormat.RGBAHalf)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && existing.width == width && existing.height == height)
        {
            existing.wrapModeU = TextureWrapMode.Repeat;
            existing.wrapModeV = TextureWrapMode.Clamp;
            existing.filterMode = FilterMode.Bilinear;
            existing.anisoLevel = 4;
            existing.SetPixels(px);
            existing.Apply(true);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            Debug.Log($"{tag} {path} (updated in place, GUID preserved)");
            return;
        }

        if (existing != null)
            Debug.LogWarning($"BakeGoldbergPattern: {path} is {existing.width}x{existing.height} but this bake "
                           + $"is {width}x{height}. Recreating the asset -- references to it must be re-assigned.");

        var tex = new Texture2D(width, height, format, true, true)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path),
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };
        tex.SetPixels(px);
        tex.Apply(true);

        AssetDatabase.CreateAsset(tex, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"{tag} {path}");
    }

    /// <summary>
    /// Saves the pattern mask the shader reconstructs from R -- the same line + cell-shading
    /// maths, on the same field -- so the result can be eyeballed outside Unity and compared
    /// against the reference art. This is a diagnostic image, not an input: do not assign it
    /// to _HexPattern.
    /// </summary>
    static void WritePreview(string pngPath, Color[] field, int width, int height,
                             float lineOffset, float lineWidth)
    {
        var outPx = new Color32[width * height];
        for (int i = 0; i < outPx.Length; i++)
        {
            float en = field[i].r;
            float rim = Mathf.Clamp01(1f - en);

            // double line
            float t = Mathf.Clamp01(1f - Mathf.Abs(en - lineOffset) / Mathf.Max(lineWidth, 1e-4f));
            float line = t * t * (3f - 2f * t);

            // soft cell shading (keep in sync with the shader's face defaults)
            float face = Mathf.Pow(rim, DefaultFaceFalloff);

            byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(line + face * DefaultFaceOpacity) * 255f);
            outPx[i] = new Color32(g, g, g, 255);
        }

        var preview = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        preview.SetPixels32(outPx);
        preview.Apply(false);

        try
        {
            string dir = System.IO.Path.GetDirectoryName(pngPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(pngPath, preview.EncodeToPNG());
            Debug.Log($"PREVIEW {pngPath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("BakeGoldbergPattern: could not write preview: " + e.Message);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(preview);
        }
    }

    /// <summary>Cell centres: the vertices of an icosphere with <paramref name="subdiv"/> subdivisions.</summary>
    static Vector3[] Icosphere(int subdiv)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new System.Collections.Generic.List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        var f = new System.Collections.Generic.List<int[]>
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
        };

        for (int s = 0; s < subdiv; s++)
        {
            var mid = new System.Collections.Generic.Dictionary<long, int>();
            var nf = new System.Collections.Generic.List<int[]>(f.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                idx = v.Count;
                v.Add((v[a] + v[b]) * 0.5f);
                mid[key] = idx;
                return idx;
            }
            foreach (int[] tri in f)
            {
                int ab = Mid(tri[0], tri[1]), bc = Mid(tri[1], tri[2]), ca = Mid(tri[2], tri[0]);
                nf.Add(new[] { tri[0], ab, ca });
                nf.Add(new[] { tri[1], bc, ab });
                nf.Add(new[] { tri[2], ca, bc });
                nf.Add(new[] { ab, bc, ca });
            }
            f = nf;
        }

        var outV = new Vector3[v.Count];
        for (int i = 0; i < v.Count; i++) outV[i] = v[i].normalized;
        return outV;
    }

    /// <summary>Mean angular distance between neighbouring cell centres, used to normalise the field.</summary>
    static float MeanSpacing(Vector3[] c)
    {
        double total = 0;
        for (int i = 0; i < c.Length; i++)
        {
            double best = double.MaxValue;
            for (int j = 0; j < c.Length; j++)
            {
                if (i == j) continue;
                double ang = Math.Acos(Math.Clamp(Vector3.Dot(c[i], c[j]), -1.0, 1.0));
                if (ang < best) best = ang;
            }
            total += best;
        }
        return (float)(total / c.Length);
    }
}
