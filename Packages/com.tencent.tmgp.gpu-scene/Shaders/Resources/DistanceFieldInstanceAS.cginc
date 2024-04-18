#ifndef DISTANCE_FIELD_INSTANCE_AS_CGINC
#define DISTANCE_FIELD_INSTANCE_AS_CGINC

#include "DistanceField.cginc"
#include "GPUSceneConfig.hlsl"

DF_QueryResult DF_IntersectAS(out uint instanceIndex, float3 origin, float3 dir, float tmin = 0.0f, float tmax = 1e30f);
bool DF_AnyHitAS(float3 origin, float3 dir, float tmin = 0.0f, float tmax = 1e30f);

DF_QueryResult DF_IntersectAS(float3 origin, float3 dir, float tmin = 0.0f, float tmax = 1e30f)
{
    uint instanceIndex;
    return DF_IntersectAS(instanceIndex, origin, dir, tmin, tmax);
}

#if USE_INLINE_RAY_TRACING
RaytracingAccelerationStructure DF_InstanceAS;

DF_QueryResult DF_IntersectAS(out uint instanceIndex, float3 origin, float3 dir, float tmin, float tmax)
{
    DF_QueryResult result;

    RayQuery<RAY_FLAG_NONE> rayQuery;

    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = tmin;
    ray.TMax = tmax;

    rayQuery.TraceRayInline(
        DF_InstanceAS,
        RAY_FLAG_NONE,
        0xff,
        ray);

    float closetHit = 1e30f;

    while (rayQuery.Proceed())
    {
        if (rayQuery.CandidateType() == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            uint candidateInstanceIndex = rayQuery.CandidatePrimitiveIndex();
            DF_InstanceData instanceData = DF_LoadInstanceData(candidateInstanceIndex);
            DF_QueryResult localR = DF_TraceRay(origin, dir, instanceData, tmin, tmax);
            if (localR.hit && localR.t < closetHit)
            {
                closetHit = localR.t;
                rayQuery.CommitProceduralPrimitiveHit(localR.t);
            }
        }
    }

    if (rayQuery.CommittedStatus() == COMMITTED_PROCEDURAL_PRIMITIVE_HIT)
    {
        instanceIndex = rayQuery.CommittedPrimitiveIndex();
        result.hit = true;
        result.t = rayQuery.CommittedRayT();
    }
    else
    {
        result.hit = false;
    }

    return result;
}

bool DF_AnyHitAS(float3 origin, float3 dir, float tmin, float tmax)
{
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> rayQuery;
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = tmin;
    ray.TMax = tmax;

    rayQuery.TraceRayInline(
        DF_InstanceAS,
        RAY_FLAG_NONE,
        0xff,
        ray);

    while (rayQuery.Proceed())
    {
        if (rayQuery.CandidateType() == CANDIDATE_PROCEDURAL_PRIMITIVE)
        {
            uint candidateInstanceIndex = rayQuery.CandidatePrimitiveIndex();
            DF_InstanceData instanceData = DF_LoadInstanceData(candidateInstanceIndex);
            DF_QueryResult localR = DF_TraceRay(origin, dir, instanceData, tmin, tmax);
            if (localR.hit)
            {
                rayQuery.CommitProceduralPrimitiveHit(localR.t);
            }
        }
    }

    return rayQuery.CommittedStatus() != COMMITTED_NOTHING;
}

#else
StructuredBuffer<uint> DF_InstanceAS_Primitives;
StructuredBuffer<uint2> DF_InstanceAS_Nodes;
ByteAddressBuffer DF_InstanceAS_AABBs;

struct DF_NodeAABB
{
    float3 min;
    float3 max;
};

DF_NodeAABB DF_LoadNodeAABB(uint nodeIndex)
{
    DF_NodeAABB aabb;
    aabb.min = asfloat(DF_InstanceAS_AABBs.Load3(nodeIndex * 24u));
    aabb.max = asfloat(DF_InstanceAS_AABBs.Load3(nodeIndex * 24u + 12u));
    return aabb;
}

struct DF_Node
{
    bool isLeaf;
    bool valid;

    uint leftNodeIndex;
    uint rightNodeIndex;

    uint startIndex;
    uint count;

    DF_NodeAABB aabb;
};

static const uint DF_NodeLeafBit = 0x80000000;
static const uint DF_NodeValueMask = 0x7FFFFFFF;

DF_Node DF_LoadNode(uint nodeIndex)
{
    DF_Node result;
    if ((nodeIndex & DF_NodeValueMask) == DF_NodeValueMask)
    {
        result.valid = false;
        return result;
    }

    uint2 nodeVal = DF_InstanceAS_Nodes[nodeIndex];
    if (nodeVal.x == ~0u)
    {
        result.valid = false;
        return result;
    }

    result.valid = true;

    result.isLeaf = (nodeVal.x & DF_NodeLeafBit) != 0u;
    if (result.isLeaf)
    {
        result.startIndex = nodeVal.x & DF_NodeValueMask;
        result.count = nodeVal.y;
    }
    else
    {
        result.leftNodeIndex = nodeVal.x;
        result.rightNodeIndex = nodeVal.y;
    }
    result.aabb = DF_LoadNodeAABB(nodeIndex);

    return result;
}

