#ifndef OSS_UTILS_CGINC
#define OSS_UTILS_CGINC

static const float __PI = 3.14159265f;
static const float __InvPI = 1.0f / 3.14159265f;

float3 ToneMapAces(float3 color)
{
    float A = 2.51;
    float B = 0.03;
    float C = 2.43;
    float D = 0.59;
    float E = 0.14;

    // color = saturate((color * (A * color + B)) / (color * (C * color + D) + E));
    return color;
}

uint Convert2DTo1D(uint2 location, uint width)
{
    return location.x + location.y * width;
}

uint2 Convert1DTo2D(uint offset, uint width)
{
    return uint2(offset % width, offset / width);
}

uint PackR16UG16UToUINT(uint2 xy)
{
    uint x = xy.x;
    uint y = xy.y << 16;
    return x | y;
}

float Gaussian(float radius, float sigma)
{
    float a = radius / sigma;
    return exp(-a * a);
}

static const uint _PrimitiveIndexBitCount = 24;
static const uint _PrimitiveInvalidFlag = 0x80000000u;

void PackPrimitiveIndex(uint primitiveIndex, uint subMeshIndex, out uint packedPrimitiveIndex)
{
    packedPrimitiveIndex = primitiveIndex | (subMeshIndex << _PrimitiveIndexBitCount);
}

void UnpackPrimitiveIndex(uint packedPrimitiveIndex, out uint primitiveIndex, out uint subMeshIndex)
{
    const uint mask26Bit = (1U << _PrimitiveIndexBitCount) - 1U;
    primitiveIndex = packedPrimitiveIndex & mask26Bit;
    subMeshIndex = packedPrimitiveIndex >> _PrimitiveIndexBitCount;
}

uint2 UnpackUINTToR16UG16U(uint xy)
{
    const uint mask16Bit = (1U << 16) - 1U;
    uint x = xy & mask16Bit;
    uint y = xy >> 16;
    return uint2(x, y);
}

uint ComputeLODFrom1DOffset(uint offset, uint chartSize)
{
    const uint a = chartSize * chartSize;
    const float factor = 1.0f - (0.75f * offset) / (float)a;
    return (uint)(log2(factor) / -2.0f);
}

uint ComputeMip1DBaseOffset(uint lod, uint chartSize)
{
    const uint a = chartSize * chartSize;
    const uint s = (uint)(4.0f / 3.0f * a * (1.0f - pow(2.0f, -2.0f * lod)));
    return s;
}

int2 GetTexelFromLODLevel(int size, int level, int x, int y)
{
    int offsetX = 0;
    int offsetY = 0;

    uint scale = 1;
    if (level > 0)
    {
        scale = 1 << level;

        offsetX = size;
        offsetY = (int)(size * (float)(1.0f - 1.0f / (1 << (level - 1))));
    }

    return int2(offsetX + x / scale, offsetY + y / scale);
}

int GetLODLevelFromTexel(int size, int x, int y)
{
    if (x < size)
    {
        return 0;
    }

    return 1 + (int)log2(size / (float)(size - y));
}

uint2 LODToOriginalTexel(uint size, uint lodLevel, uint x, uint y)
{
    if (lodLevel == 0)
    {
        return uint2(x, y);
    }

    uint scale = 1 << lodLevel;
    uint baseX = size;
    uint baseY = (uint)(size * (float)(1.0f - 1.0f / (1 << (lodLevel - 1))));

    return uint2((x - baseX) * scale, (y - baseY) * scale);
}

uint2 GetHigherLODTexel(uint size, uint lodLevel, uint x, uint y)
{
    uint2 texelLod0 = LODToOriginalTexel(size, lodLevel, x, y);
    return GetTexelFromLODLevel(size, lodLevel + 1, texelLod0.x, texelLod0.y);
}

uint2 GetLowerLODTexel(uint size, uint lodLevel, uint x, uint y)
{
    uint2 texelLod0 = LODToOriginalTexel(size, lodLevel, x, y);
    return GetTexelFromLODLevel(size, lodLevel - 1, texelLod0.x, texelLod0.y);
}

float __CalcLuminance(float3 color)
{
    return dot(color.xyz, float3(0.2126f, 0.7152f, 0.0722f));
}

uint __PackR8ToUFLOAT(float r)
{
    const uint mask = (1U << 8U) - 1U;
    return (uint)floor(r * mask + 0.5f) & mask;
}

uint __PackColor32ToUInt(float4 col)
{
    uint r = __PackR8ToUFLOAT(col.x);
    uint g = __PackR8ToUFLOAT(col.y);
    uint b = __PackR8ToUFLOAT(col.z);
    uint a = __PackR8ToUFLOAT(col.w);

    uint x = r;
    uint y = g << 8u;
    uint z = b << 16u;
    uint w = a << 24u;

    return x | y | z | w;
}

