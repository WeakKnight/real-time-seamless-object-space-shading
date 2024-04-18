#pragma once

#include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/GPUScene.cginc"
#include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/Packing.cginc"
#include "RenderTaskPrelude.cginc"

static const bool _UseTemporalResampling = true;
static const bool _UsePairwiseMIS = false;
static const float _DefensiveCanonicalWeight = 0.1f;
static const uint _TemporalResamplingMaxM = 24u;
static const uint _NumSpatialTemporalNeighborSamples = 1u;

static const float _SpatialSearchRadius = 32;

static float4 _DebugColor = 0;

struct RISContext
{
    uint halfedgeID;
    float2 triangleUV;
    int2 chartLocation;

    float spatialSearchRadius;
};

float3 GetRadianceFromIntersection(GPUScene_Intersection intersection)
{
    if (_UseIrradianceCaching)
    {
        return intersection.emissive + intersection.irradiance;
    }
    else
    {
        return intersection.emissive;
    }
}

struct GeometryContext
{
    float3 positionW;
    float3 normalW;
};

GeometryContext GeometryContext_(float3 P, float3 N)
{
    GeometryContext geom;
    geom.positionW = P;
    geom.normalW = N;
    return geom;
}

struct Reservoir
{
    float3 selfPositionW;
    float3 selfNormalW;
    float3 hitPositionW;
    float3 hitNormalW;

    float3 radiance;

    float weightSum;
    uint M;
    uint age;

    GeometryContext GetGeometryContext()
    {
        GeometryContext context;
        context.positionW = selfPositionW;
        context.normalW = selfNormalW;
        return context;
    }

    float WeightAtCurrent(float3 currentPosW, float3 currentNormalW)
    {
        float3 scatterDir = normalize(hitPositionW - currentPosW);
        float3 irradiance = saturate(dot(currentNormalW, scatterDir)) / __PI * radiance;
        return __CalcLuminance(irradiance);
    }
};

struct PackedReservoir
{
    uint4 a;
    uint4 b;
    uint4 c;

    bool IsValid()
    {
        if (all(a == ~0u))
        {
            return false;
        }

        return true;
    }
};

static Reservoir Reservoir_Zero()
{
    Reservoir r;
    r.M = 0;
    r.radiance = 0;
    r.weightSum = 0;
    r.age = 0;
    return r;
}

/*
x: dir(8x2) | M(u16)
y: weight sum(f16) | unused(f16)
z: radiance (irradiance)
w: geometry
*/

/*
x: self posW X
y: self posW Y
z: self posW Z
w: Weight Sum(f32)

x: hit posW X
y: hit posW Y
z: hit posW Z
w: hit normalW

x: M(u16) | self normalW(u16)
y: Irradiance (u32 RGBM)
z: age
*/
static PackedReservoir Reservoir_Pack(Reservoir reservoir)
{
    PackedReservoir packedReservoir;
    packedReservoir.a = uint4(asuint(reservoir.selfPositionW), asuint(reservoir.weightSum));
    packedReservoir.b = uint4(asuint(reservoir.hitPositionW), __ndirToOctUnorm32(reservoir.hitNormalW));
    packedReservoir.c = uint4(encodeNormal2x8(reservoir.selfNormalW) | (reservoir.M << 16u), __PackColor32ToUInt(RGBMEncode(reservoir.radiance)), reservoir.age, 0u);

    return packedReservoir;
}

static Reservoir Reservoir_Unpack(PackedReservoir packedReservoir)
{
    Reservoir reservoir;

    const uint mask16Bit = (1U << 16) - 1U;

    reservoir.selfPositionW = asfloat(packedReservoir.a.xyz);
    reservoir.selfNormalW = decodeNormal2x8(packedReservoir.c.x & mask16Bit);

    reservoir.hitPositionW = asfloat(packedReservoir.b.xyz);
    reservoir.hitNormalW = __octToNdirUnorm32(packedReservoir.b.w);

    reservoir.radiance = RGBMDecode(__UnpackR8G8B8A8ToUFLOAT(packedReservoir.c.y));
    reservoir.M = packedReservoir.c.x >> 16u;
    reservoir.weightSum = asfloat(packedReservoir.a.w);
    reservoir.age = packedReservoir.c.z;

    return reservoir;
}

