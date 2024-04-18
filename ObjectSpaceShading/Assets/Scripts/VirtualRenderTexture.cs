using UnityEngine;
using UnityEngine.Rendering;

public class VirtualRenderTexture
{
    public const int ShadelSize = 8;
    public const int ShadelGroupSize = 8;

    public const int StorageBufferWidth = 4096;
    public const int StorageBufferHeight = 4096;

    public const int Width = 49152;
    public const int Height = 32768;

    public const int RemapBufferWidth = 768; // Width / ShadelSize / ShadelGroupSize;
    public const int RemapBufferHeight = 512; // Height / ShadelSize / ShadelGroupSize;

    public const int PersistentPayloadBufferWidth = Height / 8;

    public ComputeBuffer HistoryRemapBuffer;
    public ComputeBuffer RemapBuffer;

    public RenderTexture HistoryStorageBuffer;
    public RenderTexture StorageBuffer;

    public ComputeBuffer PayloadBuffer;
    public ComputeBuffer HistoryPayloadBuffer;

    public ComputeBuffer PersistentPayloadBuffer;
    public ComputeBuffer HistoryPersistentPayloadBuffer;

    public ComputeBuffer HistoryOccupancyBuffer;
    public ComputeBuffer OccupancyBuffer;

    public RenderTexture AccumulatedFrameCountBuffer;
    public RenderTexture HistoryAccumulatedFrameCountBuffer;

    public FilterMode filterMode = FilterMode.Bilinear;
    public RenderTextureFormat format = RenderTextureFormat.ARGBHalf;

    public static int ShadelCapacity
    {
        get
        {
            return (StorageBufferWidth / ShadelSize) * (StorageBufferHeight / ShadelSize);
        }
    }

    static class ShaderConstants
    {
        public static int StorageBufferWidth = Shader.PropertyToID("_VRT_StorageBufferWidth");
        public static int StorageBufferHeight = Shader.PropertyToID("_VRT_StorageBufferHeight");
        public static int Width = Shader.PropertyToID("_VRT_Width");
        public static int Height = Shader.PropertyToID("_VRT_Height");
        public static int RemapBufferWidth = Shader.PropertyToID("_VRT_RemapBufferWidth");
        public static int RemapBufferHeight = Shader.PropertyToID("_VRT_RemapBufferHeight");
        public static int StorageBuffer = Shader.PropertyToID("_VRT_StorageBuffer");
        public static int RemapBuffer = Shader.PropertyToID("_VRT_RemapBuffer");
        public static int OccupancyBuffer = Shader.PropertyToID("_VRT_OccupancyBuffer");
    }

    public VirtualRenderTexture()
    {
        Init();
    }

    void Init()
    {
        RenderTexture CreateStorageBuffer(string name, int width, int height)
        {
            var result = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            result.name = name;
            result.enableRandomWrite = true;
            result.filterMode = filterMode;
            result.wrapMode = TextureWrapMode.Clamp;
            result.Create();

            return result;
        }

        StorageBuffer = CreateStorageBuffer("Storage Buffer", StorageBufferWidth, StorageBufferHeight);
        HistoryStorageBuffer = CreateStorageBuffer("History Storage Buffer", StorageBufferWidth, StorageBufferHeight);

        ComputeBuffer CreatePayloadBuffer(string name, int width, int height)
        {
            ComputeBuffer result = new ComputeBuffer(width * height + 4, 48, ComputeBufferType.Raw);
            result.name = name;

            return result;
        }

        PayloadBuffer = CreatePayloadBuffer("PayloadBuffer", StorageBufferWidth, StorageBufferHeight);
        HistoryPayloadBuffer = CreatePayloadBuffer("HistoryPayloadBuffer", StorageBufferWidth, StorageBufferHeight);

        PersistentPayloadBuffer = CreatePayloadBuffer("PersistentPayloadBuffer", PersistentPayloadBufferWidth, PersistentPayloadBufferWidth);
        HistoryPersistentPayloadBuffer = CreatePayloadBuffer("HistoryPersistentPayloadBuffer", PersistentPayloadBufferWidth, PersistentPayloadBufferWidth);

        var clearCommandBuffer = new CommandBuffer();
        Utils.ClearRawBufferDataToMaxUInt(clearCommandBuffer, PayloadBuffer);
        Utils.ClearRawBufferDataToMaxUInt(clearCommandBuffer, HistoryPayloadBuffer);
        Utils.ClearRawBufferDataToMaxUInt(clearCommandBuffer, PersistentPayloadBuffer);
        Utils.ClearRawBufferDataToMaxUInt(clearCommandBuffer, HistoryPersistentPayloadBuffer);
        Graphics.ExecuteCommandBuffer(clearCommandBuffer);

        RenderTexture CreateAccumulatedFrameCountBuffer(string name, int width, int height)
        {
            RenderTexture result = new RenderTexture(width, height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UInt, 0);
            result.name = name;
            result.enableRandomWrite = true;
            result.Create();

            return result;
        }

        AccumulatedFrameCountBuffer = CreateAccumulatedFrameCountBuffer("Accumulated Frame Count Buffer", StorageBufferWidth, StorageBufferHeight);
        HistoryAccumulatedFrameCountBuffer = CreateAccumulatedFrameCountBuffer("History Accumulated Frame Count Buffer", StorageBufferWidth, StorageBufferHeight);

        RemapBuffer = new ComputeBuffer(RemapBufferWidth * RemapBufferHeight, 4, ComputeBufferType.Raw);
        RemapBuffer.name = "Remap Buffer";

        HistoryRemapBuffer = new ComputeBuffer(RemapBufferWidth * RemapBufferHeight, 4, ComputeBufferType.Raw);
        HistoryRemapBuffer.name = "History Remap Buffer";

        OccupancyBuffer = new ComputeBuffer(RemapBufferWidth * RemapBufferHeight, 8, ComputeBufferType.Raw);
        OccupancyBuffer.name = "Occupancy Buffer";

        HistoryOccupancyBuffer = new ComputeBuffer(RemapBufferWidth * RemapBufferHeight, 8, ComputeBufferType.Raw);
        HistoryOccupancyBuffer.name = "History Occupancy Buffer";
    }

