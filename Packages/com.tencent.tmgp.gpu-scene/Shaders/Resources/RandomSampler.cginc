#ifndef RANDOM_SAMPLER_CGINC
#define RANDOM_SAMPLER_CGINC

// "Explodes" an integer, i.e. inserts a 0 between each bit.  Takes inputs up to 16 bit wide.
//      For example, 0b11111111 -> 0b1010101010101010
uint integerExplode(uint x)
{
    x = (x | (x << 8)) & 0x00FF00FF;
    x = (x | (x << 4)) & 0x0F0F0F0F;
    x = (x | (x << 2)) & 0x33333333;
    x = (x | (x << 1)) & 0x55555555;
    return x;
}

// Reverse of RTXDI_IntegerExplode, i.e. takes every other bit in the integer and compresses
// those bits into a dense bit firld. Takes 32-bit inputs, produces 16-bit outputs.
//    For example, 0b'abcdefgh' -> 0b'0000bdfh'
uint integerCompact(uint x)
{
    x = (x & 0x11111111) | ((x & 0x44444444) >> 1);
    x = (x & 0x03030303) | ((x & 0x30303030) >> 2);
    x = (x & 0x000F000F) | ((x & 0x0F000F00) >> 4);
    x = (x & 0x000000FF) | ((x & 0x00FF0000) >> 8);
    return x;
}

// Converts a 2D position to a linear index following a Z-curve pattern.
uint ZCurveToLinearIndex(uint2 xy)
{
    return integerExplode(xy[0]) | (integerExplode(xy[1]) << 1);
}

// Converts a linear to a 2D position following a Z-curve pattern.
uint2 linearIndexToZCurve(uint index)
{
    return uint2(
        integerCompact(index),
        integerCompact(index >> 1));
}

struct RandomSamplerState
{
    uint seed;
    uint index;
};

// 32 bit Jenkins hash
uint JenkinsHash(uint a)
{
    // http://burtleburtle.net/bob/hash/integer.html
    a = (a + 0x7ed55d16) + (a << 12);
    a = (a ^ 0xc761c23c) ^ (a >> 19);
    a = (a + 0x165667b1) + (a << 5);
    a = (a + 0xd3a2646c) ^ (a << 9);
    a = (a + 0xfd7046c5) + (a << 3);
    a = (a ^ 0xb55a4f09) ^ (a >> 16);
    return a;
}

uint Murmur3(inout RandomSamplerState r)
{
#define ROT32(x, y) ((x << y) | (x >> (32 - y)))

    // https://en.wikipedia.org/wiki/MurmurHash
    uint c1 = 0xcc9e2d51;
    uint c2 = 0x1b873593;
    uint r1 = 15;
    uint r2 = 13;
    uint m = 5;
    uint n = 0xe6546b64;

    uint hash = r.seed;
    uint k = r.index++;
    k *= c1;
    k = ROT32(k, r1);
    k *= c2;

    hash ^= k;
    hash = ROT32(hash, r2) * m + n;

    hash ^= 4;
    hash ^= (hash >> 16);
    hash *= 0x85ebca6b;
    hash ^= (hash >> 13);
    hash *= 0xc2b2ae35;
    hash ^= (hash >> 16);

#undef ROT32

    return hash;
}

RandomSamplerState initRandomSampler(uint2 pixelPos, uint frameIndex)
{
    RandomSamplerState state = (RandomSamplerState)0;

    uint linearPixelIndex = ZCurveToLinearIndex(pixelPos);

    state.index = 1;
    state.seed = JenkinsHash(linearPixelIndex) + frameIndex;

    return state;
}

float sampleUniformRng(inout RandomSamplerState r)
{
    uint v = Murmur3(r);
    const uint one = asuint(1.f);
    const uint mask = (1 << 23) - 1;
    return asfloat((mask & v) | one) - 1.f;
}

float2 plastic(uint index)
{
    const float p1 = 0.7548776662466927f;
    const float p2 = 0.5698402909980532f;
    float2 result;
    result.x = fmod(p1 * float(index), 1);
    result.y = fmod(p2 * float(index), 1);
    return result;
}

float halton(uint index, uint base)
{
    float f = 1.f;
    float r = 0.f;

    while (index > 0) {
        f = f / float(base);
        r += f * float(index % base);
        index /= base;
    }
    return r;
}

