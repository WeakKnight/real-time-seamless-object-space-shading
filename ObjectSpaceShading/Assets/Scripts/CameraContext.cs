using UnityEngine;

public class CameraContext : System.IDisposable
{
    public VirtualRenderTexture virtualRenderTexture;
    public ShadelAllocator shadelAllocator;
    public AtlasManager atlasManager;
    public RenderTaskAllocator renderTaskAllocator;

    public bool debug = false;
    public RenderTexture RemapBufferVisTexture;
    public RenderTexture HistoryRemapBufferVisTexture;
    public int renderFrameIndex = 0;

    public bool resetAccum = false;
    private Matrix4x4 _PrevNonJitteredProjectionMatrix = Matrix4x4.zero;
    private Matrix4x4 _PrevWorldToCameraMatrix = Matrix4x4.zero;

    private Camera _Camera;

    public CameraContext(Camera camera)
    {
        SetupMotionVectorFlag(camera);

        _Camera = camera;

        virtualRenderTexture = new VirtualRenderTexture();
        shadelAllocator = new ShadelAllocator(VirtualRenderTexture.ShadelCapacity);
        atlasManager = new AtlasManager();
        renderTaskAllocator = new RenderTaskAllocator();

        if (Application.isEditor)
        {
            if ((RemapBufferVisTexture == null || !RemapBufferVisTexture.enableRandomWrite))
            {
                if (RemapBufferVisTexture != null)
                {
                    RemapBufferVisTexture.Release();
                }

                RemapBufferVisTexture = new RenderTexture(VirtualRenderTexture.RemapBufferWidth, VirtualRenderTexture.RemapBufferHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RemapBufferVisTexture.name = "RemapBufferVisTexture";
                RemapBufferVisTexture.enableRandomWrite = true;
                RemapBufferVisTexture.Create();
            }

            if ((HistoryRemapBufferVisTexture == null || !HistoryRemapBufferVisTexture.enableRandomWrite))
            {
                if (HistoryRemapBufferVisTexture != null)
                {
                    HistoryRemapBufferVisTexture.Release();
                }

                HistoryRemapBufferVisTexture = new RenderTexture(VirtualRenderTexture.RemapBufferWidth, VirtualRenderTexture.RemapBufferHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                HistoryRemapBufferVisTexture.name = "HistoryRemapBufferVisTexture";
                HistoryRemapBufferVisTexture.enableRandomWrite = true;
                HistoryRemapBufferVisTexture.Create();
            }
        }
    }

    protected void SetupMotionVectorFlag(Camera camera)
    {
        // Can't get correct unity_MatrixPreviousM from engine without this
        camera.depthTextureMode |= (DepthTextureMode.MotionVectors | DepthTextureMode.Depth);
    }

    public void Dispose()
    {
        virtualRenderTexture.Release();

        shadelAllocator.Dispose();

        renderTaskAllocator.Dispose();

        if (RemapBufferVisTexture != null)
        {
            RemapBufferVisTexture.Release();
        }

        if (HistoryRemapBufferVisTexture != null)
        {
            HistoryRemapBufferVisTexture.Release();
        }
    }

    public void NextFrame()
    {
        renderFrameIndex++;
        virtualRenderTexture.SwapBuffers();
        shadelAllocator.Swap();

        resetAccum = !PrevNonJitteredViewProjectionMatrix().Equals(NonJitteredViewProjectionMatrix());

        _PrevNonJitteredProjectionMatrix = _Camera.nonJitteredProjectionMatrix;
        _PrevWorldToCameraMatrix = _Camera.worldToCameraMatrix;
        
        _Camera.ResetProjectionMatrix();
        _Camera.ResetWorldToCameraMatrix();
    }

    public Matrix4x4 ViewProjectionMatrix(bool renderIntoTexture = false)
    {
        return GL.GetGPUProjectionMatrix(_Camera.projectionMatrix, renderIntoTexture) * _Camera.worldToCameraMatrix;
    }

    public Matrix4x4 NonJitteredViewProjectionMatrix(bool renderIntoTexture = false)
    {
        return GL.GetGPUProjectionMatrix(_Camera.nonJitteredProjectionMatrix, renderIntoTexture) * _Camera.worldToCameraMatrix;
    }
    
    public Matrix4x4 PrevNonJitteredViewProjectionMatrix(bool renderIntoTexture = false)
    {
        return GL.GetGPUProjectionMatrix(_PrevNonJitteredProjectionMatrix, renderIntoTexture) * _PrevWorldToCameraMatrix;
    }

    public Vector2 JitterOffset()
    {
        return Vector2.zero;
    }
}
