#pragma once

/*
 * Data structure definitions
 */
struct cc_Halfedge {
    int twinID;
    int nextID;
    int prevID;
    int faceID;
    int edgeID;
    int vertexID;
    int uvID;
};

struct HalfedgeMeshTexture
{
    Texture2D tex;
    SamplerState sampler;
    float2 numQuads;
    float2 quadSize;
};

HalfedgeMeshTexture HalfedgeMeshTexture_(Texture2D tex, SamplerState sampler, float2 numQuads, float2 quadSize)
{
    HalfedgeMeshTexture t;
    t.tex = tex;
    t.sampler = sampler;
    t.numQuads = numQuads;
    t.quadSize = quadSize;
    return t;
}

/*
 * GPU resources
 */
StructuredBuffer<cc_Halfedge> _Halfedges;
StructuredBuffer<int> _VertexToHalfedgeIDs;
StructuredBuffer<int> _FaceToHalfedgeIDs;
StructuredBuffer<int> _EdgeToHalfedgeIDs;
StructuredBuffer<float2> _UV0;
SamplerState s_htex_linear_clamp_sampler;

uint _HtexTextureNumQuadsX;
uint _HtexTextureNumQuadsY;

uint _HtexTextureQuadWidth;
uint _HtexTextureQuadHeight;

/*
 * Halfedge data accessor
 */
cc_Halfedge ccm__Halfedge(int halfedgeID)
{
    return _Halfedges[halfedgeID];
}

int ccm_HalfedgeTwinID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).twinID;
}

int ccm_HalfedgeNextID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).nextID;
}

int ccm_HalfedgePrevID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).prevID;
}

int ccm_HalfedgeVertexID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).vertexID;
}

int ccm_HalfedgeUvID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).uvID;
}

int ccm_HalfedgeEdgeID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).edgeID;
}

int ccm_HalfedgeFaceID(int halfedgeID)
{
    return ccm__Halfedge(halfedgeID).faceID;
}

int ccm_HalfedgeFaceID_Quad(int halfedgeID)
{
    return halfedgeID >> 2;
}

int ccm_FaceToHalfedgeID(int faceID)
{
    return _FaceToHalfedgeIDs[faceID];
}

int ccm_EdgeToHalfedgeID(int edgeID)
{
    return _EdgeToHalfedgeIDs[edgeID];
}

float2 ccm_Uv(int uvID)
{
    return _UV0[uvID];
}

float2 ccm_HalfedgeVertexUv(int halfedgeID)
{
    return ccm_Uv(ccm_HalfedgeUvID(halfedgeID));
}

float2 computeFacePointUV(int faceID)
{
    int startHalfedge = ccm_FaceToHalfedgeID(faceID);
    int n = 0;
    float2 uv = 0;

    int halfedgeID = startHalfedge;
    do {
        float2 vertexUV = ccm_HalfedgeVertexUv(halfedgeID);
        uv += vertexUV;
        n++;

        halfedgeID = ccm_HalfedgeNextID(halfedgeID);
    } while (halfedgeID != startHalfedge);

    uv /= n;
    return uv;
}

float2 HalfedgeMesh_UV0(int halfedgeID, float2 xy)
{
    int nextID = ccm_HalfedgeNextID(halfedgeID);

    int faceID = ccm_HalfedgeFaceID(halfedgeID);
    float2 facePointUV = computeFacePointUV(faceID);

    float2 vertexUV1 = _UV0[ccm_HalfedgeUvID(halfedgeID)];
    float2 vertexUV2 = _UV0[ccm_HalfedgeUvID(nextID)];

    return (1 - xy.x - xy.y) * facePointUV + xy.x * vertexUV1 + xy.y * vertexUV2;
}

/*
 * Per-Halfedge texture sample
 */
float4 SampleQuad(HalfedgeMeshTexture tex, int quadID, float2 xy, int channel)
{
    float2 invNumQuads = 1.0 / tex.numQuads;
    float2 invQuadSize = 1.0 / tex.quadSize;

    if (any(xy < -invQuadSize * 0.5) || any(xy > 1 + invQuadSize * 0.5))
    {
        return 0;
    }

    xy = max(invQuadSize * 0.5, xy);
    xy = min(1 - invQuadSize * 0.5, xy);

    uint numQuadsX = tex.numQuads.x;
    uint tileX = quadID % numQuadsX;
    uint tileY = quadID / numQuadsX;
    float2 baseUV = float2(tileX, tileY) * invNumQuads;

    return tex.tex.Sample(tex.sampler, baseUV + xy * invNumQuads);
}

float SampleAlpha(HalfedgeMeshTexture tex, int quadID, float2 xy)
{
    float2 invNumQuads = 1.0 / tex.numQuads;
    float2 invQuadSize = 1.0 / tex.quadSize;

    if (any(xy < -invQuadSize * 0.5) || any(xy > 1 + invQuadSize * 0.5))
    {
        return 0;
    }

    if (any(xy < invQuadSize))
    {
        return 1;

        // TODO
        float2 d = abs(xy) / (invQuadSize * 0.5);
        return min(d.x, d.y);
    }

    if (any(xy > 1 - invQuadSize * 0.5))
    {
        return 1;

        // TODO
        float2 d = abs(xy - 1) / (invQuadSize * 0.5);
        return min(d.x, d.y);
    }

    xy = max(invQuadSize, xy);
    xy = min(1 - invQuadSize, xy);

    return 1;
    // ivec2 res = u_QuadLog2Resolutions[quadID];
    // sampler2D alphaTexture = sampler2D(u_HtexAlphaTextureHandles[res.y*HTEX_NUM_LOG2_RESOLUTIONS+res.x]);
    // return texture(alphaTexture, xy).r;
}

