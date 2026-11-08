Shader "Seb.Vis.Text/TextShader"
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

            struct InstanceData
            {
                float2 pos;
                float2 size;
                float4 col;
                float fontSize;
                int dataOffset;
                int groupIndex;
            };

            struct TextGroup
            {
                float2 offset;
                float2 maskMin;
                float2 maskMax;
                int useScreenSpace;
            };

            // Data stored for every instance (each individual glyph that needs to be rendered)
            StructuredBuffer<InstanceData> PerInstanceData;
            // Position data for the bezier curves
            StructuredBuffer<float2> BezierData;
            // Metadata for each glyph: bezier data offset, num contours, contour length/s
            StructuredBuffer<int> GlyphMetaData;
            // Info about each block of text being rendered: masks, position offset, etc
            StructuredBuffer<TextGroup> TextGroups;

            #define BEZIER_POINT(index) BezierData[index]
            #define GLYPH_META(index) GlyphMetaData[index]
            #include "TextCommon.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                InstanceData instance = PerInstanceData[instanceID];
                TextGroup group = TextGroups[instance.groupIndex];

                v2f o;
                float2 aaPad = CalculateWorldUnitsPerPixel() * 2;
                float2 quadSize = instance.size + aaPad;

                float2 offset = instance.pos + group.offset;
                float2 worldVertPos = v.vertex * quadSize * instance.fontSize + offset;

                o.vertex = WorldToClipPos(worldVertPos, group.useScreenSpace, ScreenSize);

                o.pos = -quadSize / 2 + quadSize * v.uv;
                o.worldPos = worldVertPos;
                o.dataOffset = instance.dataOffset;
                o.mask = float4(group.maskMin, group.maskMax);
                o.col = instance.col;

                return o;
            }
            ENDCG
        }
    }
}