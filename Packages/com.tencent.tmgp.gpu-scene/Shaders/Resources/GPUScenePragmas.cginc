#include "GPUSceneConfig.hlsl"

#if USE_MESH_RAY_TRACING
#pragma require int64BufferAtomics
#pragma require inlineraytracing
#elif USE_INLINE_RAY_TRACING
#pragma require inlineraytracing
#endif