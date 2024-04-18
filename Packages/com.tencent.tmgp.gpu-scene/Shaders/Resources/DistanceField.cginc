#ifndef DISTANCE_FIELD_CGINC
#define DISTANCE_FIELD_CGINC

//! should be consistent with the host side
static const uint DF_AssetDataByteSize = 36u;

//! should be consistent with the host side
static const uint DF_InstanceDataByteSize = 124;

static const uint DF_UniqueBrickSize = 7;
static const uint DF_BrickSize = 8;
static const uint3 DF_BrickTextureSizeInBrick = uint3(32, 32, 32);
// Use this as a linear brick texture
Texture3D<float> DF_BrickTexture;
SamplerState DF_linear_clamp_sampler;

int DF_InstanceCount;

ByteAddressBuffer DF_AssetDataList;
ByteAddressBuffer DF_InstanceDataList;

struct DF_AssetData
{
    uint brickOffset;
    uint3 resolutionInBricks;

    // encoding to unorm, (2.0f * diagonalLength, -diagonalLength)
    float2 scaleBias;
    
    float3 volumeExtent;
};

struct DF_InstanceData
{
    uint assetIndex;
    float4x4 localToWorld;
    float4x4 worldToLocal;
    // World Space
    float3 volumeCenter;
    // World Space
    float3 volumeSize;
};

bool DF_IntersectRayAABB(const float3 rayOrigin, const float3 rayDir, const float3 aabbMin, const float3 aabbMax, out float2 nearFar)
{
    const float3 invDir = 1.f / rayDir;
    const float3 lo = (aabbMin - rayOrigin) * invDir;
    const float3 hi = (aabbMax - rayOrigin) * invDir;
    const float3 tmin = min(lo, hi);
    const float3 tmax = max(lo, hi);
    nearFar.x = max(0.f, max(tmin.x, max(tmin.y, tmin.z)));
    nearFar.y = min(tmax.x, min(tmax.y, tmax.z));
    return nearFar.x <= nearFar.y;
}

float DF_IntersectRayAABB(const float3 rayOrigin, const float3 rayDir, const float3 aabbMin, const float3 aabbMax, float dMin = 0.0f, float dMax = 1e30f)
{
    const float3 invDir = 1.f / rayDir;
    const float3 lo = (aabbMin - rayOrigin) * invDir;
    const float3 hi = (aabbMax - rayOrigin) * invDir;
    const float3 tmin = min(lo, hi); 
    const float3 tmax = max(lo, hi);
    float2 nearFar;
    nearFar.x = max(0.f, max(tmin.x, max(tmin.y, tmin.z)));
    nearFar.y = min(tmax.x, min(tmax.y, tmax.z));
    if (nearFar.x <= nearFar.y && nearFar.x < dMax && nearFar.y > dMin)
    {
        return nearFar.x;
    }
    else
    {
        return -1.0f;
    }
}

DF_AssetData DF_LoadAssetData(uint assetIndex)
{
    uint address = assetIndex * DF_AssetDataByteSize;
    uint4 a = DF_AssetDataList.Load4(address);
    uint4 b = DF_AssetDataList.Load4(address + 16);
    uint c = DF_AssetDataList.Load(address + 32);
    
    DF_AssetData assetData;
    assetData.brickOffset = a.x;
    assetData.resolutionInBricks = a.yzw;
    assetData.scaleBias = asfloat(b.xy);
    assetData.volumeExtent = asfloat(uint3(b.zw, c));
    return assetData;
}

