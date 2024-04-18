#ifndef RENDER_TASK_PRELUDE_CGINC
#define RENDER_TASK_PRELUDE_CGINC

#include "RenderConfig.hlsl"
#include "InstanceData.cginc"
#include "OSSUtils.cginc"
#include "GlobalShaderVariables.hlsl"
#include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/RandomSampler.cginc"
#include "ShadelAllocator.cginc"

#include "Halfedge.hlsl"

#include "MeshUtils.cginc"

struct GeometrySample
{
    float3 position;
    float3 normal;
    float4 tangent;
    float2 uv0;
    float2 uv0dx;
    float2 uv0dy;
};

GeometrySample ConstructGeometrySample(uint primitiveIndex, float2 triangleUV)
{
    uint subMeshIndex = 0;

    uint3 triangleIndices = FetchTriangleIndices(subMeshIndex, primitiveIndex);
    VertexData vsIn0 = FetchVertex(subMeshIndex, triangleIndices.x);
    VertexData vsIn1 = FetchVertex(subMeshIndex, triangleIndices.y);
    VertexData vsIn2 = FetchVertex(subMeshIndex, triangleIndices.z);

#define INTERPOLATE_ATTRIB(A0, A1, A2, BARYCENTRIC_COORDINATES) (A0 * BARYCENTRIC_COORDINATES.x + A1 * BARYCENTRIC_COORDINATES.y + A2 * BARYCENTRIC_COORDINATES.z)

    float3 barycentric = float3(1 - triangleUV.x - triangleUV.y, triangleUV.x, triangleUV.y);
    float3 position = INTERPOLATE_ATTRIB(vsIn0.position, vsIn1.position, vsIn2.position, barycentric);
    float3 normal = INTERPOLATE_ATTRIB(vsIn0.normal, vsIn1.normal, vsIn2.normal, barycentric);
#undef INTERPOLATE_ATTRIB

    GeometrySample geomSample;
    geomSample.position = mul(_ObjectTransformation, float4(position, 1.0)).xyz;
    geomSample.normal = normalize(mul(transpose((float3x3)_ObjectInverseTransformation), normal));
    return geomSample;
}

GeometrySample GetGeometrySample(uint packedPrimitiveIndex, float3 bary, float3 barydx, float3 barydy)
{
    uint primitiveIndex, subMeshIndex;
    UnpackPrimitiveIndex(packedPrimitiveIndex, primitiveIndex, subMeshIndex);
    uint3 triangleIndices = FetchTriangleIndices(subMeshIndex, primitiveIndex);

    VertexData vertexA = FetchVertex(subMeshIndex, triangleIndices.x);
    VertexData vertexB = FetchVertex(subMeshIndex, triangleIndices.y);
    VertexData vertexC = FetchVertex(subMeshIndex, triangleIndices.z);

    float2 uv0 = interpolate(vertexA.uv0, vertexB.uv0, vertexC.uv0, bary);
    float2 uv0dx = abs(interpolate(vertexA.uv0, vertexB.uv0, vertexC.uv0, barydx) - uv0);
    float2 uv0dy = abs(interpolate(vertexA.uv0, vertexB.uv0, vertexC.uv0, barydy) - uv0);

    float3 posL = interpolate(vertexA.position, vertexB.position, vertexC.position, bary);
    float3 normalL = interpolate(vertexA.normal, vertexB.normal, vertexC.normal, bary);

    GeometrySample geometrySample;
    geometrySample.position = mul(_ObjectTransformation, float4(posL, 1.0)).xyz;
    geometrySample.normal = normalize(mul(transpose((float3x3)_ObjectInverseTransformation), normalL));
    geometrySample.uv0 = uv0;
    geometrySample.uv0dx = uv0dx;
    geometrySample.uv0dy = uv0dy;
    return geometrySample;
}

struct TaskInfo
{
    uint2 shadelLocation;
    uint instanceIndex;
};

TaskInfo UnpackTaskInfo(uint val)
{
    TaskInfo task;
    task.shadelLocation = UnpackUINTToR16UG16U(val.x);

    return task;
}

static const uint _ShadelSize = 8u;
static const uint _PayloadBufferTileSize = 16;

int _HalfedgeMesh;

int _RemapBufferWidth;
int _RemapBufferHeight;

int _StorageBufferWidth;
int _StorageBufferHeight;

int _VirtualShadelTextureWidth;
int _VirtualShadelTextureHeight;

int _FrameIndex;
int _ShadingInterval;
int _PrevShadingInterval;

