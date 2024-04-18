#ifndef SCREEN_SPACE_RAY_TRACING_CGINC
#define SCREEN_SPACE_RAY_TRACING_CGINC

#include "MeshCard.cginc"

Texture2D<float> _GPUScene_DepthTex;
Texture2D<uint> _GPUScene_IdTex;

SamplerState _GPUScene_linear_clamp_sampler;

float4 _GPUScene_PixelSize;

float4x4 _GPUScene_ViewProjection;
float4x4 _GPUScene_Projection;
float4x4 _GPUScene_WorldToCamera;
float4x4 _GPUScene_CameraToWorld;
float4x4 _GPUScene_CameraToScreenMatrix;

#ifndef UNITY_CG_INCLUDED
// x = 1 or -1 (-1 if projection is flipped)
// y = near plane
// z = far plane
// w = 1/far plane
float4 _ProjectionParams;

// Values used to linearize the Z buffer (http://www.humus.name/temp/Linearize%20depth.txt)
// x = 1-far/near
// y = far/near
// z = x/far
// w = y/far
// or in case of a reversed depth buffer (UNITY_REVERSED_Z is 1)
// x = -1+far/near
// y = 1
// z = x/far
// w = 1/far
float4 _ZBufferParams;

// Z buffer to linear depth
float LinearEyeDepth(float z)
{
    return 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w);
}
#endif

float3 ComputeViewPosition(float linearZ, float2 uv, float4x4 mProj, bool leftHanded = true, bool perspective = true)
{
    float scale = perspective ? linearZ : 1;
    scale *= leftHanded ? 1 : -1;

    float2 p11_22 = float2(mProj._11, mProj._22);
    float2 p13_31 = float2(mProj._13, mProj._23);
    return float3((uv * 2.0 - 1.0 - p13_31) / p11_22 * scale, linearZ);
}

float3 ComputeViewPositionPerspectiveLH(float linearZ, float2 uv, float4x4 mProj)
{
    return ComputeViewPosition(linearZ, uv, mProj, true, true);
}

float2 WorldPositionToScreenUV(float3 posW)
{
    float4 projected = mul(_GPUScene_ViewProjection, float4(posW, 1.0f));
    float2 uv = (projected.xy / projected.w) * 0.5f + 0.5f;
    return uv;
}

float2 WorldPositionToScreenUV(float3 posW, out float k)
{
    float4 projected = mul(_GPUScene_ViewProjection, float4(posW, 1.0f));
    k = 1.0f / projected.w;
    float2 uv = (projected.xy * k) * 0.5f + 0.5f;
    return uv;
}

float GPUScene_GetLinearizedDepth(float2 texcoord)
{
    float depth = _GPUScene_DepthTex.SampleLevel(_GPUScene_linear_clamp_sampler, 1.0f - texcoord, 0).x;
    return LinearEyeDepth(depth);
}

float3 GPUScene_GetNormalByScreenUV(float2 texcoord)
{
    float3 offset = float3(_GPUScene_PixelSize.xy, 0.0);
    float2 posCenter = texcoord.xy;
    float2 posNorth = posCenter - offset.zy;
    float2 posEast = posCenter + offset.xz;

    float3 vertCenter = float3(posCenter - 0.5, 1) * GPUScene_GetLinearizedDepth(posCenter);
    float3 vertNorth = float3(posNorth - 0.5, 1) * GPUScene_GetLinearizedDepth(posNorth);
    float3 vertEast = float3(posEast - 0.5, 1) * GPUScene_GetLinearizedDepth(posEast);

    float3 normalCS = normalize(cross(vertCenter - vertNorth, vertCenter - vertEast));
    float3 normalWS = normalize(mul(transpose((float3x3)_GPUScene_WorldToCamera), normalCS).xyz);
    return normalWS;
}

float3 GPUScene_GetPositionByScreenUV(float2 texcoord)
{
    float linearZ = GPUScene_GetLinearizedDepth(texcoord);
    float3 posCS = -ComputeViewPositionPerspectiveLH(linearZ, texcoord, _GPUScene_Projection);
    return mul(_GPUScene_CameraToWorld, float4(posCS, 1.0f)).xyz;
}

uint GPUScene_GetInstanceIndexByScreenUV(float2 texcoord)
{
    uint instanceIndex = _GPUScene_IdTex[(uint2)((1.0f - texcoord) * _GPUScene_PixelSize.zw)];
    return instanceIndex;
}

