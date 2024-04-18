using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer))]
public class HalfedgeMeshRendererData : MonoBehaviour
{
    public Htex.MeshData MeshData;
    public Mesh HalfedgeMesh;
    public int CustomQuadSize = 0;
    public float mipBias = 0.0f;

    public int shadingInterval = 0;
    [NonSerialized]
    public int prevShadingInterval = 0;

    public MeshRenderer _MeshRenderer;
    Mesh _OriginalMesh;

    GraphicsBuffer _VertexToHalfedgeIDs;
    GraphicsBuffer _EdgeToHalfedgeIDs;
    GraphicsBuffer _FaceToHalfedgeIDs;
    GraphicsBuffer _Halfedges;
    GraphicsBuffer _UV0;

    public MaterialPropertyBlock block;

    public int HtexFaceCount => MeshData.EdgeCount;

    public int NumQuadsX => Mathf.Max(1, Mathf.Min(HtexFaceCount, 128));
    public int NumQuadsY => (HtexFaceCount + NumQuadsX - 1) / NumQuadsX;

    public int QuadWidth
    {
        get
        {
            if ((NumQuadsX * NumQuadsY < 16384) && (CustomQuadSize <= 0))
            {
                return Mathf.NextPowerOfTwo((int)Mathf.Sqrt((4096 * 2048) / Mathf.Max(1, NumQuadsX * NumQuadsY)));
            }

            return CustomQuadSize > 0 ? CustomQuadSize : 16;
        }
    }

    public int QuadHeight
    {
        get
        {
            return QuadWidth;
        }
    }
    
    public HandlePool.Handle instanceHandle = HandlePool.Handle.Invalid();

    static Dictionary<int, HalfedgeMeshRendererData> dic;

    public static Dictionary<int, HalfedgeMeshRendererData> GetDictionary()
    {
        if (dic == null)
        {
            dic = new Dictionary<int, HalfedgeMeshRendererData>();
        }

        return dic;
    }

    private void UpdatePropBlock()
    {
        if (_MeshRenderer == null || MeshData == null)
        {
            return;
        }

        if (block == null)
        {
            block = new();
        }

        _MeshRenderer.GetPropertyBlock(block);
        {
            block.SetBuffer("_VertexToHalfedgeIDs", _VertexToHalfedgeIDs);
            block.SetBuffer("_EdgeToHalfedgeIDs", _EdgeToHalfedgeIDs);
            block.SetBuffer("_FaceToHalfedgeIDs", _FaceToHalfedgeIDs);
            block.SetBuffer("_Halfedges", _Halfedges);
            block.SetBuffer("_UV0", _UV0);
            block.SetInteger("_HalfedgeMesh", 1);

            block.SetInt("_HtexTextureNumQuadsX", NumQuadsX);
            block.SetInt("_HtexTextureNumQuadsY", NumQuadsY);

            block.SetInt("_HtexTextureQuadWidth", QuadWidth);
            block.SetInt("_HtexTextureQuadHeight", QuadHeight);
            block.SetFloat("_VRT_InstanceMipBias", mipBias);
            block.SetInt("_VRT_ShadingInterval", shadingInterval);

        }
        _MeshRenderer.SetPropertyBlock(block);
    }

    private void OnValidate()
    {
        UpdatePropBlock();
    }

    void OnEnable()
    {
        if (MeshData == null)
        {
            return;
        }

        if (!ObjectSpaceShadingPipeline.InstanceHandlePool.IsValid(instanceHandle))
        {
            instanceHandle = ObjectSpaceShadingPipeline.InstanceHandlePool.Allocate();

            GetDictionary()[instanceHandle] = this;
        }

        _MeshRenderer = GetComponent<MeshRenderer>();

        _VertexToHalfedgeIDs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MeshData.VertexToHalfedgeIDs.Length, sizeof(int));
        _VertexToHalfedgeIDs.SetData(MeshData.VertexToHalfedgeIDs);

        _EdgeToHalfedgeIDs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MeshData.EdgeToHalfedgeIDs.Length, sizeof(int));
        _EdgeToHalfedgeIDs.SetData(MeshData.EdgeToHalfedgeIDs);

        _FaceToHalfedgeIDs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MeshData.FaceToHalfedgeIDs.Length, sizeof(int));
        _FaceToHalfedgeIDs.SetData(MeshData.FaceToHalfedgeIDs);

        _Halfedges = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MeshData.Halfedges.Length, Marshal.SizeOf(typeof(Htex.cc_Halfedge)));
        _Halfedges.SetData(MeshData.Halfedges);

        _UV0 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MeshData.Uvs.Length, Marshal.SizeOf(typeof(Vector2)));
        _UV0.SetData(MeshData.Uvs);

        UpdatePropBlock();

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        _OriginalMesh = meshFilter.sharedMesh;
        meshFilter.sharedMesh = HalfedgeMesh;
    }

    public void Bind(CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex)
    {
        commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, "_VertexToHalfedgeIDs", _VertexToHalfedgeIDs);
        commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, "_EdgeToHalfedgeIDs", _EdgeToHalfedgeIDs);
        commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, "_FaceToHalfedgeIDs", _FaceToHalfedgeIDs);
        commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, "_Halfedges", _Halfedges);
        commandBuffer.SetComputeBufferParam(computeShader, kernelIndex, "_UV0", _UV0);

        commandBuffer.SetComputeIntParam(computeShader, "_HtexTextureNumQuadsX", NumQuadsX);
        commandBuffer.SetComputeIntParam(computeShader, "_HtexTextureNumQuadsY", NumQuadsY);

        commandBuffer.SetComputeIntParam(computeShader, "_HtexTextureQuadWidth", QuadWidth);
        commandBuffer.SetComputeIntParam(computeShader, "_HtexTextureQuadHeight", QuadHeight);
    }

    void OnDisable()
    {
        if (ObjectSpaceShadingPipeline.InstanceHandlePool.IsValid(instanceHandle))
        {
            GetDictionary().Remove(instanceHandle);

            ObjectSpaceShadingPipeline.InstanceHandlePool.Free(instanceHandle);
            instanceHandle = HandlePool.Handle.Invalid();
        }

        _VertexToHalfedgeIDs?.Dispose();
        _VertexToHalfedgeIDs = null;
        _EdgeToHalfedgeIDs?.Dispose();
        _EdgeToHalfedgeIDs = null;
        _FaceToHalfedgeIDs?.Dispose();
        _FaceToHalfedgeIDs = null;
        _Halfedges?.Dispose();
        _Halfedges = null;
        _UV0?.Dispose();
        _UV0 = null;

        if (_OriginalMesh)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = _OriginalMesh;
        }

        if (_MeshRenderer)
        {
            if (block == null)
            {
                block = new();
            }

            _MeshRenderer.GetPropertyBlock(block);
            {
                block.SetInteger("_HalfedgeMesh", 0);
            }
            _MeshRenderer.SetPropertyBlock(block);
        }
    }
}
