// Bake-only shader: writes the mesh's per-corner vertex colour (HexData) straight to the
// render target with no lighting, no fog, no tonemapping and no gamma conversion.
// Used with Camera.RenderToCubemap from the mesh bounds centre, so every ray leaving the
// centre records the cell data in the direction it leaves.
//
// The pass is deliberately untagged: URP's UniversalRenderer includes "SRPDefaultUnlit" in
// its shader tag list, so an untagged pass is drawn by URP without needing URP includes.
Shader "Hidden/HexDataBake"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            Name "HexDataBake"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