#ifdef __SLANG__
#define __MUTABLE__ [mutating]
#else
#define __MUTABLE__
#endif

ByteAddressBuffer _RemapBuffer;
ByteAddressBuffer _OccupancyBuffer;

StructuredBuffer<uint2> _TaskOffsetBuffer;
StructuredBuffer<uint> _TaskBuffer;

ByteAddressBuffer _HistoryRemapBuffer;
ByteAddressBuffer _HistoryOccupancyBuffer;

RWTexture2D<float4> _StorageBuffer;
Texture2D<float4> _HistoryStorageBuffer;

RWByteAddressBuffer _PayloadBuffer;
ByteAddressBuffer _HistoryPayloadBuffer;

RWByteAddressBuffer _PersistentPayloadBuffer;
ByteAddressBuffer _PersistentHistoryPayloadBuffer;

RWTexture2D<uint> _AccumulatedFrameCountBuffer;
Texture2D<uint> _HistoryAccumulatedFrameCountBuffer;

uint2 GetStorageBufferBaseLocation(uint shadelAddress)
{
    uint storageBufferShadelDimension = _StorageBufferWidth / _ShadelSize;
    return uint2(shadelAddress % storageBufferShadelDimension, shadelAddress / storageBufferShadelDimension) * _ShadelSize;
}

/*
Htex Helper API
*/
uint2 GetAtlasSize(int lod)
{
    uint2 sz = uint2(_HtexTextureQuadWidth, _HtexTextureQuadHeight);
    sz = sz >> lod;

    return uint2(_HtexTextureNumQuadsX, _HtexTextureNumQuadsY) * sz;
}

uint2 FindQuadLocation(uint2 locationInsideChart, uint htexTextureQuadWidth, uint htexTextureQuadHeight)
{
    return locationInsideChart / uint2(htexTextureQuadWidth, htexTextureQuadHeight);
}

uint QuadLocationToHalfEdgeId(uint2 quadLocation)
{
    return Convert2DTo1D(quadLocation, _HtexTextureNumQuadsX);
}

/*
*/
struct DR_Shadel
{
    uint layerLevel;
    uint2 shadelLocation;

    uint2 baseAddress;
    uint2 historyBaseAddress;

    bool historyValid;

    static DR_Shadel Create(uint2 shadelLocation)
    {
        DR_Shadel shadel;
        shadel.shadelLocation = shadelLocation;
        shadel.layerLevel = GetLODLevelFromTexel(_VirtualShadelTextureHeight, (int)shadelLocation.x * 8, (int)shadelLocation.y * 8);
        return shadel;
    }

    bool IsPersistent()
    {
        if (_EnablePersistentLayer == 1)
        {
            return layerLevel == _VRT_PersistentLayerLODIndex;
        }
        else
        {
            return false;
        }
    }

    bool GetPersistentTexel(uint2 texel, out DR_Shadel persistentShadel, out uint2 persistentTexel)
    {
        if (IsPersistent())
        {
            return false;
        }

        const uint2 virtualSpaceLocation = shadelLocation * _VRT_ShadelSize + texel;

        uint2 texelLod0 = LODToOriginalTexel(_VirtualShadelTextureHeight, layerLevel, virtualSpaceLocation.x, virtualSpaceLocation.y);
        const uint2 persistentLocation = GetTexelFromLODLevel(_VirtualShadelTextureHeight, _VRT_PersistentLayerLODIndex, texelLod0.x, texelLod0.y);
        const uint2 persistentShadelLocation = persistentLocation / _VRT_ShadelSize;
        
        persistentShadel = DR_Shadel::Create(persistentShadelLocation);
        persistentShadel.Build();
        persistentTexel = persistentLocation % _VRT_ShadelSize;

        return true;
    }
    
    bool IsPayloadHistoryValid()
    {
        return IsPersistent() || historyValid;
    }

    __MUTABLE__ bool Build(bool requireCurrentPresent = true)
    {
        if (!__FindMappedAddress(false, baseAddress))
        {
            if (requireCurrentPresent)
            {
                return false;
            }
        }

        historyValid = __FindMappedAddress(true, historyBaseAddress);

        return true;
    }

    float4 ReadStorage(uint2 texel, bool isTemporal)
    {
        if (!isTemporal)
        {
            return _StorageBuffer[baseAddress + texel];
        }
        else if (historyValid)
        {
            return _HistoryStorageBuffer[historyBaseAddress + texel];
        }
        else
        {
            return 0.0f;
        }
    }