PackedReservoir Reservoir_Load(DR_Shadel shadel, uint2 texel, bool isTemporal)
{
    PackedReservoir r;
    r.a = shadel.ReadPayload4(texel, 0u, isTemporal);
    r.b = shadel.ReadPayload4(texel, 16u, isTemporal);
    r.c = shadel.ReadPayload4(texel, 32u, isTemporal);
    return r;
}

void Reservoir_Store(DR_Shadel shadel, uint2 texel, PackedReservoir r)
{
    shadel.WritePayload4(texel, 0u, r.a);
    shadel.WritePayload4(texel, 16u, r.b);
    shadel.WritePayload4(texel, 32u, r.c);
}

static bool Reservoir_Stream(inout Reservoir r, float3 selfPos, float3 selfNormal, float3 hitPos, float3 hitNormal, float3 radiance, float random, float targetPdf, float invSourcePdf)
{
    r.M += 1;

    float risWeight = targetPdf * invSourcePdf;
    r.weightSum += risWeight;

    bool selectSample = (random * r.weightSum < risWeight);
    if (selectSample)
    {
        r.selfPositionW = selfPos;
        r.selfNormalW = selfNormal;
        r.hitPositionW = hitPos;
        r.hitNormalW = hitNormal;
        r.radiance = radiance;
    }

    return selectSample;
}

bool Reservoir_Combine(inout Reservoir reservoir, const Reservoir newReservoir, float random, float targetPdf, float jacobian = 1.f, float misWeight = 1.f)
{
    reservoir.M += newReservoir.M;

    float risWeight = targetPdf * jacobian * newReservoir.weightSum * misWeight;
    reservoir.weightSum += risWeight;

    bool selectSample = (random * reservoir.weightSum < risWeight);
    if (selectSample)
    {
        reservoir.selfPositionW = newReservoir.selfPositionW;
        reservoir.selfNormalW = newReservoir.selfNormalW;
        reservoir.hitPositionW = newReservoir.hitPositionW;
        reservoir.hitNormalW = newReservoir.hitNormalW;
        reservoir.radiance = newReservoir.radiance;
        reservoir.age = newReservoir.age;
    }

    return selectSample;
}

static void Reservoir_FinalizeResampling(inout Reservoir r, float normalizationNumerator,
                                         float normalizationDenominator)
{
    float denominator = normalizationDenominator;
    r.weightSum = (denominator == 0.0) ? 0.0 : (r.weightSum * normalizationNumerator) / denominator;
}

// Calculate the elements of the Jacobian to transform the sample's solid angle.
void __CalculatePartialJacobian(const float3 recieverPos, const float3 samplePos, const float3 sampleNormal,
                                out float distanceToSurface, out float cosineEmissionAngle)
{
    float3 vec = recieverPos - samplePos;

    distanceToSurface = length(vec);
    cosineEmissionAngle = saturate(dot(sampleNormal, vec / distanceToSurface));
}

// Calculates the full Jacobian for resampling neighborReservoir into a new receiver surface
float CalculateJacobian(float3 receiverPos, float3 receiverNormal, float3 neighborReceiverPos, const Reservoir neighborReservoir)
{
    // FIXME
    return 1;

    // Calculate Jacobian determinant to adjust weight.
    // See Equation (11) in the ReSTIR GI paper.
    float originalDistance, originalCosine;
    float newDistance, newCosine;
    __CalculatePartialJacobian(receiverPos, neighborReservoir.hitPositionW, neighborReservoir.hitNormalW, newDistance, newCosine);
    __CalculatePartialJacobian(neighborReceiverPos, neighborReservoir.hitPositionW, neighborReservoir.hitNormalW, originalDistance, originalCosine);

    float newNDotL = 1.0f;
    float oldNDotL = 1.0f;
    float jacobian = (oldNDotL * newCosine * originalDistance * originalDistance) / (newNDotL * originalCosine * newDistance * newDistance);

    if (isinf(jacobian) || isnan(jacobian))
        jacobian = 0;

    return jacobian;
}

