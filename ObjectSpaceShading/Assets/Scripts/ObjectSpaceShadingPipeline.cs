using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using GPUScene;
using Unity.Mathematics;
using System.Collections.Generic;

public class ObjectSpaceShadingPipeline : RenderPipeline
{
    public ObjectSpaceShadingPipeline()
    {
        MeshUtils.Init();

        _IDMapManager = new();

        fbos = new();
        referenceRenderer = new ReferenceRenderer();
        screenSpaceReSTIR = new ScreenSpaceReSTIR();
        cameraContexts = new Dictionary<Camera, CameraContext>();

        virtualShadelSpaceVisualization = Resources.Load<ComputeShader>("VirtualShadelSpaceVisualization");
        virtualShadelSpaceVisualizationKernel = virtualShadelSpaceVisualization.FindKernel("VirtualShadelSpaceVisualization");
    }

    ComputeShader virtualShadelSpaceVisualization;
    int virtualShadelSpaceVisualizationKernel;

    RenderSetting renderSetting;
    GPUSceneManager gpuSceneMgr;
    GPUSceneVisualization gpuSceneVisualization;
    FBO.Dict fbos;
    ReferenceRenderer referenceRenderer;
    ScreenSpaceReSTIR screenSpaceReSTIR;

    ComputeShader shadelMemoryProcessing;
    int clearAllocationKernel;
    int shadelMemoryAllocationFirstPassKernel;
    int clearStorageBufferKernel;

    ComputeShader renderTaskProcessing;
    int renderTaskPrepareKernel;
    int renderTaskIndirectDispatchKernel;

    ComputeShader gi;
    ComputeShader accumShader = Resources.Load<ComputeShader>("Accumulate");

    const int shadingInterval = 2;

    public Dictionary<Camera, CameraContext> cameraContexts;

    public bool enableAtlasVisualization = false;
    public bool enableShadingIntervalVisualization = false;

    public bool enablePersistentLayer = true;

    IDMapManager _IDMapManager;

    // Persistent Memory For Handle Pool
    static HandlePool instanceHandlePool;

    public static HandlePool InstanceHandlePool
    {
        get
        {
            if (instanceHandlePool == null)
            {
                instanceHandlePool = new HandlePool(4096);
            }

            return instanceHandlePool;
        }
    }

    private void LoadShadelMemoryProcessingKernel()
    {
        if (shadelMemoryProcessing == null)
        {
            shadelMemoryProcessing = Utils.FindComputeShaderByPath("ShadelMemoryProcessing");
            clearAllocationKernel = shadelMemoryProcessing.FindKernel("ClearAllocation");
            shadelMemoryAllocationFirstPassKernel = shadelMemoryProcessing.FindKernel("ShadelMemoryAllocationFirstPass");
            clearStorageBufferKernel = shadelMemoryProcessing.FindKernel("ClearStorageBuffer");
        }
    }

    private void LoadRenderTaskProcessingKernels()
    {
        if (renderTaskProcessing == null)
        {
            renderTaskProcessing = Utils.FindComputeShaderByPath("RenderTaskProcessing");
            renderTaskPrepareKernel = renderTaskProcessing.FindKernel("RenderTaskPrepare");
            renderTaskIndirectDispatchKernel = renderTaskProcessing.FindKernel("RenderTaskIndirectDispatch");
        }
    }

    public static class Props
    {
        public static int EnablePersistentLayer = Shader.PropertyToID("_EnablePersistentLayer");
        public static int VisualizationMode = Shader.PropertyToID("_VisualizationMode");
        public static int RemapBufferLength = Shader.PropertyToID("_RemapBufferLength");
        public static int RemapBuffer = Shader.PropertyToID("_RemapBuffer");
        public static int OccupancyBuffer = Shader.PropertyToID("_OccupancyBuffer");
        public static int HistoryOccupancyBuffer = Shader.PropertyToID("_HistoryOccupancyBuffer");
        public static int PrevInstanceIndex = Shader.PropertyToID("_PrevInstanceIndex");
        public static int DispatchDimension = Shader.PropertyToID("_DispatchDimension");
        public static int RemapBufferWidth = Shader.PropertyToID("_RemapBufferWidth");
        public static int RemapBufferHeight = Shader.PropertyToID("_RemapBufferHeight");
        public static int StorageBufferWidth = Shader.PropertyToID("_StorageBufferWidth");
        public static int StorageBufferHeight = Shader.PropertyToID("_StorageBufferHeight");

