#ifndef INSTANCE_DATA_CGINC
#define INSTANCE_DATA_CGINC

#include "MeshUtils.cginc"
#include "OSSUtils.cginc"

/*
Common Props
*/
float4x4 _ObjectTransformation;
float4x4 _ObjectInverseTransformation;

int4 _InstanceProps;

int GetInstanceIndex()
{
    return _InstanceProps.x;
}

int2 GetChartLocation()
{
    return _InstanceProps.yz;
}

int GetChartSize()
{
    return _InstanceProps.w;
}

struct VertexData
{
    float3 position;
    float3 normal;
    float4 tangent;
    float2 uv0;
};

VertexData FetchVertex(uint subMeshIndex, uint vertexIndex)
{
    VertexData v;
    v.position = FetchVertexAttribute3(subMeshIndex, vertexIndex, kVertexAttributePosition);
    v.uv0 = FetchVertexAttribute2(subMeshIndex, vertexIndex, kVertexAttributeTexCoord0);
    v.normal = FetchVertexAttribute3(subMeshIndex, vertexIndex, kVertexAttributeNormal);
    return v;
}

/*
UV Based Only
*/
Texture2D<uint> _IDMap;
int _IDMapSize;

float2 interpolate(float2 a, float2 b, float2 c, float3 bary)
{
    return a * bary[0] + b * bary[1] + c * bary[2];
}

float3 interpolate(float3 a, float3 b, float3 c, float3 bary)
{
    return a * bary[0] + b * bary[1] + c * bary[2];
}

float3 CalculateBarycentric(float2 A, float2 B, float2 C, float2 P)
{
    float2 v0 = B - A;
    float2 v1 = C - A;
    float2 v2 = P - A;
    float den = v0.x * v1.y - v1.x * v0.y;

    float epsilon = 1.0f;
    float y = clamp((v2.x * v1.y - v1.x * v2.y) / den, -epsilon, 1.0f + epsilon);
    float z = clamp((v0.x * v2.y - v2.x * v0.y) / den, -epsilon, 1.0f + epsilon);
    float x = 1.0f - y - z;

    return float3(x, y, z);
}

float3 GetBarycentric(float2 uv1, uint packedPrimitiveIndex)
{
    if (packedPrimitiveIndex == ~0u)
    {
        return 0.0f;
    }

    uint primitiveIndex, subMeshIndex;
    UnpackPrimitiveIndex(packedPrimitiveIndex, primitiveIndex, subMeshIndex);

    uv1.y = 1.0f - uv1.y;
    uint3 triangleIndices = FetchTriangleIndices(subMeshIndex, primitiveIndex);
    return CalculateBarycentric(FetchVertexAttribute2(subMeshIndex, triangleIndices.x, kVertexAttributeTexCoord1),
                                FetchVertexAttribute2(subMeshIndex, triangleIndices.y, kVertexAttributeTexCoord1),
                                FetchVertexAttribute2(subMeshIndex, triangleIndices.z, kVertexAttributeTexCoord1),
                                uv1);
}

#endif // INSTANCE_DATA_CGINC