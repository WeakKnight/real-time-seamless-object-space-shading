using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Burst;
using Unity.Mathematics;

namespace GPUScene
{
    [ExecuteInEditMode]
    public class GPUSceneManager : MonoBehaviour
    {
        private const int BrickNumPerAxis = 32;
        private const int InstanceDataByteSize = 124;

        public List<DistanceField> distanceFields = new();
        public Texture3D brickTexture;
        [HideInInspector, SerializeField]
        public int nextBrickPosition = 0;
        private Dictionary<Mesh, DistanceField> distanceFieldDictionary = new();

        private ComputeBuffer assetDataListBuffer;
        private int assetDataListCapacity = 1024;

        private ComputeBuffer instanceDataListBuffer;
        private int instanceDataListCapacity = 1024;
        private int instanceCount = 0;

#if UNITY_2023_2_OR_NEWER
        private RayTracingAccelerationStructure meshAS;
#endif
        private DistanceFiledInstanceAS distanceFiledInstanceAS;

        private bool ASDirty = true;

        private NativeArray<byte> brickTextureData;
        Dictionary<Mesh, (float resolutionScale, bool doubleSided)> mesheProps = new();

        public MeshCardPool MeshCardPool;

        public LightingData lightingData = new();

        public int GetInstanceCount()
        {
            return instanceCount;
        }

        public DistanceField GetDistanceField(Mesh mesh)
        {
            return distanceFieldDictionary[mesh];
        }