float3 RandomColor(uint2 uniqueIndex)
{
    RandomSamplerState rng = initRandomSampler(uniqueIndex, 0u);
    float r = sampleUniformRng(rng);

    float h = r * 360.0f;
    float s = 0.4f;
    float l = 0.5f;

    // from HSL to RGB
    float c = (1.0f - abs(2.0f * l - 1.0f)) * s;
    float x = c * (1.0f - abs(fmod((h / 60.0f), 2.0f) - 1.0f));
    float m = l - c / 2.0f;

    float3 rgb;

    if (h >= 0.0f && h < 60.0f)
        rgb = float3(c, x, 0.0f);
    else if (h >= 60.0f && h < 120.0f)
        rgb = float3(x, c, 0.0f);
    else if (h >= 120.0f && h < 180.0f)
        rgb = float3(0.0f, c, x);
    else if (h >= 180.0f && h < 240.0f)
        rgb = float3(0.0f, x, c);
    else if (h >= 240.0f && h < 300.0f)
        rgb = float3(x, 0.0f, c);
    else
        rgb = float3(c, 0.0f, x);

    return rgb;
}

float3 RandomColor(uint uniqueIndex)
{
    RandomSamplerState rng = initRandomSampler(uint2(0u, 0u), uniqueIndex);
    float r = sampleUniformRng(rng);

    float h = r * 360.0f;
    float s = 0.4f;
    float l = 0.5f;

    // from HSL to RGB
    float c = (1.0f - abs(2.0f * l - 1.0f)) * s;
    float x = c * (1.0f - abs(fmod((h / 60.0f), 2.0f) - 1.0f));
    float m = l - c / 2.0f;

    float3 rgb;

    if (h >= 0.0f && h < 60.0f)
        rgb = float3(c, x, 0.0f);
    else if (h >= 60.0f && h < 120.0f)
        rgb = float3(x, c, 0.0f);
    else if (h >= 120.0f && h < 180.0f)
        rgb = float3(0.0f, c, x);
    else if (h >= 180.0f && h < 240.0f)
        rgb = float3(0.0f, x, c);
    else if (h >= 240.0f && h < 300.0f)
        rgb = float3(x, 0.0f, c);
    else
        rgb = float3(c, 0.0f, x);

    return rgb;
}

float3 RandomGrayColor(uint2 uniqueIndex)
{
    RandomSamplerState rng = initRandomSampler(uniqueIndex, 0u);
    float r = sampleUniformRng(rng);

    return r;
}

float3 RandomGrayColor(uint uniqueIndex)
{
    RandomSamplerState rng = initRandomSampler(uint2(0u, 0u), uniqueIndex);
    float r = sampleUniformRng(rng);

    return r;
}

static const uint2 SobolMatrices[] = 
{
    uint2(0x00000001, 0x00000001),
    uint2(0x00000003, 0x00000003),
    uint2(0x00000006, 0x00000004),
    uint2(0x00000009, 0x0000000a),
    uint2(0x00000017, 0x0000001f),
    uint2(0x0000003a, 0x0000002e),
    uint2(0x00000071, 0x00000045),
    uint2(0x000000a3, 0x000000c9),
    uint2(0x00000116, 0x0000011b),
    uint2(0x00000339, 0x000002a4),
    uint2(0x00000677, 0x0000079a),
    uint2(0x000009aa, 0x00000b67),
    uint2(0x00001601, 0x0000101e),
    uint2(0x00003903, 0x0000302d),
    uint2(0x00007706, 0x00004041),
    uint2(0x0000aa09, 0x0000a0c3),
    uint2(0x00010117, 0x0001f104),
    uint2(0x0003033a, 0x0002e28a),
    uint2(0x00060671, 0x000457df),
    uint2(0x000909a3, 0x000c9bae),
    uint2(0x00171616, 0x0011a105),
    uint2(0x003a3939, 0x002a7289),
    uint2(0x00717777, 0x0079e7db),
    uint2(0x00a3aaaa, 0x00b6dba4),
    uint2(0x01170001, 0x0100011a),
    uint2(0x033a0003, 0x030002a7),
    uint2(0x06710006, 0x0400079e),
    uint2(0x09a30009, 0x0a000b6d),
    uint2(0x16160017, 0x1f001001),
    uint2(0x3939003a, 0x2e003003),
    uint2(0x77770071, 0x45004004),
    uint2(0xaaaa00a3, 0xc900a00a)
};

// High quality integer hash - this mixes bits almost perfectly
uint StrongIntegerHash(uint x)
{
    // From https://github.com/skeeto/hash-prospector
    // bias = 0.16540778981744320
    x ^= x >> 16;
    x *= 0xa812d533;
    x ^= x >> 15;
    x *= 0xb278e4ad;
    x ^= x >> 17;
    return x;
}

