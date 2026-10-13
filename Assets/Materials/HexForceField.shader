Shader "Custom/HexForceField"
{
    Properties
    {
        [Header(Face)]
        [HDR] _BaseColor ("Face Tint", Color) = (0.1, 0.4, 1, 1)
        _FaceAlpha ("Face Opacity", Range(0, 0.5)) = 0.04

        [Header(Fresnel Outline)]
        [HDR] _RimColor ("Rim Color", Color) = (0.2, 0.55, 3, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 1.5

        [Header(Goldberg Pattern)]
        // R = distance from the cell border in cell units: 0 exactly on a border, 1 at the cell
        // centre. Baked with Tools > Shield > Bake Goldberg Pattern. This is a smooth distance
        // field, NOT a picture of the pattern -- assign nothing else here, or the two
        // representations disagree and the refraction smears.
        _HexPattern ("Hex Pattern (R = border distance)", 2D) = "black" {}

        // Tangent-space normal of the panel relief, baked in the SAME equirect mapping the
        // pattern is sampled with (Tools > Shield > Bake Hex Relief Normal). This is what the
        // refraction is driven by -- see the fragment shader for why a baked vector field is the
        // only thing that can give a W at every crossing angle.
        _HexNormal ("Hex Relief Normal (tangent space)", 2D) = "bump" {}

        // Cell shading: dark core fading to a bright rim, matching the reference art.
        // _HexFaceFalloff = 1 is a straight linear ramp; higher values tighten the dark core.
        [HDR] _HexFaceColor ("Cell Shading Color", Color) = (0.06, 0.2, 0.6, 1)
        _HexFaceFalloff ("Cell Shading Falloff", Range(1, 6)) = 1.25
        // How much of the background the shell replaces with its own refracted copy. The
        // displacement is only non-zero in a band around each border, so this is what decides how
        // much of that band you actually see move; below ~0.4 the panels read as untinted glass
        // with a wireframe on them.
        _HexFaceOpacity ("Cell Transmit (refraction)", Range(0, 1)) = 0.05
        _HexFaceGlow ("Cell Shading Glow (keep low)", Range(0, 2)) = 0.05
        // Tint applied to the refracted copy. There is no subtractive term in a premultiplied
        // blend, so the DARK parts of the reference -- the panel interiors and the gap down the
        // middle of every seam -- have to come from tinting what the shell transmits, not from
        // darkening the scene behind it. _PanelTint is the glass you look through, _SeamTint is
        // the absorbing gap; the lerp between them is what makes the seams read as dark grooves
        // between lit bevels instead of as a bright wireframe.
        [HDR] _PanelTint ("Panel Transmit Tint", Color) = (0.55, 0.62, 0.8, 1)
        [HDR] _SeamTint ("Seam Absorb Tint", Color) = (0.10, 0.14, 0.28, 1)
        // How far out from a border the glass reaches full _PanelTint. Keep it well below
        // _HexLineOffset, so the dark groove stays narrow and the lit bevel sits on clean glass.
        _PanelCoreWidth ("Seam Groove Width", Range(0.02, 0.6)) = 0.10

        // The two lines that flank every border sit at +/- _HexLineOffset, so a border reads
        // bright / dark gap / bright. 0 collapses them onto the border and gives a single line.
        // Keep _HexLineOffset comfortably larger than _HexLineWidth or the two merge.
        _HexLineOffset ("Line Offset (0 = single line)", Range(0, 0.45)) = 0.22
        _HexLineWidth ("Line Half Width (fatten with this)", Range(0.004, 0.25)) = 0.05
        _HexLineAA ("Anti-alias Floor (pixels, keep near 1)", Range(0.25, 4)) = 1.0

        // The honeycomb should read where the shell turns edge-on, not uniformly. This is the
        // emissive half of the seam -- the lit bevel -- and it wants to be most of the seam's
        // visible brightness, or the lattice reads as a faint wireframe.
        [HDR] _HexColor ("Hex Edge Glow (0 = refraction only)", Color) = (0.25, 0.45, 1.2, 1)
        _HexEdgePower ("Hex Edge Angle Falloff", Range(0.25, 8)) = 3
        // Displacement amplitude, in APOTHEM UNITS (1 = one cell radius). The shader converts it to
        // pixels with the local cell size, so the W is the same fraction of a cell at any shield
        // scale, distance or FOV. Useful range is roughly 0.01 (barely there) to 0.08 (already
        // tearing the background); it is NOT a screen fraction and the old 0-0.3 range was in
        // different units entirely.
        _DistortStrength ("Refraction Strength (cell units)", Range(0, 0.4)) = 0.03
        // Screen-space clamp on the sampled offset. The relief's slope is steepest exactly where
        // the pattern is most compressed on screen (the limb), which is also where the reference
        // tears hardest, so this wants to be a backstop and not a governor: _RefractClamp 0.06 is
        // 38 px at 1280 and only bites in the last few degrees before the silhouette.
        _RefractClamp ("Refraction Clamp (screen units)", Range(0.005, 0.3)) = 0.06

        [Header(Intersection)]
        // The bright blue ring is where the shell INTERSECTS the scene, and this is an actual
        // contact test, not a height band. The shell writes no depth, so SampleSceneDepth at a
        // pixel is the nearest opaque surface behind it; the two are intersecting exactly where
        // that depth is coincident with the shell's own. Nothing below knows what the ground is,
        // so the same code lights a floor, a wall, a crate or a ramp.
        //
        // It used to be "glow wherever the fragment is within N units of _GroundY". That is not an
        // intersection test and it does not survive a real scene:
        //   * it lights a shell hanging in mid-air over a hole, because the plane it tests against
        //     is imaginary;
        //   * it lights NOTHING where the shell cuts through a wall, because a wall is not at
        //     _GroundY;
        //   * and it cannot tell that the far arc is buried behind the wall the shell is standing
        //     next to, because the far arc was solved in closed form with no visibility test at
        //     all -- so it drew straight through geometry.
        //
        // The contact test gets all three right for free, including occlusion, because the depth
        // it reads is the NEAREST opaque surface rather than whatever the shell happens to be near.
        [HDR] _IntersectColor ("Intersection Color", Color) = (0.3, 0.7, 4, 1)
        // How close the opaque surface has to come to the shell before it counts as touching, as a
        // FRACTION OF THE SHIELD'S RADIUS. Fraction rather than world units because the same shield
        // is used at scale 0.64 and at scale 5, and a band measured in metres is a different
        // thickness on each.
        _IntersectReach ("Contact Distance (x shield radius)", Range(0.005, 0.6)) = 0.06
        _IntersectPower ("Intersection Falloff", Range(0.5, 8)) = 2
        // There is deliberately only ONE glow. The intersection of a sphere with a surface is a
        // single closed curve; its near half lies on a front face and its far half on a back face,
        // and both halves are the same light. They were separately tuned here for a while -- own
        // reach, own falloff, own intensity -- which is how the far arc ended up at a different
        // brightness and a different thickness from the near one on the same ring. Both halves now
        // run the identical contact test above and differ only in where it is evaluated.
        //
        // The ring is NOT a smooth stripe in the reference. It is a chain of bright chevrons with
        // dark notches between them, because what lights up is the SEAM where the shell turns
        // tangent to the surface it is cutting, not a uniform ribbon. Multiplying the contact term
        // by  saturate(1 - _IntersectSeam * en)  cuts the glow back to a collar hugging every cell
        // border, which turns the smooth stripe into that chain for free. 0 disables it.
        _IntersectSeam ("Intersection Seam Collar (0 = smooth band)", Range(0, 6)) = 2.0
        // How much of the near surface's refraction the FAR arc inherits. The far arc is behind the
        // shell, so the relief displaces it exactly the way it displaces the floor, and that is what
        // puts the reference's jagged teeth on the band. Taken at 1.0 the displacement is a large
        // fraction of the band's own width and tears it apart into a thin outline; this dials it.
        _IntersectBackRefract ("Far Arc Distortion", Range(0, 1.5)) = 0.5

        // How much of the shell's far side renders. This is the "backside rendering" switch: 0
        // leaves only the back intersection glow, 1 makes the far hemisphere full glass.
        _BackGlassOpacity ("Backside Glass", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ---------------------------------------------------------------------------------------
        // Back faces: the far side of the shell.
        //
        // Declared FIRST, and the composite is why. Transparent geometry is laid down far-to-near
        // as src + dst*(1 - src.a), so the far hemisphere has to be in the buffer before the near
        // shell blends over it. This pass deliberately carries GLASS ONLY -- no seam glow, no
        // groove, no chevrons. The far arc of the waterline and everything else that has to draw
        // at full strength is evaluated in the main pass instead; see the note there.
        //
        // It draws no lattice on purpose: the groove is the `en` field, so reusing it here would
        // draw the far hexes and stack a second honeycomb behind the near one. Drawing the far
        // hemisphere through the main pass would do the same, which is why the shell is
        // front-culled to begin with.
        Pass
        {
            Name "BackGlass"
            // NOT "UniversalForward". URP selects ONE pass per shader tag, so a second pass with
            // the same tag does not add to the first -- it replaces it, and the main pass stops
            // drawing entirely (the dome renders as flat backside glass with no lattice at all).
            // URP's draw settings also carry SRPDefaultUnlit, so giving the back pass its own tag
            // is what makes both passes render.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertBack
            #pragma fragment fragBack

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half   _FaceAlpha;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half4  _HexFaceColor;
                half   _HexFaceFalloff;
                half   _HexFaceOpacity;
                half   _HexFaceGlow;
                half4  _PanelTint;
                half4  _SeamTint;
                half   _PanelCoreWidth;
                half   _HexLineOffset;
                half   _HexLineWidth;
                half   _HexLineAA;
                half4  _HexColor;
                half   _HexEdgePower;
                half   _DistortStrength;
                half   _RefractClamp;
                half4  _IntersectColor;
                half   _IntersectReach;
                half   _IntersectPower;
                half   _BackGlassOpacity;
                half   _IntersectSeam;
                half   _IntersectBackRefract;
            CBUFFER_END

            TEXTURE2D(_HexPattern);
            SAMPLER(sampler_HexPattern);
            TEXTURE2D(_HexNormal);
            SAMPLER(sampler_HexNormal);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float  eyeDepth   : TEXCOORD2;
            };

            Varyings vertBack(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.eyeDepth   = -vp.positionVS.z;
                return OUT;
            }

            float2 DirToEquirect(float3 d)
            {
                return float2(atan2(d.z, d.x) * INV_TWO_PI + 0.5,
                              acos(clamp(d.y, -1.0, 1.0)) * (1.0 / PI));
            }

            half4 fragBack(Varyings IN) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // The far surface carries the same relief, so it refracts too -- but only its
                // glass is emitted, never the lattice.
                float3 ndir  = normalize(IN.positionOS);
                float2 uvPat = DirToEquirect(ndir);
                float3 nTS   = SAMPLE_TEXTURE2D(_HexNormal, sampler_HexNormal, uvPat).rgb * 2.0 - 1.0;

                float sinT    = sqrt(saturate(1.0 - ndir.y * ndir.y));
                float invSinT = 1.0 / max(sinT, 1e-4);
                float3 T = float3(-ndir.z, 0.0, ndir.x) * invSinT;
                float3 B = float3(ndir.y * ndir.x * invSinT, -sinT, ndir.y * ndir.z * invSinT);
                float3 reliefV = TransformWorldToViewDir(
                                     TransformObjectToWorldDir(T * nTS.x + B * nTS.y, false), false);

                // Same cell-relative displacement as the main pass; see the note there.
                float  gLenB   = max(length(float2(ddx(ndir.x), ddy(ndir.x))), 1e-5);
                float2 offset  = clamp(reliefV.xy * (_DistortStrength / gLenB) / _ScreenParams.xy,
                                       -_RefractClamp, _RefractClamp);
                float2 distUV  = saturate(screenUV + offset);
                float  eyeAt   = LinearEyeDepth(SampleSceneDepth(distUV), _ZBufferParams);
                distUV = (eyeAt < IN.eyeDepth) ? screenUV : distUV;
                half3 refracted = SampleSceneColor(distUV);

                // GLASS ONLY. The far half of the intersection ring used to live here, and moving it
                // out is the whole reason this pass is simple again: anything drawn here is
                // composited far-first and then multiplied by the near shell's (1 - alpha), so it
                // arrived at 18% and no intensity value could recover it. It runs the same contact
                // test as the near half now, in the main pass, from the exit point of the view ray.
                float mask = _HexFaceOpacity * _BackGlassOpacity;
                half3 col = refracted * _PanelTint.rgb * mask;
                return half4(col, mask);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForceField"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied-style blend:
            // result = glow + refractedScene * patternMask + scene * (1 - patternMask)
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // Front faces only. Drawing both sides stacks the far hemisphere's lattice on top of
            // the near one, which reads as a doubled / moire honeycomb -- the reference shows a
            // single lattice, so the back side must be culled.
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // PI, TWO_PI and INV_TWO_PI come from the render pipeline's Macros.hlsl.

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half   _FaceAlpha;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half4  _HexFaceColor;
                half   _HexFaceFalloff;
                half   _HexFaceOpacity;
                half   _HexFaceGlow;
                half4  _PanelTint;
                half4  _SeamTint;
                half   _PanelCoreWidth;
                half   _HexLineOffset;
                half   _HexLineWidth;
                half   _HexLineAA;
                half4  _HexColor;
                half   _HexEdgePower;
                half   _DistortStrength;
                half   _RefractClamp;
                half4  _IntersectColor;
                half   _IntersectReach;
                half   _IntersectPower;
                half   _BackGlassOpacity;
                half   _IntersectSeam;
                half   _IntersectBackRefract;
            CBUFFER_END

            TEXTURE2D(_HexPattern);
            SAMPLER(sampler_HexPattern);
            TEXTURE2D(_HexNormal);
            SAMPLER(sampler_HexNormal);

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
                OUT.eyeDepth   = -vp.positionVS.z; // linear eye depth of this fragment
                return OUT;
            }

            // Equirectangular lookup. This is the exact inverse of the mapping used by both
            // bakers, so there is no projected-seam or cube-face-convention ambiguity to get wrong.
            float2 DirToEquirect(float3 d)
            {
                return float2(atan2(d.z, d.x) * INV_TWO_PI + 0.5,
                              acos(clamp(d.y, -1.0, 1.0)) * (1.0 / PI));
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // ---------- 1) Fresnel outline ----------
                float faceSign = IS_FRONT_VFACE(isFront, 1.0, -1.0);
                float3 N = normalize(IN.normalWS) * faceSign;
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fres = pow(1.0 - saturate(dot(N, V)), _RimPower);

                // The shell is a plain centred unit sphere, so its world centre is the object origin
                // and its world radius is HALF the transformed x axis. Both are needed by the
                // intersection test below, which works in fractions of that radius.
                float3 centreWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float  radiusWS = 0.5 * length(float3(GetObjectToWorldMatrix()._m00_m10_m20));

                // ---------- 2) Goldberg cell field ----------
                // Sampled in object space so the lattice rotates with the shield, and by direction
                // rather than UV, so a Unity sphere and a hexasphere mesh both work at any scale.
                float3 ndir = normalize(IN.positionOS);
                float2 uvPat = DirToEquirect(ndir);
                // en: 0 exactly on a cell border, 1 at the cell centre (apothem == 1).
                float en = SAMPLE_TEXTURE2D(_HexPattern, sampler_HexPattern, uvPat).r;
                float rim = saturate(1.0 - en); // 1 on a border, 0 at a cell centre

                // Screen footprint of one cell unit. Used to keep the lines at least a pixel wide:
                // without it the lattice dissolves into a soft ramp as soon as a cell is only a
                // few pixels across, which is what reads as "blurred hex edges".
                float2 gradEn = float2(ddx(en), ddy(en));
                float  gLen   = length(gradEn);

                // ---------- 2a) Cell shading: the soft dome of the reference art ----------
                float face = pow(rim, _HexFaceFalloff);

                // ---------- 2b) Double line ----------
                // One bump per side of the border. en is an unsigned distance, so a profile in en
                // is mirrored about the border for free -- that is the whole trick.
                float d    = en - _HexLineOffset;
                // The two flanks of a border sit at |en| = _HexLineOffset, one either side of it.
                // They merge into a single wide band only once the half width reaches the offset
                // itself, because the band then swallows en = 0. The cap is that merge condition and
                // nothing else. It used to be a flat 0.6 * _HexLineOffset, which is far tighter than
                // the merge point and is why _HexLineWidth stopped responding part way up its range:
                // past 0.228 at the shipped offset the clamp ate every further increase.
                float cap  = max(_HexLineOffset * 0.98, gLen);
                float lw   = clamp(max(_HexLineWidth, gLen * _HexLineAA), gLen, cap);
                float t    = saturate(1.0 - abs(d) / lw);
                // NOTE: do not name this "line" -- it is a reserved primitive-topology keyword in
                // HLSL/Metal and the shader fails to compile with a bare syntax error.
                float edgeLine = t * t * (3.0 - 2.0 * t); // compact support and C1: no ringing

                // ---------- 2c) Refraction through the relief's real normal ----------
                // The relief is a DOUBLE RIDGE flanking every border, baked as a tangent-space
                // normal map in this same equirect frame. Sampling it gives us grad h directly.
                //
                // Why the baked normal and not a formula in `en`: any offset of the form
                // -dir * f(en) with dir = normalize(grad en) flips at every border, so a
                // single-lobed f gives one up-lobe and one down-lobe -- an S -- and a crossing
                // near-perpendicular to the border has its offset run along the background line
                // and vanish (the H / N tick). The double ridge's own gradient is not odd about
                // the border: it changes sign at the ridge crest on BOTH flanks, so the profile
                // is -, +, -, + -- a W -- at every crossing angle. Only amplitude scales with how
                // obliquely the line meets the border, which is correct.
                //
                // Decoded by hand rather than with UnpackNormal: the baker writes plain RGB into
                // an uncompressed RGBAHalf asset, while UnpackNormal assumes the DXT5nm AG layout
                // on desktop and would read the wrong channels.
                float3 nTS = SAMPLE_TEXTURE2D(_HexNormal, sampler_HexNormal, uvPat).rgb * 2.0 - 1.0;

                // Rebuild the orthonormal frame the map was baked in: T along +u (azimuth),
                // B along +v (polar), N along the direction itself.
                float sinT = sqrt(saturate(1.0 - ndir.y * ndir.y));
                float invSinT = 1.0 / max(sinT, 1e-4);
                float3 T = float3(-ndir.z, 0.0, ndir.x) * invSinT;
                float3 B = float3(ndir.y * ndir.x * invSinT, -sinT, ndir.y * ndir.z * invSinT);

                // Only the relief's perturbation is refracted. The sphere's own normal would add a
                // smooth radial lens shift, and the reference shows the grid passing through the
                // dome dead straight apart from the jogs at the seams -- so the shell is thin and
                // the base normal contributes nothing.
                float3 reliefW = T * nTS.x + B * nTS.y;
                float3 reliefV = TransformWorldToViewDir(TransformObjectToWorldDir(reliefW, false), false);

                // Displacement is expressed in CELLS, not as a fraction of the screen. gLen is
                // |d en / d pixel|, so 1/gLen is the local cell size in pixels; scaling by it makes
                // the W the same fraction of a cell at any shield scale, distance or FOV. As a fixed
                // fraction of the SCREEN it did the opposite -- on a shield at scale 5 the offset
                // stayed ~20 px while the cells grew, so a jog meant to be a fraction of one cell
                // landed across several and shredded the background grid into the speckle that
                // reads as noise. _DistortStrength is now in apothem units (1 cell = 1).
                float2 offset = reliefV.xy * (_DistortStrength / max(gLen, 1e-5)) / _ScreenParams.xy;
                offset = clamp(offset, -_RefractClamp, _RefractClamp);

                float2 distUV = saturate(screenUV + offset);

                // Don't pull in anything that is in FRONT of the shield (avoids foreground leaking)
                float eyeAtDist = LinearEyeDepth(SampleSceneDepth(distUV), _ZBufferParams);
                distUV = (eyeAtDist < IN.eyeDepth) ? screenUV : distUV;

                half3 refracted = SampleSceneColor(distUV);

                // ---------- 3) Intersection glow: CONTACT with the scene ----------
                // One depth read is the whole test. The opaque depth behind this pixel is the
                // nearest solid surface along the ray, so the shell is intersecting something
                // exactly where that surface's eye depth matches the shell's own. No plane, no
                // ground assumption, and geometry that is merely NEAR the shell -- the ball at the
                // dome's centre is a third of a radius behind it -- falls off on its own.
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float reach  = _IntersectReach * radiusWS;
                float contact = saturate(1.0 - abs(sceneZ - IN.eyeDepth) / reach);
                float collar  = saturate(1.0 - _IntersectSeam * en);
                float inter   = pow(contact, _IntersectPower) * collar;

                // The FAR arc of the same ring. Cull Back can only ever draw the half of the
                // waterline that lies on a front face, and the obvious cure -- a second
                // Cull Front pass -- has a fatal price: transparent compositing runs far-to-near,
                // so a far pass is laid down FIRST and then multiplied by the near shell's
                // (1 - _HexFaceOpacity), i.e. 0.18. The far arc arrived at a fifth of the near arc
                // and no intensity value could move it, because it was being multiplied away rather
                // than under-driven.
                //
                // So don't draw it twice. A view ray through a sphere leaves it at a point that is
                // known in closed form, so the main pass already has everything it needs: solve for
                // the exit point and run the SAME contact test there. One pass, no ordering
                // subtleties, no attenuation, and the far half of the ring is a first-class term.
                float3 rayDir   = normalize(IN.positionWS - _WorldSpaceCameraPos);

                // The far arc lives BEHIND the shell, so the near surface's relief displaces it for
                // exactly the same reason it displaces the floor -- and it has to be displaced the
                // same way, by evaluating it along the REFRACTED ray rather than this fragment's
                // own. Evaluating it along the true ray was the last thing wrong with it: the band
                // came out as a smooth ribbon while the reference's is a chain of bright teeth.
                // Those teeth are not drawn, they are the relief moving a band that is already
                // there, one ridge at a time. ddx/ddy give the per-pixel screen derivative of the
                // ray, which is all that is needed for an offset this small.
                float3 rayDirFar = normalize(rayDir
                                   + _IntersectBackRefract * ddx(rayDir) * (offset.x * _ScreenParams.x)
                                   + _IntersectBackRefract * ddy(rayDir) * (offset.y * _ScreenParams.y));
                float3 toCentre  = centreWS - _WorldSpaceCameraPos;
                float  bRay      = dot(rayDirFar, toCentre);
                float  disc      = bRay * bRay - (dot(toCentre, toCentre) - radiusWS * radiusWS);
                // A ray the relief pushed off the sphere clears the silhouette; there is no far arc
                // along it, and forcing one would paint teeth outside the dome.
                float3 pFar      = (disc > 0.0)
                                 ? _WorldSpaceCameraPos + (bRay + sqrt(disc)) * rayDirFar
                                 : centreWS + float3(0.0, -1e6, 0.0);
                // Same contact test, run against the EXIT point's depth instead of this fragment's.
                // This is also the occlusion test the old version was missing: sceneZ is the nearest
                // opaque surface, so if a wall sits between the camera and the far arc then
                // sceneZ is the wall, not the far shell, and the difference is large. A far arc
                // buried under the floor or hidden behind the wall the shell is standing next to
                // now correctly goes dark, which a closed-form exit point could never do.
                float  eyeFar   = -TransformWorldToView(pFar).z;
                // The collar is sampled at the EXIT direction, so the far half of the ring gets the
                // chevrons of the lattice it actually crosses rather than the near half's.
                float2 uvFar    = DirToEquirect(normalize(pFar - centreWS));
                float  enFar    = SAMPLE_TEXTURE2D(_HexPattern, sampler_HexPattern, uvFar).r;
                float  collarFar = saturate(1.0 - _IntersectSeam * enFar);
                // Identical reach and falloff as the near half -- same ring, same light.
                float  contactFar = (disc > 0.0)
                                  ? saturate(1.0 - abs(sceneZ - eyeFar) / reach)
                                  : 0.0;
                float  interFar   = pow(contactFar, _IntersectPower) * collarFar;

                // ---------- Combine ----------
                // Transmission and displacement must cover the SAME band: the displacement is only
                // non-zero within the relief's ridges, so if transmission decayed toward the cell
                // centre the moved background would be cut off mid-ridge and each crossing would
                // read as an isolated tick instead of a W. So transmission is flat across the
                // whole shell and the seam reads dark because of the TINT it transmits, not
                // because less gets through.
                // The edge term is scaled by view angle alone -- a constant floor lights the whole
                // lattice evenly, which the reference does not do.
                float ndv = saturate(dot(N, V));
                float edgeView = pow(saturate(1.0 - ndv), _HexEdgePower);

                float transmit = _HexFaceOpacity;
                float mask = saturate(edgeLine + transmit);
                float groove = smoothstep(0.0, _PanelCoreWidth, en);
                // Face-on, the reference's panels are CLEAN GLASS: no hexagon is drawn on them in any
                // colour, dark or bright. Applying _SeamTint across the whole groove produced exactly
                // the dark honeycomb that the reference does not have. The groove only opens where the
                // shell is turning edge-on -- which is where the reference's seams DO appear -- so the
                // groove tint is gated by the same view-angle term as the bevel highlight.
                half3 transmitTint = lerp(_PanelTint.rgb, _SeamTint.rgb, groove * edgeView);

                half3 glow = _BaseColor.rgb * _FaceAlpha
                           + _RimColor.rgb * (fres * _RimIntensity)
                           + _HexFaceColor.rgb * (face * _HexFaceGlow)
                           + _HexColor.rgb * (edgeLine * edgeView)
                           + _IntersectColor.rgb * (inter + interFar);

                half3 col = glow + refracted * transmitTint * mask;
                return half4(col, mask);
            }
            ENDHLSL
        }

    }
}
