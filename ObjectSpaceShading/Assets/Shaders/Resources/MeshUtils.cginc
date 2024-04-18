#ifndef MESH_UTILS_INCLUDED
#define MESH_UTILS_INCLUDED

// This helper file contains a list of utility functions needed to fetch vertex attributes from within closesthit or anyhit shaders.

// HLSL example:
// struct Vertex
// {
//     float3 position;
//     float2 texcoord;
// };

// Vertex FetchVertex(uint vertexIndex)
// {
//      Vertex v;
//      v.position = FetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
//      v.texcoord = FetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
//      return v;
// }

// uint3 triangleIndices = FetchTriangleIndices(PrimitiveIndex());
// Vertex v0, v1, v2;
// v0 = FetchVertex(triangleIndices.x);
// v1 = FetchVertex(triangleIndices.y);
// v2 = FetchVertex(triangleIndices.z);
// Interpolate the vertices using the barycentric coordinates available as input to the closesthit or anyhit shaders.

#define kMaxVertexStreams 4

struct MeshInfo
{
    uint vertexSize[kMaxVertexStreams];                 // The stride between 2 consecutive vertices in the vertex buffer. There is an entry for each vertex stream.
    uint baseVertex;                                    // A value added to each index before reading a vertex from the vertex buffer.
    uint vertexStart;
    uint indexSize;                                     // 0 when an index buffer is not used, 2 for 16-bit indices or 4 for 32-bit indices.
    uint indexStart;                                    // The location of the first index to read from the index buffer.
};

struct VertexAttributeInfo
{
    uint Stream;                                        // The stream index used to fetch the vertex attribute. There can be up to kMaxVertexStreams streams.
    uint Format;                                        // One of the kVertexFormat* values from bellow.
    uint ByteOffset;                                    // The attribute offset in bytes into the vertex structure.
    uint Dimension;                                     // The dimension (#channels) of the vertex attribute.
};

// Valid values for the attributeType parameter in FetchVertexAttribute* functions.
#define kVertexAttributePosition    0
#define kVertexAttributeNormal      1
#define kVertexAttributeTangent     2
#define kVertexAttributeColor       3
#define kVertexAttributeTexCoord0   4
#define kVertexAttributeTexCoord1   5
#define kVertexAttributeTexCoord2   6
#define kVertexAttributeTexCoord3   7
#define kVertexAttributeTexCoord4   8
#define kVertexAttributeTexCoord5   9
#define kVertexAttributeTexCoord6   10
#define kVertexAttributeTexCoord7   11
#define kVertexAttributeCount       12

static float4 _DefaultVertexAttributes[kVertexAttributeCount] =
{
    float4(0, 0, 0, 0),     // kVertexAttributePosition - always present in ray tracing.
    float4(0, 0, 1, 0),     // kVertexAttributeNormal
    float4(1, 0, 0, 1),     // kVertexAttributeTangent
    float4(1, 1, 1, 1),     // kVertexAttributeColor
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord0
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord1
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord2
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord3
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord4
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord5
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord6
    float4(0, 0, 0, 0),     // kVertexAttributeTexCoord7
};

// Supported
#define kVertexFormatFloat          0
#define kVertexFormatFloat16        1
#define kVertexFormatUNorm8         2
#define kVertexFormatUNorm16        4
#define kVertexFormatSNorm16        5
// Not supported
#define kVertexFormatSNorm8         3
#define kVertexFormatUInt8          6
#define kVertexFormatSInt8          7
#define kVertexFormatUInt16         8
#define kVertexFormatSInt16         9
#define kVertexFormatUInt32         10
#define kVertexFormatSInt32         11

StructuredBuffer<MeshInfo>              _MeshInfo;
StructuredBuffer<VertexAttributeInfo>   _MeshVertexDeclaration;

ByteAddressBuffer _MeshVertexBuffer0;
ByteAddressBuffer _MeshVertexBuffer1;
ByteAddressBuffer _MeshVertexBuffer2;
ByteAddressBuffer _MeshVertexBuffer3;

ByteAddressBuffer _MeshIndexBuffer;

static float4 _VertexChannelMask[5] =
{
    float4(0, 0, 0, 0),
    float4(1, 0, 0, 0),
    float4(1, 1, 0, 0),
    float4(1, 1, 1, 0),
    float4(1, 1, 1, 1)
};

// A normalized short (16-bit signed integer) is encode into data. Returns a float in the range [-1, 1].
float DecodeSNorm16(uint data)
{
    const float invRange = 1.0f / (float)0x7fff;

    // Get the two's complement if the sign bit is set (0x8000) meaning the bits will represent a short negative number.
    int signedValue = data & 0x8000 ? -1 * ((~data & 0x7fff) + 1) : data;

    // Use max otherwise a value of 32768 as input would be decoded to -1.00003052f. https://www.khronos.org/opengl/wiki/Normalized_Integer
    return max(signedValue * invRange, -1.0f);
}

