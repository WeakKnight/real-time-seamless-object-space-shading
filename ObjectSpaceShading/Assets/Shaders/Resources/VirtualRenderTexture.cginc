#ifndef VIRTUAL_RENDER_TEXTURE_CGINC
#define VIRTUAL_RENDER_TEXTURE_CGINC

#include "RenderConfig.hlsl"
#include "Halfedge.hlsl"
#include "ShadelAllocator.cginc"
#include "OSSUtils.cginc"

float4 _DS_StartLocationAndDimension; // Per Object

struct VRT_RemapInfo
{
    // base address in Storage Texture
    uint baseOffset;
    // 64 bits, 8x8 chunks
    uint2 occupancyBitfield;
};

int _VRT_StorageBufferWidth;
int _VRT_StorageBufferHeight;

int _VRT_Width;
int _VRT_Height;

int _VRT_RemapBufferWidth;
int _VRT_RemapBufferHeight;

#ifdef VRT_WRITE
RWByteAddressBuffer _VRT_OccupancyBuffer : register(u4);
#else
ByteAddressBuffer _VRT_OccupancyBuffer;
#endif

ByteAddressBuffer _VRT_RemapBuffer;

Texture2D _VRT_StorageBuffer;
SamplerState _VRT_linear_clamp_sampler;
SamplerState _VRT_point_clamp_sampler;

float _VRT_InstanceMipBias;
int _VRT_ShadingInterval;

float ResolveTextureLod(float2 uv)
{
    uint mipLayerCount = log2(min(_HtexTextureQuadWidth, _HtexTextureQuadHeight));
    float minorLength = min(length(ddx(uv)), length(ddy(uv)));
    float lod = max(0.0f, mipLayerCount + log2(minorLength) + _VRT_MipBias + _VRT_InstanceMipBias);
    return lod;
}

uint VRT_2D_To_1D(uint2 location, uint width)
{
    return location.x + location.y * width;
}

uint2 VRT_1D_To_2D(uint offset, uint width)
{
    return uint2(offset % width, offset / width);
}

uint VRT_CountOccupancyBits(VRT_RemapInfo remapInfo, uint offsetInBlock)
{
    uint2 bitfield = reversebits(remapInfo.occupancyBitfield);
    if (offsetInBlock >= 32u)
    {
        uint a = countbits(bitfield.x);
        uint b;
        if (offsetInBlock == 32)
        {
            b = 0;
        }
        else
        {
            b = countbits(bitfield.y >> (63u - offsetInBlock + 1));
        }
        return a + b;
    }
    else
    {
        if (offsetInBlock == 0)
        {
            return 0;
        }

        return countbits(bitfield.x >> (31u - offsetInBlock + 1));
    }
}

VRT_RemapInfo VRT_ReadRemapInfo(uint2 location)
{
    uint2 remapLocation = location / _VRT_ShadelGroupSize / _VRT_ShadelSize;
    uint remapIndex = VRT_2D_To_1D(remapLocation, _VRT_RemapBufferWidth);
    VRT_RemapInfo remapInfo;
    remapInfo.baseOffset = _VRT_RemapBuffer.Load(remapIndex * 4u);
    remapInfo.occupancyBitfield = _VRT_OccupancyBuffer.Load2(remapIndex * 8u);
    return remapInfo;
}

struct VRT_QueryResult
{
    bool mapped;
    float4 data;
};

VRT_QueryResult VRT_ReadTexture(uint2 virtualPixelLocation, float2 subPixel)
{
    VRT_QueryResult result;

    VRT_RemapInfo remapInfo = VRT_ReadRemapInfo(virtualPixelLocation);
    result.mapped = (remapInfo.baseOffset != ~0u);
    if (!result.mapped)
    {
        return result;
    }

    uint2 shadel2DIndexInShadelGroup = (virtualPixelLocation / _VRT_ShadelSize) % (_VRT_ShadelGroupSize);
    uint offsetInShadelGroup = VRT_2D_To_1D(shadel2DIndexInShadelGroup, _VRT_ShadelGroupSize);
    uint resolvedBaseOffset = ShadelAllocator::ResolveAddress(remapInfo.baseOffset, remapInfo.occupancyBitfield, false);
    uint shadelAddress = resolvedBaseOffset + VRT_CountOccupancyBits(remapInfo, offsetInShadelGroup);

    uint2 storageShadelIndex = VRT_1D_To_2D(shadelAddress, _VRT_StorageBufferWidth / _VRT_ShadelSize);
    uint2 storageBufferBaseLocation = storageShadelIndex * _VRT_ShadelSize;
    uint2 locationInShadel = virtualPixelLocation % _VRT_ShadelSize;

    if (locationInShadel.x == 0)
        subPixel.x = max(subPixel.x, 0.5);
    if (locationInShadel.y == 0)
        subPixel.y = max(subPixel.y, 0.5);
    if (locationInShadel.x == _VRT_ShadelSize - 1)
        subPixel.x = min(subPixel.x, 0.5);
    if (locationInShadel.y == _VRT_ShadelSize - 1)
        subPixel.y = min(subPixel.y, 0.5);

    float2 uv = (float2(storageBufferBaseLocation + locationInShadel) + subPixel) / float2(_VRT_StorageBufferWidth, _VRT_StorageBufferHeight);
    result.data = _VRT_StorageBuffer.SampleLevel(_VRT_linear_clamp_sampler, uv, 0.0f);

    return result;
}