        public static int HalfedgeMesh = Shader.PropertyToID("_HalfedgeMesh");

        public static int FrameIndex = Shader.PropertyToID("_FrameIndex");
        public static int ShadingInterval = Shader.PropertyToID("_ShadingInterval");
        public static int PrevShadingInterval = Shader.PropertyToID("_PrevShadingInterval");
        public static int VirtualShadelTextureWidth = Shader.PropertyToID("_VirtualShadelTextureWidth");
        public static int VirtualShadelTextureHeight = Shader.PropertyToID("_VirtualShadelTextureHeight");
        public static int TaskBufferRW = Shader.PropertyToID("_TaskBufferRW");
        public static int ArgsBuffer = Shader.PropertyToID("_ArgsBuffer");
        public static int TaskCounterBufferRW = Shader.PropertyToID("_TaskCounterBufferRW");
        public static int TaskCounterBuffer = Shader.PropertyToID("_TaskCounterBuffer");
        public static int TaskBuffer = Shader.PropertyToID("_TaskBuffer");
        public static int TaskOffsetBufferRW = Shader.PropertyToID("_TaskOffsetBufferRW");
        public static int TaskOffsetBuffer = Shader.PropertyToID("_TaskOffsetBuffer");
        public static int HistoryRemapBuffer = Shader.PropertyToID("_HistoryRemapBuffer");

        public static int ObjectTransformation = Shader.PropertyToID("_ObjectTransformation");
        public static int ObjectInverseTransformation = Shader.PropertyToID("_ObjectInverseTransformation");
        public static int SecondPassArgsBuffer = Shader.PropertyToID("_SecondPassArgsBuffer");

        public static int StorageBuffer = Shader.PropertyToID("_StorageBuffer");
        public static int HistoryStorageBuffer = Shader.PropertyToID("_HistoryStorageBuffer");

        public static int PayloadBuffer = Shader.PropertyToID("_PayloadBuffer");
        public static int HistoryPayloadBuffer = Shader.PropertyToID("_HistoryPayloadBuffer");

        public static int PersistentPayloadBuffer = Shader.PropertyToID("_PersistentPayloadBuffer");
        public static int PersistentHistoryPayloadBuffer = Shader.PropertyToID("_PersistentHistoryPayloadBuffer");

        public static int AccumulatedFrameCountBuffer = Shader.PropertyToID("_AccumulatedFrameCountBuffer");
        public static int HistoryAccumulatedFrameCountBuffer = Shader.PropertyToID("_HistoryAccumulatedFrameCountBuffer");

        public static int StorageLayer = Shader.PropertyToID("_StorageLayer");

        public static int InstanceProps = Shader.PropertyToID("_InstanceProps");

        public static int IDMap = Shader.PropertyToID("_IDMap");
        public static int IDMapSize = Shader.PropertyToID("_IDMapSize");

        public static int PrevNonJitteredViewProjectionMatrix = Shader.PropertyToID("_PrevNonJitteredViewProjectionMatrix");

        public static int LightingOnly = Shader.PropertyToID("_LightingOnly");

        public static int ExposureValue = Shader.PropertyToID("_ExposureValue");
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        if (renderSetting == null)
        {
            renderSetting = Object.FindAnyObjectByType<RenderSetting>();
            if (renderSetting == null)
            {
                GameObject go = new GameObject("__RenderSetting");
                renderSetting = go.AddComponent<RenderSetting>();
            }
        }

        if (gpuSceneMgr == null)
        {
            gpuSceneMgr = Object.FindAnyObjectByType<GPUSceneManager>();
            if (gpuSceneMgr == null)
            {
                GameObject go = new GameObject("__GPUSceneManager");
                gpuSceneMgr = go.AddComponent<GPUSceneManager>();
            }

            gpuSceneVisualization = new GPUSceneVisualization(gpuSceneMgr);
        }

        // Fill Lighting Data
        {
            gpuSceneMgr.lightingData.MainLightColor = float4.zero;

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var light in lights)
            {
                if (!light.isActiveAndEnabled)
                {
                    continue;
                }

                if (light.type == LightType.Directional)
                {
                    Vector3 lightDir = -light.transform.forward;
                    Vector3 lightCol = light.intensity * new Vector3(light.color.r, light.color.g, light.color.b);
                    gpuSceneMgr.lightingData.MainLightColor = new float4(lightCol, 0.0f);
                    gpuSceneMgr.lightingData.MainLightDirection = new float4(lightDir, 0.0f);

                    break;
                }
            }
        }

