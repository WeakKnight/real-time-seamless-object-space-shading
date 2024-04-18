using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace GPUScene
{
    public static class CPURT
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RayDesc
        {
            public float3 origin;
            public float tmin;
            public float3 direction;
            public float tmax;

            public RayDesc(float3 o, float3 d, float tmin = 0.0f, float tmax = 1e30f)
            {
                origin = o;
                direction = d;
                this.tmin = tmin;
                this.tmax = tmax;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HitInfo
        {
            public int hit;
            public float t;
            public int primIdx;
            public float baryX;
            public float baryY;
            public int hitBackFace;
            public float pad1;
            public float pad2;
        }

#if UNITY_IPHONE
        [DllImport ("__Internal")]
#else
        [DllImport("CPURT")]
#endif
        static extern IntPtr cpurt_init(IntPtr trisPtr, int triNum);

#if UNITY_IPHONE
        [DllImport ("__Internal")]
#else
        [DllImport("CPURT")]
#endif
        static extern void cpurt_dispatch_rays(IntPtr context, IntPtr rayDescs, int rayCount, IntPtr results);

#if UNITY_IPHONE
        [DllImport ("__Internal")]
#else
        [DllImport("CPURT")]
#endif
        static extern void cpurt_release(IntPtr context);

        public static unsafe IntPtr Init(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;

            NativeArray<float> tris = new NativeArray<float>(indices.Length * 3, Allocator.Temp);
            for (int i = 0; i < indices.Length; i++)
            {
                Vector3 vertex = vertices[indices[i]];
                tris[i * 3 + 0] = vertex.x;
                tris[i * 3 + 1] = vertex.y;
                tris[i * 3 + 2] = vertex.z;
            }

            return cpurt_init((IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(tris), indices.Length / 3);
        }
        
        public static unsafe void DispatchRays(IntPtr context, NativeArray<RayDesc> rays, NativeArray<HitInfo> hitInfos)
        {
            cpurt_dispatch_rays(context, (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(rays), rays.Length, (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(hitInfos));
        }

        public static unsafe void Release(IntPtr context)
        {
            cpurt_release(context);
        }
    }
}
