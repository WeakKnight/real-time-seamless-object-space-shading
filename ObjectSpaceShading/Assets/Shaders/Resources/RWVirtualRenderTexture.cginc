#ifndef RW_VIRTUAL_RENDER_TEXTURE_CGINC
#define RW_VIRTUAL_RENDER_TEXTURE_CGINC

#define VRT_WRITE
#include "VirtualRenderTexture.cginc"

void VRT_MarkShadel(uint remapInfoIndex, uint offsetInShadelGroup)
{
    if (offsetInShadelGroup >= 32)
    {
        uint occupancyBitfieldAddress = remapInfoIndex * 8u + 4u;
        uint occupancyBitfield = _VRT_OccupancyBuffer.Load(occupancyBitfieldAddress);
        uint bitmask = 1 << (offsetInShadelGroup - 32u);

        if ((occupancyBitfield & bitmask) == 0)
        {
            _VRT_OccupancyBuffer.InterlockedOr(occupancyBitfieldAddress, bitmask);
        }
    }
    else
    {
        uint occupancyBitfieldAddress = remapInfoIndex * 8u;
        uint occupancyBitfield = _VRT_OccupancyBuffer.Load(occupancyBitfieldAddress);
        uint bitmask = 1 << offsetInShadelGroup;

        if ((occupancyBitfield & bitmask) == 0)
        {
            _VRT_OccupancyBuffer.InterlockedOr(occupancyBitfieldAddress, bitmask);
        }
    }
}

void VRT_MarkShadelWithLocation(uint2 location)
{
    uint2 remapLocation = location / _VRT_ShadelGroupSize / _VRT_ShadelSize;
    uint remapInfoIndex = VRT_2D_To_1D(remapLocation, _VRT_RemapBufferWidth);

    uint2 shadel2DIndexInShadelGroup = (location / _VRT_ShadelSize) % (_VRT_ShadelGroupSize);
    uint offsetInShadelGroup = VRT_2D_To_1D(shadel2DIndexInShadelGroup, _VRT_ShadelGroupSize);

    VRT_MarkShadel(remapInfoIndex, offsetInShadelGroup);
}

void VRT_MarkShadelViaLightmapUV(float2 uv1)
{
    if (_DS_StartLocationAndDimension.z <= 0.0f)
    {
        return;
    }

    const int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    const int2 dimension = (int2)_DS_StartLocationAndDimension.zz;
    const int2 virtualLocation = startLocation + clamp(dimension * uv1, 0, dimension - 1);

    uint mipLayerCount = log2(dimension.x);
    float minorLength = min(length(ddx(uv1)), length(ddy(uv1)));
    float lod = max(0.0f, mipLayerCount + log2(minorLength) + _VRT_MipBias + _VRT_InstanceMipBias);
    
    uint lodLow = (uint)lod;
    uint lodHigh = lod + 1u;
    FilterLodLevel(mipLayerCount, lod, lodLow, lodHigh);

    int2 virtualLocationLODLow = GetTexelFromLODLevel(_VRT_Height, lodLow, virtualLocation.x, virtualLocation.y);
    VRT_MarkShadelWithLocation(virtualLocationLODLow);

    int2 virtualLocationLODHigh = GetTexelFromLODLevel(_VRT_Height, lodHigh, virtualLocation.x, virtualLocation.y);
    VRT_MarkShadelWithLocation(virtualLocationLODHigh);
}

void VRT_MarkShadelHelper(float3 footprint, uint lod)
{
    int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    int2 dimension = (int2)_DS_StartLocationAndDimension.zw;

    dimension = dimension >> lod;
    startLocation = GetTexelFromLODLevel(_VRT_Height, lod, startLocation.x, startLocation.y);

    const int2 virtualLocation = startLocation + (int2)clamp(dimension * footprint.xy, 0, dimension - 1);
    VRT_MarkShadelWithLocation(virtualLocation);
}

void VRT_MarkShadelViaHtexSampleFootprint(HtexSampleFootprint footprint, uint lod, int halfedgeID, float2 baryYZ)
{
    HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, lod);

    if (sampleFootprint.a.z > 0.0f)
    {
        VRT_MarkShadelHelper(sampleFootprint.a, lod);
    }

    if (sampleFootprint.b.z > 0.0f)
    {
        VRT_MarkShadelHelper(sampleFootprint.b, lod);
    }

    if (sampleFootprint.c.z > 0.0f)
    {
        VRT_MarkShadelHelper(sampleFootprint.c, lod);
    }
}

void VRT_MarkShadel(int halfedgeID, float2 baryYZ, float lod)
{
    int2 startLocation = (int2)_DS_StartLocationAndDimension.xy;
    int2 dimension = (int2)_DS_StartLocationAndDimension.zw;

    uint mipLayerCount = (uint)log2(min(_HtexTextureQuadWidth, _HtexTextureQuadHeight));

    uint lodLow = (uint)lod;
    uint lodHigh = lodLow + 1u;

    FilterLodLevel(mipLayerCount, lod, lodLow, lodHigh);

    {
        HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, lodLow);
        VRT_MarkShadelViaHtexSampleFootprint(sampleFootprint, lodLow, halfedgeID, baryYZ);
    }

    if (_VRT_MipmapFiltering)
    {
        if (lodLow != lodHigh)
        {
            HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, lodHigh);
            VRT_MarkShadelViaHtexSampleFootprint(sampleFootprint, lodHigh, halfedgeID, baryYZ);
        }
    }

    if (_EnablePersistentLayer == 1)
    {
        if (_VRT_PersistentLayerLODIndex != lodHigh && _VRT_PersistentLayerLODIndex != lodLow)
        {
            HtexSampleFootprint sampleFootprint = Htexture(halfedgeID, baryYZ, _VRT_PersistentLayerLODIndex);
            VRT_MarkShadelViaHtexSampleFootprint(sampleFootprint, _VRT_PersistentLayerLODIndex, halfedgeID, baryYZ);
        }
    }
}

#endif // RW_VIRTUAL_RENDER_TEXTURE_CGINC