float __UnpackR8ToUFLOAT(uint r)
{
    const uint mask = (1U << 8) - 1U;
    return (float)(r & mask) / (float)mask;
}

float4 __UnpackR8G8B8A8ToUFLOAT(uint rgba)
{
    float r = __UnpackR8ToUFLOAT(rgba);
    float g = __UnpackR8ToUFLOAT(rgba >> 8);
    float b = __UnpackR8ToUFLOAT(rgba >> 16);
    float a = __UnpackR8ToUFLOAT(rgba >> 24);
    return float4(r, g, b, a);
}

float3 RGBMDecode(float4 rgbm)
{
    rgbm = saturate(rgbm);

    float3 result = 6.0f * rgbm.rgb * rgbm.a;
    return result * result;
}

float4 RGBMEncode(float3 color)
{
    color = clamp(color, 0.0f, 25.0f);

    color = float3(sqrt(color.x), sqrt(color.y), sqrt(color.z)) / 6.0f;
    float m = saturate(max(max(color.x, color.y), max(color.z, 1e-6f)));
    m = ceil(m * 255.0f) / 255.0f;
    color = float3(color.x / m, color.y / m, color.z / m);
    return float4(color, m);
}

/**********************/

// Helper function to reflect the folds of the lower hemisphere
// over the diagonals in the octahedral map
float2 __octWrap(float2 v)
{
#if UNITY_COMPILER_DXC
    return (1.f - abs(v.yx)) * select(v.xy >= 0.f, 1.f, -1.f);
#else
    return (1.f - abs(v.yx)) * (v.xy >= 0.f ? 1.f : -1.f);
#endif
}

/**********************/
// Signed encodings
// Converts a normalized direction to the octahedral map (non-equal area, signed)
// n - normalized direction
// Returns a signed position in octahedral map [-1, 1] for each component
float2 __ndirToOctSigned(float3 n)
{
    // Project the sphere onto the octahedron (|x|+|y|+|z| = 1) and then onto the xy-plane
    float2 p = n.xy * (1.f / (abs(n.x) + abs(n.y) + abs(n.z)));
    return (n.z < 0.f) ? __octWrap(p) : p;
}

// Converts a point in the octahedral map to a normalized direction (non-equal area, signed)
// p - signed position in octahedral map [-1, 1] for each component
// Returns normalized direction
float3 __octToNdirSigned(float2 p)
{
    // https://twitter.com/Stubbesaurus/status/937994790553227264
    float3 n = float3(p.x, p.y, 1.0 - abs(p.x) - abs(p.y));
    float t = max(0, -n.z);
#if UNITY_COMPILER_DXC
    n.xy += select(n.xy >= 0.0, -t, t);
#else
    n.xy += n.xy >= 0.0 ? -t : t;
#endif
    return normalize(n);
}

// Unorm 32 bit encodings
// Converts a normalized direction to the octahedral map (non-equal area, unsigned normalized)
// n - normalized direction
// Returns a packed 32 bit unsigned normalized position in octahedral map
// The two components of the result are stored in UNORM16 format, [0..1]
uint __ndirToOctUnorm32(float3 n)
{
    float2 p = __ndirToOctSigned(n);
    p = saturate(p.xy * 0.5 + 0.5);
    return uint(p.x * 0xfffe) | (uint(p.y * 0xfffe) << 16);
}

// Converts a point in the octahedral map (non-equal area, unsigned normalized) to normalized direction
// pNorm - a packed 32 bit unsigned normalized position in octahedral map
// Returns normalized direction
float3 __octToNdirUnorm32(uint pUnorm)
{
    float2 p;
    p.x = saturate(float(pUnorm & 0xffff) / 0xfffe);
    p.y = saturate(float(pUnorm >> 16) / 0xfffe);
    p = p * 2.0 - 1.0;
    return __octToNdirSigned(p);
}

/*
https://graphics.pixar.com/library/OrthonormalB/paper.pdf
*/
void branchlessONB(in float3 n, out float3 b1, out float3 b2)
{
    float sign = n.z >= 0.0f ? 1.0f : -1.0f;
    float a = -1.0f / (sign + n.z);
    float b = n.x * n.y * a;
    b1 = float3(1.0f + sign * n.x * n.x * a, sign * b, -sign * n.x);
    b2 = float3(b, sign + n.y * n.y * a, -n.y);
}

