using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    public class MeshCardPool
    {
        const int MeshCardAtlasSize = 2048;
        const int MeshCardTileSize = 32;

        ComputeBuffer mMeshCardIndirection;
        ComputeBuffer mMeshCardData;

        public RenderTexture MeshCardAtlasTextureBaseColor;
        public RenderTexture MeshCardAtlasTextureEmissive;
        public RenderTexture MeshCardAtlasTextureNormal;
        public RenderTexture MeshCardAtlasTexturePosition;
        public RenderTexture MeshCardAtlasTextureIrradiance;

        RectAllocator mRectAllocator;

        uint mNextDataIndex = 0;

        public bool dirty = true;

        public ComputeShader mMeshCardRadiosityShader;
        public int mMeshCardRadiosityFinalShadingKernel;

        static class Constants
        {
            public static int FaceIndex = Shader.PropertyToID("_FaceIndex");
            public static int VolumeExtent = Shader.PropertyToID("_VolumeExtent");
            public static int WorldToVolume = Shader.PropertyToID("_WorldToVolume");
            public static int LocalToWorld = Shader.PropertyToID("_LocalToWorld");
            public static int WorldToLocal = Shader.PropertyToID("_WorldToLocal");

            public static int MeshCardIndirection = Shader.PropertyToID("_MeshCardIndirection");
            public static int MeshCardData = Shader.PropertyToID("_MeshCardData");
            public static int MeshCardAtlasBaseColor = Shader.PropertyToID("_MeshCardAtlasBaseColor");
            public static int MeshCardAtlasEmissive = Shader.PropertyToID("_MeshCardAtlasEmissive");
            public static int MeshCardAtlasNormal = Shader.PropertyToID("_MeshCardAtlasNormal");
            public static int MeshCardAtlasPosition = Shader.PropertyToID("_MeshCardAtlasPosition");
            public static int MeshCardAtlasIrradiance = Shader.PropertyToID("_MeshCardAtlasIrradiance");

            public static int InstanceIndex = Shader.PropertyToID("_InstanceIndex");
            public static int FrameIndex = Shader.PropertyToID("_FrameIndex");
            public static int MeshCardAtlasIrradianceRW = Shader.PropertyToID("_MeshCardAtlasIrradianceRW");
        }

        public MeshCardPool()
        {
            mMeshCardIndirection = new ComputeBuffer(1024, 4, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
            mMeshCardData = new ComputeBuffer(1024 * 6, 16, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
            MeshCardAtlasTextureBaseColor = new RenderTexture(MeshCardAtlasSize, MeshCardAtlasSize, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            MeshCardAtlasTextureEmissive = new RenderTexture(MeshCardAtlasSize, MeshCardAtlasSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            MeshCardAtlasTextureNormal = new RenderTexture(MeshCardAtlasSize, MeshCardAtlasSize, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UInt);
            MeshCardAtlasTexturePosition = new RenderTexture(MeshCardAtlasSize, MeshCardAtlasSize, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt);

            MeshCardAtlasTextureIrradiance = new RenderTexture(MeshCardAtlasSize, MeshCardAtlasSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            MeshCardAtlasTextureIrradiance.enableRandomWrite = true;
            MeshCardAtlasTextureIrradiance.Create();

            mRectAllocator = new RectAllocator(MeshCardTileSize, 0, MeshCardAtlasSize / MeshCardTileSize);

            mMeshCardRadiosityShader = Resources.Load<ComputeShader>("MeshCardRadiosity");
            mMeshCardRadiosityFinalShadingKernel = mMeshCardRadiosityShader.FindKernel("FinalShading");
        }

        public void Clear()
        {
            mRectAllocator = new RectAllocator(MeshCardTileSize, 0, MeshCardAtlasSize / MeshCardTileSize);

            mNextDataIndex = 0;
        }

        public void PrepareMeshCardIrradiance(GPUSceneManager sceneManager, CommandBuffer commandBuffer, int frameIndex)
        {
            sceneManager.BindUniforms(commandBuffer, mMeshCardRadiosityShader);
            commandBuffer.SetComputeIntParam(mMeshCardRadiosityShader, Constants.FrameIndex, frameIndex);

            sceneManager.BindBuffers(commandBuffer, mMeshCardRadiosityShader, mMeshCardRadiosityFinalShadingKernel);
            commandBuffer.SetComputeTextureParam(mMeshCardRadiosityShader, mMeshCardRadiosityFinalShadingKernel, Constants.MeshCardAtlasIrradianceRW, MeshCardAtlasTextureIrradiance);
        }

        public void UpdateMeshCardIrradiance(CommandBuffer commandBuffer, GPUSceneInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            commandBuffer.BeginSample("Update Mesh Card Irradiance");

            commandBuffer.SetComputeIntParam(mMeshCardRadiosityShader, Constants.InstanceIndex, instance.instanceIndex);
            commandBuffer.DispatchCompute(mMeshCardRadiosityShader, mMeshCardRadiosityFinalShadingKernel, (MeshCardTileSize + 7) / 8, (MeshCardTileSize + 7) / 8, 6);

            commandBuffer.EndSample("Update Mesh Card Irradiance");
        }

        public void CreateMeshCard(GPUSceneInstance instance)
        {
            if (instance == null || instance.instanceIndex < 0)
            {
                return;
            }

            MeshRenderer meshRenderer = instance.gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                return;
            }

            MeshFilter meshFilter = instance.gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            Material material = meshRenderer.sharedMaterial;
            int meshCardPass = material.FindPass("MeshCard");
            if (meshCardPass < 0)
            {
                return;
            }

            GPUSceneManager mgr = Object.FindAnyObjectByType<GPUSceneManager>();
            if (mgr == null)
            {
                return;
            }

            NativeArray<uint> meshCardIndirectionItem = mMeshCardIndirection.BeginWrite<uint>(instance.instanceIndex, 1);
            meshCardIndirectionItem[0] = mNextDataIndex;
            mMeshCardIndirection.EndWrite<uint>(1);

            NativeArray<uint4> meshCardDataItems = mMeshCardData.BeginWrite<uint4>((int)mNextDataIndex, 6);
            
            RenderTargetBinding renderTargetBinding = new()
            {
                colorRenderTargets = new RenderTargetIdentifier[] { MeshCardAtlasTextureBaseColor.colorBuffer, MeshCardAtlasTextureEmissive.colorBuffer, MeshCardAtlasTextureNormal.colorBuffer, MeshCardAtlasTexturePosition.colorBuffer },
                depthRenderTarget = MeshCardAtlasTextureBaseColor.depthBuffer,
                colorLoadActions = new RenderBufferLoadAction[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.Load, RenderBufferLoadAction.Load, RenderBufferLoadAction.Load },
                colorStoreActions = new RenderBufferStoreAction[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store, RenderBufferStoreAction.Store, RenderBufferStoreAction.Store }
            };

            for (int i = 0; i < 6; i++)
            {
                uint handle = mRectAllocator.Alloc(MeshCardTileSize, MeshCardTileSize);
                meshCardDataItems[i] = mRectAllocator.GetSizePosition(handle);

                uint2 size = new uint2(meshCardDataItems[i].x, meshCardDataItems[i].y);
                uint2 position = new uint2(meshCardDataItems[i].z, meshCardDataItems[i].w);

                CommandBuffer commandBuffer = new CommandBuffer();
                commandBuffer.name = "Render Mesh Card";

                commandBuffer.SetRenderTarget(renderTargetBinding);

                commandBuffer.ClearRenderTarget(true, false, Color.black);
                commandBuffer.SetViewport(new Rect(position.x, position.y, size.x, size.y));
                
                commandBuffer.SetGlobalInt(Constants.FaceIndex, i);
                commandBuffer.SetGlobalMatrix(Constants.WorldToVolume, instance.GetWorldToVolume());
                commandBuffer.SetGlobalVector(Constants.VolumeExtent, new float4(mgr.GetDistanceField(mesh).volumeExtent, 0.0f));
                commandBuffer.SetGlobalMatrix(Constants.LocalToWorld, instance.transform.localToWorldMatrix);
                commandBuffer.SetGlobalMatrix(Constants.WorldToLocal, instance.transform.worldToLocalMatrix);

                MaterialPropertyBlock block = new();
                meshRenderer.GetPropertyBlock(block);
                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, subMeshIndex, meshCardPass, block);
                }

                Graphics.ExecuteCommandBuffer(commandBuffer);

                commandBuffer.Release();
            }

            mMeshCardData.EndWrite<uint4>(6);

            mNextDataIndex += 6;
        }

        public void Release()
        {
            MeshCardAtlasTextureBaseColor.Release();
            MeshCardAtlasTextureEmissive.Release();
            MeshCardAtlasTextureNormal.Release();
            MeshCardAtlasTexturePosition.Release();
            MeshCardAtlasTextureIrradiance.Release();

            mMeshCardIndirection.Release();
            mMeshCardData.Release();
        }

        public void Bind(CommandBuffer commandBuffer, ComputeShader shader, int kernelIndex)
        {
            commandBuffer.SetComputeBufferParam(shader, kernelIndex, Constants.MeshCardIndirection, mMeshCardIndirection);
            commandBuffer.SetComputeBufferParam(shader, kernelIndex, Constants.MeshCardData, mMeshCardData);

            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.MeshCardAtlasBaseColor, MeshCardAtlasTextureBaseColor);
            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.MeshCardAtlasEmissive, MeshCardAtlasTextureEmissive);
            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.MeshCardAtlasNormal, MeshCardAtlasTextureNormal);
            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.MeshCardAtlasPosition, MeshCardAtlasTexturePosition);
            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.MeshCardAtlasIrradiance, MeshCardAtlasTextureIrradiance);
        }
    }
}