        public Bounds GetInstanceBounds(int instanceIndex)
        {
            uint[] rawData = new uint[6];
            instanceDataListBuffer.GetData(rawData, 0, instanceIndex * InstanceDataByteSize / 4 + 25, 6);

            float3 center = new float3(
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[0])),
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[1])),
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[2]))
                );

            float3 size = new float3(
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[3])),
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[4])),
                BitConverter.ToSingle(BitConverter.GetBytes(rawData[5]))
                );

            return new Bounds(center, size);
        }

        public void WriteInstanceData(ref NativeArray<uint> dstArr, int offset, Transform transform, Mesh mesh, float resolutionScale)
        {
            float3 posOffset = mesh.DistanceFieldPositionOffset(resolutionScale);

            Matrix4x4 localToWorld = Matrix4x4.Translate(posOffset) * transform.localToWorldMatrix;
            Matrix4x4 worldToLocal = Matrix4x4.Translate(-posOffset) * transform.worldToLocalMatrix;
            Vector4 localToWorldCol0 = localToWorld.GetRow(0);
            Vector4 localToWorldCol1 = localToWorld.GetRow(1);
            Vector4 localToWorldCol2 = localToWorld.GetRow(2);
            Vector4 worldToLocalCol0 = worldToLocal.GetRow(0);
            Vector4 worldToLocalCol1 = worldToLocal.GetRow(1);
            Vector4 worldToLocalCol2 = worldToLocal.GetRow(2);

            float3 localBounds = mesh.DistanceFieldGridSize(resolutionScale);

            float x = localBounds.x * 0.5f;
            float y = localBounds.y * 0.5f;
            float z = localBounds.z * 0.5f;

            float3 worldSpaceMin = new float3(float.PositiveInfinity);
            float3 worldSpaceMax = new float3(float.NegativeInfinity);

            float3 P = localToWorld.MultiplyPoint3x4(posOffset + new float3(x, y, z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(-x, y, z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(x, -y, z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(x, y, -z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(x, -y, -z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(-x, y, -z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(-x, -y, z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            P = localToWorld.MultiplyPoint3x4(posOffset + new float3(-x, -y, -z));
            worldSpaceMin = math.min(worldSpaceMin, P);
            worldSpaceMax = math.max(worldSpaceMax, P);

            float3 worldSpaceCenter = (worldSpaceMax + worldSpaceMin) * 0.5f;
            float3 worldSpaceSize = (worldSpaceMax - worldSpaceMin);

            dstArr[offset + 1] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol0.x));
            dstArr[offset + 2] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol0.y));
            dstArr[offset + 3] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol0.z));
            dstArr[offset + 4] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol0.w));

            dstArr[offset + 5] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol1.x));
            dstArr[offset + 6] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol1.y));
            dstArr[offset + 7] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol1.z));
            dstArr[offset + 8] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol1.w));

            dstArr[offset + 9] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol2.x));
            dstArr[offset + 10] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol2.y));
            dstArr[offset + 11] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol2.z));
            dstArr[offset + 12] = BitConverter.ToUInt32(BitConverter.GetBytes(localToWorldCol2.w));

            dstArr[offset + 13] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol0.x));
            dstArr[offset + 14] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol0.y));
            dstArr[offset + 15] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol0.z));
            dstArr[offset + 16] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol0.w));

            dstArr[offset + 17] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol1.x));
            dstArr[offset + 18] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol1.y));
            dstArr[offset + 19] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol1.z));
            dstArr[offset + 20] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol1.w));

            dstArr[offset + 21] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol2.x));
            dstArr[offset + 22] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol2.y));
            dstArr[offset + 23] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol2.z));
            dstArr[offset + 24] = BitConverter.ToUInt32(BitConverter.GetBytes(worldToLocalCol2.w));

            dstArr[offset + 25] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceCenter.x));
            dstArr[offset + 26] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceCenter.y));
            dstArr[offset + 27] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceCenter.z));

            dstArr[offset + 28] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceSize.x));
            dstArr[offset + 29] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceSize.y));
            dstArr[offset + 30] = BitConverter.ToUInt32(BitConverter.GetBytes(worldSpaceSize.z));
        }

        public void UpdateInstanceTransform(int instanceIndex, Transform transform, Mesh mesh, float resolutionScale)
        {
            if (instanceIndex < 0 || instanceIndex >= instanceCount || instanceDataListBuffer == null || mesh == null)
            {
                return;
            }

            int instanceDataStride = InstanceDataByteSize / 4;

            NativeArray<uint> instanceDataList = instanceDataListBuffer.BeginWrite<uint>(instanceIndex * instanceDataStride, instanceDataStride);

            WriteInstanceData(ref instanceDataList, 0, transform, mesh, resolutionScale);

            instanceDataListBuffer.EndWrite<uint>(instanceDataStride);

            ASDirty = true;
        }

        private const uint mMeshCardRadiosityUpdateNum = 2;
        private uint mMeshCardRadiosityTimer = 0;

        public void Prepare(CommandBuffer commandBuffer)
        {
            bool boostMeshCard = false;

            if (ASDirty)
            {
                ASDirty = false;

                boostMeshCard = true;

                if (Config.UseMeshRayTracing == 1)
                {
#if UNITY_2023_2_OR_NEWER
                    commandBuffer.BuildRayTracingAccelerationStructure(meshAS);
#endif
                }
                else
                {
                    distanceFiledInstanceAS?.Build(this);
                }
            }

            lightingData.Update();

            GPUSceneInstance[] instances = FindObjectsByType<GPUSceneInstance>(FindObjectsSortMode.None);

            void RefreshMeshCards()
            {
                MeshCardPool.Clear();

                foreach (var instance in instances)
                {
                    MeshCardPool.CreateMeshCard(instance);
                }
            }

            if (MeshCardPool != null && MeshCardPool.dirty)
            {
                MeshCardPool.dirty = false;

                RefreshMeshCards();
            }

            if (instances.Length > 0 && MeshCardPool != null)
            {
                int iterNum = (mMeshCardRadiosityTimer < 64 || boostMeshCard)? 16: 1;

                for (int iterIndex = 0; iterIndex < iterNum; iterIndex++)
                {
                    MeshCardPool.PrepareMeshCardIrradiance(this, commandBuffer, (int)mMeshCardRadiosityTimer);

                    for (int i = 0; i < mMeshCardRadiosityUpdateNum; i++)
                    {
                        MeshCardPool.UpdateMeshCardIrradiance(commandBuffer, instances[(mMeshCardRadiosityTimer * mMeshCardRadiosityUpdateNum + i) % instances.Length]);
                    }

                    mMeshCardRadiosityTimer++;
                }
            }
        }

        public void BindUniforms(CommandBuffer commandBuffer, ComputeShader shader)
        {
            commandBuffer.SetComputeIntParam(shader, "DF_InstanceCount", instanceCount);

            lightingData.BindUniforms(commandBuffer, shader);
        }

        public void BindBuffers(CommandBuffer commandBuffer, ComputeShader shader, int kernelIndex)
        {
            if (brickTexture != null)
            {
                commandBuffer.SetComputeTextureParam(shader, kernelIndex, "DF_BrickTexture", brickTexture);
            }

            if (assetDataListBuffer != null)
            {
                commandBuffer.SetComputeBufferParam(shader, kernelIndex, "DF_AssetDataList", assetDataListBuffer);
            }

            if (instanceDataListBuffer != null)
            {
                commandBuffer.SetComputeBufferParam(shader, kernelIndex, "DF_InstanceDataList", instanceDataListBuffer);
            }

#if UNITY_2023_2_OR_NEWER
            if (meshAS != null)
            {
                commandBuffer.SetRayTracingAccelerationStructure(shader, kernelIndex, "GPUScene_MeshAS", meshAS);
            }
#endif

            distanceFiledInstanceAS?.Bind(commandBuffer, shader, kernelIndex);
            MeshCardPool?.Bind(commandBuffer, shader, kernelIndex);
        }

        private void OnDisable()
        {
            Release();
        }

        void Release()
        {
            distanceFieldDictionary.Clear();

            assetDataListBuffer?.Release();
            assetDataListBuffer = null;

            instanceDataListBuffer?.Release();
            instanceDataListBuffer = null;

            distanceFiledInstanceAS?.Release();
            distanceFiledInstanceAS = null;

            MeshCardPool?.Release();
            MeshCardPool = null;
        }