// "Insert" a 0 bit after each of the 16 low bits of x.
// Ref: https://fgiesen.wordpress.com/2009/12/13/decoding-morton-codes/
uint __Part1By1(uint x)
{
    x &= 0x0000ffff;                 // x = ---- ---- ---- ---- fedc ba98 7654 3210
    x = (x ^ (x << 8)) & 0x00ff00ff; // x = ---- ---- fedc ba98 ---- ---- 7654 3210
    x = (x ^ (x << 4)) & 0x0f0f0f0f; // x = ---- fedc ---- ba98 ---- 7654 ---- 3210
    x = (x ^ (x << 2)) & 0x33333333; // x = --fe --dc --ba --98 --76 --54 --32 --10
    x = (x ^ (x << 1)) & 0x55555555; // x = -f-e -d-c -b-a -9-8 -7-6 -5-4 -3-2 -1-0
    return x;
}

uint __EncodeMorton2D(uint2 coord)
{
    return (__Part1By1(coord.y) << 1) + __Part1By1(coord.x);
}

float3 CosineWeightedSampling(float2 XY, float3 N, out float pdf)
{
    float3 w = N;
    float3 u = 0.0;
    if (abs(w.x) > 0.1)
    {
        u = normalize(cross(float3(0.0, 1.0, 0.0), w));
    }
    else
    {
        u = normalize(cross(float3(1.0, 0.0, 0.0), w));
    }
    float3 v = cross(w, u);
    float r1 = 2.0 * __PI * XY.x;
    float r2 = XY.y;
    float r2s = sqrt(r2);
    float3 dir = normalize((u * cos(r1) * r2s + v * sin(r1) * r2s + w * sqrt(1.0 - r2)));

    pdf = dot(N, dir) / __PI;

    return dir;
}

float3 UniformSampleHemisphere(float2 u) 
{
    float z = u[0];
    float r = sqrt(max(0.0f, 1.0f - z * z));
    float phi = 2 * __PI * u[1];
    return float3(r * cos(phi), r * sin(phi), z);
}

float3 ComputeRayOrigin(float3 pos, float3 normal)
{
    const float origin = 1.f / 16.f;
    const float fScale = 3.f / 65536.f;
    const float iScale = 3 * 256.f;

    // const float origin = 1.f / 16.f;
    // const float fScale = 6.f / 65536.f;
    // const float iScale = 6.2 * 256.f;

    // Per-component integer offset to bit representation of fp32 position.
    int3 iOff = int3(normal * iScale);
#if UNITY_COMPILER_DXC
    float3 iPos = asfloat(asint(pos) + select(pos < 0.f, -iOff, iOff));
#else
    float3 iPos = asfloat(asint(pos) + (pos < 0.f ? -iOff : iOff));
#endif

    // Select per-component between small fixed offset or above variable offset depending on distance to origin.
    float3 fOff = normal * fScale;
#if UNITY_COMPILER_DXC
    float3 result = select(abs(pos) < origin, pos + fOff, iPos);
#else
    float3 result = abs(pos) < origin ? pos + fOff : iPos;
#endif

    if (dot(result - pos, result - pos) < 1e-6f)
    {
        result = pos + normal * 1e-3f;
    }

    return result;
}

uint CountOccupancyBits(uint2 occupancyBitfield, uint offsetInBlock)
{
    if (offsetInBlock >= 32u)
    {
        uint a = countbits(occupancyBitfield.x);
        uint b;
        if (offsetInBlock == 32)
        {
            b = 0;
        }
        else
        {
            b = countbits(occupancyBitfield.y >> (63u - offsetInBlock + 1));
        }
        return a + b;
    }
    else
    {
        if (offsetInBlock == 0)
        {
            return 0;
        }

        return countbits(occupancyBitfield.x >> (31u - offsetInBlock + 1));
    }
}

uint GetBit(uint value, uint bitIndex)
{
    uint mask = 1u << bitIndex;
    return (value & mask) >> bitIndex;
}

uint GetBit(uint2 value, uint bitIndex)
{
    if (bitIndex < 32)
    {
        return GetBit(value.x, bitIndex);
    }
    else
    {
        return GetBit(value.y, bitIndex);
    }
}

/*
Make sure specific LOD levels can be handled by decoupled shading, which means Mipmap size can not be less than 64
*/
void FilterLodLevel(uint mipLayerCount, inout float lod, inout uint lodLow, inout uint lodHigh)
{
    const uint forceLodLevel = ~0u;

    {
        const uint maxLod = 3;

        lod = min(lod, maxLod);
        lodLow = min(lodLow, maxLod);
        lodHigh = min(lodHigh, maxLod);
    }

    if (forceLodLevel != ~0u)
    {
        lod = forceLodLevel;
        lodLow = forceLodLevel;
        lodHigh = forceLodLevel;
    }
}

#endif // OSS_COMMON_CGINC