bool ValidateJacobian(inout float jacobian)
{
    if (isinf(jacobian) || isnan(jacobian))
    {
        return false;
    }

    // Sold angle ratio is too different. Discard the sample.
    if (jacobian > 10.0 || jacobian < 1 / 10.0)
    {
        return false;
    }

    // clamp Jacobian.
    // jacobian = clamp(jacobian, 1 / 3.0, 3.0);

    return true;
}

float ShiftMappingReconnection(Reservoir srcReservoir, GeometryContext dstSurface, out float jacobian)
{
    //! TODO: Use Age For Geometry Reconstruction.
    jacobian = CalculateJacobian(dstSurface.positionW, dstSurface.normalW, srcReservoir.selfPositionW, srcReservoir);
    if (!ValidateJacobian(jacobian))
    {
        jacobian = 0;
        return 0;
    }

    float targetPdf = srcReservoir.WeightAtCurrent(dstSurface.positionW, dstSurface.normalW);
    return targetPdf;
}

float pairwiseMisWeight(float w_i, float w_c, float M_i, float M_c)
{
    float denom = w_i * M_i + w_c * M_c;
    return denom <= 0 ? 1 : ((w_i * M_i) / denom);
}

bool HtextureSpatialSearch(int currentHalfedgeID, float2 uv, float2 dir, float t, out float2 atlasUV, out int neighborEdgeIndex)
{
    // Ensure uv coordinate is inside the triangle
    if (uv.x + uv.y == 1)
    {
        uv.x = 0.9999 - uv.y;
    }

    for (uint iter = 0; iter < 64; ++iter)
    {
        float t_prev = -uv.y / dir.y;
        float t_next = -uv.x / dir.x;
        float t_twin = (1 - uv.x - uv.y) / (dir.x + dir.y);

        int hitEdge = -1;
        float hitT = 1e10;
        if (t_prev > 0 && t_prev < hitT)
        {
            hitEdge = 0;
            hitT = t_prev;
        }
        if (t_next > 0 && t_next < hitT)
        {
            hitEdge = 1;
            hitT = t_next;
        }
        if (t_twin > 0 && t_twin < hitT)
        {
            hitEdge = 2;
            hitT = t_twin;
        }

        if (hitEdge == -1)
        {
            return false;
        }

        if (t < hitT)
        {
            neighborEdgeIndex = currentHalfedgeID;
            atlasUV = uv + dir * t;
            return true;
        }

        uv = uv + dir * hitT;
        t -= hitT;
        if (hitEdge == 0)
        {
            uv = float2(-uv.y, uv.x);
            dir = float2(-dir.y, dir.x);
            currentHalfedgeID = ccm_HalfedgePrevID(currentHalfedgeID);

            // numerical precision fix
            uv.x = 0.0001;
        }
        else if (hitEdge == 1)
        {
            uv = float2(uv.y, -uv.x);
            dir = float2(dir.y, -dir.x);
            currentHalfedgeID = ccm_HalfedgeNextID(currentHalfedgeID);

            // numerical precision fix
            uv.y = 0.0001;
        }
        else
        {
            uv = 1 - uv;
            dir = -dir;
            currentHalfedgeID = ccm_HalfedgeTwinID(currentHalfedgeID);

            // numerical precision fix
            uv.x = 0.9999 - uv.y;
        }
    }

    neighborEdgeIndex = currentHalfedgeID;
    atlasUV = uv;
    return true;
}

bool FindNeighborShadelNoTopology(inout RandomSequence randomSequence, DR_Shadel shadel, uint2 texel, float spatialSearchRadius, out DR_Shadel neighborShadel, out uint2 neighborTexel)
{
    int2 offset = (int2)((RandomSequence_GenerateSample2D(randomSequence) - 0.5f) * 2.0f * spatialSearchRadius);

    uint2 neighborGlobalTexel = clamp(shadel.shadelLocation * 8 + texel + offset, 0, uint2(_VirtualShadelTextureWidth, _VirtualShadelTextureHeight) - 1u);
    uint2 neighborShadelLocation = neighborGlobalTexel / 8;

    neighborTexel = neighborGlobalTexel % 8;
    neighborShadel;
    if (all(neighborShadelLocation == shadel.shadelLocation))
    {
        neighborShadel = shadel;
        return neighborShadel.IsPayloadHistoryValid();
    }
    else
    {
        neighborShadel = DR_Shadel::Create(neighborShadelLocation);
        if (neighborShadel.layerLevel != shadel.layerLevel)
        {
            return false;
        }

        int2 chartSize = GetChartSize() >> shadel.layerLevel;
        const int2 chartLocation = GetTexelFromLODLevel(_VirtualShadelTextureHeight, shadel.layerLevel, GetChartLocation().x, GetChartLocation().y);
        if (any(neighborGlobalTexel < chartLocation) || any(neighborGlobalTexel >= (chartLocation + chartSize)))
        {
            return false;
        }

        return neighborShadel.Build(false) && neighborShadel.IsPayloadHistoryValid();
    }
}