DF_QueryResult DF_IntersectAS(out uint instanceIndex, float3 origin, float3 dir, float tmin, float tmax)
{
    DF_QueryResult result;
    result.t = 1e30f;

    uint stack[16];
    uint stackTop = 0;

#define DF_STACK_TOP() DF_LoadNode(stack[stackTop - 1])
#define DF_STACK_POP() stackTop--
#define DF_STACK_PUSH(val) stack[stackTop] = val; stackTop++
#define DF_STACK_EMPTY() (stackTop <= 0)

    DF_Node node = DF_LoadNode(0);
    if (DF_IntersectRayAABB(origin, dir, node.aabb.min, node.aabb.max, tmin, tmax) >= 0.0f)
    {
        do
        {
            if (!node.valid)
            {
                if (DF_STACK_EMPTY())
                {
                    break;
                }

                node = DF_STACK_TOP();
                DF_STACK_POP();
            }
            node.valid = false;

            if (node.isLeaf)
            {
                for (int i = 0; i < node.count; i++)
                {
                    uint primitiveIndex = DF_InstanceAS_Primitives[node.startIndex + i];
                    DF_InstanceData instanceData = DF_LoadInstanceData(primitiveIndex);
                    DF_QueryResult localR = DF_TraceRay(origin, dir, instanceData, tmin, tmax);
                    if (localR.hit && (localR.t < result.t))
                    {
                        instanceIndex = primitiveIndex;
                        result.t = localR.t;
                        tmax = result.t;
                    }
                }
            }
            else
            {
                bool hitLeft = false;
                bool hitRight = false;

                DF_Node leftNode = DF_LoadNode(node.leftNodeIndex);
                if (leftNode.valid && DF_IntersectRayAABB(origin, dir, leftNode.aabb.min, leftNode.aabb.max, tmin, tmax) >= 0.0f)
                {
                    hitLeft = true;
                }

                DF_Node rightNode = DF_LoadNode(node.rightNodeIndex);
                if (rightNode.valid && DF_IntersectRayAABB(origin, dir, rightNode.aabb.min, rightNode.aabb.max, tmin, tmax) >= 0.0f)
                {
                    hitRight = true;
                }

                if (hitLeft && hitRight)
                {
                    DF_STACK_PUSH(node.leftNodeIndex);
                    node = rightNode;
                }
                else if (hitLeft && !hitRight)
                {
                    node = leftNode;
                }
                else if (hitRight && !hitLeft)
                {
                    node = rightNode;
                }
            }
        }
        while (!DF_STACK_EMPTY() || node.valid);
    }
#undef DF_STACK_TOP
#undef DF_STACK_POP
#undef DF_STACK_PUSH
#undef DF_STACK_EMPTY

    result.hit = result.t < 1e10f;

    return result;
}

bool DF_AnyHitAS(float3 origin, float3 dir, float tmin, float tmax)
{
    if (tmin >= tmax)
    {
        return false;
    }
    
    uint stack[16];
    uint stackTop = 0;

#define DF_STACK_TOP() DF_LoadNode(stack[stackTop - 1])
#define DF_STACK_POP() stackTop--
#define DF_STACK_PUSH(val) stack[stackTop] = val; stackTop++
#define DF_STACK_EMPTY() (stackTop <= 0)

    DF_Node node = DF_LoadNode(0);
    if (DF_IntersectRayAABB(origin, dir, node.aabb.min, node.aabb.max, tmin, tmax) >= 0.0f)
    {
        do
        {
            if (!node.valid)
            {
                if (DF_STACK_EMPTY())
                {
                    break;
                }

                node = DF_STACK_TOP();
                DF_STACK_POP();
            }
            node.valid = false;

            if (node.isLeaf)
            {
                for (int i = 0; i < node.count; i++)
                {
                    uint primitiveIndex = DF_InstanceAS_Primitives[node.startIndex + i];
                    DF_InstanceData instanceData = DF_LoadInstanceData(primitiveIndex);
                    DF_QueryResult localR = DF_TraceRay(origin, dir, instanceData, tmin, tmax);
                    if (localR.hit)
                    {
                        return true;
                    }
                }
            }
            else
            {
                bool hitLeft = false;
                bool hitRight = false;

                DF_Node leftNode = DF_LoadNode(node.leftNodeIndex);
                if (leftNode.valid && DF_IntersectRayAABB(origin, dir, leftNode.aabb.min, leftNode.aabb.max, tmin, tmax) >= 0.0f)
                {
                    hitLeft = true;
                }

                DF_Node rightNode = DF_LoadNode(node.rightNodeIndex);
                if (rightNode.valid && DF_IntersectRayAABB(origin, dir, rightNode.aabb.min, rightNode.aabb.max, tmin, tmax) >= 0.0f)
                {
                    hitRight = true;
                }

                if (hitLeft && hitRight)
                {
                    DF_STACK_PUSH(node.leftNodeIndex);
                    node = rightNode;
                }
                else if (hitLeft && !hitRight)
                {
                    node = leftNode;
                }
                else if (hitRight && !hitLeft)
                {
                    node = rightNode;
                }
            }
        }
        while (!DF_STACK_EMPTY() || node.valid);
    }
#undef DF_STACK_TOP
#undef DF_STACK_POP
#undef DF_STACK_PUSH
#undef DF_STACK_EMPTY

    return false;
}
#endif

#endif // DISTANCE_FIELD_INSTANCE_AS_CGINC