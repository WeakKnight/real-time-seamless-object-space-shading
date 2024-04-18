#ifndef MESH_CARD_RENDER_CGINC
#define MESH_CARD_RENDER_CGINC

int _FaceIndex;
float4 _VolumeExtent;

float4x4 _LocalToWorld;
float4x4 _WorldToLocal;
float4x4 _WorldToVolume;

struct MeshCardFragmentOutput
{
    float4 baseColor : SV_Target0;
    float4 emissive : SV_Target1;
    uint normal : SV_Target2;
    uint position : SV_Target3;
};

float4 MeshCardClipSpacePos(float3 pos /*local position*/, float3 normal)
{
    const float3 localDirections[6] =
    {
        float3(-1.0f, 0.0f, 0.0f),
        float3(1.0f, 0.0f, 0.0f),
        float3(0.0f, -1.0f, 0.0f),
        float3(0.0f, 1.0f, 0.0f),
        float3(0.0f, 0.0f, -1.0f),
        float3(0.0f, 0.0f, 1.0f),
    };

    pos = mul(_LocalToWorld, float4(pos, 1.0)).xyz;
    pos = mul(_WorldToVolume, float4(pos, 1.0)).xyz;
    float3 uvw = saturate((pos + _VolumeExtent.xyz * 0.5f) / _VolumeExtent.xyz);

    float2 uv;
    float depth;

    int compExcept = _FaceIndex / 2;
    // Left Right
    if (compExcept == 0)
    {
        uv = uvw.yz;
        depth = ((_FaceIndex % 2) == 0) ? uvw.x : (1.0f - uvw.x);
    }
    // Bottom Top
    else if (compExcept == 1)
    {
        uv = uvw.xz;
        depth = ((_FaceIndex % 2) == 0) ? uvw.y : (1.0f - uvw.y);
    }
    // Backward Forward
    else
    {
        uv = uvw.xy;
        depth = ((_FaceIndex % 2) == 0) ? uvw.z : (1.0f - uvw.z);
    }

    uv.y = 1.0f - uv.y;

    return float4(uv * 2.0f - 1.0f, clamp(1.0f - depth, 0.0001f, 0.9999f), 1.0f);
}

#endif