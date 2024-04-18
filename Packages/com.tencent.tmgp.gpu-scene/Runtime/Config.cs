using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    [GenerateHLSL(PackingRules.Exact, false, sourcePath: "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/GPUSceneConfig")]
    public static class Config
    {
#if UNITY_2023_2_OR_NEWER
        public static int UseMeshRayTracing = 1;
#else
        public static int UseMeshRayTracing = 0;
#endif

#if UNITY_2023_2_OR_NEWER
        public static int UseInlineRayTracing = 1;
#else
        public static int UseInlineRayTracing = 0;
#endif
    }
}
