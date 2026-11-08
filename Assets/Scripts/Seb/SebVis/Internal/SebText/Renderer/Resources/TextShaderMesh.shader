Shader "Seb.Vis.Text/TextShaderMesh"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent"
        }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            // Control points (xy) and glyph metadata (x), one per texel, row by row.
            Texture2D<float4> BezierTex;
            Texture2D<float4> GlyphMetaTex;
            int DataTexWidth;

            float2 LoadBezierPoint(int index)
            {
                return BezierTex.Load(int3(index % DataTexWidth, index / DataTexWidth, 0)).xy;
            }

            int LoadGlyphMeta(int index)
            {
                return (int)round(GlyphMetaTex.Load(int3(index % DataTexWidth, index / DataTexWidth, 0)).x);
            }

            #define BEZIER_POINT(index) LoadBezierPoint(index)
            #define GLYPH_META(index) LoadGlyphMeta(index)
            #include "TextCommon.hlsl"

            // Every vertex carries its glyph, written by TextRenderer's mesh path.
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 posSize : TEXCOORD1; // glyph centre (with the text's offset) xy, size zw
                float4 fontData : TEXCOORD2; // font size, data offset, use screen space
                float4 col : TEXCOORD3;
                float4 mask : TEXCOORD4; // mask min.xy, max.xy
            };

            v2f vert(appdata v)
            {
                v2f o;
                float2 aaPad = CalculateWorldUnitsPerPixel() * 2;
                float2 quadSize = v.posSize.zw + aaPad;

                float2 worldVertPos = v.vertex * quadSize * v.fontData.x + v.posSize.xy;
                o.vertex = WorldToClipPos(worldVertPos, v.fontData.z > 0.5, ScreenSize);

                o.pos = -quadSize / 2 + quadSize * v.uv;
                o.worldPos = worldVertPos;
                o.dataOffset = (int)round(v.fontData.y);
                o.mask = v.mask;
                o.col = v.col;
                return o;
            }
            ENDCG
        }
    }
}