using GPUScene;
using UnityEngine;
using UnityEngine.Rendering;

public class ReferenceRenderer
{
    ComputeShader shader;

    public ReferenceRenderer()
    {
    }

    int frameIndex = 0;

    public void Render(CommandBuffer commandBuffer, FBO fbo, GPUSceneManager sceneManager, Camera camera)
    {
        frameIndex++;

        commandBuffer.BeginSample("Reference Pass");

        if (shader == null)
        {
            shader = Resources.Load<ComputeShader>("Reference");
        }

        sceneManager.BindUniforms(commandBuffer, shader);
        sceneManager.BindBuffers(commandBuffer, shader, 0);

        commandBuffer.SetComputeTextureParam(shader, 0, "_Position", fbo.posW);
        commandBuffer.SetComputeTextureParam(shader, 0, "_Normal", fbo.normalW);
        commandBuffer.SetComputeTextureParam(shader, 0, "_BaseColor", fbo.baseColor);

        commandBuffer.SetComputeTextureParam(shader, 0, "_SumColor", fbo.sumColor);
        commandBuffer.SetComputeTextureParam(shader, 0, "_Output", fbo.output);

        commandBuffer.SetComputeIntParam(shader, "_FrameIndex", frameIndex);
        commandBuffer.SetComputeVectorParam(shader, "_CameraPosition", camera.transform.position);
        commandBuffer.DispatchCompute(shader, 0, (fbo.width + 15) / 16, (fbo.height + 15) / 16, 1);

        commandBuffer.SetComputeMatrixParam(shader, "_CameraToWorld", camera.cameraToWorldMatrix);
        commandBuffer.SetComputeMatrixParam(shader, "_CameraInverseProjection", camera.projectionMatrix.inverse);

        commandBuffer.EndSample("Reference Pass");
    }
}