uint3 FetchTriangleIndices(uint subMeshIndex, uint primitiveIndex)
{
    uint3 indices;

    MeshInfo meshInfo = _MeshInfo[subMeshIndex];

    if (meshInfo.indexSize == 2)
    {
        const uint offsetInBytes = (meshInfo.indexStart + primitiveIndex * 3) << 1;
        const uint dwordAlignedOffset = offsetInBytes & ~3;
        const uint2 fourIndices = _MeshIndexBuffer.Load2(dwordAlignedOffset);

        if (dwordAlignedOffset == offsetInBytes)
        {
            indices.x = fourIndices.x & 0xffff;
            indices.y = (fourIndices.x >> 16) & 0xffff;
            indices.z = fourIndices.y & 0xffff;
        }
        else
        {
            indices.x = (fourIndices.x >> 16) & 0xffff;
            indices.y = fourIndices.y & 0xffff;
            indices.z = (fourIndices.y >> 16) & 0xffff;
        }

        indices = indices + meshInfo.baseVertex.xxx;
    }
    else if (meshInfo.indexSize == 4)
    {
        const uint offsetInBytes = (meshInfo.indexStart + primitiveIndex * 3) << 2;
        indices = _MeshIndexBuffer.Load3(offsetInBytes) + meshInfo.baseVertex.xxx;
    }
    else // meshInfo.indexSize == 0
    {
        const uint firstVertexIndex = primitiveIndex * 3 + meshInfo.vertexStart;
        indices = firstVertexIndex.xxx + uint3(0, 1, 2);
    }

    return indices;
}

// Checks if the vertex attribute attributeType is present in one of the _MeshVertexBuffers vertex streams.
bool HasVertexAttribute(uint attributeType)
{
    VertexAttributeInfo vertexDecl = _MeshVertexDeclaration[attributeType];

    return vertexDecl.Dimension != 0;
}

// attributeType is one of the kVertexAttribute* defines
float2 FetchVertexAttribute2(uint subMeshIndex, uint vertexIndex, uint attributeType)
{
    VertexAttributeInfo vertexDecl = _MeshVertexDeclaration[attributeType];

    const uint attributeDimension = vertexDecl.Dimension;

    if (!HasVertexAttribute(attributeType) || attributeDimension > 4)
        return _DefaultVertexAttributes[attributeType].xy;

    const uint attributeByteOffset = vertexDecl.ByteOffset;
    const uint vertexSize = _MeshInfo[subMeshIndex].vertexSize[vertexDecl.Stream];
    const uint vertexAddress        = vertexIndex * vertexSize;
    const uint attributeAddress     = vertexAddress + attributeByteOffset;
    const uint attributeFormat      = vertexDecl.Format;

    float2 value = float2(0, 0);

    if (vertexDecl.Stream == 0)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer0.Load2(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint twoHalfs = _MeshVertexBuffer0.Load(attributeAddress);
            value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer0.Load(attributeAddress);
            const float x = DecodeSNorm16(twoShorts & 0xffff);
            const float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
            value = float2(x, y);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer0.Load(attributeAddress);
            const float x = (twoShorts & 0xffff) / float(0xffff);
            const float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
            value = float2(x, y);
        }
    }
    else if (vertexDecl.Stream == 1)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer1.Load2(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint twoHalfs = _MeshVertexBuffer1.Load(attributeAddress);
            value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer1.Load(attributeAddress);
            const float x = DecodeSNorm16(twoShorts & 0xffff);
            const float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
            value = float2(x, y);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer1.Load(attributeAddress);
            const float x = (twoShorts & 0xffff) / float(0xffff);
            const float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
            value = float2(x, y);
        }
    }
    else if (vertexDecl.Stream == 2)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer2.Load2(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint twoHalfs = _MeshVertexBuffer2.Load(attributeAddress);
            value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer2.Load(attributeAddress);
            const float x = DecodeSNorm16(twoShorts & 0xffff);
            const float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
            value = float2(x, y);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer2.Load(attributeAddress);
            const float x = (twoShorts & 0xffff) / float(0xffff);
            const float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
            value = float2(x, y);
        }
    }
    else
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer3.Load2(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint twoHalfs = _MeshVertexBuffer3.Load(attributeAddress);
            value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer3.Load(attributeAddress);
            const float x = DecodeSNorm16(twoShorts & 0xffff);
            const float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
            value = float2(x, y);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint twoShorts = _MeshVertexBuffer3.Load(attributeAddress);
            const float x = (twoShorts & 0xffff) / float(0xffff);
            const float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
            value = float2(x, y);
        }
    }
    
    return _VertexChannelMask[attributeDimension].xy * value;
}

