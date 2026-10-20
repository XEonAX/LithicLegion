// Additive laser ribbon: a white-hot core inside a coloured halo, both as a function of the UV across
// the line. No texture is needed, so a beam of any width stays crisp at every distance.
Shader "LithicLegion/Beam"
{
    Properties
    {
        _Color ("Halo Colour", Color) = (0.2, 0.5, 1, 1)
        _CoreColor ("Core Colour", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 8
        _GlowPower ("Halo Falloff", Float) = 1.8
        _CorePower ("Core Tightness", Float) = 10
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CoreColor;
                float _Intensity;
                float _GlowPower;
                float _CorePower;
                float _Fade;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float distanceFromCentre = abs(input.uv.y * 2.0 - 1.0);
                float halo = pow(saturate(1.0 - distanceFromCentre), _GlowPower);
                float core = pow(saturate(1.0 - distanceFromCentre), _CorePower);

                float3 colour = (_Color.rgb * halo + _CoreColor.rgb * core) * _Intensity * _Fade;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
