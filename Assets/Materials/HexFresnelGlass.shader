// ---------------------------------------------------------------------------------------------
// Custom/HexFresnelGlass
//
// A fresnel force-field: the mesh interior is see-through glass, the silhouette is a bright
// glowing rim, and an ANIMATED hexagonal lattice rides on the surface -- strongest where the
// surface turns edge-on, fading to almost nothing face-on.
//
// Fully procedural. There is no baked pattern or normal-map asset behind this shader; the
// honeycomb is evaluated per pixel, which is what lets it flow, swirl and pulse with _Time.
// The whole grid is driven by one 2D hex function, so changing the mapping or the density
// costs nothing but a re-compile.
//
// Output is premultiplied (Blend One OneMinusSrcAlpha):
//     result = glow + refractedScene * tint * alpha + scene * (1 - alpha)
// so "transparent like glass" is the alpha term and the rim/lattice are pure emission on top.
// ---------------------------------------------------------------------------------------------
Shader "Custom/HexFresnelGlass"
{
    Properties
    {
        [Header(Glass Surface)]
        [HDR] _GlassTint ("Glass Tint", Color) = (0.55, 0.75, 1.0, 1)
        // Opacity of the interior. This is the "glass" dial: 0 is invisible, 1 is solid.
        _GlassAlpha ("Interior Opacity", Range(0, 1)) = 0.04
        // Screen-space offset of the background seen through the shell, driven by the surface
        // normal -- that is what reads as thickness/curvature rather than flat transparency.
        // 0 gives perfectly clear (non-refracting) glass.
        _RefractStrength ("Refraction (screen units)", Range(0, 0.25)) = 0.02
        _RefractClamp ("Refraction Clamp", Range(0.005, 0.3)) = 0.05
        // The glass refracts the real background, which needs the URP asset's Opaque Texture AND
        // Depth Texture switched ON. The project's PC asset has both; the Mobile asset has NEITHER,
        // and sampling them there returns nothing usable -- the interior would go black instead of
        // clear. Set this to 0 on any pipeline asset without them: the interior then falls back to
        // plain tinted transparency, which is what mobile wants anyway.
        _SceneColorGlass ("Refract Scene Colour (needs Opaque+Depth Texture)", Range(0, 1)) = 1

        [Header(Fresnel Rim)]
        [HDR] _RimColor ("Rim Color", Color) = (0.15, 0.7, 3.6, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 12)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0, 12)) = 1.1
        // Opacity the silhouette reaches. High values make the edge read as solid light and
        // keep the interior's transparency from washing the rim out.
        _RimAlpha ("Edge Opacity", Range(0, 1)) = 0.85
        // The rim breathes. Depth 0 disables it; the speed is in cycles per second.
        _BreathSpeed ("Rim Breath Speed", Range(0, 4)) = 0.35
        _BreathDepth ("Rim Breath Depth", Range(0, 1)) = 0.25

        [Header(Hex Grid)]
        // Cells around the equator. Snapped to an EVEN number at shade time: a flat-top hex lattice
        // only tiles seamlessly across the equirect seam on a 3-unit period, which in cell counts
        // is an even number. That is what stops a visible stitch where the sphere's u wraps.
        _HexDensity ("Hex Density (cells around, even)", Float) = 24
        [HDR] _HexColor ("Hex Edge Color", Color) = (0.2, 0.55, 4.8, 1)
        _HexIntensity ("Hex Edge Intensity", Range(0, 12)) = 3.5
        _HexLineWidth ("Hex Line Width", Range(0.01, 0.6)) = 0.07
        // Keeps the lattice at least a pixel wide, so it stays crisp instead of dissolving into a
        // soft ramp when a cell is only a few pixels across.
        _HexAA ("Hex Anti-alias (px)", Range(0.25, 4)) = 1.0
        [HDR] _HexFillColor ("Hex Interior Fill", Color) = (0.05, 0.25, 1.2, 1)
        _HexFillIntensity ("Hex Fill Intensity", Range(0, 4)) = 0.15
        _HexFillPower ("Hex Fill Falloff", Range(0.5, 6)) = 2.0
        // THE edge-prominence dial: how visible the honeycomb is on a face pointing straight at the
        // camera. 0 keeps the interior as clean glass and confines the lattice to the edge band
        // below; 1 draws it uniformly across the face like a decal.
        _HexFaceAmount ("Face-on Visibility (0 = rim only)", Range(0, 1)) = 0.0
        // --- the edge-prominence band, in "how edge-on is this fragment" units (1 - N.V) ---
        // Below _HexEdgeStart the lattice is not dimmed, it is ABSENT, which is what keeps the
        // interior clean glass instead of a faint wash of the pattern. A plain fresnel power
        // cannot do this: pow(1-N.V, p) keeps a long tail, and at HDR lattice intensities even
        // 2% of the peak is clearly visible across the whole face.
        _HexEdgeStart ("Edge Band Start", Range(0, 1)) = 0.30
        _HexEdgeEnd ("Edge Band Full", Range(0, 1)) = 0.80
        _HexAlpha ("Hex Line Opacity", Range(0, 1)) = 0.35

        [Header(Animation)]
        // One master multiplier over every rate below. 0 freezes the pattern completely.
        _AnimSpeed ("Master Speed", Range(0, 4)) = 1.0
        // Lattice drift, in pattern-units per second. X flows around the object (seamless);
        // Y slides toward the poles.
        _FlowSpeed ("Flow Speed (uv/sec)", Vector) = (0.06, 0, 0, 0)
        // Rotates the pattern about the object's Y axis, in whole turns per second.
        _SwirlSpeed ("Swirl Speed (turns/sec)", Range(-2, 2)) = 0.05
        // A bright band sweeping through the object along _PulseDir. Because the phase is the
        // object-space position, the wave stays locked to the object however it moves or rotates.
        _PulseDir ("Pulse Direction (obj space)", Vector) = (0, 1, 0, 0)
        _PulseDensity ("Pulse Bands", Range(0.5, 12)) = 3.0
        _PulseSpeed ("Pulse Speed", Range(-4, 4)) = 0.6
        _PulseWidth ("Pulse Width", Range(0.02, 1)) = 0.3
        _PulseBoost ("Pulse Boost", Range(0, 6)) = 1.6
        [HDR] _PulseColor ("Pulse Color", Color) = (0.35, 0.9, 5.5, 1)
        // Per-cell twinkle, hashed off the cell id so each hexagon flickers independently.
        _FlickerSpeed ("Cell Flicker Speed", Range(0, 12)) = 2.5
        _FlickerAmount ("Cell Flicker Amount", Range(0, 1)) = 0.35

        [Header(Mapping)]
        // 0 = spherical (equirect on the object-space direction): ideal on spheres and round
        // shells. 1 = triplanar (object-space XYZ): no UVs needed, works on boxes and arbitrary
        // meshes.
        _Mapping ("Mapping (0 = Spherical, 1 = Triplanar)", Range(0, 1)) = 0
        _TriplanarScale ("Triplanar Cell Scale", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "HexFresnelGlass"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied. See the header note: this is what lets the rim be emissive while the
            // interior stays a thin sheet of glass over the real background.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // PI / TWO_PI / INV_TWO_PI come from the pipeline's Macros.hlsl.

            CBUFFER_START(UnityPerMaterial)
                half4 _GlassTint;
                half  _GlassAlpha;
                half  _RefractStrength;
                half  _RefractClamp;
                half  _SceneColorGlass;

                half4 _RimColor;
                half  _FresnelPower;
                half  _RimIntensity;
                half  _RimAlpha;
                half  _BreathSpeed;
                half  _BreathDepth;

                float _HexDensity;
                half4 _HexColor;
                half  _HexIntensity;
                half  _HexLineWidth;
                half  _HexAA;
                half4 _HexFillColor;
                half  _HexFillIntensity;
                half  _HexFillPower;
                half  _HexFaceAmount;
                half  _HexEdgeStart;
                half  _HexEdgeEnd;
                half  _HexAlpha;

                half  _AnimSpeed;
                float4 _FlowSpeed;
                half  _SwirlSpeed;
                float4 _PulseDir;
                half  _PulseDensity;
                half  _PulseSpeed;
                half  _PulseWidth;
                half  _PulseBoost;
                half4 _PulseColor;
                half  _FlickerSpeed;
                half  _FlickerAmount;

                half  _Mapping;
                float _TriplanarScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS   : TEXCOORD3;
                float  eyeDepth   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;
                OUT.eyeDepth   = -vp.positionVS.z;
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Equirectangular lookup. Exact inverse of nothing in particular -- this is the mapping
            // the lattice is *defined* in, so there is no seam or convention to get wrong.
            float2 DirToEquirect(float3 d)
            {
                return float2(atan2(d.z, d.x) * INV_TWO_PI + 0.5,
                              acos(clamp(d.y, -1.0, 1.0)) * (1.0 / PI));
            }

            float3 RotateY(float3 p, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
            }

            // ---------------------------------------------------------------------------------
            // The hex lattice.
            //
            // Flat-top hexagons, circumradius 1, evaluated by rounding the point's cube
            // coordinates to the nearest cell centre -- the nearest-site property is what makes
            // the Voronoi cell of a triangular lattice a hexagon, so the cell lookup and the
            // distance field fall out of the same step.
            //
            // Returns: x = 1 at the cell centre, 0 exactly on an edge (normalized by the apothem)
            //          y = per-cell random value in [0,1]
            // ---------------------------------------------------------------------------------
            float2 HexField(float2 p)
            {
                float q = (2.0 / 3.0) * p.x;
                float r = (-1.0 / 3.0) * p.x + (1.0 / 1.7320508) * p.y;

                // Cube coordinates, then round to the nearest lattice site.
                float3 c  = float3(q, -q - r, r);
                float3 rc = round(c);
                float3 d  = abs(rc - c);
                if (d.x > d.y && d.x > d.z)      rc.x = -rc.y - rc.z;
                else if (d.y > d.z)              rc.y = -rc.x - rc.z;
                else                             rc.z = -rc.x - rc.y;

                // Cube -> pixel centre for the flat-top layout, then the hexagon border metric.
                // Inside a flat-top hexagon the apothem bound is |y| <= a and
                // 0.866|x| + 0.5|y| <= a, so the max of those two is the distance to the border.
                float2 centre = float2(1.5 * rc.x, 1.7320508 * (rc.z + 0.5 * rc.x));
                float2 local  = abs(p - centre);
                float  border = max(local.x * 0.8660254 + local.y * 0.5, local.y);

                float e = saturate(1.0 - border / 0.8660254);
                return float2(e, Hash21(rc.xz));
            }

            // Spherical mapping: turn the object-space direction into equirect coordinates, apply
            // the time motion, and scale into cell units.
            float2 SphericalHexCoord(float3 positionOS, float t, float swirlAngle)
            {
                float3 dir = RotateY(normalize(positionOS), swirlAngle);
                float2 uv  = DirToEquirect(dir);
                uv.x += t * _FlowSpeed.x;
                uv.y += t * _FlowSpeed.y;

                // _HexDensity is the number of cells around the equator. The lattice's horizontal
                // period is 1.5 circumradii, so the coordinate scale is 1.5x that count. A flat-top
                // lattice only repeats across the equirect seam on a 3-unit period, which in cell
                // counts means an EVEN number -- so snap the count to even and the seam stays
                // invisible however the grid is scrolled or swirled.
                float around = max(4.0, round(max(_HexDensity, 4.0) * 0.5) * 2.0);
                float scale  = around * 1.5;
                return float2(uv.x * scale, uv.y * scale * 0.5);
            }

            // Triplanar mapping: project the animated object-space position onto the dominant axis
            // plane. Selection is HARD, not blended, and that is deliberate: averaging two or three
            // projections averages their distance fields too, and the sum of two hex fields is not a
            // hex field -- it leaves ghost patterns and radiating chevrons wherever the normal sits
            // near 45 degrees. Hard selection has no ghosting at all, and on box-like geometry the
            // swap lands exactly on the mesh edges, where it is invisible anyway.
            float2 HexSample(float3 positionOS, float3 normalOS, float t)
            {
                float angle = t * _SwirlSpeed * TWO_PI;

                if (_Mapping <= 0.5)
                {
                    return HexField(SphericalHexCoord(positionOS, t, angle));
                }

                float3 rp = RotateY(positionOS, angle)
                          + float3(t * _FlowSpeed.x, t * _FlowSpeed.y, 0.0);
                float3 an = abs(normalize(normalOS));

                if (an.x >= an.y && an.x >= an.z) return HexField(rp.zy * _TriplanarScale);
                if (an.y >= an.z)                  return HexField(rp.xz * _TriplanarScale);
                return HexField(rp.xy * _TriplanarScale);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float  t        = _Time.y * _AnimSpeed;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float  ndv = saturate(dot(N, V));

                // ---------- Fresnel ----------
                // 0 face-on, 1 at the silhouette. This drives the RIM only; the lattice runs off the
                // raw (1 - N.V) through its own edge band further down, so the two can be tuned
                // independently instead of one power having to serve both.
                float fres = pow(saturate(1.0 - ndv), _FresnelPower);

                // ---------- Animated hex lattice ----------
                float2 hex  = HexSample(IN.positionOS, IN.normalOS, t);
                float  e    = hex.x;          // 0 exactly on a border, 1 at a cell centre
                float  hash = hex.y;

                // e is already a distance-to-border field measured inward from the border, so the
                // band hugging each border is simply the small-e end of it. (Reading this the other
                // way round lights the cell CENTRES and the lattice turns into a field of blobs.)
                // Keeping the line at least ~1px wide is what stops it dissolving into a soft ramp
                // once a cell is only a few pixels across on screen.
                // NOTE: never name this "line" -- that is a reserved primitive-topology keyword in
                // HLSL/Metal and the shader dies with a bare "unexpected token" syntax error.
                float aa = max(fwidth(e), 1e-5) * _HexAA;
                float w  = max(_HexLineWidth, aa);
                float hexLine = saturate(1.0 - e / w);
                hexLine = hexLine * hexLine * (3.0 - 2.0 * hexLine); // compact support, C1: no ringing

                // Soft halo filling each cell from its border inward.
                float halo = pow(saturate(1.0 - e), _HexFillPower);

                // How much of the lattice is allowed to show here. _HexFaceAmount is the face-on
                // floor; everything above it is earned by turning toward the silhouette. This is
                // the term that makes the honeycomb an EDGE effect instead of a decal.
                float facing   = 1.0 - ndv;
                float edgeMask = smoothstep(_HexEdgeStart,
                                            max(_HexEdgeEnd, _HexEdgeStart + 1e-3), facing);
                float hexVis   = lerp(_HexFaceAmount, 1.0, edgeMask);

                // ---------- Animation terms ----------
                // A band sweeping through object space, locked to the object rather than to the
                // world so it does not slide when the shell is carried around.
                float pulsePhase = dot(IN.positionOS, normalize(_PulseDir.xyz)) * _PulseDensity
                                 - t * _PulseSpeed;
                float band  = frac(pulsePhase);
                float pulse = exp(-pow((band - 0.5) / max(_PulseWidth, 1e-3), 2.0));

                // Per-cell twinkle.
                float flicker = lerp(1.0, 0.5 + 0.5 * sin(t * _FlickerSpeed + hash * 12.566),
                                     _FlickerAmount);

                // Rim breath.
                float breath = 1.0 + _BreathDepth * sin(t * _BreathSpeed * TWO_PI);

                // Interior is nearly clear; the silhouette is nearly solid. The lattice adds a
                // little opacity of its own so the lines sit on the glass rather than inside it.
                // Computed here because the glass term below is premultiplied by it.
                float alpha = saturate(_GlassAlpha + fres * _RimAlpha + hexLine * _HexAlpha * hexVis);

                // ---------- Glass: the background, refracted ----------
                half3 glass;
                if (_SceneColorGlass > 0.5)
                {
                    // The offset is taken from the VIEW-space normal, not the world one. World-space
                    // N.xy would swing the refraction around as the object turned, because it is tied
                    // to the object's world orientation rather than to the screen; view space makes it
                    // a radial lens that stays put however the shell is carried or rotated -- zero at
                    // the point facing the camera, strongest at the silhouette. That gradient is what
                    // reads as a thick curved shell instead of a flat tint over the background.
                    float3 nView  = TransformWorldToViewDir(N, false);
                    float2 offset = nView.xy * _RefractStrength;
                    offset = clamp(offset, -_RefractClamp, _RefractClamp);
                    float2 distUV = saturate(screenUV + offset);

                    // Never pull in anything sitting in FRONT of the shell.
                    float eyeAtDist = LinearEyeDepth(SampleSceneDepth(distUV), _ZBufferParams);
                    distUV = (eyeAtDist < IN.eyeDepth) ? screenUV : distUV;

                    glass = SampleSceneColor(distUV) * _GlassTint.rgb * alpha;
                }
                else
                {
                    // No scene colour available: plain tinted transparency. Less "thick glass", but
                    // it never samples a texture the pipeline did not build.
                    glass = _GlassTint.rgb * alpha;
                }

                // ---------- Combine ----------
                half3 glow = _RimColor.rgb * (fres * _RimIntensity * breath)
                           + _HexColor.rgb * (hexLine * hexVis * _HexIntensity * flicker)
                           + _HexFillColor.rgb * (halo * hexVis * _HexFillIntensity)
                           + _PulseColor.rgb * (hexLine * hexVis * pulse * _PulseBoost);

                return half4(glass + glow, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
