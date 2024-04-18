using System;
using UnityEngine;
using UnityEngine.Rendering;
using GPUScene;

public class ScreenSpaceReSTIR : IDisposable
{
    int frameIndex = 0;
    ComputeShader shader = Resources.Load<ComputeShader>("ScreenSpaceReSTIR");

    int screenWidth = 0;
    int screenHeight = 0;
    RenderTexture historyColorTexture;

    ComputeBuffer[] reservoirBuffer = new ComputeBuffer[2];
    Vector2Int screenTiles = Vector2Int.zero;

    static readonly Vector2Int kScreenTileDim = new Vector2Int(16, 16);
    static readonly Vector2Int kScreenTileBits = new Vector2Int(4, 4);
    static readonly int kReservoirSize = 72;

    public ScreenSpaceReSTIR()
    {
    }

    public void Dispose()
    {
        Release();
    }

    void Release()
    {
        reservoirBuffer[0]?.Release();
        reservoirBuffer[0] = null;

        reservoirBuffer[1]?.Release();
        reservoirBuffer[1] = null;

        historyColorTexture?.Release();
        historyColorTexture = null;
    }

    bool Resize(int w, int h)
    {
        if (screenWidth == w && screenHeight == h)
        {
            return false;
        }

        screenWidth = w;
        screenHeight = h;

        Release();

        historyColorTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

        // Create reservoir buffer
        {
            screenTiles.x = Utils.DivideRoundUp(screenWidth, kScreenTileDim.x);
            screenTiles.y = Utils.DivideRoundUp(screenHeight, kScreenTileDim.y);

            int numElems = screenTiles.x * screenTiles.y * kScreenTileDim.x * kScreenTileDim.y;

            reservoirBuffer[0] = new ComputeBuffer(numElems, kReservoirSize, ComputeBufferType.Structured | ComputeBufferType.Raw, ComputeBufferMode.Immutable);
            reservoirBuffer[1] = new ComputeBuffer(numElems, kReservoirSize, ComputeBufferType.Structured | ComputeBufferType.Raw, ComputeBufferMode.Immutable);
        }

        return true;
    }

    public void Render(CommandBuffer commandBuffer, FBO fbo, GPUSceneManager sceneManager, Camera camera)
    {
        if (Resize(fbo.width, fbo.height))
        {
            Utils.ClearRawBufferData(commandBuffer, reservoirBuffer[0]);
            Utils.ClearRawBufferData(commandBuffer, reservoirBuffer[1]);
        }

        frameIndex++;

        commandBuffer.BeginSample("Reference Pass");

        sceneManager.BindUniforms(commandBuffer, shader);
        sceneManager.BindBuffers(commandBuffer, shader, 0);

        Utils.Swap(ref reservoirBuffer[0], ref reservoirBuffer[1]);
        commandBuffer.SetComputeBufferParam(shader, 0, "_HistoryReservoirBuffer", reservoirBuffer[0]);
        commandBuffer.SetComputeBufferParam(shader, 0, "_OutputReservoirBuffer", reservoirBuffer[1]);
        commandBuffer.SetComputeIntParams(shader, "_ScreenTiles", new int[] {screenTiles.x, screenTiles.y});
        commandBuffer.SetComputeVectorParam(shader, "_ScreenSize", new Vector4(screenWidth, screenHeight));

        commandBuffer.SetComputeTextureParam(shader, 0, "_Position", fbo.posW);
        commandBuffer.SetComputeTextureParam(shader, 0, "_Normal", fbo.normalW);
        commandBuffer.SetComputeTextureParam(shader, 0, "_BaseColor", fbo.baseColor);
        commandBuffer.SetComputeTextureParam(shader, 0, "_MotionVector", fbo.motionVector);

        commandBuffer.SetComputeTextureParam(shader, 0, "_HistoryColorTexture", historyColorTexture);

        commandBuffer.SetComputeTextureParam(shader, 0, "_Output", fbo.output);

        commandBuffer.SetComputeIntParam(shader, "_FrameIndex", frameIndex);
        commandBuffer.SetComputeVectorParam(shader, "_CameraPosition", camera.transform.position);
        commandBuffer.DispatchCompute(shader, 0, (fbo.width + 15) / 16, (fbo.height + 15) / 16, 1);

        commandBuffer.SetComputeMatrixParam(shader, "_CameraToWorld", camera.cameraToWorldMatrix);
        commandBuffer.SetComputeMatrixParam(shader, "_CameraInverseProjection", camera.projectionMatrix.inverse);

        // TODO: optimize
        commandBuffer.Blit(fbo.output, historyColorTexture);

        commandBuffer.EndSample("Reference Pass");
    }
}