        CommandBuffer prepareCommandBuffer = new CommandBuffer();
        prepareCommandBuffer.name = "Prepare";
        prepareCommandBuffer.SetGlobalInt("_FrameIndex", Time.frameCount);
        if (enableShadingIntervalVisualization)
        {
            prepareCommandBuffer.SetGlobalInt(Props.VisualizationMode, 2);
        }
        else
        {
            prepareCommandBuffer.SetGlobalInt(Props.VisualizationMode, enableAtlasVisualization ? 1 : 0);
        }
        prepareCommandBuffer.SetGlobalInt(Props.EnablePersistentLayer, enablePersistentLayer ? 1 : 0);

        gpuSceneMgr.Prepare(prepareCommandBuffer);

        context.ExecuteCommandBuffer(prepareCommandBuffer);

        foreach (var camera in cameras)
        {
            if (camera != null)
            {
                RenderCamera(context, camera);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            foreach (var cameraContext in cameraContexts)
            {
                cameraContext.Value?.Dispose();
            }
            cameraContexts.Clear();

            screenSpaceReSTIR.Dispose();
            screenSpaceReSTIR = null;

            _IDMapManager?.Dispose();
        }
    }

    void RenderCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType == CameraType.Preview)
        {
            return;
        }

        CameraContext cameraContext;
        if (!cameraContexts.TryGetValue(camera, out cameraContext))
        {
            cameraContext = new CameraContext(camera);
            cameraContexts.Add(camera, cameraContext);
        }

        HalfedgeMeshRendererData[] halfedgeMeshRendererDatas = Object.FindObjectsByType<HalfedgeMeshRendererData>(FindObjectsSortMode.None);
        foreach (var halfEdgeRenderData in halfedgeMeshRendererDatas)
        {
            cameraContext.atlasManager.AddInstance(halfEdgeRenderData);
        }

        UVMeshRendererData[] UVMeshRendererDatas = Object.FindObjectsByType<UVMeshRendererData>(FindObjectsSortMode.None);
        foreach (var rendererData in UVMeshRendererDatas)
        {
            cameraContext.atlasManager.AddInstance(rendererData);
        }

        FBO fbo = fbos.GetFBO(camera);

        context.SetupCameraProperties(camera);

        if (!camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            return;
        }

        var commandBuffer = new CommandBuffer { name = camera.name };

        fbo.SetRenderTargetToOutput(commandBuffer);

        CameraClearFlags clearFlags = camera.clearFlags;
        commandBuffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );

        CullingResults cullingResults = context.Cull(ref p);

        commandBuffer.SetGlobalMatrix("_WorldToCameraMat", camera.worldToCameraMatrix);
        commandBuffer.SetGlobalMatrix(Props.PrevNonJitteredViewProjectionMatrix, cameraContext.PrevNonJitteredViewProjectionMatrix());
        commandBuffer.SetGlobalInt(Props.LightingOnly, renderSetting.LightingOnly ? 1 : 0);
        commandBuffer.SetGlobalFloat(Props.ExposureValue, Mathf.Pow(2, renderSetting.Exposure));

        if (renderSetting.RenderMode == RenderMode.Reference ||
            renderSetting.RenderMode == RenderMode.ScreenSpaceReSTIR)
        {
            {
                commandBuffer.BeginSample("Pre Z");

                cameraContext.virtualRenderTexture.Bind(commandBuffer, true);

                RendererListDesc desc = new RendererListDesc(new ShaderTagId("PreZ"), cullingResults, camera)
                {
                    rendererConfiguration = PerObjectData.None,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                };

                RendererList rendererList = context.CreateRendererList(desc);

                commandBuffer.DrawRendererList(rendererList);

                commandBuffer.EndSample("Pre Z");
            }

            {
                commandBuffer.BeginSample("G Buffer");
                fbo.SetRenderTargetToGBuffer(commandBuffer);

                RendererListDesc desc = new RendererListDesc(new ShaderTagId("GBuffer"), cullingResults, camera)
                {
                    rendererConfiguration = PerObjectData.MotionVectors,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                };

                RendererList rendererList = context.CreateRendererList(desc);

                commandBuffer.DrawRendererList(rendererList);

                commandBuffer.EndSample("G Buffer");
            }

            if (renderSetting.RenderMode == RenderMode.Reference)
            {
                referenceRenderer.Render(commandBuffer, fbo, gpuSceneMgr, camera);
            }
            else
            {
                screenSpaceReSTIR.Render(commandBuffer, fbo, gpuSceneMgr, camera);
            }
        }
        else
        {
            {
                commandBuffer.BeginSample("ClearOccupancy");
                Utils.ClearRawBufferData(commandBuffer, cameraContext.virtualRenderTexture.OccupancyBuffer);
                commandBuffer.EndSample("ClearOccupancy");
            }

            {
                commandBuffer.BeginSample("Pre Z");

                cameraContext.virtualRenderTexture.Bind(commandBuffer, true);

                RendererListDesc desc = new RendererListDesc(new ShaderTagId("PreZ"), cullingResults, camera)
                {
                    rendererConfiguration = PerObjectData.None,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                };

                RendererList rendererList = context.CreateRendererList(desc);

                commandBuffer.DrawRendererList(rendererList);

                commandBuffer.EndSample("Pre Z");
            }

            {
                commandBuffer.BeginSample("Shadel Memory Allocation");

                LoadShadelMemoryProcessingKernel();

                {
                    commandBuffer.BeginSample("ClearAllocation");

                    cameraContext.shadelAllocator.Bind(commandBuffer, shadelMemoryProcessing);

                    cameraContext.shadelAllocator.BindKernel(commandBuffer, shadelMemoryProcessing, clearAllocationKernel);
                    commandBuffer.DispatchCompute(shadelMemoryProcessing, clearAllocationKernel, 1, 1, 1);

                    commandBuffer.EndSample("ClearAllocation");
                }

                {
                    commandBuffer.BeginSample("ShadelMemoryAllocationFirstPass");
                    cameraContext.shadelAllocator.BindKernel(commandBuffer, shadelMemoryProcessing, shadelMemoryAllocationFirstPassKernel);
                    commandBuffer.SetComputeIntParam(shadelMemoryProcessing, Props.RemapBufferWidth, VirtualRenderTexture.RemapBufferWidth);
                    commandBuffer.SetComputeIntParam(shadelMemoryProcessing, Props.RemapBufferHeight, VirtualRenderTexture.RemapBufferHeight);
                    commandBuffer.SetComputeIntParam(shadelMemoryProcessing, Props.StorageBufferWidth, VirtualRenderTexture.StorageBufferWidth);
                    commandBuffer.SetComputeIntParam(shadelMemoryProcessing, Props.StorageBufferHeight, VirtualRenderTexture.StorageBufferHeight);
                    commandBuffer.SetComputeBufferParam(shadelMemoryProcessing, shadelMemoryAllocationFirstPassKernel, Props.RemapBuffer, cameraContext.virtualRenderTexture.RemapBuffer);
                    commandBuffer.SetComputeBufferParam(shadelMemoryProcessing, shadelMemoryAllocationFirstPassKernel, Props.OccupancyBuffer, cameraContext.virtualRenderTexture.OccupancyBuffer);
                    commandBuffer.DispatchCompute(shadelMemoryProcessing, shadelMemoryAllocationFirstPassKernel, (VirtualRenderTexture.RemapBufferWidth + 7) / 8, (VirtualRenderTexture.RemapBufferHeight + 7) / 8, 1);

                    commandBuffer.EndSample("ShadelMemoryAllocationFirstPass");
                }

                // We do not need clear storage buffer except for first frame and dubugging purpose
                if (cameraContext.renderFrameIndex == 0 || cameraContext.debug)
                {
                    commandBuffer.BeginSample("ClearStorageBuffer");

                    commandBuffer.SetComputeTextureParam(shadelMemoryProcessing, clearStorageBufferKernel, Props.StorageBuffer, cameraContext.virtualRenderTexture.StorageBuffer);
                    commandBuffer.DispatchCompute(shadelMemoryProcessing, clearStorageBufferKernel, (VirtualRenderTexture.StorageBufferWidth * 2 + 7) / 8, (VirtualRenderTexture.StorageBufferHeight + 7) / 8, 1);

                    commandBuffer.EndSample("ClearStorageBuffer");
                }

                // Read Counter Asynchronously
#if UNITY_EDITOR
                if (cameraContext.debug)
                {
                    commandBuffer.BeginSample("ReadAllocatorCounter");
                    cameraContext.shadelAllocator.ReadCounter(cameraContext.renderFrameIndex);
                    commandBuffer.EndSample("ReadAllocatorCounter");
                }
#endif

                commandBuffer.EndSample("Shadel Memory Allocation");
            }

            // Render Task Processing
            {
                LoadRenderTaskProcessingKernels();

                {
                    commandBuffer.BeginSample("RenderTaskClear");
                    Utils.ClearRawBufferData(commandBuffer, cameraContext.renderTaskAllocator.GetCounterBuffer());
                    Utils.ClearRawBufferData(commandBuffer, cameraContext.renderTaskAllocator.GetOffsetBuffer());
                    commandBuffer.EndSample("RenderTaskClear");
                }

                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.RemapBufferWidth, VirtualRenderTexture.RemapBufferWidth);
                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.RemapBufferHeight, VirtualRenderTexture.RemapBufferHeight);
                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.StorageBufferWidth, VirtualRenderTexture.StorageBufferWidth);
                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.StorageBufferHeight, VirtualRenderTexture.StorageBufferHeight);
                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.VirtualShadelTextureWidth, VirtualRenderTexture.Width);
                commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.VirtualShadelTextureHeight, VirtualRenderTexture.Height);

                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskPrepareKernel, Props.RemapBuffer, cameraContext.virtualRenderTexture.RemapBuffer);
                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskPrepareKernel, Props.OccupancyBuffer, cameraContext.virtualRenderTexture.OccupancyBuffer);
                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskPrepareKernel, Props.TaskCounterBufferRW, cameraContext.renderTaskAllocator.GetCounterBuffer());
                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskPrepareKernel, Props.TaskBufferRW, cameraContext.renderTaskAllocator.GetTaskBuffer());

                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskIndirectDispatchKernel, Props.TaskOffsetBufferRW, cameraContext.renderTaskAllocator.GetOffsetBuffer());
                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskIndirectDispatchKernel, Props.ArgsBuffer, cameraContext.renderTaskAllocator.GetArgsBuffer());
                commandBuffer.SetComputeBufferParam(renderTaskProcessing, renderTaskIndirectDispatchKernel, Props.TaskCounterBuffer, cameraContext.renderTaskAllocator.GetCounterBuffer());

                {
                    if (gi == null)
                    {
                        gi = Utils.FindComputeShaderByPath("GI");
                    }
                    gpuSceneMgr.BindUniforms(commandBuffer, gi);
                    gpuSceneMgr.BindBuffers(commandBuffer, gi, 0);
                    commandBuffer.SetComputeIntParam(gi, Props.FrameIndex, cameraContext.renderFrameIndex);

                    commandBuffer.SetComputeIntParam(gi, Props.RemapBufferWidth, VirtualRenderTexture.RemapBufferWidth);
                    commandBuffer.SetComputeIntParam(gi, Props.RemapBufferHeight, VirtualRenderTexture.RemapBufferHeight);
                    commandBuffer.SetComputeIntParam(gi, Props.StorageBufferWidth, VirtualRenderTexture.StorageBufferWidth);
                    commandBuffer.SetComputeIntParam(gi, Props.StorageBufferHeight, VirtualRenderTexture.StorageBufferHeight);
                    commandBuffer.SetComputeIntParam(gi, Props.VirtualShadelTextureWidth, VirtualRenderTexture.Width);
                    commandBuffer.SetComputeIntParam(gi, Props.VirtualShadelTextureHeight, VirtualRenderTexture.Height);

                    commandBuffer.SetComputeBufferParam(gi, 0, Props.TaskOffsetBuffer, cameraContext.renderTaskAllocator.GetOffsetBuffer());
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.TaskBuffer, cameraContext.renderTaskAllocator.GetTaskBuffer());
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.RemapBuffer, cameraContext.virtualRenderTexture.RemapBuffer);
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.HistoryRemapBuffer, cameraContext.virtualRenderTexture.HistoryRemapBuffer);
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.OccupancyBuffer, cameraContext.virtualRenderTexture.OccupancyBuffer);
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.HistoryOccupancyBuffer, cameraContext.virtualRenderTexture.HistoryOccupancyBuffer);

                    commandBuffer.SetComputeTextureParam(gi, 0, Props.StorageBuffer, cameraContext.virtualRenderTexture.StorageBuffer);
                    commandBuffer.SetComputeTextureParam(gi, 0, Props.HistoryStorageBuffer, cameraContext.virtualRenderTexture.HistoryStorageBuffer);

                    commandBuffer.SetComputeBufferParam(gi, 0, Props.PayloadBuffer, cameraContext.virtualRenderTexture.PayloadBuffer);
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.HistoryPayloadBuffer, cameraContext.virtualRenderTexture.HistoryPayloadBuffer);

                    commandBuffer.SetComputeBufferParam(gi, 0, Props.PersistentPayloadBuffer, cameraContext.virtualRenderTexture.PersistentPayloadBuffer);
                    commandBuffer.SetComputeBufferParam(gi, 0, Props.PersistentHistoryPayloadBuffer, cameraContext.virtualRenderTexture.HistoryPersistentPayloadBuffer);

                    commandBuffer.SetComputeTextureParam(gi, 0, Props.AccumulatedFrameCountBuffer, cameraContext.virtualRenderTexture.AccumulatedFrameCountBuffer);
                    commandBuffer.SetComputeTextureParam(gi, 0, Props.HistoryAccumulatedFrameCountBuffer, cameraContext.virtualRenderTexture.HistoryAccumulatedFrameCountBuffer);

                    cameraContext.shadelAllocator.Bind(commandBuffer, gi);
                    cameraContext.shadelAllocator.BindKernelReadOnly(commandBuffer, gi, 0);

                    ScreenSpaceRayTracing.Bind(commandBuffer, gi, camera);
                }

                int prevInstanceIndex = -1;

                // Dispatch Htex Renderers
                {
                    commandBuffer.SetComputeIntParam(gi, Props.HalfedgeMesh, 1);
                    commandBuffer.SetComputeTextureParam(gi, 0, Props.IDMap, Texture2D.whiteTexture);

                    foreach (var pair in HalfedgeMeshRendererData.GetDictionary())
                    {
                        int instanceIndex = pair.Key;
                        HalfedgeMeshRendererData rendererData = pair.Value;

                        if (!cameraContext.atlasManager.Contain(rendererData))
                        {
                            continue;
                        }

                        int localShadingInterval = rendererData.shadingInterval == 0 ? shadingInterval : rendererData.shadingInterval;
                        commandBuffer.SetComputeIntParam(gi, Props.ShadingInterval, localShadingInterval);
                        commandBuffer.SetComputeIntParam(gi, Props.PrevShadingInterval, rendererData.prevShadingInterval);
                        rendererData.prevShadingInterval = localShadingInterval;

                        uint4 objectChartSizePosition = cameraContext.atlasManager.GetSizePosition(rendererData);
                        uint mipCount = (uint)Mathf.Log((objectChartSizePosition.x / 64), 2.0f);

                        int[] instanceProps = new int[4] { rendererData.instanceHandle, (int)objectChartSizePosition.z, (int)objectChartSizePosition.w, (int)objectChartSizePosition.x };

                        commandBuffer.SetComputeIntParams(renderTaskProcessing, Props.InstanceProps, instanceProps);
                        commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.PrevInstanceIndex, prevInstanceIndex);

                        {
                            int dispatchDimension = (int)Utils.ComputeMip1DBaseOffset(mipCount + 1, objectChartSizePosition.x / 64u);
                            commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.DispatchDimension, dispatchDimension);
                            commandBuffer.DispatchCompute(renderTaskProcessing, renderTaskPrepareKernel, (dispatchDimension + 7) / 8, 1, 1);
                        }

                        {
                            commandBuffer.DispatchCompute(renderTaskProcessing, renderTaskIndirectDispatchKernel, 1, 1, 1);
                        }

                        {
                            commandBuffer.SetComputeIntParams(gi, Props.InstanceProps, instanceProps);

                            MeshUtils.SetupMeshInfo(rendererData.HalfedgeMesh, commandBuffer, gi, 0);
                            MeshUtils.SetupVertexAttributeInfo(rendererData.HalfedgeMesh, commandBuffer, gi, 0);

                            rendererData.Bind(commandBuffer, gi, 0);

                            //! TODO: Support Skinned Mesh
                            commandBuffer.SetComputeMatrixParam(gi, Props.ObjectTransformation, rendererData.transform.localToWorldMatrix);
                            commandBuffer.SetComputeMatrixParam(gi, Props.ObjectInverseTransformation, rendererData.transform.worldToLocalMatrix);


                            commandBuffer.DispatchCompute(gi, 0, cameraContext.renderTaskAllocator.GetArgsBuffer(), (uint)instanceIndex * 6 * sizeof(uint) + 0);
                        }

                        prevInstanceIndex = instanceIndex;
                    }
                }

                // Dispatch UV Renderers
                {
                    commandBuffer.SetComputeIntParam(gi, Props.HalfedgeMesh, 0);
                    foreach (var pair in UVMeshRendererData.GetDictionary())
                    {
                        int instanceIndex = pair.Key;
                        UVMeshRendererData rendererData = pair.Value;

                        Mesh mesh = rendererData?.meshFilter?.sharedMesh;
                        if (mesh == null)
                        {
                            continue;
                        }

                        if (!cameraContext.atlasManager.Contain(rendererData))
                        {
                            continue;
                        }

                        int localShadingInterval = rendererData.shadingInterval == 0 ? shadingInterval : rendererData.shadingInterval;
                        commandBuffer.SetComputeIntParam(gi, Props.ShadingInterval, localShadingInterval);
                        commandBuffer.SetComputeIntParam(gi, Props.PrevShadingInterval, rendererData.prevShadingInterval);
                        rendererData.prevShadingInterval = localShadingInterval;

                        Texture idMap = _IDMapManager.GetIDMap(mesh, 1024);
                        if (idMap != null)
                        {
                            commandBuffer.SetComputeTextureParam(gi, 0, Props.IDMap, idMap);
                            commandBuffer.SetComputeIntParam(gi, Props.IDMapSize, (int)rendererData.idMapSize);
                        }

                        uint4 objectChartSizePosition = cameraContext.atlasManager.GetSizePosition(rendererData);
                        uint mipCount = (uint)Mathf.Log((objectChartSizePosition.x / 64), 2.0f);

                        int[] instanceProps = new int[4] { rendererData.instanceHandle, (int)objectChartSizePosition.z, (int)objectChartSizePosition.w, (int)objectChartSizePosition.x };

                        commandBuffer.SetComputeIntParams(renderTaskProcessing, Props.InstanceProps, instanceProps);
                        commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.PrevInstanceIndex, prevInstanceIndex);

                        {
                            int dispatchDimension = (int)Utils.ComputeMip1DBaseOffset(mipCount + 1, objectChartSizePosition.x / 64u);
                            commandBuffer.SetComputeIntParam(renderTaskProcessing, Props.DispatchDimension, dispatchDimension);
                            commandBuffer.DispatchCompute(renderTaskProcessing, renderTaskPrepareKernel, (dispatchDimension + 7) / 8, 1, 1);
                        }

                        {
                            commandBuffer.DispatchCompute(renderTaskProcessing, renderTaskIndirectDispatchKernel, 1, 1, 1);
                        }

                        {
                            commandBuffer.SetComputeIntParams(gi, Props.InstanceProps, instanceProps);

                            MeshUtils.SetupMeshInfo(rendererData.meshFilter.sharedMesh, commandBuffer, gi, 0);
                            MeshUtils.SetupVertexAttributeInfo(rendererData.meshFilter.sharedMesh, commandBuffer, gi, 0);

                            //! TODO: Support Skinned Mesh
                            commandBuffer.SetComputeMatrixParam(gi, Props.ObjectTransformation, rendererData.transform.localToWorldMatrix);
                            commandBuffer.SetComputeMatrixParam(gi, Props.ObjectInverseTransformation, rendererData.transform.worldToLocalMatrix);


                            commandBuffer.DispatchCompute(gi, 0, cameraContext.renderTaskAllocator.GetArgsBuffer(), (uint)instanceIndex * 6 * sizeof(uint) + 0);
                        }

                        prevInstanceIndex = instanceIndex;
                    }
                }
            }

            {
                commandBuffer.BeginSample("G Buffer");
                fbo.SetRenderTargetToGBuffer(commandBuffer);

                RendererListDesc desc = new RendererListDesc(new ShaderTagId("GBuffer"), cullingResults, camera)
                {
                    rendererConfiguration = PerObjectData.MotionVectors,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                };

                RendererList rendererList = context.CreateRendererList(desc);

                commandBuffer.DrawRendererList(rendererList);

                commandBuffer.EndSample("G Buffer");
            }

            fbo.SetRenderTargetToOutput(commandBuffer);

            {
                commandBuffer.BeginSample("Final Gathering");

                cameraContext.virtualRenderTexture.Bind(commandBuffer, false);
                cameraContext.shadelAllocator.Bind(commandBuffer);

                RendererListDesc desc = new RendererListDesc(new ShaderTagId("FinalGathering"), cullingResults, camera)
                {
                    rendererConfiguration = PerObjectData.None,
                    renderQueueRange = RenderQueueRange.opaque,
                    sortingCriteria = SortingCriteria.CommonOpaque,
                };

                RendererList rendererList = context.CreateRendererList(desc);

                commandBuffer.DrawRendererList(rendererList);

                commandBuffer.EndSample("Final Gathering");
            }

            if (camera.clearFlags.HasFlag(CameraClearFlags.Skybox))
            {
                commandBuffer.BeginSample("Sky Rendering");
                RendererList skyRendererList = context.CreateSkyboxRendererList(camera);
                commandBuffer.DrawRendererList(skyRendererList);
                commandBuffer.EndSample("Sky Rendering");
            }
        }

        RenderTexture sceneColorTexture = fbo.output;
        if (renderSetting.EnableAccumulation)
        {
            if (cameraContext.resetAccum || renderSetting.dirty)
            {
                renderSetting.dirty = false;

                cameraContext.resetAccum = false;

                commandBuffer.SetRenderTarget(fbo.sumColor);
                commandBuffer.ClearRenderTarget(false, true, Color.clear);
            }

            commandBuffer.SetComputeTextureParam(accumShader, 0, "_Input", fbo.output);
            commandBuffer.SetComputeTextureParam(accumShader, 0, "_Accum", fbo.sumColor);
            accumShader.GetKernelThreadGroupSizes(0, out uint x, out uint y, out uint z);
            commandBuffer.DispatchCompute(accumShader, 0, Utils.DivideRoundUp(fbo.width, (int)x), Utils.DivideRoundUp(fbo.height, (int)y), 1);

            sceneColorTexture = fbo.sumColor;
        }