// attributeType is one of the kVertexAttribute* defines
float3 FetchVertexAttribute3(uint subMeshIndex, uint vertexIndex, uint attributeType)
{
    VertexAttributeInfo vertexDecl = _MeshVertexDeclaration[attributeType];

    const uint attributeDimension = vertexDecl.Dimension;

    if (!HasVertexAttribute(attributeType) || attributeDimension > 4)
    {
        return _DefaultVertexAttributes[attributeType].xyz;
    }
    
    const uint attributeByteOffset = vertexDecl.ByteOffset;
    const uint vertexSize = _MeshInfo[subMeshIndex].vertexSize[vertexDecl.Stream];
    const uint vertexAddress        = vertexIndex * vertexSize;
    const uint attributeAddress     = vertexAddress + attributeByteOffset;
    const uint attributeFormat      = vertexDecl.Format;

    float3 value = float3(0, 0, 0);

    if (vertexDecl.Stream == 0)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer0.Load3(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer0.Load2(attributeAddress);
            value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer0.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer0.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer0.Load(attributeAddress);
            value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
        }
    }
    else if (vertexDecl.Stream == 1)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer1.Load3(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer1.Load2(attributeAddress);
            value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer1.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer1.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer1.Load(attributeAddress);
            value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
        }
    }
    else if (vertexDecl.Stream == 2)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer2.Load3(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer2.Load2(attributeAddress);
            value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer2.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer2.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer2.Load(attributeAddress);
            value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
        }
    }
    else
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer3.Load3(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer3.Load2(attributeAddress);
            value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer3.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer3.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            value = float3(x, y, z);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer3.Load(attributeAddress);
            value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
        }
    }

    return _VertexChannelMask[attributeDimension].xyz * value;
}

// attributeType is one of the kVertexAttribute* defines
float4 FetchVertexAttribute4(uint subMeshIndex, uint vertexIndex, uint attributeType)
{
    VertexAttributeInfo vertexDecl = _MeshVertexDeclaration[attributeType];

    const uint attributeDimension = vertexDecl.Dimension;

    if (!HasVertexAttribute(attributeType) || attributeDimension > 4)
        return _DefaultVertexAttributes[attributeType];

    const uint attributeByteOffset = vertexDecl.ByteOffset;
    const uint vertexSize = _MeshInfo[subMeshIndex].vertexSize[vertexDecl.Stream];
    const uint vertexAddress        = vertexIndex * vertexSize;
    const uint attributeAddress     = vertexAddress + attributeByteOffset;
    const uint attributeFormat      = vertexDecl.Format;

    float4 value = float4(0, 0, 0, 0);

    if (vertexDecl.Stream == 0)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer0.Load4(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer0.Load2(attributeAddress);
            value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer0.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            const float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer0.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            const float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer0.Load(attributeAddress);
            value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
        }
    }
    else if (vertexDecl.Stream == 1)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer1.Load4(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer1.Load2(attributeAddress);
            value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer1.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            const float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer1.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            const float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer1.Load(attributeAddress);
            value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
        }
    }
    else if (vertexDecl.Stream == 2)
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer2.Load4(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer2.Load2(attributeAddress);
            value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer2.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            const float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer2.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            const float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer2.Load(attributeAddress);
            value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
        }
    }
    else
    {
        if (attributeFormat == kVertexFormatFloat)
        {
            value = asfloat(_MeshVertexBuffer3.Load4(attributeAddress));
        }
        else if (attributeFormat == kVertexFormatFloat16)
        {
            const uint2 fourHalfs = _MeshVertexBuffer3.Load2(attributeAddress);
            value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
        }
        else if (attributeFormat == kVertexFormatSNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer3.Load2(attributeAddress);
            const float x = DecodeSNorm16(fourShorts.x & 0xffff);
            const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
            const float z = DecodeSNorm16(fourShorts.y & 0xffff);
            const float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm16)
        {
            const uint2 fourShorts = _MeshVertexBuffer3.Load2(attributeAddress);
            const float x = (fourShorts.x & 0xffff) / float(0xffff);
            const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
            const float z = (fourShorts.y & 0xffff) / float(0xffff);
            const float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
            value = float4(x, y, z, w);
        }
        else if (attributeFormat == kVertexFormatUNorm8)
        {
            const uint data = _MeshVertexBuffer3.Load(attributeAddress);
            value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
        }
    }

    return _VertexChannelMask[attributeDimension] * value;
}

#endif  //#ifndef MESH_UTILS_INCLUDED