DF_InstanceData DF_LoadInstanceData(uint instanceIndex)
{
    uint address = instanceIndex * DF_InstanceDataByteSize;
    uint4 a = DF_InstanceDataList.Load4(address + 16 * 0);
    uint4 b = DF_InstanceDataList.Load4(address + 16 * 1);
    uint4 c = DF_InstanceDataList.Load4(address + 16 * 2);
    uint4 d = DF_InstanceDataList.Load4(address + 16 * 3);
    uint4 e = DF_InstanceDataList.Load4(address + 16 * 4);
    uint4 f = DF_InstanceDataList.Load4(address + 16 * 5);
    uint4 g = DF_InstanceDataList.Load4(address + 16 * 6);
    uint3 h = DF_InstanceDataList.Load3(address + 16 * 7);

    DF_InstanceData instanceData;
    instanceData.assetIndex = a.x;
    instanceData.localToWorld = float4x4(
        asfloat(uint4(a.yzw, b.x)),
        asfloat(uint4(b.yzw, c.x)),
        asfloat(uint4(c.yzw, d.x)),
        float4(0.0f, 0.0f, 0.0f, 1.0f));
    instanceData.worldToLocal = float4x4(
        asfloat(uint4(d.yzw, e.x)),
        asfloat(uint4(e.yzw, f.x)),
        asfloat(uint4(f.yzw, g.x)),
        float4(0.0f, 0.0f, 0.0f, 1.0f));
    instanceData.volumeCenter = asfloat(g.yzw);
    instanceData.volumeSize = asfloat(h);

    return instanceData;
}

uint DF_Convert3DIndexTo1D(uint3 index3D, uint3 dimensions)
{
    return index3D.z * dimensions.x * dimensions.y + index3D.y * dimensions.x + index3D.x;
}

uint3 DF_Convert1DIndexTo3D(uint index1D, uint3 dimensions)
{
    uint x = index1D % dimensions.x;
    uint y = (index1D / dimensions.x) % dimensions.y;
    uint z = index1D / (dimensions.x * dimensions.y);
    return int3(x, y, z);
}

float DF_SampleDistance(float3 uvw /* [0, 1] unorm */, DF_AssetData assetData)
{
    int3 totalTexel = DF_BrickTextureSizeInBrick * DF_BrickSize;
    float3 texelSizeInUVSpace = 1.0f / float3(totalTexel);
    
    int3 totalUniqueTexel = assetData.resolutionInBricks * DF_UniqueBrickSize;
    float3 uniqueTexCoord = uvw * (float3)totalUniqueTexel;
    int3 brickIndex = (int3)(max(uniqueTexCoord, 0.0f) / (float)DF_UniqueBrickSize);
    float3 localTexCoord = (uniqueTexCoord - brickIndex * (float)DF_UniqueBrickSize);

    int3 globalBrickIndex = DF_Convert1DIndexTo3D(assetData.brickOffset + DF_Convert3DIndexTo1D(brickIndex, assetData.resolutionInBricks), DF_BrickTextureSizeInBrick);

    float3 brickSpaceUVW = (globalBrickIndex * DF_BrickSize + localTexCoord + 0.5f) * texelSizeInUVSpace;
    float encodedDistance = DF_BrickTexture.SampleLevel(DF_linear_clamp_sampler, brickSpaceUVW, 0.0f);
    float dist = encodedDistance * assetData.scaleBias.x + assetData.scaleBias.y;
    return dist;
}

float DF_SampleDistanceWorldSpace(float3 position, DF_InstanceData instanceData, DF_AssetData assetData)
{
    float3 posL = mul(instanceData.worldToLocal, float4(position, 1.0)).xyz;
    float3 uvw = (posL + assetData.volumeExtent * 0.5f) / assetData.volumeExtent;
    float scale = length(instanceData.localToWorld[0].xyz);
    float dis = DF_SampleDistance(uvw, assetData);
    return scale * dis;
}

struct DF_QueryResult
{
    bool hit;
    float t;
};