    RandomSequence MakeRandomSequence(uint2 texelCoord)
    {
        const uint shadingFrameIndex = _FrameIndex / (uint)_ShadingInterval;
        uint2 virtualLocation = shadelLocation * 8u + texelCoord;
        const uint seed = (virtualLocation.x + (uint)(virtualLocation.y * _VirtualShadelTextureHeight));
        RandomSequence randomSequence;
        RandomSequence_Initialize(randomSequence, seed, shadingFrameIndex);
        return randomSequence;
    }

    uint PayloadByteSize()
    {
        return 48u;
    }

    uint __LinearizeIndexScanline(uint2 texel, uint width)
    {
        return texel.x + texel.y * width;
    }

    uint __LinearizeIndexZOrder(uint2 texel, uint width)
    {
        uint x = texel.x;
        uint y = texel.y;
        uint tileSize = _PayloadBufferTileSize;

        // Calculate tile indices
        uint tileX = x / tileSize;
        uint tileY = y / tileSize;

        // Calculate pixel indices within the tile
        uint pixelX = x % tileSize;
        uint pixelY = y % tileSize;

        // Calculate 1D index for the tile
        uint tileIndex1D = __EncodeMorton2D(uint2(tileX, tileY));

        // Calculate 1D index for the pixel within the tile
        uint pixelIndex1D = __EncodeMorton2D(uint2(pixelX, pixelY));

        // Combine the tile index and the pixel index to get the final 1D index
        // Each tile contains tileSize * tileSize pixels
        return (tileIndex1D * tileSize * tileSize) + pixelIndex1D;
    }

    uint __PayloadAddressHelper(uint2 texel, uint payloadByteSize, uint offset, uint2 _baseAddress, uint payloadBufferWidth)
    {
        uint2 location = _baseAddress + texel;
        uint address = __LinearizeIndexZOrder(location, payloadBufferWidth) * payloadByteSize + offset;
        return address;
    }

    uint __PayloadAddress(uint2 texel, uint payloadByteSize, uint offset, bool isTemporal)
    {
        const uint storageBufferShadelWidth = _StorageBufferWidth / _ShadelSize;
        const uint payloadBufferWidth = storageBufferShadelWidth * _ShadelSize;

        return __PayloadAddressHelper(texel, payloadByteSize, offset, (isTemporal ? historyBaseAddress : baseAddress), payloadBufferWidth);
    }

    uint2 __GetPersistentBaseLocation()
    {
        const uint2 globalBase = shadelLocation * 8u;
        uint2 globalBaseLod0 = LODToOriginalTexel(_VirtualShadelTextureHeight, layerLevel, globalBase.x, globalBase.y);
        return globalBaseLod0 >> layerLevel;
    }

    uint __PersistentPayloadAddress(uint2 texel, uint offset)
    {
        const uint payloadBufferWidth = _VirtualShadelTextureHeight >> _VRT_PersistentLayerLODIndex;
        return __PayloadAddressHelper(texel, PayloadByteSize(), offset, __GetPersistentBaseLocation(), payloadBufferWidth);
    }

    void WritePayload(uint2 texel, uint offset, uint data)
    {
        if (IsPersistent())
        {
            uint address = __PersistentPayloadAddress(texel, offset);
            _PersistentPayloadBuffer.Store(address, data);
        }
        else
        {
            uint address = __PayloadAddress(texel, PayloadByteSize(), offset, false);
            _PayloadBuffer.Store(address, data);
        }
    }

    void WritePayload2(uint2 texel, uint offset, uint2 data)
    {
        if (IsPersistent())
        {
            uint address = __PersistentPayloadAddress(texel, offset);
            _PersistentPayloadBuffer.Store2(address, data);
        }
        else
        {
            uint address = __PayloadAddress(texel, PayloadByteSize(), offset, false);
            _PayloadBuffer.Store2(address, data);
        }
    }

    void WritePayload3(uint2 texel, uint offset, uint3 data)
    {
        if (IsPersistent())
        {
            uint address = __PersistentPayloadAddress(texel, offset);
            _PersistentPayloadBuffer.Store3(address, data);
        }
        else
        {
            uint address = __PayloadAddress(texel, PayloadByteSize(), offset, false);
            _PayloadBuffer.Store3(address, data);
        }
    }

