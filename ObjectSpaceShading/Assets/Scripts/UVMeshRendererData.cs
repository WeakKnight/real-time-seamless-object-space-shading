using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshRenderer))]
public class UVMeshRendererData : MonoBehaviour
{
    public HandlePool.Handle instanceHandle = HandlePool.Handle.Invalid();
    public uint size = 1024;
    public uint idMapSize = 1024;
    public float mipBias = 0.0f;
    public int shadingInterval = 0;
    [NonSerialized]
    public int prevShadingInterval = 0;

    public MeshRenderer _MeshRenderer;
    public MeshFilter meshFilter;

    static Dictionary<int, UVMeshRendererData> dic;

    public static Dictionary<int, UVMeshRendererData> GetDictionary()
    {
        if (dic == null)
        {
            dic = new Dictionary<int, UVMeshRendererData>();
        }

        return dic;
    }

    private void OnValidate()
    {
        UpdatePropBlock();
    }

    [ContextMenu("Flip X")]
    public void FlipX()
    {
        var mesh = meshFilter.mesh;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        for (int vid = 0; vid < vertices.Length; vid++)
        {
            vertices[vid] = new float3(-1.0f, 1.0f, 1.0f) * vertices[vid];
            if (normals != null)
            {
                normals[vid] = new float3(-1.0f, 1.0f, 1.0f) * normals[vid];
            }
        }
        mesh.vertices = vertices;
        mesh.normals = normals;

        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var indices = mesh.GetIndices(subMesh);
            for (int triIndex = 0; triIndex < (indices.Length / 3); triIndex++)
            {
                int a = indices[triIndex * 3 + 2];
                int b = indices[triIndex * 3 + 1];
                int c = indices[triIndex * 3 + 0];

                indices[triIndex * 3 + 0] = a;
                indices[triIndex * 3 + 1] = b;
                indices[triIndex * 3 + 2] = c;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, subMesh);
        }
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;
    }

    void UpdatePropBlock()
    {
        if (_MeshRenderer == null)
        {
            return;    
        }

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        _MeshRenderer.GetPropertyBlock(propertyBlock);
        {
            propertyBlock.SetFloat("_VRT_InstanceMipBias", mipBias);
            propertyBlock.SetInt("_VRT_ShadingInterval", shadingInterval);
        }
        _MeshRenderer.SetPropertyBlock(propertyBlock);
    }

    void OnEnable()
    {
        if (!ObjectSpaceShadingPipeline.InstanceHandlePool.IsValid(instanceHandle))
        {
            instanceHandle = ObjectSpaceShadingPipeline.InstanceHandlePool.Allocate();

            GetDictionary()[instanceHandle] = this;
        }

        _MeshRenderer = GetComponent<MeshRenderer>();
        meshFilter = GetComponent<MeshFilter>();

        UpdatePropBlock();
    }

    void OnDisable()
    {
        if (ObjectSpaceShadingPipeline.InstanceHandlePool.IsValid(instanceHandle))
        {
            GetDictionary().Remove(instanceHandle);

            ObjectSpaceShadingPipeline.InstanceHandlePool.Free(instanceHandle);
            instanceHandle = HandlePool.Handle.Invalid();
        }
    }
}
