using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor.AssetImporters;

[ScriptedImporter(3, "ccm")]
public class HalfedgeMeshImporter : ScriptedImporter
{
    public unsafe override void OnImportAsset(AssetImportContext ctx)
    {
        IntPtr pHalfedgeMesh = Htex.Bindings.Htex_loadMesh(ctx.assetPath);
        try
        {
            ImportHalfedgeMesh(ctx, pHalfedgeMesh);   
        }
        finally
        {
            if (pHalfedgeMesh != IntPtr.Zero)
                Htex.Bindings.Htex_releaseMesh(pHalfedgeMesh);
        }
    }

    void ImportHalfedgeMesh(AssetImportContext ctx, IntPtr pHalfedgeMesh)
    {
        Htex.MeshData meshData = ScriptableObject.CreateInstance<Htex.MeshData>();
        meshData.LoadHalfedgeMesh(pHalfedgeMesh);    

        Vector3[] vertices = new Vector3[meshData.VertexCount + meshData.FaceCount];
        Vector3[] normals = new Vector3[meshData.VertexCount + meshData.FaceCount];

        Parallel.For(0, meshData.VertexCount, vertexID =>
        {
            var p = meshData.VertexPoints[vertexID];
            vertices[vertexID] = new Vector3(p.x, p.y, p.z);
            normals[vertexID] = meshData.computeVertexNormal(vertexID);
        });

        Parallel.For(0, meshData.FaceCount, faceID =>
        {
            vertices[faceID + meshData.VertexCount] = meshData.computeBarycenter(faceID);
            normals[faceID + meshData.VertexCount] = meshData.computeFacePointNormal(faceID);
        });

        int[] triangles = new int[meshData.HalfedgeCount * 3];
        Parallel.For(0, meshData.HalfedgeCount, halfedgeID =>
        {
            int nextID = meshData.ccm_HalfedgeNextID(halfedgeID);
            triangles[halfedgeID * 3] = meshData.VertexCount + meshData.ccm_HalfedgeFaceID(halfedgeID);
            triangles[halfedgeID * 3 + 1] = meshData.ccm_HalfedgeVertexID(halfedgeID);
            triangles[halfedgeID * 3 + 2] = meshData.ccm_HalfedgeVertexID(nextID);
        });

        Mesh mesh = new Mesh();
        mesh.name = "HalfedgeMesh";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetIndices(triangles, MeshTopology.Triangles, 0);
        ctx.AddObjectToAsset("HalfedgeMesh", mesh);

        meshData.name = "MeshData";
        ctx.AddObjectToAsset("MeshData", meshData);

        // Add the main game object
        GameObject go = new GameObject();        
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();

        MeshFilter meshFilter = go.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        HalfedgeMeshRendererData renderData = go.AddComponent<HalfedgeMeshRendererData>();
        renderData.MeshData = meshData;
        renderData.HalfedgeMesh = mesh;

        ctx.AddObjectToAsset("GameObject", go);

        ctx.SetMainObject(go);
    }
}
