using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class IDMapManager : System.IDisposable
{
    struct MeshKey
    {
        public Mesh mesh;
        public int size;

        public MeshKey(Mesh mesh, int size)
        {
            this.mesh = mesh;
            this.size = size;
        }

        public override int GetHashCode()
        {
            Hash128 hash = new Hash128();
            hash.Append(mesh.GetHashCode());
            hash.Append(size);
            return hash.GetHashCode();
        }
    }

    Dictionary<MeshKey, RenderTexture> dic = new();
    CommandBuffer commandBuffer;
    ComputeShader idMapProcessingShader;
    int idMapClearKernel;
    int idMapDistanceClearKernel;
    int idMapBorderFixKernel;
    int idMapFloodKernel;

    Material material;
    Material materialConservative;

    public IDMapManager()
    {
        commandBuffer = new CommandBuffer()
        {
            name = "IDMap Rendering"
        };

        idMapProcessingShader = Utils.FindComputeShaderByPath("IDMapProcessing");
        idMapClearKernel = idMapProcessingShader.FindKernel("IDMapClear");
        idMapDistanceClearKernel = idMapProcessingShader.FindKernel("IDMapDistanceClear");
        idMapBorderFixKernel = idMapProcessingShader.FindKernel("IDMapBorderFix");
        idMapFloodKernel = idMapProcessingShader.FindKernel("IDMapFlood");
    }

    public void Dispose()
    {
        foreach (var idMap in dic.Values)
        {
            if (idMap != null)
            {
                idMap.Release();
            }
        }

        commandBuffer.Release();
    }

    public RenderTexture GetIDMap(Mesh mesh, int size)
    {
        if (mesh == null)
        {
            return null;
        }

        var meshKey = new MeshKey(mesh, size);
        if (dic.TryGetValue(meshKey, out var idMap))
        {
            return idMap;
        }
        else
        {
            RenderTexture rt = new RenderTexture(size, size, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt);
            rt.name = mesh.name + " ID Map";
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = FilterMode.Point;
            rt.enableRandomWrite = true;
            rt.useMipMap = false;
            rt.dimension = TextureDimension.Tex2D;
            rt.Create();

            Render(rt, mesh);

            dic.Add(meshKey, rt);

            return rt;
        }
    }

    static readonly float[] uvOffset =
        {
            -2, -2,
            2, -2,
            -2, 2,
            2, 2,

            -1, -2,
            1, -2,
            -2, -1,
            2, -1,
            -2, 1,
            2, 1,
            -1, 2,
            1, 2,

            -2, 0,
            2, 0,
            0, -2,
            0, 2,

            -1, -1,
            1, -1,
            -1, 0,
            1, 0,
            -1, 1,
            1, 1,
            0, -1,
            0, 1,

            0, 0
        };

    private void Render(RenderTexture rt, Mesh mesh)
    {
        int size = rt.width;

        RenderTexture temp = new RenderTexture(size, size, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt);
        temp.name = mesh.name + " ID Map";
        temp.wrapMode = TextureWrapMode.Clamp;
        temp.filterMode = FilterMode.Point;
        temp.enableRandomWrite = true;
        temp.useMipMap = false;
        temp.dimension = TextureDimension.Tex2D;
        temp.Create();

        RenderTexture CreateDistanceTexture()
        {
            RenderTexture result = new RenderTexture(size, size, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Point;
            result.enableRandomWrite = true;
            result.useMipMap = false;
            result.dimension = TextureDimension.Tex2D;
            result.Create();

            return result;
        }

        RenderTexture inputDistanceTex = CreateDistanceTexture();
        RenderTexture outputDistanceTex = CreateDistanceTexture();

        commandBuffer.Clear();

        commandBuffer.SetComputeIntParams(idMapProcessingShader, "_TextureDimension", new int[] { rt.width, rt.height });

        // Map Clear
        {
            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapClearKernel, "_IDMap", temp);

            commandBuffer.DispatchCompute(idMapProcessingShader, idMapClearKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
        }

        commandBuffer.SetRenderTarget(temp);
        commandBuffer.SetViewport(new Rect(0.0f, 0.0f, rt.width, rt.height));

        if (materialConservative == null)
        {
            materialConservative = new Material(Utils.FindShaderByPath("IDMapRenderConservative"));
        }

        if (material == null)
        {
            material = new Material(Utils.FindShaderByPath("IDMapRender"));
        }

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            commandBuffer.SetGlobalInt("_SubMeshIndex", subMeshIndex);
            commandBuffer.SetGlobalVector("_Jitter", Vector4.zero);
            commandBuffer.DrawMesh(mesh, Matrix4x4.identity, materialConservative, subMeshIndex);

            float texelSize = 1.0f / (float)size / 5.0f;

            for (int j = 0; j < uvOffset.Length / 2; j++)
            {
                Vector4 jittor = new Vector4(uvOffset[j * 2] * texelSize, uvOffset[j * 2 + 1] * texelSize, 0.0f, 0.0f);
                commandBuffer.SetGlobalVector("_Jitter", jittor);
                commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, subMeshIndex);
            }
        }

        {
            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapDistanceClearKernel, "_OutputDistanceTexture", inputDistanceTex);
            commandBuffer.DispatchCompute(idMapProcessingShader, idMapDistanceClearKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
        }

        int radius = 1;
        // Map BorderFix
        {
            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputDistanceTexture", inputDistanceTex);
            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputDistanceTexture", outputDistanceTex);

            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputTexture", temp);
            commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputTexture", rt);
            commandBuffer.SetComputeIntParam(idMapProcessingShader, "_Radius", radius);
            commandBuffer.DispatchCompute(idMapProcessingShader, idMapBorderFixKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
        }

        void Swap<T>(ref T x, ref T y)
        {
            T temp = x;
            x = y;
            y = temp;
        }

        for (int iter = 0; iter < 4; iter++)
        {
            Swap(ref inputDistanceTex, ref outputDistanceTex);
            radius *= 2;
            {
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputDistanceTexture", inputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputDistanceTexture", outputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputTexture", rt);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputTexture", temp);
                commandBuffer.SetComputeIntParam(idMapProcessingShader, "_Radius", radius);

                commandBuffer.DispatchCompute(idMapProcessingShader, idMapBorderFixKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
            }

            Swap(ref inputDistanceTex, ref outputDistanceTex);
            radius *= 2;
            {
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputDistanceTexture", inputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputDistanceTexture", outputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_InputTexture", temp);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapBorderFixKernel, "_OutputTexture", rt);
                commandBuffer.SetComputeIntParam(idMapProcessingShader, "_Radius", radius);

                commandBuffer.DispatchCompute(idMapProcessingShader, idMapBorderFixKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
            }
        }

        commandBuffer.SetComputeIntParam(idMapProcessingShader, "_Radius", 1);

        for (int iter = 0; iter < 4; iter++)
        {
            {
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_InputDistanceTexture", inputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_OutputDistanceTexture", outputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_InputTexture", rt);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_OutputTexture", temp);

                commandBuffer.DispatchCompute(idMapProcessingShader, idMapFloodKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);

                Swap(ref inputDistanceTex, ref outputDistanceTex);
            }

            {
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_InputDistanceTexture", inputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_OutputDistanceTexture", outputDistanceTex);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_InputTexture", temp);
                commandBuffer.SetComputeTextureParam(idMapProcessingShader, idMapFloodKernel, "_OutputTexture", rt);

                commandBuffer.DispatchCompute(idMapProcessingShader, idMapFloodKernel, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);

                Swap(ref inputDistanceTex, ref outputDistanceTex);
            }
        }

        Graphics.ExecuteCommandBuffer(commandBuffer);

        temp.Release();
    }
}