    void WritePayload4(uint2 texel, uint offset, uint4 data)
    {
        if (IsPersistent())
        {
            uint address = __PersistentPayloadAddress(texel, offset);
            _PersistentPayloadBuffer.Store4(address, data);
        }
        else
        { 
            uint address = __PayloadAddress(texel, PayloadByteSize(), offset, false);
            _PayloadBuffer.Store4(address, data);
        }
    }

#define READ_PAYLOAD_N(N)                                                              \
    do                                                                                 \
    {                                                                                  \
        uint address = __PayloadAddress(texel, PayloadByteSize(), offset, isTemporal); \
        if (!isTemporal)                                                               \
        {                                                                              \
            return _PayloadBuffer.Load##N(address);                                    \
        }                                                                              \
        else if (historyValid)                                                         \
        {                                                                              \
            return _HistoryPayloadBuffer.Load##N(address);                             \
        }                                                                              \
        else                                                                           \
        {                                                                              \
            return ~0u;                                                                \
        }                                                                              \
    } while (false)

#define READ_PERSISTENT_PAYLOAD_N(N)                                 \
    do                                                               \
    {                                                                \
        uint address = __PersistentPayloadAddress(texel, offset);    \
        if (!isTemporal)                                             \
        {                                                            \
            return _PersistentPayloadBuffer.Load##N(address);        \
        }                                                            \
        else                                                         \
        {                                                            \
            return _PersistentHistoryPayloadBuffer.Load##N(address); \
        }                                                            \
    } while (false)

    uint ReadPayload(uint2 texel, uint offset, bool isTemporal)
    {
        if (IsPersistent())
        {
            uint address = __PersistentPayloadAddress(texel, offset);
            if (!isTemporal)
            {
                return _PersistentPayloadBuffer.Load(address);
            }
            else
            {
                return _PersistentHistoryPayloadBuffer.Load(address);
            }
        }
        else
        {
            uint address = __PayloadAddress(texel, PayloadByteSize(), offset, isTemporal);
            if (!isTemporal)
            {
                return _PayloadBuffer.Load(address);
            }
            else if (historyValid)
            {
                return _HistoryPayloadBuffer.Load(address);
            }
            //! TODO: retrieve data from persistent layer
            else
            {
                return ~0u;
            }
        }
    }

    uint2 ReadPayload2(uint2 texel, uint offset, bool isTemporal)
    {
        if (IsPersistent())
        {
            READ_PERSISTENT_PAYLOAD_N(2);
        }
        else
        {
            READ_PAYLOAD_N(2);
        }

        return ~0u;
    }

    uint3 ReadPayload3(uint2 texel, uint offset, bool isTemporal)
    {
        if (IsPersistent())
        {
            READ_PERSISTENT_PAYLOAD_N(3);
        }
        else
        {
            READ_PAYLOAD_N(3);
        }

        return ~0u;
    }

    uint4 ReadPayload4(uint2 texel, uint offset, bool isTemporal)
    {
        if (IsPersistent())
        {
            READ_PERSISTENT_PAYLOAD_N(4);
        }
        else
        {
            READ_PAYLOAD_N(4);
        }

        return ~0u;
    }

    void WriteStorage(uint2 texel, float4 data)
    {
        _StorageBuffer[baseAddress + texel] = data;
    }

    void WriteAccumulatedCount(uint2 texel, uint data)
    {
        _AccumulatedFrameCountBuffer[baseAddress + texel] = data;
    }

    uint ReadAccumulatedCount(uint2 texel, bool isTemporal)
    {
        if (!isTemporal)
        {
            return _AccumulatedFrameCountBuffer[baseAddress + texel];
        }
        else if (historyValid)
        {
            return _HistoryAccumulatedFrameCountBuffer[historyBaseAddress + texel];
        }
        //! TODO: retrieve data from persistent layer
        else
        {
            return 0u;
        }
    }

    bool CheckAndFallbackToHistory(uint2 texelCoord)
    {
        if (_PrevShadingInterval != _ShadingInterval)
        {
            return false;
        }

        bool shouldFallback = ((_FrameIndex % (uint)_ShadingInterval) != 0) && historyValid;

        // if (shouldFallback)
        // {
        //     uint2 virtualLocation = shadelLocation * 8u + texelCoord;
        //     int2 higherVirtualLocation = GetHigherLODTexel(_VirtualShadelTextureHeight, layerLevel, virtualLocation.x, virtualLocation.y);
        //     int2 higherShadelLocation = higherVirtualLocation / 8;
            
        //     DR_Shadel higherShadel = DR_Shadel::Create(higherShadelLocation);
        //     if (higherShadel.Build(true) && !higherShadel.historyValid)
        //     {
        //         shouldFallback = false;
        //     }
        //     else if (layerLevel >= 1)
        //     {
        //         int2 lowerVirtualLocation = GetLowerLODTexel(_VirtualShadelTextureHeight, layerLevel, virtualLocation.x, virtualLocation.y);
        //         int2 lowerShadelLocation = lowerVirtualLocation / 8;

        //         DR_Shadel lowerShadel = DR_Shadel::Create(shadelLocation);
        //         if (!lowerShadel.Build(true) && !lowerShadel.historyValid)
        //         {
        //             return false;
        //         }
        //     }
        // }

        if (shouldFallback)
        {
            float4 historySample = ReadStorage(texelCoord, true);
            WriteStorage(texelCoord, historySample);
            
            [unroll]
            for (int i = 0; i < 3; i++)
            {
                uint4 historyPayload = ReadPayload4(texelCoord, i * 16u, true);
                WritePayload4(texelCoord, i * 16u, historyPayload);
            }
        }

        return shouldFallback;
    }

