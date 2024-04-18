using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FBO
{
    public class Dict
    {
        Dictionary<Camera, FBO> dics = new();

        public FBO GetFBO(Camera camera)
        {
            if (!dics.TryGetValue(camera, out FBO fbo))
            {
                fbo = new FBO();
                dics[camera] = fbo;
            }

            fbo.cameraTexture = GetCameraTexture(camera);

            fbo.Resize(camera);

            return fbo;
        }

        public void Release()
        {
            foreach (var fbo in dics)
            {
                fbo.Value.Dispose();
            }
            dics.Clear();
        }
    }

    public void Resize(Camera camera)
    {
        if (width == camera.pixelWidth && height == camera.pixelHeight && linearResult != null && output != null)
        {
            return;
        }

        Dispose();

        width = camera.pixelWidth;
        height = camera.pixelHeight;

        linearResult = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
        linearResult.Create();

        output = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        output.enableRandomWrite = true;
        output.filterMode = FilterMode.Point;
        output.Create();

        sumColor = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        sumColor.enableRandomWrite = true;
        sumColor.Create();

        posW = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        posW.Create();

        normalW = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        normalW.Create();

        baseColor = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
        baseColor.Create();

        motionVector = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
        motionVector.Create();
    }

    public void SetRenderTargetToLinearResult(CommandBuffer commandBuffer)
    {
        commandBuffer.SetRenderTarget(linearResult.colorBuffer);
        commandBuffer.ClearRenderTarget(false, true, Color.black);

        RenderTargetBinding renderTargetBinding = new()
        {
            colorRenderTargets = new RenderTargetIdentifier[] { linearResult.colorBuffer },
            depthRenderTarget = GetDepth(),
            colorLoadActions = new RenderBufferLoadAction[] { RenderBufferLoadAction.Load },
            colorStoreActions = new RenderBufferStoreAction[] { RenderBufferStoreAction.Store }
        };

        commandBuffer.SetRenderTarget(renderTargetBinding);
    }

    public void SetRenderTargetToOutput(CommandBuffer commandBuffer)
    {
        RenderTargetBinding renderTargetBinding = new()
        {
            colorRenderTargets = new RenderTargetIdentifier[] { output.colorBuffer },
            depthRenderTarget = GetDepth(),
            colorLoadActions = new RenderBufferLoadAction[] { RenderBufferLoadAction.Load },
            colorStoreActions = new RenderBufferStoreAction[] { RenderBufferStoreAction.Store }
        };

        commandBuffer.SetRenderTarget(renderTargetBinding);
    }

    public void SetRenderTargetToGBuffer(CommandBuffer commandBuffer)
    {
        RenderTargetBinding renderTargetBinding = new()
        {
            colorRenderTargets = new RenderTargetIdentifier[] { posW.colorBuffer, normalW.colorBuffer, baseColor.colorBuffer, motionVector.colorBuffer },
            depthRenderTarget = GetDepth(),
            colorLoadActions = new RenderBufferLoadAction[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.Load, RenderBufferLoadAction.Load, RenderBufferLoadAction.Load },
            colorStoreActions = new RenderBufferStoreAction[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store, RenderBufferStoreAction.Store, RenderBufferStoreAction.Store }
        };

        commandBuffer.SetRenderTarget(renderTargetBinding);
        commandBuffer.ClearRenderTarget(false, true, Color.clear);
    }

    public void Dispose()
    {
        if (linearResult)
        {
            linearResult.Release();
        }

        if (output)
        {
            output.Release();
        }

        if (sumColor)
        {
            sumColor.Release();
        }

        if (posW)
        {
            posW.Release();
        }

        if (normalW)
        {
            normalW.Release();
        }

        if (baseColor)
        {
            baseColor.Release();
        }

        if (motionVector)
        {
            motionVector.Release();
        }

        linearResult = null;
        output = null;
        sumColor = null;
        posW = null;
        normalW = null;
        baseColor = null;
        motionVector = null;
    }

    public RenderTargetIdentifier GetDepth()
    {
        return linearResult.depthBuffer;
    }

    public int width = -1;
    public int height = -1;

    private static RenderTargetIdentifier GetCameraTexture(Camera camera)
    {
        if (camera.targetTexture)
        {
            return camera.targetTexture;
        }

        return BuiltinRenderTextureType.CameraTarget;
    }

    public RenderTargetIdentifier cameraTexture;

    public RenderTexture linearResult;

    public RenderTexture output;

    public RenderTexture sumColor;

    public RenderTexture posW;
    public RenderTexture normalW;
    public RenderTexture baseColor;
    public RenderTexture motionVector;
}
