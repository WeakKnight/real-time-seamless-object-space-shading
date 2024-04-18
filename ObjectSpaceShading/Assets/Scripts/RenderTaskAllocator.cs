using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class RenderTaskAllocator
{
    ComputeBuffer taskBuffer;
    ComputeBuffer counterBuffer;
    ComputeBuffer argsBuffer;
    ComputeBuffer offsetBuffer;

    public const int capacity = 65536 * 4;

    public bool needPrintDebugInfo = false;

    public int counter;

    public RenderTaskAllocator()
    {
        taskBuffer = new ComputeBuffer(capacity, sizeof(uint), ComputeBufferType.Structured | ComputeBufferType.Raw);
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured | ComputeBufferType.Raw);
        argsBuffer = new ComputeBuffer(2 * ObjectSpaceShadingPipeline.InstanceHandlePool.capacity, 3 * sizeof(uint), ComputeBufferType.IndirectArguments | ComputeBufferType.Structured);
        offsetBuffer = new ComputeBuffer(ObjectSpaceShadingPipeline.InstanceHandlePool.capacity, 2 * sizeof(uint), ComputeBufferType.Structured | ComputeBufferType.Raw);
    }

    public int GetCounter()
    {
        if (SystemInfo.supportsAsyncGPUReadback)
        {
            AsyncGPUReadback.Request(counterBuffer, 4 * 1, 0, (AsyncGPUReadbackRequest request) =>
            {
                var data = request.GetData<uint>();
                counter = (int)data[0];


            });
        }
        else
        {
            uint[] data = new uint[4];
            counterBuffer.GetData(data, 0, 0, 4);

            counter = (int)data[0];
        }

        return counter;
    }

    public ComputeBuffer GetTaskBuffer()
    {
        return taskBuffer;
    }

    public ComputeBuffer GetCounterBuffer()
    {
        return counterBuffer;
    }

    public ComputeBuffer GetArgsBuffer()
    {
        return argsBuffer;
    }

    public ComputeBuffer GetOffsetBuffer()
    {
        return offsetBuffer;
    }

    public void Dispose()
    {
        taskBuffer.Release();
        counterBuffer.Release();
        argsBuffer.Release();
        offsetBuffer.Release();
    }
}
