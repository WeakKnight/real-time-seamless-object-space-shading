#ifndef GPU_SCENE_CGINC
#define GPU_SCENE_CGINC

#include "AnalyticalSky.cginc"
#include "DistanceFieldInstanceAS.cginc"
#include "GPUSceneConfig.hlsl"
#include "LightingData.cginc"
#include "MeshCard.cginc"

struct GPUScene_Intersection
{
    float3 hitPosW;
    float3 hitNormalW;
    float3 baseColor;
    float3 emissive;
    float3 irradiance;
};

bool GPUScene_AnyHit(float3 posW, float3 dirW, float tmax);
bool GPUScene_Intersect(float u, float3 posW, float3 dirW, float tmax, out GPUScene_Intersection intersection);
bool GPUScene_Intersect_NoMeshCardData(float3 posW, float3 dirW, float tmin, float tmax, out uint instanceIndex, out GPUScene_Intersection intersection);

float3 GPUScene_GetSkyColor(float3 dirW)
{
    return 1.0f * GPUScene_MainLightColor.xyz * hosek_wilkie_sky_rgb(dirW);
}

#if USE_MESH_RAY_TRACING
RaytracingAccelerationStructure GPUScene_MeshAS;

bool GPUScene_AnyHit(float3 posW, float3 dirW, float tmax)
{
    RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> rayQuery;
    RayDesc ray;
    ray.Origin = posW;
    ray.Direction = dirW;
    ray.TMin = 0.0f;
    ray.TMax = tmax;

    rayQuery.TraceRayInline(
        GPUScene_MeshAS,
        RAY_FLAG_NONE,
        0xff,
        ray);

    rayQuery.Proceed();

    return rayQuery.CommittedStatus() != COMMITTED_NOTHING;
}

bool GPUScene_Intersect(float u, float3 posW, float3 dirW, float tmax, out GPUScene_Intersection intersection)
{
    RayQuery<RAY_FLAG_NONE> rayQuery;

    RayDesc ray;
    ray.Origin = posW;
    ray.Direction = dirW;
    ray.TMin = 0.0f;
    ray.TMax = tmax;

    rayQuery.TraceRayInline(
        GPUScene_MeshAS,
        RAY_FLAG_NONE,
        0xff,
        ray);

    rayQuery.Proceed();

    if (rayQuery.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        uint instanceIndex = rayQuery.CommittedInstanceID();
        float t = rayQuery.CommittedRayT();
        intersection.hitPosW = posW + t * dirW;
        DF_InstanceData instance = DF_LoadInstanceData(instanceIndex);
        DF_AssetData asset = DF_LoadAssetData(instance.assetIndex);
        intersection.hitNormalW = normalize(DF_CalculateGradient(intersection.hitPosW, instance, asset));
        MeshCardAttribute meshCardAttribute = SampleMeshCard(instanceIndex, intersection.hitPosW, intersection.hitNormalW, u);
        intersection.baseColor = meshCardAttribute.baseColor;
        intersection.emissive = meshCardAttribute.emissive;
        intersection.irradiance = meshCardAttribute.irradiance;
        return true;
    }

    return false;
}

bool GPUScene_Intersect_NoMeshCardData(float3 posW, float3 dirW, float tmin, float tmax, out uint instanceIndex, out GPUScene_Intersection intersection)
{
    RayQuery<RAY_FLAG_NONE> rayQuery;

    RayDesc ray;
    ray.Origin = posW;
    ray.Direction = dirW;
    ray.TMin = tmin;
    ray.TMax = tmax;

    rayQuery.TraceRayInline(
        GPUScene_MeshAS,
        RAY_FLAG_NONE,
        0xff,
        ray);

    rayQuery.Proceed();

    if (rayQuery.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        instanceIndex = rayQuery.CommittedInstanceID();
        float t = rayQuery.CommittedRayT();
        intersection.hitPosW = posW + t * dirW;
        DF_InstanceData instance = DF_LoadInstanceData(instanceIndex);
        DF_AssetData asset = DF_LoadAssetData(instance.assetIndex);
        intersection.hitNormalW = normalize(DF_CalculateGradient(intersection.hitPosW, instance, asset));
        return true;
    }

    return false;
}
#endif

#endif // GPU_SCENE_CGINC