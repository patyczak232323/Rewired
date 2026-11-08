Shader "Vis/DrawMesh"
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
            #pragma target 3.5

            #include "DrawCommon.hlsl"
            #include "DrawShapes.hlsl"

            // Every vertex carries its shape, written by InstancedDrawer's mesh path.
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 ab : TEXCOORD1; // a.xy, b.xy
                float4 typeParamC : TEXCOORD2; // type, param, triangle corner c.xy
                float4 col : TEXCOORD3;
                float4 mask : TEXCOORD4; // mask min.xy, max.xy
            };

            v2f vert(appdata v)
            {
                ShapeVertexData instance;
                instance.type = (int)round(v.typeParamC.x);
                instance.a = v.ab.xy;
                instance.b = v.ab.zw;
                instance.param = v.typeParamC.y;
                instance.col = v.col;
                instance.maskMinMax = v.mask;
                instance.triC = v.typeParamC.zw;

                ShapeVertexInput input;
                input.vertex = v.vertex;
                input.uv = v.uv;
                return VertShape(input, instance);
            }
            ENDCG
        }
    }
}