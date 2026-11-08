Shader "Vis/Draw"
{
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
        }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "DrawCommon.hlsl"
            #include "DrawShapes.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            // Must match Seb.Vis.Internal.ShapeData
            struct ShapeData
            {
                int type; // shape type
                float2 a; // arbitrary data A (depends on shape type)
                float2 b; // arbitrary data B (depends on shape type)
                float param;
                float4 col;
                float4 maskMinMax;
            };

            StructuredBuffer<ShapeData> InstanceData;
            uint InstanceOffset;

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                ShapeData shape = InstanceData[instanceID + InstanceOffset];
                ShapeVertexData instance;
                instance.type = shape.type;
                instance.a = shape.a;
                instance.b = shape.b;
                instance.param = shape.param;
                instance.col = shape.col;
                instance.maskMinMax = shape.maskMinMax;
                // A triangle's third corner is packed into param as two halves.
                uint packedC = asuint(shape.param);
                instance.triC = float2(f16tof32(packedC >> 16), f16tof32(packedC));

                ShapeVertexInput input;
                input.vertex = v.vertex;
                input.uv = v.uv;
                return VertShape(input, instance);
            }
            ENDCG
        }
    }
}