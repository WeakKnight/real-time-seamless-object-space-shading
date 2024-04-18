using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Setup vertex/index/declaration buffers for mesh processing in compute shader
// Buffer layout and binding scheme are same as UnityRayTracingMeshUtils so that we can reused those code
public static class MeshUtils
{
    private const int kMaxVertexStreams     = 4;
    private const int kVertexAttributeCount = 12;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MeshInfo
    {
        public fixed uint vertexSize[kMaxVertexStreams];
        public       uint baseVertex;
        public       uint vertexStart;
        public       uint indexSize;
        public       uint indexStart;
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexAttributeInfo
    {
        public uint Stream;
        public uint Format;
        public uint ByteOffset;
        public uint Dimension;
    };

    static GraphicsBuffer vertexDeclBuffer;

    static GraphicsBuffer meshInfoBuffer;

    static GraphicsBuffer[] vertexBuffers;

    static VertexAttributeInfo[] vertexDeclaration = new VertexAttributeInfo[kVertexAttributeCount];
    static VertexAttributeDescriptor[] vertexDescriptors = new VertexAttributeDescriptor[kVertexAttributeCount];
    static MeshInfo[] meshInfo = new MeshInfo[32];
    static int[] kernelArray = new int[1];

    public static void Init()
    {
        Release();

        vertexDeclBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                                                kVertexAttributeCount,
                                                                Marshal.SizeOf<VertexAttributeInfo>());

        meshInfoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                                                32,
                                                                Marshal.SizeOf<MeshInfo>());