#if UNITY_EDITOR
        gpuSceneVisualization.Render(commandBuffer, camera, sceneColorTexture);
#endif

        commandBuffer.SetRenderTarget(fbo.cameraTexture);
        commandBuffer.Blit(sceneColorTexture, fbo.cameraTexture);

#if UNITY_EDITOR
        if (cameraContext.debug)
        {
            if (virtualShadelSpaceVisualization == null)
            {
                virtualShadelSpaceVisualization = Resources.Load<ComputeShader>("VirtualShadelSpaceVisualization");
                virtualShadelSpaceVisualizationKernel = virtualShadelSpaceVisualization.FindKernel("VirtualShadelSpaceVisualization");
            }

            commandBuffer.BeginSample("ShadelSpaceVisualization");
            commandBuffer.SetComputeIntParam(virtualShadelSpaceVisualization, "_RemapBufferWidth", VirtualRenderTexture.RemapBufferWidth);
            commandBuffer.SetComputeIntParam(virtualShadelSpaceVisualization, "_RemapBufferHeight", VirtualRenderTexture.RemapBufferHeight);

            commandBuffer.SetComputeBufferParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_RemapBuffer", cameraContext.virtualRenderTexture.RemapBuffer);
            commandBuffer.SetComputeBufferParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_OccupancyBuffer", cameraContext.virtualRenderTexture.OccupancyBuffer);
            commandBuffer.SetComputeTextureParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_Output", cameraContext.RemapBufferVisTexture);

            commandBuffer.DispatchCompute(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, (VirtualRenderTexture.RemapBufferWidth + 15) / 16, (VirtualRenderTexture.RemapBufferHeight + 15) / 16, 1);

            commandBuffer.SetComputeBufferParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_RemapBuffer", cameraContext.virtualRenderTexture.HistoryRemapBuffer);
            commandBuffer.SetComputeBufferParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_OccupancyBuffer", cameraContext.virtualRenderTexture.HistoryOccupancyBuffer);
            commandBuffer.SetComputeTextureParam(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, "_Output", cameraContext.HistoryRemapBufferVisTexture);

            commandBuffer.DispatchCompute(virtualShadelSpaceVisualization, virtualShadelSpaceVisualizationKernel, (VirtualRenderTexture.RemapBufferWidth + 15) / 16, (VirtualRenderTexture.RemapBufferHeight + 15) / 16, 1);

            commandBuffer.EndSample("ShadelSpaceVisualization");
        }
#endif

        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Release();

#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
#endif

        context.DrawUIOverlay(camera);

        context.Submit();

        cameraContext.NextFrame();
    }
}