    public void Bind(CommandBuffer cmd, bool writable)
    {
        if (StorageBuffer == null)
        {
            Release();
            Init();
        }

        if (StorageBuffer.filterMode != filterMode)
        {
            StorageBuffer.filterMode = filterMode;
        }

        cmd.SetGlobalInt(ShaderConstants.StorageBufferWidth, StorageBufferWidth);
        cmd.SetGlobalInt(ShaderConstants.StorageBufferHeight, StorageBufferHeight);
        cmd.SetGlobalInt(ShaderConstants.Width, Width);
        cmd.SetGlobalInt(ShaderConstants.Height, Height);
        cmd.SetGlobalInt(ShaderConstants.RemapBufferWidth, RemapBufferWidth);
        cmd.SetGlobalInt(ShaderConstants.RemapBufferHeight, RemapBufferHeight);
        if (writable)
        {
            cmd.SetRandomWriteTarget(4, OccupancyBuffer);
        }
        else
        {
            cmd.SetGlobalBuffer(ShaderConstants.OccupancyBuffer, OccupancyBuffer);
        }
        cmd.SetGlobalBuffer(ShaderConstants.RemapBuffer, RemapBuffer);
        cmd.SetGlobalTexture(ShaderConstants.StorageBuffer, StorageBuffer);
    }

    public void SwapBuffers()
    {
        static void Swap<T>(ref T x, ref T y)
        {
            T temp = x;
            x = y;
            y = temp;
        }

        Swap(ref StorageBuffer, ref HistoryStorageBuffer);
        Swap(ref PayloadBuffer, ref HistoryPayloadBuffer);
        Swap(ref PersistentPayloadBuffer, ref HistoryPersistentPayloadBuffer);
        Swap(ref AccumulatedFrameCountBuffer, ref HistoryAccumulatedFrameCountBuffer);

        Swap(ref RemapBuffer, ref HistoryRemapBuffer);
        Swap(ref OccupancyBuffer, ref HistoryOccupancyBuffer);
    }

    public void Release()
    {
        if (StorageBuffer != null)
        {
            StorageBuffer.Release();
        }
        if (HistoryStorageBuffer != null)
        {
            HistoryStorageBuffer.Release();
        }

        if (PayloadBuffer != null)
        {
            PayloadBuffer.Release();
        }
        if (HistoryPayloadBuffer != null)
        {
            HistoryPayloadBuffer.Release();
        }

        if (PersistentPayloadBuffer != null)
        {
            PersistentPayloadBuffer.Release();
        }
        if (HistoryPersistentPayloadBuffer != null)
        {
            HistoryPersistentPayloadBuffer.Release();
        }

        if (AccumulatedFrameCountBuffer != null)
        {
            AccumulatedFrameCountBuffer.Release();
        }
        if (HistoryAccumulatedFrameCountBuffer != null)
        {
            HistoryAccumulatedFrameCountBuffer.Release();
        }

        if (RemapBuffer != null)
        {
            RemapBuffer.Release();
        }
        if (HistoryRemapBuffer != null)
        {
            HistoryRemapBuffer.Release();
        }
        if (OccupancyBuffer != null)
        {
            OccupancyBuffer.Release();
        }
        if (HistoryOccupancyBuffer != null)
        {
            HistoryOccupancyBuffer.Release();
        }
    }
}
