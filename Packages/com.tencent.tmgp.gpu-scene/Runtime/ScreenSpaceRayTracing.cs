using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    public static class ScreenSpaceRayTracing
    {
        static class Constants
        {
            public static int DepthTex = Shader.PropertyToID("_GPUScene_DepthTex");
            public static int IdTex = Shader.PropertyToID("_GPUScene_IdTex");
            
            public static int PixelSize = Shader.PropertyToID("_GPUScene_PixelSize");
            public static int ViewProjection = Shader.PropertyToID("_GPUScene_ViewProjection");
            public static int Projection = Shader.PropertyToID("_GPUScene_Projection"); 
            public static int WorldToCamera = Shader.PropertyToID("_GPUScene_WorldToCamera");
            public static int CameraToWorld = Shader.PropertyToID("_GPUScene_CameraToWorld");
            public static int CameraToScreenMatrix = Shader.PropertyToID("_GPUScene_CameraToScreenMatrix");
        }

        public static void Bind(CommandBuffer commandBuffer, ComputeShader shader, Camera camera)
        {
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);

            Matrix4x4 GetCameraToScreenMatrix(int screenWidth, int screenHeight)
            {
                Matrix4x4 ndcToPixelMat = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0)) *
                                          Matrix4x4.Scale(new Vector3(-0.5f, -0.5f, 1));
                ndcToPixelMat = Matrix4x4.Scale(new Vector3(screenWidth, screenHeight, 1)) * ndcToPixelMat;
                Matrix4x4 projectionToPixelMatrix = ndcToPixelMat * projection;
                return projectionToPixelMatrix;
            }

            var viewProjection = projection * camera.transform.worldToLocalMatrix;
            commandBuffer.SetComputeMatrixParam(shader, Constants.ViewProjection, viewProjection);
            commandBuffer.SetComputeMatrixParam(shader, Constants.Projection, projection);
            commandBuffer.SetComputeMatrixParam(shader, Constants.WorldToCamera, camera.worldToCameraMatrix);
            commandBuffer.SetComputeMatrixParam(shader, Constants.CameraToWorld, camera.cameraToWorldMatrix);
            commandBuffer.SetComputeMatrixParam(shader, Constants.CameraToScreenMatrix, GetCameraToScreenMatrix(camera.pixelWidth, camera.pixelHeight));

            commandBuffer.SetComputeVectorParam(shader, Constants.PixelSize, new Vector4(1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight, camera.pixelWidth, camera.pixelHeight));
        }

        public static void Bind(CommandBuffer commandBuffer, ComputeShader shader, int kernelIndex, Camera camera, RenderTargetIdentifier depth, RenderTargetIdentifier id)
        {
            Bind(commandBuffer, shader, camera);

            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.DepthTex, depth, 0, RenderTextureSubElement.Depth);
            commandBuffer.SetComputeTextureParam(shader, kernelIndex, Constants.IdTex, id);
        }
    }
}