#if UNITY_EDITOR
        [BurstCompile]
        public struct FillingJob : IJob
        {
            public int BaseBrickIndex;
            public int3 ResolutionInBricks;

            [ReadOnly]
            public NativeArray<byte> AssetData;

            [WriteOnly]
            public NativeArray<byte> BrickTextureData;

            int Convert3DIndexTo1D(int3 index3D, int3 dimensions)
            {
                return index3D.z * dimensions.x * dimensions.y + index3D.y * dimensions.x + index3D.x;
            }

            int3 Convert1DIndexTo3D(int index1D, int3 dimensions)
            {
                int x = index1D % dimensions.x;
                int y = (index1D / dimensions.x) % dimensions.y;
                int z = index1D / (dimensions.x * dimensions.y);
                return new int3(x, y, z);
            }

            public void Execute()
            {
                for (int i = 0; i < AssetData.Length; i++)
                {
                    int3 assetCoord = Convert1DIndexTo3D(i, ResolutionInBricks * DistanceField.BrickSize);
                    int3 localCoord = assetCoord % DistanceField.BrickSize;
                    byte val = AssetData[i];
                    int brickIndex = BaseBrickIndex + Convert3DIndexTo1D(assetCoord / DistanceField.BrickSize, ResolutionInBricks);
                    int3 brickPos = Convert1DIndexTo3D(brickIndex, new int3(BrickNumPerAxis, BrickNumPerAxis, BrickNumPerAxis));
                    int3 coord = brickPos * DistanceField.BrickSize + localCoord;
                    BrickTextureData[Convert3DIndexTo1D(coord, DistanceField.BrickSize * new int3(BrickNumPerAxis, BrickNumPerAxis, BrickNumPerAxis))] = val;
                }
            }
        }

        public bool showGizmos = false;
        public void OnDrawGizmos()
        {
            if (!showGizmos)
            {
                return;
            }

            distanceFiledInstanceAS?.OnGizmos();
        }

        public void AddMesh(Mesh mesh, float resolutionScale, bool doubleSided)
        {
            if (mesheProps.ContainsKey(mesh))
            {
                return;
            }
            else
            {
                mesheProps[mesh] = (resolutionScale, doubleSided);
            }

            Vector3 GridSize = mesh.DistanceFieldGridSize(resolutionScale);
            Vector3 Center = mesh.bounds.center;
            Vector3 BoundingBoxMin = Center - GridSize * 0.5f;

            DistanceFieldConfig config = new DistanceFieldConfig();
            config.mesh = mesh;
            config.cellSize = mesh.DistanceFieldVoxelSize(resolutionScale);
            config.boundingBoxMin = BoundingBoxMin;
            config.resolution = mesh.DistanceFieldResolution(resolutionScale);
            config.doubleSided = doubleSided;
            config.sampleNum = 256;
            config.gridSize = GridSize;

            DistanceFieldUtils.DistanceFieldIntermediateResult intermediateResult = DistanceFieldUtils.GenerateDistanceField(config);
            // scale: maxVal - minVal, bias: minVal
            Vector2 ScaleBias = new Vector2(intermediateResult.maxDistance - intermediateResult.minDistance, intermediateResult.minDistance);

            DistanceField df = ScriptableObject.CreateInstance<DistanceField>();
            df.mesh = mesh;
            df.assetIndex = distanceFields.Count;
            df.brickOffset = nextBrickPosition;
            df.resolutionInBricks = mesh.DistanceFieldResolutionInBricks(resolutionScale);
            df.scaleBias = ScaleBias;
            df.volumeExtent = GridSize;
            distanceFields.Add(df);

            FillingJob fillingJob = new FillingJob();
            fillingJob.BrickTextureData = brickTextureData;
            fillingJob.BaseBrickIndex = df.brickOffset;
            fillingJob.ResolutionInBricks = new int3(df.resolutionInBricks.x, df.resolutionInBricks.y, df.resolutionInBricks.z);
            fillingJob.AssetData = intermediateResult.brickData;
            fillingJob.Schedule().Complete();

            intermediateResult.brickData.Dispose();

            int brickNum = df.resolutionInBricks.x * df.resolutionInBricks.y * df.resolutionInBricks.z;
            nextBrickPosition = nextBrickPosition + brickNum;

            distanceFieldDictionary[df.mesh] = df;

            NativeArray<uint> assetDataList = assetDataListBuffer.BeginWrite<uint>(distanceFields.Count * DistanceField.AssetDataByteSize / 4, DistanceField.AssetDataByteSize / 4);
            df.WriteToUIntArray(ref assetDataList, 0);
            assetDataListBuffer.EndWrite<uint>(DistanceField.AssetDataByteSize / 4);
        }

        public void Build()
        {
            distanceFieldDictionary.Clear();

            distanceFields.Clear();

            mesheProps.Clear();

            nextBrickPosition = 0;

            // Create Brick Texture
            int brickPixelCountPerAxis = BrickNumPerAxis * DistanceField.BrickSize;
            brickTextureData = new NativeArray<byte>(brickPixelCountPerAxis * brickPixelCountPerAxis * brickPixelCountPerAxis, Allocator.TempJob);
            {
                brickTexture = new Texture3D(brickPixelCountPerAxis, brickPixelCountPerAxis, brickPixelCountPerAxis, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                brickTexture.wrapMode = TextureWrapMode.Clamp;
                brickTexture.filterMode = FilterMode.Bilinear;
            }

            // Collect Mesh And Generate Distance Field
            {
                GPUSceneInstance[] binders = FindObjectsByType<GPUSceneInstance>(FindObjectsSortMode.None);
                foreach (GPUSceneInstance binder in binders)
                {
                    var mf = binder.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                    {
                        continue;
                    }

                    AddMesh(mf.sharedMesh, binder.resolutionScale, binder.doubleSided);
                }

                MeshFilter[] meshFilters = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null)
                    {
                        continue;
                    }

                    var instance = mf.GetComponent<GPUSceneInstance>();
                    if (instance == null)
                    {
                        instance = mf.gameObject.AddComponent<GPUSceneInstance>();
                    }

                    AddMesh(mf.sharedMesh, instance.resolutionScale, instance.doubleSided);
                }
            }

            brickTexture.SetPixelData(brickTextureData, 0);
            brickTexture.Apply();

            brickTextureData.Dispose();

            OnEnable();
        }