VRT_QueryResult VRT_ReadTextureViaLightmapUV(float2 uv1, int2 chartLocation, int2 chartDimension, int lodLevel)
{
    VRT_QueryResult result;
    int2 virtualPixelLocation = chartLocation + clamp(chartDimension * uv1, 0, chartDimension - 1);
    virtualPixelLocation = GetTexelFromLODLevel(_VRT_Height, lodLevel, virtualPixelLocation.x, virtualPixelLocation.y);
    chartDimension = chartDimension >> lodLevel;
    float2 subPixel = frac(uv1 * (float)chartDimension.x) / (float)chartDimension.x;

    return VRT_ReadTexture(virtualPixelLocation, subPixel);
}

float4 VRT_ReadTextureViaLightmapUV(float2 uv1)
{
    int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    int2 dimension = (int2)_DS_StartLocationAndDimension.zz;

    uint mipLayerCount = (uint)log2(dimension.x);
    float minorLength = min(length(ddx(uv1)), length(ddy(uv1)));
    float lod = max(0.0f, mipLayerCount + log2(minorLength) + _VRT_MipBias + _VRT_InstanceMipBias);
    
    uint lodLow = (uint)lod;
    uint lodHigh = lodLow + 1;

    FilterLodLevel(mipLayerCount, lod, lodLow, lodHigh);

    int2 lowLODChartDimension = dimension >> lodLow;
    float2 subPixel = frac(uv1 * (float)lowLODChartDimension.x) / (float)lowLODChartDimension.x;
    VRT_QueryResult lodLowQuery = VRT_ReadTextureViaLightmapUV(uv1, startLocation, dimension, lodLow);
    VRT_QueryResult lodHighQuery = VRT_ReadTextureViaLightmapUV(uv1, startLocation, dimension, lodHigh);

    float lerpWeight = 1.0f;
    if (lodLowQuery.mapped && lodHighQuery.mapped)
    {
        // lerpWeight = 1.0f;
        lerpWeight = frac(lod);
    }
    float4 lodLowCol = lodLowQuery.mapped ? lodLowQuery.data : float4(1.0f, 0.0f, 0.0f, 1.0f);
    float4 lodHighCol = lodHighQuery.mapped ? lodHighQuery.data : float4(1.0f, 0.0f, 0.0f, 1.0f);
    float4 res = lerp(lodLowCol, lodHighCol, lerpWeight);

    return res;
}

void VRT_SampleHtextureHelper(float3 sampleFootprint, uint lod, inout float4 accum)
{
    if (sampleFootprint.z <= 0.0f)
    {
        return;
    }

    int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    int2 atlasDimension = (int2)_DS_StartLocationAndDimension.zw;

    float2 objectAtlasTexel = atlasDimension * sampleFootprint.xy;

    int2 virtualLocation = startLocation + (int2)clamp(objectAtlasTexel, 0, atlasDimension - 1);
    virtualLocation = GetTexelFromLODLevel(_VRT_Height, lod, virtualLocation.x, virtualLocation.y);

    atlasDimension = atlasDimension >> lod;

    float2 subPixel = frac(atlasDimension * sampleFootprint.xy);

    VRT_QueryResult queryResult = VRT_ReadTexture(virtualLocation, subPixel);
    if (queryResult.mapped)
    {
        accum += queryResult.data * sampleFootprint.z;
    }
}

float4 VRT_SampleHtexture(int halfedgeID, float2 baryYZ, float lod)
{
    int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    int2 atlasDimension = (int2)_DS_StartLocationAndDimension.zw;

    uint lodLow = (uint)lod;
    uint lodHigh = lodLow + 1u;

    uint mipLayerCount = (uint)log2(min(_HtexTextureQuadWidth, _HtexTextureQuadHeight));
    FilterLodLevel(mipLayerCount, lod, lodLow, lodHigh);

    float4 lodLowSample;
    {
        float4 accum = 0.0f;

        HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, lodLow);

        VRT_SampleHtextureHelper(sampleFootprint.a, lodLow, accum);

        VRT_SampleHtextureHelper(sampleFootprint.b, lodLow, accum);

        VRT_SampleHtextureHelper(sampleFootprint.c, lodLow, accum);

        lodLowSample = accum / max(sampleFootprint.alpha, 1e-5f);
    }

    if (!_VRT_MipmapFiltering)
    {
        return lodLowSample;
    }

    float4 lodHighSample = 0.0f;
    if (lodLow != lodHigh)
    {
        float4 accum = 0.0f;

        HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, lodHigh);

        VRT_SampleHtextureHelper(sampleFootprint.a, lodHigh, accum);

        VRT_SampleHtextureHelper(sampleFootprint.b, lodHigh, accum);

        VRT_SampleHtextureHelper(sampleFootprint.c, lodHigh, accum);

        lodHighSample = accum / max(sampleFootprint.alpha, 1e-5f);
    }

    return lerp(lodLowSample, lodHighSample, frac(lod));
}

#endif // VIRTUAL_RENDER_TEXTURE_CGINC