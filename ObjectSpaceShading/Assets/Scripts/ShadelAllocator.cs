using System;
using UnityEngine;
using UnityEngine.Rendering;

public class ShadelAllocator : IDisposable
{
    public ComputeBuffer counterBuffer;
    public ComputeBuffer historyCounterBuffer;

    public uint allocatedShadelCount;

    public void ReadCounter(int renderFrameIndex)
    {
        const int syncInterval = 16;
        if ((renderFrameIndex != 0) && ((renderFrameIndex % syncInterval) != 0))
        {
            return;
        }

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            AsyncGPUReadback.Request(counterBuffer, 4 * 8, 0, (AsyncGPUReadbackRequest request) =>
            {
                var data = request.GetData<uint>();

                allocatedShadelCount = 0;

                allocatedShadelCount += data[0] * 8;
                allocatedShadelCount += data[1] * 16;
                allocatedShadelCount += data[2] * 24;
                allocatedShadelCount += data[3] * 32;
                allocatedShadelCount += data[4] * 40;
                allocatedShadelCount += data[5] * 48;
                allocatedShadelCount += data[6] * 56;
                allocatedShadelCount += data[7] * 64;
            });
        }
        else
        {
            {
                uint[] data = new uint[4];
                counterBuffer.GetData(data, 0, 0, 4);

                allocatedShadelCount = 0;

                allocatedShadelCount += data[0] * 8;
                allocatedShadelCount += data[1] * 16;
                allocatedShadelCount += data[2] * 24;
                allocatedShadelCount += data[3] * 32;
                allocatedShadelCount += data[4] * 40;
                allocatedShadelCount += data[5] * 48;
                allocatedShadelCount += data[6] * 56;
                allocatedShadelCount += data[7] * 64;
            }
        }
    }


    static int ShadelCapacityProp = Shader.PropertyToID("_ShadelCapacity");

    static int ConcurrentAllocatorCounterBufferProp = Shader.PropertyToID("_ConcurrentAllocatorCounterBuffer");
    static int HistoryConcurrentAllocatorCounterBufferProp = Shader.PropertyToID("_HistoryConcurrentAllocatorCounterBuffer");

    static int ConcurrentAllocatorCounterBufferRWProp = Shader.PropertyToID("_ConcurrentAllocatorCounterBufferRW");

    public ShadelAllocator(int capacity)
    {
        counterBuffer = new ComputeBuffer(8, 4, ComputeBufferType.Structured);
        counterBuffer.name = "Concurrent Allocator Counter Buffer A";

        historyCounterBuffer = new ComputeBuffer(8, 4, ComputeBufferType.Structured);
        historyCounterBuffer.name = "Concurrent Allocator Counter Buffer B";
    }

    public void Bind(CommandBuffer commandBuffer, ComputeShader shader)
    {
        commandBuffer.SetComputeIntParam(shader, ShadelCapacityProp, VirtualRenderTexture.ShadelCapacity);
    }

    public void BindKernel(CommandBuffer commandBuffer, ComputeShader shader, int kernelIndex)
    {
        commandBuffer.SetComputeBufferParam(shader, kernelIndex, ConcurrentAllocatorCounterBufferRWProp, counterBuffer);
    }

    public void BindKernelReadOnly(CommandBuffer commandBuffer, ComputeShader shader, int kernelIndex)
    {
        commandBuffer.SetComputeBufferParam(shader, kernelIndex, ConcurrentAllocatorCounterBufferProp, counterBuffer);
        commandBuffer.SetComputeBufferParam(shader, kernelIndex, HistoryConcurrentAllocatorCounterBufferProp, historyCounterBuffer);
    }

    public void Bind(CommandBuffer commandBuffer)
    {
        commandBuffer.SetGlobalInt(ShadelCapacityProp, VirtualRenderTexture.ShadelCapacity);

        commandBuffer.SetGlobalBuffer(ConcurrentAllocatorCounterBufferProp, counterBuffer);
        commandBuffer.SetGlobalBuffer(HistoryConcurrentAllocatorCounterBufferProp, historyCounterBuffer);
    }

    public void Swap()
    {
        static void Swap<T>(ref T x, ref T y)
        {
            T temp = x;
            x = y;
            y = temp;
        }

        Swap(ref counterBuffer, ref historyCounterBuffer);
    }

    public void Dispose()
    {
        counterBuffer?.Dispose();
        historyCounterBuffer?.Dispose();
    }
}