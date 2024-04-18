Shader "DecoupledRendering/IDMapRender"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always
        Cull Off
        Blend Off
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma use_dxc
            #pragma require barycentrics
            #pragma vertex vert
            #pragma fragment frag
            // TODO: vulkan support
            #pragma only_renderers d3d11 metal

            #include "OSSUtils.cginc"

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            float4 _Jitter;
            int _SubMeshIndex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4((v.uv1 + _Jitter.xy) * 2.0f - 1.0f, 0.0f, 1.0f);
                return o;
            }

            uint frag(v2f i, uint primitiveIndex: SV_PrimitiveID) : SV_Target
            {
                uint packedPrimitiveIndex;
                PackPrimitiveIndex(primitiveIndex, (uint)_SubMeshIndex, packedPrimitiveIndex);
                return packedPrimitiveIndex;
            }

            ENDCG
        }
    }
}
