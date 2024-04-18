#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    public class GPUSceneVisualization : IDisposable
    {
        ComputeShader shader;
        GPUSceneManager sceneManager;

        public GPUSceneVisualization(GPUSceneManager sceneManager)
        {
            this.sceneManager = sceneManager;
            shader = Resources.Load<ComputeShader>("SDFVisualization");

            SceneView.ClearUserDefinedCameraModes();
            SceneView.AddCameraMode("BaseColor", "GPU Scene");
            SceneView.AddCameraMode("Emissive", "GPU Scene");
            SceneView.AddCameraMode("Normal", "GPU Scene");
            SceneView.AddCameraMode("Irradiance", "GPU Scene");
            SceneView.AddCameraMode("Position", "GPU Scene");

            EditorApplication.update += OnUpdateEditor;
        }

        public void Render(CommandBuffer commandBuffer, Camera camera, RenderTargetIdentifier main)
        {
            if (camera.cameraType == CameraType.SceneView && cameraMode.section == "GPU Scene")
            {
                sceneManager.BindUniforms(commandBuffer, shader);
                sceneManager.BindBuffers(commandBuffer, shader, 0);
                sceneManager?.MeshCardPool?.Bind(commandBuffer, shader, 0);

                if (cameraMode.name == "BaseColor")
                {
                    commandBuffer.SetComputeIntParam(shader, "_VisMode", 0);
                }
                else if (cameraMode.name == "Emissive")
                {
                    commandBuffer.SetComputeIntParam(shader, "_VisMode", 1);
                }
                else if (cameraMode.name == "Normal")
                {
                    commandBuffer.SetComputeIntParam(shader, "_VisMode", 2);
                }
                else if (cameraMode.name == "Irradiance")
                {
                    commandBuffer.SetComputeIntParam(shader, "_VisMode", 3);
                }
                else if (cameraMode.name == "Position")
                {
                    commandBuffer.SetComputeIntParam(shader, "_VisMode", 4);
                }

                commandBuffer.SetComputeMatrixParam(shader, "_CameraToWorld", camera.cameraToWorldMatrix);
                commandBuffer.SetComputeMatrixParam(shader, "_CameraInverseProjection", camera.projectionMatrix.inverse);

                commandBuffer.SetComputeTextureParam(shader, 0, "_Output", main);

                commandBuffer.DispatchCompute(shader, 0, (camera.pixelWidth + 7) / 8, (camera.pixelHeight + 7) / 8, 1);
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= OnUpdateEditor;
        }

        SceneView currentSceneView;
        SceneView.CameraMode cameraMode;

        public void OnUpdateEditor()
        {
            if (SceneView.lastActiveSceneView != currentSceneView)
            {
                if (currentSceneView != null)
                {
                    currentSceneView.onCameraModeChanged -= OnCameraModeChanged;
                }
                if (SceneView.lastActiveSceneView != null)
                {
                    currentSceneView = SceneView.lastActiveSceneView;
                    currentSceneView.onCameraModeChanged += OnCameraModeChanged;
                }

            }
        }

        public void OnCameraModeChanged(SceneView.CameraMode cameraMode)
        {
            this.cameraMode = cameraMode;
        }
    }
}
#endif