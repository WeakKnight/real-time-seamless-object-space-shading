using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
#if UNITY_2023_2_OR_NEWER
using Unity.Mathematics;
#endif

namespace GPUScene
{
    public class DistanceFiledInstanceAS
    {
        BVH bvh;

        GraphicsBuffer aabbBuffer;
#if UNITY_2023_2_OR_NEWER
        RayTracingAccelerationStructure accelerationStructure;
#endif

        static class Constants
        {
            public static int Primitives = Shader.PropertyToID("DF_InstanceAS_Primitives");
            public static int AABBs = Shader.PropertyToID("DF_InstanceAS_AABBs");
            public static int Nodes = Shader.PropertyToID("DF_InstanceAS_Nodes");
            public static int InstanceAS = Shader.PropertyToID("DF_InstanceAS");
        }

        public DistanceFiledInstanceAS()
        {
            bvh = new BVH();
        }

        public void OnGizmos()
        {
            bvh?.OnGizmos();
        }

        public void Release()
        {
            bvh?.Release();
            bvh = null;
        }

        public void Build(GPUSceneManager distanceFieldManager)
        {
            if (Config.UseInlineRayTracing == 1)
            {
                BuildForInlineRayTracing(distanceFieldManager);
            }
            else
            {
                BuildCPU(distanceFieldManager);
            }
        }

        public void BuildForInlineRayTracing(GPUSceneManager distanceFieldManager)
        {
#if UNITY_2023_2_OR_NEWER
            accelerationStructure = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings
            {
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual
            });

            List<AABB> aabbList = new List<AABB>(distanceFieldManager.GetInstanceCount());
            for (int instanceIndex = 0; instanceIndex < distanceFieldManager.GetInstanceCount(); instanceIndex++)
            {
                AABB aabb = new AABB(distanceFieldManager.GetInstanceBounds(instanceIndex));
                aabb.Expand(1e-5f);
                aabbList.Add(aabb);
            }
            if (aabbBuffer == null || aabbBuffer.count < aabbList.Count)
            {
                aabbBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, math.max(128, (int)(aabbList.Count * 1.5f)), 24);
            }
            aabbBuffer.SetData(aabbList);

            RayTracingAABBsInstanceConfig AABBsInstanceConfig = new RayTracingAABBsInstanceConfig();
            AABBsInstanceConfig.aabbBuffer = aabbBuffer;
            AABBsInstanceConfig.aabbOffset = 0;
            AABBsInstanceConfig.aabbCount = aabbBuffer.count;
            AABBsInstanceConfig.accelerationStructureBuildFlags = RayTracingAccelerationStructureBuildFlags.PreferFastBuild;
            AABBsInstanceConfig.dynamicGeometry = false;
            accelerationStructure.AddInstance(AABBsInstanceConfig, Matrix4x4.identity);

            accelerationStructure.Build();
#endif
        }

        public void BuildCPU(GPUSceneManager distanceFieldManager)
        {
            List<AABB> aabbList= new List<AABB>(distanceFieldManager.GetInstanceCount());
            for (int instanceIndex = 0; instanceIndex < distanceFieldManager.GetInstanceCount(); instanceIndex++)
            {
                AABB aabb = new AABB(distanceFieldManager.GetInstanceBounds(instanceIndex));
                aabb.Expand(1e-5f);
                aabbList.Add(aabb);
            }

            if (bvh == null)
            {
                bvh = new BVH();
            }
            bvh.Build(aabbList);
        }

        public void Bind(CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex)
        {
            if (bvh != null)
            {
                commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, Constants.Primitives, bvh.primitiveBuffer);
                commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, Constants.AABBs, bvh.aabbBuffer);
                commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, Constants.Nodes, bvh.nodeBuffer);
            }

#if UNITY_2023_2_OR_NEWER
            if (accelerationStructure != null)
            {
                commandBuffer.SetRayTracingAccelerationStructure(computeShader, kernelIndex, Constants.InstanceAS, accelerationStructure);                
            }
#endif
        }
    }
}