float2 TriangleToQuadUV(int halfedgeID, float2 uv)
{
    int twinID = ccm_HalfedgeTwinID(halfedgeID);

    if (halfedgeID > twinID) {
        return uv;
    } else {
        return 1-uv;
    }
}

float4 Htexture(HalfedgeMeshTexture tex, int halfedgeID, float2 uv, int channel)
{
    int nextID = ccm_HalfedgeNextID(halfedgeID);
    int prevID = ccm_HalfedgePrevID(halfedgeID);

    float4 c = 0;
    float alpha = 0;

    int quadID;
    float2 xy;
    float w;

    quadID = ccm_HalfedgeEdgeID(halfedgeID);
    xy = TriangleToQuadUV(halfedgeID, uv);
    w = SampleAlpha(tex, quadID, xy);
    c += SampleQuad(tex, quadID, xy, channel) * w;
    alpha += w;

    quadID = ccm_HalfedgeEdgeID(nextID);
    xy = TriangleToQuadUV(nextID, float2(uv.y, -uv.x));
    w = SampleAlpha(tex, quadID, xy);
    c += SampleQuad(tex, quadID, xy, channel) * w;
    alpha += w;

    quadID = ccm_HalfedgeEdgeID(prevID);
    xy = TriangleToQuadUV(prevID, float2(-uv.y, uv.x));
    w = SampleAlpha(tex, quadID, xy);
    c += SampleQuad(tex, quadID, xy, channel) * w;
    alpha += w;

    return c / alpha;
}

struct HtexSampleFootprint
{
    float alpha;
    float3 a; // xy: uv, z:w
    float3 b;
    float3 c;
};

float2 ComputeQuadUV(int quadID, float2 xy, uint lod)
{
    float2 invNumQuads = 1.0 / float2(_HtexTextureNumQuadsX, _HtexTextureNumQuadsY);
    float2 invQuadSize = 1.0 / float2(_HtexTextureQuadWidth >> lod, _HtexTextureQuadHeight >> lod);

    if (any(xy < -invQuadSize * 0.5) || any(xy > 1 + invQuadSize * 0.5))
    {
        return 0;
    }

    xy = max(invQuadSize * 0.5, xy);
    xy = min(1 - invQuadSize * 0.5, xy);

    uint numQuadsX = _HtexTextureNumQuadsX;
    uint tileX = quadID % numQuadsX;
    uint tileY = quadID / numQuadsX;
    float2 baseUV = float2(tileX, tileY) * invNumQuads;

    return baseUV + xy * invNumQuads;
}

float SampleAlpha(int quadID, float2 xy, uint lod)
{
    float2 invNumQuads = 1.0 / float2(_HtexTextureNumQuadsX, _HtexTextureNumQuadsY);
    float2 invQuadSize = 1.0 / float2(_HtexTextureQuadWidth >> lod, _HtexTextureQuadHeight >> lod);

    if (any(xy < -invQuadSize * 0.5) || any(xy > 1 + invQuadSize * 0.5))
    {
        return 0;
    }

    if (any(xy < invQuadSize))
    {
        return 1;

        // TODO
        float2 d = abs(xy) / (invQuadSize * 0.5);
        return min(d.x, d.y);
    }

    if (any(xy > 1 - invQuadSize * 0.5))
    {
        return 1;

        // TODO
        float2 d = abs(xy - 1) / (invQuadSize * 0.5);
        return min(d.x, d.y);
    }

    xy = max(invQuadSize, xy);
    xy = min(1 - invQuadSize, xy);

    return 1;
}

HtexSampleFootprint Htexture(int halfedgeID, float2 baryYZ, uint lod)
{
    int nextID = ccm_HalfedgeNextID(halfedgeID);
    int prevID = ccm_HalfedgePrevID(halfedgeID);

    float4 c = 0;
    float alpha = 0;

    int quadID;
    float2 xy;
    float w;

    HtexSampleFootprint result;
    result.alpha = 0.0f;

    quadID = ccm_HalfedgeEdgeID(halfedgeID);
    xy = TriangleToQuadUV(halfedgeID, baryYZ);
    w = SampleAlpha(quadID, xy, lod);
    result.a = float3(ComputeQuadUV(quadID, xy, lod), w);
    result.alpha += w;

    quadID = ccm_HalfedgeEdgeID(nextID);
    xy = TriangleToQuadUV(nextID, float2(baryYZ.y, -baryYZ.x));
    w = SampleAlpha(quadID, xy, lod);
    result.b = float3(ComputeQuadUV(quadID, xy, lod), w);
    result.alpha += w;

    quadID = ccm_HalfedgeEdgeID(prevID);
    xy = TriangleToQuadUV(prevID, float2(-baryYZ.y, baryYZ.x));
    w = SampleAlpha(quadID, xy, lod);
    result.c = float3(ComputeQuadUV(quadID, xy, lod), w);
    result.alpha += w;

    return result;
}

#define _DEBUG_HTEX_SPATIAL_SAMPLE 0

float4 HtextureSpatialSample(HalfedgeMeshTexture tex, int currentHalfedgeID, float2 uv, float2 dir, float t)
{
    // Ensure uv coordinate is inside the triangle
    if (uv.x + uv.y == 1)
    {
        uv.x = 0.9999 - uv.y;
    }

    for (uint iter = 0; iter < 32; ++iter)
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
#if _DEBUG_HTEX_SPATIAL_SAMPLE
            return float4(0, 0, 1000, 1);
#else
            return 0;
#endif
        }

        if (t < hitT)
        {
            return Htexture(tex, currentHalfedgeID, uv + dir * t, 0);
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

#if _DEBUG_HTEX_SPATIAL_SAMPLE
    return float4(1000, 0, 0, 1);
#else
    return 0;
#endif
}