using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Htex
{
    public enum MeshType
    {
        mt_triangle,
        mt_quad
    }

    public enum DataType
    {
        dt_uint8,
        dt_uint16,
        dt_half,
        dt_float
    }

    public enum BorderMode
    {
        m_clamp,
        m_black,
        m_periodic
    }

    public enum EdgeFilterMode
    {
        efm_none,
        efm_tanvec
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct cc_VertexPoint
    {
        public float x, y, z;

        public static implicit operator Vector3(cc_VertexPoint v) => new Vector3(v.x, v.y, v.z);
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct cc_VertexUv
    {
        public float u, v;

        public static implicit operator Vector2(cc_VertexUv v) => new Vector2(v.u, v.v);
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct cc_Halfedge
    {
        public int twinID;
        public int nextID;
        public int prevID;
        public int faceID;
        public int edgeID;
        public int vertexID;
        public int uvID;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct cc_Crease
    {
        public int nextID;
        public int prevID;
        public float sharpness;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct cc_Mesh
    {
        public int vertexCount;
        public int uvCount;
        public int halfedgeCount;
        public int edgeCount;
        public int faceCount;
        public IntPtr vertexToHalfedgeIDs;
        public IntPtr edgeToHalfedgeIDs;
        public IntPtr faceToHalfedgeIDs;
        public IntPtr vertexPoints;
        public IntPtr uvs;
        public IntPtr halfedges;
        public IntPtr creases;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Info
    {
        public MeshType meshType;
        public DataType dataType;
        public BorderMode uBorderMode;
        public BorderMode vBorderMode;
        public EdgeFilterMode edgeFilterMode;
        public int alphaChannel;
        public int numChannels;
        public int numFaces;
    }

    public static class Bindings
    {
        private const string DllName = "htex_c_api";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr HtexTexture_open(string filename);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void HtexTexture_release(IntPtr tex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern Info HtexTexture_getInfo(IntPtr tex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void HtexTexture_getQuadResolution(IntPtr tex, int quadID, ref int width, ref int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void HtexTexture_getData(IntPtr tex, int faceid, IntPtr buffer, int stride);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr HtexTexture_getHalfedgeMesh(IntPtr tex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Htex_loadMesh(string filename);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Htex_releaseMesh(IntPtr mesh);
    }
}