float3 DF_CalculateGradient(float3 positionW, DF_InstanceData instanceData, DF_AssetData assetData)
{
    float scale = length(instanceData.localToWorld[0].xyz);
    float3 volumeSpaceVoxelSize = scale * assetData.volumeExtent / (assetData.resolutionInBricks * DF_UniqueBrickSize);

    // Mesh distance fields have a 1 voxel border around the mesh bounds and can't bilinear sample further than 0.5 voxel from that border
    float3 voxelOffset = 0.5f * volumeSpaceVoxelSize;
    float R = DF_SampleDistanceWorldSpace(float3(positionW.x + voxelOffset.x, positionW.y, positionW.z), instanceData, assetData);
    float L = DF_SampleDistanceWorldSpace(float3(positionW.x - voxelOffset.x, positionW.y, positionW.z), instanceData, assetData);
    float F = DF_SampleDistanceWorldSpace(float3(positionW.x, positionW.y + voxelOffset.y, positionW.z), instanceData, assetData);
    float B = DF_SampleDistanceWorldSpace(float3(positionW.x, positionW.y - voxelOffset.y, positionW.z), instanceData, assetData);
    float U = DF_SampleDistanceWorldSpace(float3(positionW.x, positionW.y, positionW.z + voxelOffset.z), instanceData, assetData);
    float D = DF_SampleDistanceWorldSpace(float3(positionW.x, positionW.y, positionW.z - voxelOffset.z), instanceData, assetData);

    float3 gradient = float3(R - L, F - B, U - D);
    return gradient;
}

DF_QueryResult DF_TraceRay(float3 originW, float3 directionW, DF_InstanceData instanceData, float tmin = 0.0f, float tmax = 1e30f)
{
    float3 origin = mul(instanceData.worldToLocal, float4(originW, 1.0)).xyz;
    float3 direction = normalize(mul(transpose((float3x3)instanceData.localToWorld), directionW).xyz);

    float scale = length(instanceData.localToWorld[0].xyz);
    float tminLocal = tmin / scale;
    float tmaxLocal = tmax / scale;

    DF_AssetData assetData = DF_LoadAssetData(instanceData.assetIndex);

    DF_QueryResult result;
    result.hit = false;
    result.t = 0.0f;

    float voxelSize = assetData.volumeExtent.x / (float)(assetData.resolutionInBricks.x * DF_UniqueBrickSize + 1);
    float3 boundingBoxMin = -0.5f * assetData.volumeExtent + voxelSize;
    float3 boundingBoxMax = 0.5f * assetData.volumeExtent - voxelSize;

    float t;
    float maxDistance;
    // Inside Bounding Box
    if (all(origin > boundingBoxMin) && all(origin < boundingBoxMax))
    {
        t = 0.0f;
        float2 nearFar;
        DF_IntersectRayAABB(origin, direction, boundingBoxMin, boundingBoxMax, nearFar);

        if (nearFar.y <= tminLocal || nearFar.x >= tmaxLocal)
        {
            return result;
        }

        maxDistance = nearFar.y;
    }
    else
    {
        float2 nearFar;
        if (!DF_IntersectRayAABB(origin, direction, boundingBoxMin, boundingBoxMax, nearFar))
        {
            return result;
        }

        if (nearFar.y <= tminLocal || nearFar.x >= tmaxLocal)
        {
            return result;
        }

        t = max(nearFar.x, 0.0f);
        maxDistance = nearFar.y;
    }

    for (int i = 0; i < 128 && t < maxDistance; i++)
    {
        float3 pos = origin + t * direction;
        float3 uvw = (pos + assetData.volumeExtent * 0.5f) / assetData.volumeExtent;
        float dis = DF_SampleDistance(uvw, assetData);

        if (dis <= 0 && t >= tminLocal)
        {
            result.hit = true;
            result.t = max(scale * t, 0.0f);
            
            return result;
        }

        if (t >= tmaxLocal)
        {
            return result;
        }

        t += dis;
    }

    return result;
}

DF_QueryResult DF_TraceScene(float3 originW, float3 directionW, float tmin = 0.0f)
{
    DF_QueryResult result;
    result.hit = false;
    result.t = 0.0f;

    for (int i = 0; i < DF_InstanceCount; i++)
    {
        DF_InstanceData instanceData = DF_LoadInstanceData(i);
        DF_QueryResult queryResult = DF_TraceRay(originW, directionW, instanceData, tmin);
        if (queryResult.hit)
        {
            result.hit = true;
            if (result.t > queryResult.t)
            {
                result.t = queryResult.t;
            }
        }
    }

    return result;
}

#endif // DISTANCE_FIELD_CGINC