// A small HDR sphere whose brightness falls off towards the silhouette. Bloom turns it into a soft
// orb from any viewing angle, which a camera-facing quad cannot guarantee.
Shader "LithicLegion/GlowSphere"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Intensity ("HDR Intensity", Float) = 10
        _Softness ("Edge Softness", Range(0.5, 8)) = 2
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
            Name "Glow Sphere"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Softness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);
                float facing = saturate(dot(normalize(input.normalWS), viewDirection));
                float falloff = pow(facing, _Softness);

                float3 colour = _Color.rgb * _Intensity * falloff;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