#endif

        public void OnEnable()
        {
            if (assetDataListBuffer == null)
            {
                assetDataListBuffer = new ComputeBuffer(assetDataListCapacity * DistanceField.AssetDataByteSize / 4, 4, ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            }

            NativeArray<uint> assetDataList = assetDataListBuffer.BeginWrite<uint>(0, assetDataListBuffer.count);
            for (int dfIndex = 0; dfIndex < distanceFields.Count; dfIndex++)
            {
                DistanceField df = distanceFields[dfIndex];
                df.WriteToUIntArray(ref assetDataList, dfIndex);

                distanceFieldDictionary[df.mesh] = df;
            }
            assetDataListBuffer.EndWrite<uint>(assetDataListBuffer.count);

            MeshFilter[] mesheFilters = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            instanceDataListCapacity = ((instanceDataListCapacity * 0.5f) > mesheFilters.Length) ? instanceDataListCapacity : (mesheFilters.Length * 2);
            instanceDataListBuffer = new ComputeBuffer(instanceDataListCapacity * InstanceDataByteSize / 4, 4, ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            NativeArray<uint> instanceDataList = instanceDataListBuffer.BeginWrite<uint>(0, mesheFilters.Length * InstanceDataByteSize / 4);
            instanceCount = 0;

            foreach (var mf in mesheFilters)
            {
                if (mf.sharedMesh != null && distanceFieldDictionary.TryGetValue(mf.sharedMesh, out DistanceField df))
                {
                    var binder = mf.GetComponent<GPUSceneInstance>();
                    binder.instanceIndex = instanceCount;

                    int startIndex = instanceCount * (InstanceDataByteSize / 4);

                    instanceDataList[startIndex + 0] = (uint)df.assetIndex;
                    WriteInstanceData(ref instanceDataList, startIndex, mf.transform, mf.sharedMesh, binder.resolutionScale);

                    instanceCount++;
                }
            }
            instanceDataListBuffer.EndWrite<uint>(mesheFilters.Length * InstanceDataByteSize / 4);

            Shader.SetGlobalInt("DF_InstanceCount", instanceCount);
            Shader.SetGlobalTexture("DF_BrickTexture", brickTexture);
            Shader.SetGlobalBuffer("DF_AssetDataList", assetDataListBuffer);
            Shader.SetGlobalBuffer("DF_InstanceDataList", instanceDataListBuffer);

            distanceFiledInstanceAS = new();

            if (Config.UseMeshRayTracing == 1)
            {
#if UNITY_2023_2_OR_NEWER
                meshAS = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings
                {
                    rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.DynamicTransform | RayTracingAccelerationStructure.RayTracingModeMask.Static,
                    managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic
                });

                meshAS.Build();

                GPUSceneInstance[] gpuInstances = FindObjectsByType<GPUSceneInstance>(FindObjectsSortMode.None);
                foreach (GPUSceneInstance instance in gpuInstances)
                {
                    MeshRenderer mr = instance.GetComponent<MeshRenderer>();
                    if (mr == null)
                    {
                        continue;
                    }

                    meshAS.UpdateInstanceID(mr, (uint)instance.instanceIndex);
                }

                meshAS.Build();
#endif
            }

            MeshCardPool = new MeshCardPool();
        }
    }
}