MeshCardAttribute GPUScene_GetMeshCardAttributeByScreenUV(float2 texcoord, float u)
{
    uint instanceIndex = GPUScene_GetInstanceIndexByScreenUV(texcoord);

    float3 posW = GPUScene_GetPositionByScreenUV(texcoord);
    float3 normalW = GPUScene_GetNormalByScreenUV(texcoord);

    return SampleMeshCard(instanceIndex, posW, normalW, u);
}

MeshCardAttribute GPUScene_GetMeshCardAttributeByScreenUV(float2 texcoord, float u, float3 posW, float3 normalW)
{
    uint instanceIndex = GPUScene_GetInstanceIndexByScreenUV(texcoord);
    return SampleMeshCard(instanceIndex, posW, normalW, u);
}

bool __TraceScreenSpaceRay(float3 csOrigin,
                           float3 csDirection,
                           float4x4 projectToPixelMatrix,
                           Texture2D<float> depthTexture,
                           float2 zBufferSize,
                           float csZThickness,
                           float nearPlaneZ,
                           float stride,
                           float jitterFraction,
                           float maxSteps,
                           float maxRayTraceDistance,
                           out float2 hitPixel,
                           out float3 csHitPoint);

bool GPUScene_TraceScreenSpaceRay(float3 posW, float3 dirW, float tmax, out float3 hitPosW, out float2 screenUV)
{
    float3 beginWS = posW;
    float3 endWS = posW + tmax * dirW;

    float3 posCS = mul(_GPUScene_WorldToCamera, float4(posW, 1.0f)).xyz;
    float3 dirCS = normalize(mul(transpose((float3x3)_GPUScene_CameraToWorld), dirW).xyz);
    posCS.z *= -1.0f;
    dirCS.z *= -1.0f;

    float zThickness = 0.35f;
    float nearPlane = 0.1f;
    float pixelStride = 1;
    float jitterFraction = 1;
    float maxDDAStep = 256;
    float2 hitPixel;
    float3 hitPosCS;
    bool hit = __TraceScreenSpaceRay(posCS, dirCS, _GPUScene_CameraToScreenMatrix, _GPUScene_DepthTex, _GPUScene_PixelSize.zw, zThickness, nearPlane, pixelStride, jitterFraction, maxDDAStep, tmax, hitPixel, hitPosCS);
    if (hit)
    {
        hitPosCS.z *= -1.0f;
        hitPosW = mul(_GPUScene_CameraToWorld, float4(hitPosCS, 1.0f)).xyz;
        screenUV = hitPixel.xy / _GPUScene_PixelSize.zw;
        screenUV.x = 1.0f - screenUV.x;
        screenUV.y = 1.0f - screenUV.y;
    }

    return hit;
}

bool __RaySurfaceIntersect(float rayZMin, float rayZMax, float sceneZMax, float zThickness)
{
    return (rayZMax >= sceneZMax) && (rayZMin <= sceneZMax + zThickness);
}

// Assume P0 is within the rect (P1 - P0 is never zero when clipping)
// Result can be used as lerp(P0, P1, alpha)
float __ClipLineByRect(float2 P0, float2 P1, float4 rect)
{
    float xMin = rect.x, yMin = rect.y, xMax = rect.z, yMax = rect.w;
    float alpha = 0.0;

    // Assume P0 is in the viewport (P1 - P0 is never zero when clipping)
    if ((P1.y > yMax) || (P1.y < yMin))
        alpha = (P1.y - ((P1.y > yMax) ? yMax : yMin)) / (P1.y - P0.y);

    if ((P1.x > xMax) || (P1.x < xMin))
        alpha = max(alpha, (P1.x - ((P1.x > xMax) ? xMax : xMin)) / (P1.x - P0.x));

    return 1 - alpha;
}

float __SquaredDistance(float2 p0, float2 p1)
{
    float2 v = p0 - p1;
    return dot(v, v);
}

void __Swap(in out float a, in out float b)
{
    float tmp = a;
    a = b;
    b = tmp;
}

float __ClipRayByNearPlane(float3 origin, float3 dir, float rayMaxDist, float zPlane)
{
    return ((origin.z + dir.z * rayMaxDist) < zPlane) ? (zPlane - origin.z) / dir.z : rayMaxDist;
}

bool __IsEqualToFarPlane(float z)
{
    return abs(z - _ProjectionParams.z) < 2;
}

