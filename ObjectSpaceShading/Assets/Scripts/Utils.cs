using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public static class Utils
{
    private static ComputeShader shader;
    private static int clearRawBufferKernel;
    private static int clearRawBufferToMaxUIntKernel;
    private static int copyRawBufferKernel;

    private static GraphicsBuffer emptyBuffer;

    private static void PrepareShader()
    {
        if (shader == null)
        {
            shader = FindComputeShaderByPath("Utils");
            clearRawBufferKernel = shader.FindKernel("ClearRawBufferData");
            clearRawBufferToMaxUIntKernel = shader.FindKernel("ClearRawBufferDataToMaxUInt");
            copyRawBufferKernel = shader.FindKernel("CopyRawBufferData");
        }
    }

    public static uint DivideRoundUp(uint numerator, uint denominator)
    {
        return System.Math.Max(0, (numerator + denominator - 1) / denominator);
    }

    public static int DivideRoundUp(int numerator, int denominator)
    {
        return System.Math.Max(0, (numerator + denominator - 1) / denominator);
    }

    public static void ClearRawBufferData(CommandBuffer commandBuffer, ComputeBuffer buffer)
    {
        PrepareShader();

        int bufferSizeInBytes = buffer.count * buffer.stride;
        int countInQuadBytes = DivideRoundUp(bufferSizeInBytes, 4);

        // Keep in-sync with ClearRawBufferData
        int numItemsPerThreadGroup = 256 * 32;
        int groupSizeX = DivideRoundUp(countInQuadBytes, numItemsPerThreadGroup);

        commandBuffer.SetComputeBufferParam(shader, clearRawBufferKernel, "_Buffer", buffer);
        commandBuffer.DispatchCompute(shader, clearRawBufferKernel, groupSizeX, 1, 1);
    }

    public static void ClearRawBufferDataToMaxUInt(CommandBuffer commandBuffer, ComputeBuffer buffer)
    {
        PrepareShader();

        int bufferSizeInBytes = buffer.count * buffer.stride;
        int countInQuadBytes = DivideRoundUp(bufferSizeInBytes, 4);

        // Keep in-sync with ClearRawBufferData
        int numItemsPerThreadGroup = 256 * 32;
        int groupSizeX = DivideRoundUp(countInQuadBytes, numItemsPerThreadGroup);

        commandBuffer.SetComputeBufferParam(shader, clearRawBufferToMaxUIntKernel, "_Buffer", buffer);
        commandBuffer.DispatchCompute(shader, clearRawBufferToMaxUIntKernel, groupSizeX, 1, 1);
    }

    public static void CopyRawBufferData(CommandBuffer commands,
                                         ComputeBuffer srcBuffer, int srcOffsetInBytes, int srcLengthInBytes,
                                         ComputeBuffer dstBuffer, int dstOffsetInBytes)
    {
        PrepareShader();

        int countInQuadBytes = DivideRoundUp(srcLengthInBytes, 4);

        // Keep in-sync with CopyRawBufferData
        int numItemsPerThreadGroup = 64 * 16;
        int groupSizeX = DivideRoundUp(countInQuadBytes, numItemsPerThreadGroup);

        commands.SetComputeBufferParam(shader, 0, "_SrcBuffer", srcBuffer);
        commands.SetComputeIntParam(shader, "_SrcOffsetInBytes", srcOffsetInBytes);
        commands.SetComputeIntParam(shader, "_SrcLengthInBytes", srcLengthInBytes);
        commands.SetComputeBufferParam(shader, 0, "_DstBuffer", dstBuffer);
        commands.SetComputeIntParam(shader, "_DstOffsetInBytes", dstOffsetInBytes);
        commands.DispatchCompute(shader, 0, groupSizeX, 1, 1);
    }

    public static uint ComputeMip1DBaseOffset(uint lod, uint chartSize)
    {
        uint a = chartSize * chartSize;
        uint s = (uint)(4.0f / 3.0f * a * (1.0f - Mathf.Pow(2.0f, -2.0f * lod)));
        return s;
    }

    public static void PrintList<T>(List<T> list)
    {
        string listString = "";

        foreach (T item in list)
        {
            listString += item.ToString() + ", ";
        }

        // Remove the trailing comma and space
        if (listString.Length > 0)
        {
            listString = listString.Substring(0, listString.Length - 2);
        }

        Debug.Log("List: [" + listString + "]");
    }

    public static void PrintArray(System.Array list)
    {
        string listString = "";

        foreach (var item in list)
        {
            listString += item.ToString() + ", ";
        }

        // Remove the trailing comma and space
        if (listString.Length > 0)
        {
            listString = listString.Substring(0, listString.Length - 2);
        }

        Debug.Log("List: [" + listString + "]");
    }

    public static GraphicsBuffer GetEmptyBuffer()
    {
        if (emptyBuffer == null)
        {
            emptyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw, 1, sizeof(uint));
        }

        return emptyBuffer;
    }

    public static Shader FindShaderByPath(string resourceRelativePath)
    {
        return FindResourceByPath<Shader>(resourceRelativePath);
    }

    public static ComputeShader FindComputeShaderByPath(string resourceRelativePath)
    {
        return FindResourceByPath<ComputeShader>(resourceRelativePath);
    }

    public static T FindResourceByPath<T>(string resourceRelativePath) where T : UnityEngine.Object
    {
        var res = Resources.Load<T>(resourceRelativePath);
        if (!res)
        {
            throw new FileNotFoundException(String.Format("Fail to find resource [{0}]", resourceRelativePath));
        }

        return res;
    }

    public static void Swap<T>(ref T lhs, ref T rhs)
    {
        T temp;
        temp = lhs;
        lhs  = rhs;
        rhs  = temp;
        temp = default(T);
    }
}