bool FindNeighborShadel(int halfEdgeID, float2 triangleUV, float spatialSearchRadius, int2 chartLocation, int layerLevel, inout RandomSequence randomSequence, out DR_Shadel neighborShadel, out uint2 neighborTexel)
{
    float2 spatialDir = normalize(RandomSequence_GenerateSample2D(randomSequence) * 2 - 1);
    float spatialDist = RandomSequence_GenerateSample1D(randomSequence) * spatialSearchRadius;

    float2 neighborUV;
    int neighborEdgeIndex;
    if (HtextureSpatialSearch(halfEdgeID, triangleUV, spatialDir, spatialDist, neighborUV, neighborEdgeIndex))
    {
        HtexSampleFootprint footprint = Htexture(neighborEdgeIndex, neighborUV, layerLevel);
        if (footprint.a.z > 0.0f)
        {
            uint2 atlasSize = GetAtlasSize(layerLevel);
            int2 virtualLocation = chartLocation + (int2)clamp(atlasSize * footprint.a.xy, 0, atlasSize - 1);

            int2 shadelLocation = virtualLocation / 8;
            neighborTexel = virtualLocation % 8;

            neighborShadel = DR_Shadel::Create(shadelLocation);
            neighborShadel.Build(false);
            if (neighborShadel.IsPayloadHistoryValid())
            {
                return true;
            }
            else
            {
                if (_EnablePersistentLayer == 1)
                {
                    uint2 persistentTexel;
                    DR_Shadel persistentShadel;
                    if (neighborShadel.GetPersistentTexel(neighborTexel, persistentShadel, persistentTexel))
                    {
                        neighborShadel = persistentShadel;
                        neighborTexel = persistentTexel;
                        
                        return true;
                    }
                }
                else
                { 
                    int2 higherVirtualLocation = GetHigherLODTexel(_VirtualShadelTextureHeight, layerLevel, virtualLocation.x, virtualLocation.y);
                    shadelLocation = higherVirtualLocation / 8;
                    neighborTexel = higherVirtualLocation % 8;

                    neighborShadel = DR_Shadel::Create(shadelLocation);
                    neighborShadel.Build(false);
                    if (neighborShadel.IsPayloadHistoryValid())
                    {
                        return true;
                    }
                    else if (layerLevel >= 1)
                    {
                        int2 lowerVirtualLocation = GetLowerLODTexel(_VirtualShadelTextureHeight, layerLevel, virtualLocation.x, virtualLocation.y);
                        shadelLocation = lowerVirtualLocation / 8;
                        neighborTexel = lowerVirtualLocation % 8;

                        neighborShadel = DR_Shadel::Create(shadelLocation);
                        neighborShadel.Build(false);
                        if (neighborShadel.IsPayloadHistoryValid())
                        {
                            return true;
                        }
                    }
                }
            }
        }
    }

    return false;
}

bool GeometryTest(GeometryContext local, GeometryContext neighbor)
{
    const float normalThreshold = 0.75f;
    const float depthThreshold = 0.07f;

    float localLinearZ = abs(mul(_WorldToCameraMat, float4(local.positionW, 1.0f)).z);
    float neighborLinearZ = abs(mul(_WorldToCameraMat, float4(neighbor.positionW, 1.0f)).z);

    if (abs(localLinearZ - neighborLinearZ) > depthThreshold * localLinearZ)
    {
        return false;
    }

    if (dot(local.normalW, neighbor.normalW) < normalThreshold)
    {
        return false;
    }

    return true;
}