// http://jcgt.org/published/0003/04/04/
bool __TraceScreenSpaceRay(float3 csOrigin,
                           float3 csDirection,
                           float4x4 projectToPixelMatrix,
                           Texture2D<float> depthTexture,
                           float2 zBufferSize,
                           float csZThickness,
                           float nearPlaneZ,
                           float stride,
                           float jitterFraction,
                           float maxSteps,
                           float maxRayTraceDistance,
                           out float2 hitPixel,
                           out float3 csHitPoint)
{

    // TODO: adaptive z offset in order to avoid self intersection
    const float SelfIntersectionZOffset = 0.15;

    // Initialize to off screen
    hitPixel = float2(-1.0, -1.0);
    csHitPoint = float3(0, 0, 0);

    // Clip ray to a near plane in 3D (doesn't have to be *the* near plane, although that would be a good idea)
    float rayLength = __ClipRayByNearPlane(csOrigin, csDirection, maxRayTraceDistance, nearPlaneZ);
    float3 csEndPoint = csDirection * rayLength + csOrigin;

    // Project into screen space
    float4 H0 = mul(projectToPixelMatrix, float4(csOrigin, 1.0));
    float4 H1 = mul(projectToPixelMatrix, float4(csEndPoint, 1.0));

    // There are a lot of divisions by w that can be turned into multiplications
    // at some minor precision loss...and we need to interpolate these 1/w values
    // anyway.
    //
    // Because the caller was required to clip to the near plane,
    // this homogeneous division (projecting from 4D to 2D) is guaranteed
    // to succeed.
    float k0 = 1.0 / H0.w;
    float k1 = 1.0 / H1.w;

    // Switch the original points to values that interpolate linearly in 2D
    float3 Q0 = csOrigin * k0;
    float3 Q1 = csEndPoint * k1;

    // Screen-space endpoints
    float2 P0 = H0.xy * k0;
    float2 P1 = H1.xy * k1;

    //! TODO: Remove this
    if ((P0.x < 0 && P1.x < 0) 
        || (P0.y < 0 && P1.y < 0)
        || (P0.x >= _GPUScene_PixelSize.z && P1.x >= _GPUScene_PixelSize.z)
        || (P0.y >= _GPUScene_PixelSize.w && P1.y >= _GPUScene_PixelSize.w))
    {
        return false;
    }

    // If the line is degenerate, make it cover at least one pixel
    // to avoid handling zero-pixel extent as a special case later
    P1 += ((__SquaredDistance(P0, P1) < 0.0001) ? 0.01 : 0.0);

    // [Optional clipping to frustum sides here]
    float alpha = __ClipLineByRect(P0, P1, float4(0.5, 0.5, zBufferSize.x - 0.5, zBufferSize.y - 0.5));
    P1 = lerp(P0, P1, alpha);
    k1 = lerp(k0, k1, alpha);
    Q1 = lerp(Q0, Q1, alpha);

    // Calculate difference between P1 and P0
    float2 delta = P1 - P0;

    // Permute so that the primary iteration is in x to reduce
    // large branches later
    bool permute = (abs(delta.x) < abs(delta.y));
    if (permute) {
        // More-vertical line. Create a permutation that swaps x and y in the output
        // by directly swizzling the inputs.
        delta = delta.yx;
        P1 = P1.yx;
        P0 = P0.yx;
    }

    // From now on, "x" is the primary iteration direction and "y" is the secondary one
    float stepDirection = sign(delta.x);
    float invdx = stepDirection / delta.x;
    float2 dP = float2(stepDirection, invdx * delta.y);

    // Track the derivatives of Q and k
    float3 dQ = (Q1 - Q0) * invdx;
    float dk = (k1 - k0) * invdx;

    // Because we test 1/2 a texel forward along the ray, on the very last iteration
    // the interpolation can go past the end of the ray. Use these bounds to clamp it.
    float zMin = min(csEndPoint.z, csOrigin.z);
    float zMax = max(csEndPoint.z, csOrigin.z);

    // Scale derivatives by the desired pixel stride
    dP *= stride; dQ *= stride; dk *= stride;

    // Offset the starting values by the jitter fraction
    P0 += dP * jitterFraction; Q0 += dQ * jitterFraction; k0 += dk * jitterFraction;

    // Slide P from P0 to P1, (now-homogeneous) Q from Q0 to Q1, and k from k0 to k1
    float3 Q = Q0;
    float k = k0;

    // We track the ray depth at +/- 1/2 pixel to treat pixels as clip-space solid
    // voxels. Because the depth at -1/2 for a given pixel will be the same as at
    // +1/2 for the previous iteration, we actually only have to compute one value
    // per iteration.
    float prevZMaxEstimate = csOrigin.z - SelfIntersectionZOffset;
    float stepCount = 0.0;
    float rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;
    float sceneZMax = 1e6;

    // P1.x is never modified after this point, so pre-scale it by
    // the step direction for a signed comparison
    float end = P1.x * stepDirection;

    // We only advance the z field of Q in the inner loop, since
    // Q.xy is never used until after the loop terminates.

    int loopIdx = 0;
    float2 P;
    for (P = P0;
         ((P.x * stepDirection) <= end) &&
         (stepCount < maxSteps) &&
         // make sure there is no occluder in front of the ray path
         //! hitPotentialOccluder(rayZMin, sceneZMax, csZThickness) &&
         // ray intersection test
         !__RaySurfaceIntersect(rayZMin, rayZMax, sceneZMax, csZThickness)
         // if ray hit the far plane
         //&& (!isEqualToFarPlane(sceneZMax))
         ;
         P += dP, Q.z += dQ.z, k += dk, stepCount += 1.0) {

        // The depth range that the ray covers within this loop
        // iteration.  Assume that the ray is moving in increasing z
        // and swap if backwards.  Because one end of the interval is
        // shared between adjacent iterations, we track the previous
        // value and then swap as needed to ensure correct ordering
        rayZMin = prevZMaxEstimate;

        // Compute the value at 1/2 step into the future
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        rayZMax = clamp(rayZMax, zMin, zMax);
        prevZMaxEstimate = rayZMax;

        // Since we don't know if the ray is stepping forward or backward in depth,
        // maybe swap. Note that we preserve our original z "max" estimate first.
        if (rayZMin > rayZMax) { __Swap(rayZMin, rayZMax); }

        // Camera-space z of the background
        hitPixel = permute ? P.yx : P;
        
        sceneZMax = LinearEyeDepth(depthTexture[int2(hitPixel)].r);
    } // pixel on ray

    // Undo the last increment, which ran after the test variables
    // were set up.
    P -= dP; Q.z -= dQ.z; k -= dk; stepCount -= 1.0;

    bool hit = __RaySurfaceIntersect(rayZMin, rayZMax, sceneZMax, csZThickness);

    // If using non-unit stride and we hit a depth surface...
    if ((stride > 1) && hit) {
        // Refine the hit point within the last large-stride step

        // Retreat one whole stride step from the previous loop so that
        // we can re-run that iteration at finer scale
        P -= dP; Q.z -= dQ.z; k -= dk; stepCount -= 1.0;

        // Take the derivatives back to single-pixel stride
        float invStride = 1.0 / stride;
        dP *= invStride; dQ.z *= invStride; dk *= invStride;

        // For this test, we don't bother checking thickness or passing the end, since we KNOW there will
        // be a hit point. As soon as
        // the ray passes behind an object, call it a hit. Advance (stride + 1) steps to fully check this
        // interval (we could skip the very first iteration, but then we'd need identical code to prime the loop)
        float refinementStepCount = 0;

        // This is the current sample point's z-value, taken back to camera space
        prevZMaxEstimate = Q.z / k;
        rayZMin = prevZMaxEstimate;

        // Ensure that the FOR-loop test passes on the first iteration since we
        // won't have a valid value of sceneZMax to test.
        sceneZMax = rayZMin + 1e7;

        for (;
             (refinementStepCount <= stride * 1.4) &&
             (rayZMin <= sceneZMax) && (!__IsEqualToFarPlane(sceneZMax));
             P += dP, Q.z += dQ.z, k += dk, refinementStepCount += 1.0) {

            rayZMin = prevZMaxEstimate;

            // Compute the ray camera-space Z value at 1/2 fine step (pixel) into the future
            rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
            rayZMax = clamp(rayZMax, zMin, zMax);

            prevZMaxEstimate = rayZMax;
            rayZMin = min(rayZMax, rayZMin);

            hitPixel = permute ? P.yx : P;
            
            sceneZMax = LinearEyeDepth(depthTexture[int2(hitPixel)].r);
        }

        // Undo the last increment, which happened after the test variables were set up
        Q.z -= dQ.z; refinementStepCount -= 1;

        // Count the refinement steps as fractions of the original stride. Save a register
        // by not retaining invStride until here
        stepCount += refinementStepCount / stride;
        //  debugColor = float3(refinementStepCount / stride);
    } // refinement

    Q.xy += dQ.xy * stepCount;
    csHitPoint = Q * (1.0 / k);

    // Does the last point discovered represent a valid hit?
    return hit;
}

#endif // SCREEN_SPACE_RAY_TRACING_CGINC