    GeometrySample ReadGeometrySample(uint2 texelCoord, float2 jitter, out float spatialSearchRadius)
    {
        const float chartSize = GetChartSize() >> layerLevel;
        const float chartTexelSize = 1.0f / chartSize;

        const int2 chartPosition = GetTexelFromLODLevel(_VirtualShadelTextureHeight, layerLevel, GetChartLocation().x, GetChartLocation().y);

        const uint2 virtualSpaceLocation = shadelLocation * 8 + texelCoord;
        const uint2 locationInsideChart = (virtualSpaceLocation - chartPosition);
        float2 uv1 = ((float2)(locationInsideChart)*chartTexelSize);

        float2 duv_dx = ddx(uv1);
        float2 duv_dy = ddy(uv1);
        float2 max_duv = max(abs(duv_dx), abs(duv_dy));
        spatialSearchRadius = max(max_duv.x, max_duv.y) * _ObjectSpaceSpatialReuseSearchRadius * chartSize;

        // consider jitter
        {
            jitter = jitter * chartTexelSize;
            uv1 += jitter;
        }

        const uint2 idMapLocation = (uint2)(uv1 * _IDMapSize);
        const uint primitiveIndex = _IDMap[idMapLocation];

        float3 barydx = GetBarycentric(uv1 + float2(chartTexelSize, 0.0f), primitiveIndex);
        float3 barydy = GetBarycentric(uv1 + float2(0.0f, chartTexelSize), primitiveIndex);
        float3 barycentric = GetBarycentric(uv1, primitiveIndex);
        return GetGeometrySample(primitiveIndex, barycentric, barydx, barydy);
    }

    bool __FindMappedAddress(bool isTemporal, out uint2 baseAddress)
    {
        const uint2 remapInfo2DIndex = shadelLocation / 8u;
        const uint2 localShadel2DIndex = shadelLocation % 8u;
        const uint offsetInShadelGroup = localShadel2DIndex.x + localShadel2DIndex.y * 8;
        const uint remapInfoIndex = remapInfo2DIndex.x + remapInfo2DIndex.y * _RemapBufferWidth;

        if (!isTemporal)
        {
            const uint baseOffset = _RemapBuffer.Load((int)(remapInfoIndex * 4u));
            if (baseOffset == ~0u)
            {
                return false;
            }

            const uint2 bitfield = reversebits(_OccupancyBuffer.Load2((int)(remapInfoIndex * 8u)));
            const uint resolvedBaseOffset = ShadelAllocator::ResolveAddress(baseOffset, bitfield, false);
            const uint shadelAddress = resolvedBaseOffset + CountOccupancyBits(bitfield, offsetInShadelGroup);

            baseAddress = GetStorageBufferBaseLocation(shadelAddress);

            return true;
        }
        else
        {
            const uint shadingFrameIndex = _FrameIndex / (uint)_ShadingInterval;
            if (shadingFrameIndex == 0)
            {
                return false;
            }

            const uint baseOffset = _HistoryRemapBuffer.Load((int)(remapInfoIndex * 4u));
            if (baseOffset == ~0u)
            {
                return false;
            }

            const uint2 bitfield = _HistoryOccupancyBuffer.Load2((int)(remapInfoIndex * 8u));
            if (GetBit(bitfield, offsetInShadelGroup) != 1)
            {
                return false;
            }

            const uint resolvedBaseOffset = ShadelAllocator::ResolveAddress(baseOffset, reversebits(bitfield), true);
            const uint shadelAddress = resolvedBaseOffset + CountOccupancyBits(reversebits(bitfield), offsetInShadelGroup);

            baseAddress = GetStorageBufferBaseLocation(shadelAddress);

            return true;
        }
    }
};

#endif // RENDER_TASK_PRELUDE_CGINC