// This is a much weaker hash, but is faster and can be used to drive other hashes
uint WeakIntegerHash(uint x)
{
    // Generated using https://github.com/skeeto/hash-prospector
    // Estimated Bias ~583
    x *= 0x92955555u;
    x ^= x >> 15;
    return x;
}

uint EvolveSobolSeed(inout uint Seed)
{
    // constant from: https://www.pcg-random.org/posts/does-it-beat-the-minimal-standard.html
    const uint MCG_C = 2739110765;
    // a slightly weaker hash is ok since this drives FastOwenScrambling which is itself a hash
    // Note that the Seed evolution is just an integer addition and the hash should optimize away
    // when a particular dimension is not used
    return WeakIntegerHash(Seed += MCG_C);
}

uint FastOwenScrambling(uint Index, uint Seed) 
{
    // Laine and Karras / Stratified Sampling for Stochastic Transparency / EGSR 2011

    // The operations below will mix bits toward the left, so temporarily reverse the order
    // NOTE: This operation has been performed outside this call
    // Index = reversebits(Index);

    // This follows the basic construction from the paper above. The error-diffusion sampler which
    // tiles a single point set across the screen, makes it much easier to visually "see" the impact of the scrambling.
    // When the scrambling is not good enough, the structure of the space-filling curve is left behind.
    // Care must be taken to ensure that the scrambling is still unbiased on average though. I found some cases
    // that seemed to produce lower visual error but were actually biased due to not touching certain bits,
    // leading to correlations across dimensions.

    // After much experimentation with the hash prospector, I discovered the following two constants which appear to
    // give results as good (or better) than the original 4 xor-mul constants from the Laine/Karras paper.
    // It isn't entirely clear to me why some constants work better than others. Some hashes with slightly less
    // bias produced visibly more error or worse looking power spectra.
    // Estimates below from hash prospector for all hashes of the form: add,xmul=c0,xmul=c1 (with c0 and c1 being
    // the constants below).
    // Ran with score_quality=16 for about ~10000 random hashes
    // Average bias: ~727.02
    //   Best  bias: ~723.05
    //   Worst bias: ~735.19
    Index += Seed; // randomize the index by our seed (pushes bits toward the left)
    Index ^= Index * 0x9c117646u;
    Index ^= Index * 0xe0705d72u;

    // Undo the reverse so that we get left-to-right scrambling
    // thereby emulating owen-scrambling
    return reversebits(Index);
}

float4 SobolSampler(uint SampleIndex, inout uint Seed)
{
    // first scramble the index to decorelate from other 4-tuples
    uint SobolIndex = FastOwenScrambling(SampleIndex, EvolveSobolSeed(Seed));
    // now get Sobol' point from this index
    uint4 Result = uint4(SobolIndex, 0, 0, 0);
    for (uint b = 0, v = 1; SobolIndex > 0u; SobolIndex >>= 1, b++)
    {
        uint IndexBit = SobolIndex & 1;
        Result.y ^= IndexBit * v;
        Result.zw ^= IndexBit * SobolMatrices[b];
        v ^= v << 1;
    }
    // finally scramble the points to avoid structured artifacts
    Result.x = FastOwenScrambling(Result.x, EvolveSobolSeed(Seed));
    Result.y = FastOwenScrambling(Result.y, EvolveSobolSeed(Seed));
    Result.z = FastOwenScrambling(Result.z, EvolveSobolSeed(Seed));
    Result.w = FastOwenScrambling(Result.w, EvolveSobolSeed(Seed));

    // output as float in [0,1) taking care not to skew the distribution
    // due to the non-uniform spacing of floats in this range
    return (Result >> 8) * 5.96046447754e-08; // * 2^-24
}

struct RandomSequence
{
    uint SampleIndex; // index into the random sequence
    uint SampleSeed;  // changes as we draw samples to reflect the change in dimension
};

void RandomSequence_Initialize(inout RandomSequence RandSequence, uint PositionSeed, uint TimeSeed)
{
    // pre-compute bit reversal needed for FastOwenScrambling since this index doesn't change
    RandSequence.SampleIndex = reversebits(TimeSeed);
    // change seed to get a unique sequence per pixel
    RandSequence.SampleSeed = StrongIntegerHash(PositionSeed);
}

float RandomSequence_GenerateSample1D(inout RandomSequence RandSequence)
{
    float Result = SobolSampler(RandSequence.SampleIndex, RandSequence.SampleSeed).x;
    return Result;
}

float2 RandomSequence_GenerateSample2D(inout RandomSequence RandSequence)
{
    float2 Result = SobolSampler(RandSequence.SampleIndex, RandSequence.SampleSeed).xy;
    return Result;
}

#endif // RANDOM_SAMPLER_CGINC