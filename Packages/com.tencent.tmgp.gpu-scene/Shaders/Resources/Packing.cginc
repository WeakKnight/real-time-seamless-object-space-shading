#ifndef PACKING_CGINC
#define PACKING_CGINC

float2 oct_wrap(float2 v)
{
    return (1.f - abs(v.yx)) * float2(v.x >= 0.f ? 1.f : -1.f, v.y >= 0.f ? 1.f : -1.f);
}

/** Converts normalized direction to the octahedral map (non-equal area, signed normalized).
    \param[in] n Normalized direction.
    \return Position in octahedral map in [-1,1] for each component.
*/
float2 ndir_to_oct_snorm(float3 n)
{
    // Project the sphere onto the octahedron (|x|+|y|+|z| = 1) and then onto the xy-plane.
    float2 p = n.xy * (1.f / (abs(n.x) + abs(n.y) + abs(n.z)));
    p = (n.z < 0.f) ? oct_wrap(p) : p;
    return p;
}

/** Convert float value to 8-bit snorm value.
    Values outside [-1,1] are clamped and NaN is encoded as zero.
    \return 8-bit snorm value in low bits, high bits are all zeros or ones depending on sign.
*/
int floatToSnorm8(float v)
{
    v = isnan(v) ? 0.f : min(max(v, -1.f), 1.f);
    return (int)trunc(v * 127.f + (v >= 0.f ? 0.5f : -0.5f));
}

/** Pack two floats into 8-bit snorm values in the lo bits of a dword.
    \return Two 8-bit snorm in low bits, high bits all zero.
*/
uint packSnorm2x8(precise float2 v)
{
    return (floatToSnorm8(v.x) & 0x000000ff) | ((floatToSnorm8(v.y) << 8) & 0x0000ff00);
}

/** Unpack two 8-bit snorm values from the lo bits of a dword.
    \param[in] packed Two 8-bit snorm in low bits, high bits don't care.
    \return Two float values in [-1,1].
*/
float2 unpackSnorm2x8(uint packed)
{
    int2 bits = int2((int)(packed << 24), (int)(packed << 16)) >> 24;
    precise float2 unpacked = max((float2)bits / 127.f, -1.0f);
    return unpacked;
}

/** Converts point in the octahedral map to normalized direction (non-equal area, signed normalized).
    \param[in] p Position in octahedral map in [-1,1] for each component.
    \return Normalized direction.
*/
float3 oct_to_ndir_snorm(float2 p)
{
    float3 n = float3(p.xy, 1.0 - abs(p.x) - abs(p.y));
    n.xy = (n.z < 0.0) ? oct_wrap(n.xy) : n.xy;
    return normalize(n);
}

/** Encode a normal packed as 2x 8-bit snorms in the octahedral mapping. The high 16 bits are unused.
 */
uint encodeNormal2x8(float3 normal)
{
    float2 octNormal = ndir_to_oct_snorm(normal);
    return packSnorm2x8(octNormal);
}

/** Decode a normal packed as 2x 8-bit snorms in the octahedral mapping.
 */
float3 decodeNormal2x8(uint packedNormal)
{
    float2 octNormal = unpackSnorm2x8(packedNormal);
    return oct_to_ndir_snorm(octNormal);
}

uint PackR10G11B11UnormToUINT(float3 xyz)
{
    const uint mask10Bit = (1U << 10) - 1U;
    const uint mask11Bit = (1U << 11) - 1U;

    uint x = uint(xyz.x * mask10Bit) << 22;
    uint y = uint(xyz.y * mask11Bit) << 11;
    uint z = uint(xyz.z * mask11Bit);
    return x | y | z;
}

float3 UnpackR10G11B11UnormFromUINT(uint xyz)
{
    const uint mask10Bit = (1U << 10) - 1U;
    const uint mask11Bit = (1U << 11) - 1U;

    uint x = xyz >> 22 & mask10Bit;
    uint y = xyz >> 11 & mask11Bit;
    uint z = xyz & mask11Bit;

    return float3((float)x / (float)mask10Bit, (float)y / (float)mask11Bit, (float)z / (float)mask11Bit);
}

float3 GPUScene_RGBMDecode(float4 rgbm)
{
    float3 result = 6.0f * rgbm.rgb * rgbm.a;
    return result * result;
}

float4 GPUScene_RGBMEncode(float3 color)
{
    color = float3(sqrt(color.x), sqrt(color.y), sqrt(color.z)) / 6.0f;
    float m = saturate(max(max(color.x, color.y), max(color.z, 1e-6f)));
    m = ceil(m * 255.0f) / 255.0f;
    color = float3(color.x / m, color.y / m, color.z / m);
    return float4(color, m);
}

#endif // PACKING_CGINC