float3 FusedResampling(DR_Shadel shadel, uint2 texel, float3 rayOrigin, RISContext risContext, inout RandomSequence randomSequence, GeometrySample geometrySample)
{
    float3 res = 0.0f;

    Reservoir initialSamplingReservoir = Reservoir_Zero();

    float selectedTargetPdf = 1.0f;

    float3 disOccludedRes = 0.0f;
    {
        const int boostNum = 8;
        const int initialNum = (shadel.historyValid ? 1 : boostNum);
        const float sampleWeight = 1.0f / initialNum;
        for (int i = 0; i < initialNum; i++)
        {
            float scatterPdf = 1.0f / (2.0f * __PI);
            float3 scatterDir = UniformSampleHemisphere(RandomSequence_GenerateSample2D(randomSequence));
            float3 b1;
            float3 b2;
            branchlessONB(geometrySample.normal, b1, b2);
            scatterDir = scatterDir.x * b1 + scatterDir.y * b2 + scatterDir.z * geometrySample.normal;

            if (scatterPdf > 0.0f)
            {
                GPUScene_Intersection intersection;
                if (GPUScene_Intersect(RandomSequence_GenerateSample1D(randomSequence), rayOrigin, scatterDir, 1e30f, intersection))
                {
                    float3 irradiance = __InvPI * saturate(dot(geometrySample.normal, scatterDir)) * GetRadianceFromIntersection(intersection) / scatterPdf;

                    float targetPdf = __CalcLuminance(irradiance);
                    if (Reservoir_Stream(initialSamplingReservoir, rayOrigin, geometrySample.normal, intersection.hitPosW, intersection.hitNormalW, GetRadianceFromIntersection(intersection), RandomSequence_GenerateSample1D(randomSequence), targetPdf, 1.0f / scatterPdf))
                    {
                        selectedTargetPdf = targetPdf;
                    }

                    disOccludedRes += irradiance * sampleWeight;
                }
                else
                {
                    initialSamplingReservoir.M += 1;
                }
            }
            else
            {
                initialSamplingReservoir.M += 1;
            }
        }

        Reservoir_FinalizeResampling(initialSamplingReservoir, 1.0f, selectedTargetPdf * initialSamplingReservoir.M);
    }

    initialSamplingReservoir.M = 1;

    Reservoir state;
    if (_UseTemporalResampling)
    {
        state = Reservoir_Zero();

        Reservoir centralReservoir = initialSamplingReservoir;
        float pdfCanonical = selectedTargetPdf;
        float canonicalWeight = _DefensiveCanonicalWeight;
        uint validNeighborCount = 0;

        float neighborSampleLimit = 1;

        // Stream Temporal Sample
        {
            GeometryContext localGeometry = GeometryContext_(geometrySample.position, geometrySample.normal);

            if (shadel.IsPayloadHistoryValid())
            {
                uint2 temporalTexel = texel;
                DR_Shadel temporalShadel = shadel;

                PackedReservoir temporalPackedReservoir = Reservoir_Load(temporalShadel, temporalTexel, true);
                if (temporalPackedReservoir.IsValid())
                {
                    Reservoir temporalReservoir = Reservoir_Unpack(temporalPackedReservoir);
                    if (GeometryTest(localGeometry, temporalReservoir.GetGeometryContext()))
                    {
                        temporalReservoir.M = min(temporalReservoir.M, _TemporalResamplingMaxM);

                        float targetPdf = temporalReservoir.WeightAtCurrent(rayOrigin, geometrySample.normal);
                        if (Reservoir_Combine(state, temporalReservoir, RandomSequence_GenerateSample1D(randomSequence), targetPdf, 1, temporalReservoir.M))
                        {
                            selectedTargetPdf = targetPdf;

                            state.selfPositionW = rayOrigin;
                            state.selfNormalW = geometrySample.normal;

                            neighborSampleLimit = 0.5;
                        }                        
                    }
                }
            }
            else if (_EnablePersistentLayer == 1)
            {
                uint2 persistentTexel;
                DR_Shadel persistentShadel;
                if (shadel.GetPersistentTexel(texel, persistentShadel, persistentTexel))
                {
                    PackedReservoir temporalPackedReservoir = Reservoir_Load(persistentShadel, persistentTexel, true);
                    if (temporalPackedReservoir.IsValid())
                    {
                        Reservoir temporalReservoir = Reservoir_Unpack(temporalPackedReservoir);
                        if (GeometryTest(localGeometry, temporalReservoir.GetGeometryContext()))
                        {
                            temporalReservoir.M = min(temporalReservoir.M, _TemporalResamplingMaxM);

                            float targetPdf = temporalReservoir.WeightAtCurrent(rayOrigin, geometrySample.normal);
                            if (Reservoir_Combine(state, temporalReservoir, RandomSequence_GenerateSample1D(randomSequence), targetPdf, 1, temporalReservoir.M))
                            {
                                selectedTargetPdf = targetPdf;

                                state.selfPositionW = rayOrigin;
                                state.selfNormalW = geometrySample.normal;

                                neighborSampleLimit = 0.5;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < _NumSpatialTemporalNeighborSamples; i++)
            {
                uint2 neighborTexel;
                DR_Shadel neighborShadel;

                if (_HalfedgeMesh == 0)
                {
                    if (!FindNeighborShadelNoTopology(randomSequence, shadel, texel, risContext.spatialSearchRadius, neighborShadel, neighborTexel))
                    {
                        continue;
                    }
                }
                else
                { 
                    if (!FindNeighborShadel(risContext.halfedgeID, risContext.triangleUV, risContext.spatialSearchRadius,
                                            risContext.chartLocation, shadel.layerLevel,
                                            randomSequence, neighborShadel, neighborTexel))
                    {
                        continue;
                    }
                }

                PackedReservoir neighborPackedReservoir = Reservoir_Load(neighborShadel, neighborTexel, true);
                if (!neighborPackedReservoir.IsValid())
                {
                    continue;
                }

                Reservoir neighborReservoir = Reservoir_Unpack(neighborPackedReservoir);
                if (!GeometryTest(localGeometry, neighborReservoir.GetGeometryContext()))
                {
                    continue;
                }

                neighborReservoir.M = min(neighborReservoir.M, _TemporalResamplingMaxM * neighborSampleLimit);

                float targetPdf = neighborReservoir.WeightAtCurrent(rayOrigin, geometrySample.normal);
                if (Reservoir_Combine(state, neighborReservoir, RandomSequence_GenerateSample1D(randomSequence), targetPdf, 1, neighborReservoir.M))
                {
                    selectedTargetPdf = targetPdf;

                    state.selfPositionW = rayOrigin;
                    state.selfNormalW = geometrySample.normal;
                }

                validNeighborCount += 1;
                if (validNeighborCount >= _NumSpatialTemporalNeighborSamples)
                {
                    break;
                }
            }
        }

        {
            float targetPdf = centralReservoir.WeightAtCurrent(rayOrigin, geometrySample.normal);
            if (Reservoir_Combine(state, centralReservoir, RandomSequence_GenerateSample1D(randomSequence), targetPdf, 1, centralReservoir.M))
            {
                selectedTargetPdf = targetPdf;

                state.selfPositionW = rayOrigin;
                state.selfNormalW = geometrySample.normal;
            }
        }

        float norm = state.M;
        Reservoir_FinalizeResampling(state, 1.0, selectedTargetPdf * norm);
    }
    else
    {
        state = initialSamplingReservoir;
    }

    if (!shadel.IsPayloadHistoryValid())
    {
        res = disOccludedRes;
    }
    else if (state.weightSum > 0.0f)
    {
        float3 stateSampleDir = (state.hitPositionW - rayOrigin);
        float sampleDist = sqrt(dot(stateSampleDir, stateSampleDir));
        stateSampleDir /= sampleDist;

        if (!GPUScene_AnyHit(rayOrigin, stateSampleDir, max(0.0f, sampleDist - 0.01f)))
        {
            res = saturate(dot(geometrySample.normal, stateSampleDir)) / __PI * state.radiance * state.weightSum;
        }
        else
        {
            state.weightSum = 0.0f;
        }
    }
    // else
    // {
    //     res += irradiance * risContext.invSourcePdf;
    // }

    state.age += 1;
    Reservoir_Store(shadel, texel, Reservoir_Pack(state));

    if (_DebugColor.w > 0)
        return _DebugColor.xyz;

    //res = state.M / 48.0;
    return res;
}