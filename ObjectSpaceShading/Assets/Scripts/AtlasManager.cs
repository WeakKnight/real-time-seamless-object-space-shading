using System.Collections.Generic;
using GPUScene;
using Unity.Mathematics;
using UnityEngine;

public class AtlasManager
{
    RectAllocator allocator;

    Dictionary<HalfedgeMeshRendererData, uint> halfedgeDic;
    Dictionary<UVMeshRendererData, uint> uvDic;

    static class Props
    {
        public static int _DS_StartLocationAndDimension = Shader.PropertyToID("_DS_StartLocationAndDimension");
    }

    public uint4 GetSizePosition(HalfedgeMeshRendererData halfedgeMeshRendererData)
    {
        return allocator.GetSizePosition(halfedgeDic[halfedgeMeshRendererData]);
    }

    public uint4 GetSizePosition(UVMeshRendererData _UVMeshRendererData)
    {
        return allocator.GetSizePosition(uvDic[_UVMeshRendererData]);
    }

    public bool Contain(HalfedgeMeshRendererData rendererData)
    {
        if (halfedgeDic.TryGetValue(rendererData, out uint handle))
        {
            return handle != RectAllocator.INDEX_NONE;
        }

        return false;
    }

    public bool Contain(UVMeshRendererData rendererData)
    {
        if (uvDic.TryGetValue(rendererData, out uint handle))
        {
            return handle != RectAllocator.INDEX_NONE;
        }

        return false;
    }

    public AtlasManager()
    {
        allocator = new RectAllocator(64, 0, VirtualRenderTexture.Height / 64);
        halfedgeDic = new();
        uvDic = new();
    }

    public void AddInstance(UVMeshRendererData rendererData)
    {
        if (!uvDic.ContainsKey(rendererData))
        {
            uint handle = allocator.Alloc(rendererData.size, rendererData.size);
            uvDic[rendererData] = handle;

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            rendererData._MeshRenderer.GetPropertyBlock(propertyBlock);
            {
                var objectChartSizePosition = allocator.GetSizePosition(handle);
                propertyBlock.SetVector("_DS_StartLocationAndDimension", new Vector4(objectChartSizePosition.z, objectChartSizePosition.w, objectChartSizePosition.x, objectChartSizePosition.y));
            }
            rendererData._MeshRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    public void AddInstance(HalfedgeMeshRendererData rendererData)
    {
        if (!halfedgeDic.ContainsKey(rendererData))
        {
            int atlasWidth = rendererData.QuadWidth * rendererData.NumQuadsX;
            int atlasHeight = rendererData.QuadHeight * rendererData.NumQuadsY;

            uint handle = allocator.Alloc((uint)math.max(atlasWidth, atlasHeight), (uint)math.max(atlasWidth, atlasHeight));
            if (handle == RectAllocator.INDEX_NONE)
            {
                return;
            }

            halfedgeDic[rendererData] = handle;

            MaterialPropertyBlock propertyBlock = rendererData.block;
            rendererData._MeshRenderer.GetPropertyBlock(propertyBlock);
            {
                var objectChartSizePosition = allocator.GetSizePosition(handle);
                propertyBlock.SetVector("_DS_StartLocationAndDimension", new Vector4(objectChartSizePosition.z, objectChartSizePosition.w, atlasWidth, atlasHeight));
            }
            rendererData._MeshRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