        vertexBuffers = new GraphicsBuffer[kMaxVertexStreams];
    }

    public static void Release()
    {
        vertexDeclBuffer?.Release();
        meshInfoBuffer?.Release();
        vertexBuffers = null;
    }

    static void ResetVertexInfos()
    {
        // Invalidate all vertex buffers
        for (int i = 0; i < kMaxVertexStreams; ++i)
        {
            vertexBuffers[i] = Utils.GetEmptyBuffer();
        }

        // Invalidate all vertex attributes
        for (int i = 0; i < kVertexAttributeCount; ++i)
        {
            vertexDeclaration[i].ByteOffset = 0xFFFFFFFF;
        }
    }

    public static void SetupVertexAttributeInfo(Mesh mesh, CommandBuffer commands, ComputeShader computeShader, int kernel = 0)
    {
        kernelArray[0] = kernel;
        SetupVertexAttributeInfo(mesh, commands, computeShader, kernelArray);
    }

    public static void SetupVertexAttributeInfo(Mesh mesh, CommandBuffer commands, ComputeShader computeShader, int[] kernels)
    {
        ResetVertexInfos();

        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexBufferTarget  |= GraphicsBuffer.Target.Raw;

        int attributesCount = mesh.GetVertexAttributes(vertexDescriptors);
        Debug.Assert(attributesCount < kVertexAttributeCount);

        int requiredVertexStreams = 0;
        for (int i = 0; i < attributesCount; ++i)
        {
            VertexAttributeDescriptor desc = vertexDescriptors[i];
            if (desc.attribute == VertexAttribute.BlendWeight || desc.attribute == VertexAttribute.BlendIndices)
                continue;

            vertexDeclaration[(int) desc.attribute].Stream     = (uint) desc.stream;
            vertexDeclaration[(int) desc.attribute].Format     = (uint) desc.format;
            vertexDeclaration[(int) desc.attribute].Dimension  = (uint) desc.dimension;
            vertexDeclaration[(int) desc.attribute].ByteOffset = (uint) mesh.GetVertexAttributeOffset(desc.attribute);

            int vbStreamMask = 0x01 << desc.stream;
            if ((requiredVertexStreams & vbStreamMask) == 0)
            {
                requiredVertexStreams |= vbStreamMask;
                vertexBuffers[desc.stream] = mesh.GetVertexBuffer(desc.stream);
            }
        }

        commands.SetBufferData(vertexDeclBuffer, vertexDeclaration);

        GraphicsBuffer indexBuffer  = mesh.GetIndexBuffer();
        for (int i = 0; i < kernels.Length; ++i)
        {
            int k = kernels[i];
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexDeclaration", vertexDeclBuffer);
            commands.SetComputeBufferParam(computeShader, k, "_MeshIndexBuffer", indexBuffer);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer0", vertexBuffers[0]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer1", vertexBuffers[1]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer2", vertexBuffers[2]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer3", vertexBuffers[3]);
        }

        indexBuffer.Dispose();
        for (int i = 0; i < kMaxVertexStreams; ++i)
        {
            if ((requiredVertexStreams & (0x01 << i)) != 0)
            {
                vertexBuffers[i].Dispose();
                vertexBuffers[i] = null;
            }
        }
    }

    public static void SetupVertexAttributeInfo(SkinnedMeshRenderer skinnedMesh, CommandBuffer commands, ComputeShader computeShader, int kernel = 0)
    {
        kernelArray[0] = kernel;
        SetupVertexAttributeInfo(skinnedMesh, commands, computeShader, kernelArray);
    }

    public static void SetupVertexAttributeInfo(SkinnedMeshRenderer skinnedMesh, CommandBuffer commands, ComputeShader computeShader, int[] kernels)
    {
        ResetVertexInfos();

        skinnedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        skinnedMesh.sharedMesh.indexBufferTarget  |= GraphicsBuffer.Target.Raw;

        int attributesCount = skinnedMesh.sharedMesh.GetVertexAttributes(vertexDescriptors);
        Debug.Assert(attributesCount < kVertexAttributeCount);

        int requiredVertexStreams = 0;
        for (int i = 0; i < attributesCount; ++i)
        {
            VertexAttributeDescriptor desc = vertexDescriptors[i];
            if (desc.attribute == VertexAttribute.BlendWeight || desc.attribute == VertexAttribute.BlendIndices)
                continue;

            vertexDeclaration[(int) desc.attribute].Stream     = (uint) desc.stream;
            vertexDeclaration[(int) desc.attribute].Format     = (uint) desc.format;
            vertexDeclaration[(int) desc.attribute].Dimension  = (uint) desc.dimension;
            vertexDeclaration[(int) desc.attribute].ByteOffset = (uint) skinnedMesh.sharedMesh.GetVertexAttributeOffset(desc.attribute);

            int vbStreamMask = 0x01 << desc.stream;
            if ((requiredVertexStreams & vbStreamMask) == 0)
            {
                requiredVertexStreams |= vbStreamMask;

                GraphicsBuffer vertexBuffer = null;
                if (desc.stream == 0)
                    vertexBuffer = skinnedMesh.GetVertexBuffer();

                if (vertexBuffer == null)
                    vertexBuffer = skinnedMesh.sharedMesh.GetVertexBuffer(desc.stream);

                vertexBuffers[desc.stream] = vertexBuffer;
            }
        }

        commands.SetBufferData(vertexDeclBuffer, vertexDeclaration);

        GraphicsBuffer indexBuffer  = skinnedMesh.sharedMesh.GetIndexBuffer();
        for (int i = 0; i < kernels.Length; ++i)
        {
            int k = kernels[i];
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexDeclaration", vertexDeclBuffer);
            commands.SetComputeBufferParam(computeShader, k, "_MeshIndexBuffer", indexBuffer);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer0", vertexBuffers[0]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer1", vertexBuffers[1]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer2", vertexBuffers[2]);
            commands.SetComputeBufferParam(computeShader, k, "_MeshVertexBuffer3", vertexBuffers[3]);
        }

        indexBuffer.Dispose();
        for (int i = 0; i < kMaxVertexStreams; ++i)
        {
            if ((requiredVertexStreams & (0x01 << i)) != 0)
            {
                vertexBuffers[i].Dispose();
                vertexBuffers[i] = null;
            }
        }
    }

    public static void SetupVertexAttributeInfo(Mesh mesh, CommandBuffer commands)
    {
        ResetVertexInfos();

        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

        int attributesCount = mesh.GetVertexAttributes(vertexDescriptors);
        Debug.Assert(attributesCount < kVertexAttributeCount);

        int requiredVertexStreams = 0;
        for (int i = 0; i < attributesCount; ++i)
        {
            VertexAttributeDescriptor desc = vertexDescriptors[i];
            if (desc.attribute == VertexAttribute.BlendWeight || desc.attribute == VertexAttribute.BlendIndices)
                continue;

            vertexDeclaration[(int)desc.attribute].Stream = (uint)desc.stream;
            vertexDeclaration[(int)desc.attribute].Format = (uint)desc.format;
            vertexDeclaration[(int)desc.attribute].Dimension = (uint)desc.dimension;
            vertexDeclaration[(int)desc.attribute].ByteOffset = (uint)mesh.GetVertexAttributeOffset(desc.attribute);

            int vbStreamMask = 0x01 << desc.stream;
            if ((requiredVertexStreams & vbStreamMask) == 0)
            {
                requiredVertexStreams |= vbStreamMask;
                vertexBuffers[desc.stream] = mesh.GetVertexBuffer(desc.stream);
            }
        }

        commands.SetBufferData(vertexDeclBuffer, vertexDeclaration);

        GraphicsBuffer indexBuffer = mesh.GetIndexBuffer();
        commands.SetGlobalBuffer("_MeshVertexDeclaration", vertexDeclBuffer);
        commands.SetGlobalBuffer("_MeshIndexBuffer", indexBuffer);
        commands.SetGlobalBuffer("_MeshVertexBuffer0", vertexBuffers[0]);
        commands.SetGlobalBuffer("_MeshVertexBuffer1", vertexBuffers[1]);
        commands.SetGlobalBuffer("_MeshVertexBuffer2", vertexBuffers[2]);
        commands.SetGlobalBuffer("_MeshVertexBuffer3", vertexBuffers[3]);

        indexBuffer.Dispose();
        for (int i = 0; i < kMaxVertexStreams; ++i)
        {
            if ((requiredVertexStreams & (0x01 << i)) != 0)
            {
                vertexBuffers[i].Dispose();
                vertexBuffers[i] = null;
            }
        }
    }

    public static unsafe void SetupMeshInfo(Mesh mesh, CommandBuffer commands, ComputeShader computeShader, int kernel = 0)
    {
        kernelArray[0] = kernel;
        SetupMeshInfo(mesh, commands, computeShader, kernelArray);
    }

    public static unsafe void SetupMeshInfo(Mesh mesh, CommandBuffer commands, ComputeShader computeShader, int[] kernels)
    {
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            SubMeshDescriptor desc = mesh.GetSubMesh(subMeshIndex);
            Debug.Assert(desc.topology == MeshTopology.Triangles);

            for (int i = 0; i < kMaxVertexStreams; ++i)
            {
                meshInfo[subMeshIndex].vertexSize[i] = (uint)mesh.GetVertexBufferStride(i);
            }
            meshInfo[subMeshIndex].baseVertex = (uint)desc.baseVertex;
            meshInfo[subMeshIndex].vertexStart = (uint)desc.firstVertex;
            meshInfo[subMeshIndex].indexStart = (uint)desc.indexStart;
            meshInfo[subMeshIndex].indexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2u : 4u;
        }

        commands.SetBufferData(meshInfoBuffer, meshInfo, 0, 0, mesh.subMeshCount);

        for (int i = 0; i < kernels.Length; ++i)
        {
            int k = kernels[i];
            commands.SetComputeBufferParam(computeShader, k, "_MeshInfo", meshInfoBuffer);
        }
    }

    public static unsafe void SetupMeshInfo(Mesh mesh, CommandBuffer commands)
    {
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            SubMeshDescriptor desc = mesh.GetSubMesh(subMeshIndex);
            Debug.Assert(desc.topology == MeshTopology.Triangles);

            for (int i = 0; i < kMaxVertexStreams; ++i)
            {
                meshInfo[subMeshIndex].vertexSize[i] = (uint)mesh.GetVertexBufferStride(i);
            }
            meshInfo[subMeshIndex].baseVertex = (uint)desc.baseVertex;
            meshInfo[subMeshIndex].vertexStart = (uint)desc.firstVertex;
            meshInfo[subMeshIndex].indexStart = (uint)desc.indexStart;
            meshInfo[subMeshIndex].indexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2u : 4u;
        }

        commands.SetBufferData(meshInfoBuffer, meshInfo, 0, 0, mesh.subMeshCount);
        commands.SetGlobalBuffer("_MeshInfo", meshInfoBuffer);
    }
}
