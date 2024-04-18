#ifndef MESH_CARD_CGINC
#define MESH_CARD_CGINC

#include "DistanceField.cginc"
#include "Packing.cginc"

struct MeshCard
{
    uint2 size;
    uint2 position;
};

// Key: Instance Index; Value: Base Offset
StructuredBuffer<uint> _MeshCardIndirection;
StructuredBuffer<uint4> _MeshCardData;

// RGBA32
Texture2D<float4> _MeshCardAtlasBaseColor; // xyz: baseColor, w: none
// RGBA32 RGBM Encoding
Texture2D<float4> _MeshCardAtlasEmissive; // xyz: emissive, w: none
// R16Uint
Texture2D<uint> _MeshCardAtlasNormal;
// R10G11B11Unorm
Texture2D<uint> _MeshCardAtlasPosition;
// RGBA32, RGBM Encoding
Texture2D<float4> _MeshCardAtlasIrradiance;

uint2 StochasticBilinear(float2 st, float u /* random sample */)
{
// Pointer
#if 0
    return st;
#else
    int s = (int)floor(st[0]);
    int t = (int)floor(st[1]);
    float ds = st[0] - floor(st[0]);
    float dt = st[1] - floor(st[1]);
    if (u < ds) 
    {
        ++s;
        u /= ds;
    }
    else
    {
        u = (u - ds) / (1 - ds);
    }
    if (u < dt) 
    {
        ++t;
        u /= dt;
    } 
    else
    {
        u = (u - dt) / (1 - dt);
    }

    return uint2(s, t);
#endif
}

MeshCard LoadMeshCard(uint meshCardIndex)
{
    uint4 data = _MeshCardData[meshCardIndex];
    MeshCard meshCard;
    meshCard.size = data.xy;
    meshCard.position = data.zw;
    return meshCard;
}

struct MeshCardAttribute
{
    float3 baseColor;
    float3 emissive;
    float3 irradiance;
};

uint2 __ComputeMeshCardPixel(uint instanceIndex, float3 pos, float3 normal, float u)
{
    DF_InstanceData instanceData = DF_LoadInstanceData(instanceIndex);
    DF_AssetData assetData = DF_LoadAssetData(instanceData.assetIndex);
    pos = mul(instanceData.worldToLocal, float4(pos, 1.0)).xyz;
    normal = normalize(mul(transpose((float3x3)instanceData.localToWorld), normal));

    const float3 localDirections[6] =
        {
            float3(-1.0f, 0.0f, 0.0f),
            float3(1.0f, 0.0f, 0.0f),
            float3(0.0f, -1.0f, 0.0f),
            float3(0.0f, 1.0f, 0.0f),
            float3(0.0f, 0.0f, -1.0f),
            float3(0.0f, 0.0f, 1.0f),
        };

    int faceIndex;
    float3 absNormal = abs(normal);
    float maxComp = max(max(absNormal.x, absNormal.y), absNormal.z);
    if (maxComp == absNormal.x)
    {
        faceIndex = normal.x < 0 ? 0 : 1;
    }
    else if (maxComp == absNormal.y)
    {
        faceIndex = normal.y < 0 ? 2 : 3;
    }
    else if (maxComp == absNormal.z)
    {
        faceIndex = normal.z < 0 ? 4 : 5;
    }

    MeshCard meshCard = LoadMeshCard(_MeshCardIndirection[instanceIndex] + faceIndex);
    float3 uvw = saturate((pos + assetData.volumeExtent * 0.5f) / assetData.volumeExtent);

    float2 uv;
    int compExcept = faceIndex / 2;
    // Left Right
    if (compExcept == 0)
    {
        uv = uvw.yz;
    }
    // Bottom Top
    else if (compExcept == 1)
    {
        uv = uvw.xz;
    }
    // Backward Forward
    else
    {
        uv = uvw.xy;
    }

    float2 position = meshCard.position + uv * (meshCard.size - 1);
    uint2 pixel = StochasticBilinear(position, u);
    return pixel;
}

MeshCardAttribute SampleMeshCard(uint instanceIndex, float3 pos, float3 normal, float u)
{
    uint2 pixel = __ComputeMeshCardPixel(instanceIndex, pos, normal, u);

    MeshCardAttribute result;
    result.baseColor = _MeshCardAtlasBaseColor[pixel].xyz;
    result.emissive = _MeshCardAtlasEmissive[pixel].xyz;
    result.irradiance = _MeshCardAtlasIrradiance[pixel].xyz;

    return result;
}

float3 SampleMeshCardNormal(uint instanceIndex, float3 pos, float3 normal, float u)
{
    uint2 pixel = __ComputeMeshCardPixel(instanceIndex, pos, normal, u);
    return decodeNormal2x8(_MeshCardAtlasNormal[pixel]);
}

float3 SampleMeshCardPosition(uint instanceIndex, float3 pos, float3 normal, float u)
{
    uint2 pixel = __ComputeMeshCardPixel(instanceIndex, pos, normal, u);
    float3 uvw = UnpackR10G11B11UnormFromUINT(_MeshCardAtlasPosition[pixel]);

    DF_InstanceData instanceData = DF_LoadInstanceData(instanceIndex);
    DF_AssetData assetData = DF_LoadAssetData(instanceData.assetIndex);

    float3 position = (uvw - 0.5f) * assetData.volumeExtent;
    position = mul(instanceData.localToWorld, float4(position, 1.0)).xyz;
    
    return position;
}

float3 SampleMeshCardIrradiance(uint instanceIndex, float3 pos, float3 normal, float u)
{
    uint2 pixel = __ComputeMeshCardPixel(instanceIndex, pos, normal, u);
    return _MeshCardAtlasIrradiance[pixel].xyz;
}

#endif // MESH_CARD_CGINC