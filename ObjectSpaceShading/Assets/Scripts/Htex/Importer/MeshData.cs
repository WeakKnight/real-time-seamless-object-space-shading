using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Htex
{
    [Serializable]
    public class MeshData : ScriptableObject, IDisposable
    {
        private IntPtr _nativeMeshPtr;

        private bool _disposed = false;

        public int VertexCount;
        public int UvCount;
        public int HalfedgeCount;
        public int EdgeCount;
        public int FaceCount;

        // Properties to expose the mesh data in a managed way
        public int[] VertexToHalfedgeIDs;
        public int[] EdgeToHalfedgeIDs;
        public int[] FaceToHalfedgeIDs;
        public cc_VertexPoint[] VertexPoints;
        public cc_VertexUv[] Uvs;
        public cc_Halfedge[] Halfedges;
        public cc_Crease[] Creases;

        public Vector3[] HalfedgeNormals;

        public void LoadHalfedgeMesh(string filename)
        {
            _nativeMeshPtr = Bindings.Htex_loadMesh(filename);
            PrepreData(Marshal.PtrToStructure<cc_Mesh>(_nativeMeshPtr));
        }

        public void LoadHalfedgeMesh(IntPtr ccmeshPtr)
        {
            _nativeMeshPtr = IntPtr.Zero;
            PrepreData(Marshal.PtrToStructure<cc_Mesh>(ccmeshPtr));
        }

        private void PrepreData(cc_Mesh nativeMesh)
        {
            VertexCount = nativeMesh.vertexCount;
            UvCount = nativeMesh.uvCount;
            HalfedgeCount = nativeMesh.halfedgeCount;
            EdgeCount = nativeMesh.edgeCount;
            FaceCount = nativeMesh.faceCount;

            VertexToHalfedgeIDs = new int[nativeMesh.vertexCount];
            Marshal.Copy(nativeMesh.vertexToHalfedgeIDs, VertexToHalfedgeIDs, 0, nativeMesh.vertexCount);

            EdgeToHalfedgeIDs = new int[nativeMesh.edgeCount];
            Marshal.Copy(nativeMesh.edgeToHalfedgeIDs, EdgeToHalfedgeIDs, 0, nativeMesh.edgeCount);

            FaceToHalfedgeIDs = new int[nativeMesh.faceCount];
            Marshal.Copy(nativeMesh.faceToHalfedgeIDs, FaceToHalfedgeIDs, 0, nativeMesh.faceCount);

            VertexPoints = new cc_VertexPoint[nativeMesh.vertexCount];
            Parallel.For(0, nativeMesh.vertexCount, i =>
            {
                IntPtr ptr = IntPtr.Add(nativeMesh.vertexPoints, i * Marshal.SizeOf<cc_VertexPoint>());
                VertexPoints[i] = Marshal.PtrToStructure<cc_VertexPoint>(ptr);
            });

            Uvs = new cc_VertexUv[nativeMesh.uvCount];
            Parallel.For(0, nativeMesh.uvCount, i =>
            {
                IntPtr ptr = IntPtr.Add(nativeMesh.uvs, i * Marshal.SizeOf<cc_VertexUv>());
                Uvs[i] = Marshal.PtrToStructure<cc_VertexUv>(ptr);
            });

            Halfedges = new cc_Halfedge[nativeMesh.halfedgeCount];
            Parallel.For(0, nativeMesh.halfedgeCount, i =>
            {
                IntPtr ptr = IntPtr.Add(nativeMesh.halfedges, i * Marshal.SizeOf<cc_Halfedge>());
                Halfedges[i] = Marshal.PtrToStructure<cc_Halfedge>(ptr);
            });

            Creases = new cc_Crease[nativeMesh.edgeCount];
            Parallel.For(0, nativeMesh.edgeCount, i =>
            {
                IntPtr ptr = IntPtr.Add(nativeMesh.creases, i * Marshal.SizeOf<cc_Crease>());
                Creases[i] = Marshal.PtrToStructure<cc_Crease>(ptr);
            });

            HalfedgeNormals = new Vector3[nativeMesh.halfedgeCount];
            Parallel.For(0, nativeMesh.halfedgeCount, halfedgeID =>
            {
                Vector3 v0 = computeBarycenter(ccm_HalfedgeFaceID(halfedgeID));
                Vector3 v1 = ccm_HalfedgeVertexPoint(halfedgeID);
                Vector3 v2 = ccm_HalfedgeVertexPoint(ccm_HalfedgeNextID(halfedgeID));
                Vector3 v1_v0 = (v1 - v0).normalized;
                Vector3 v2_v0 = (v2 - v0).normalized;
                Vector3 n = Vector3.Cross(v1_v0, v2_v0).normalized;
                HalfedgeNormals[halfedgeID] = n;
            });
        }

        public cc_Halfedge ccm__Halfedge(int halfedgeID)
        {
            return Halfedges[halfedgeID];
        }

        int ccm_HalfedgeTwinID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).twinID;
        }

        int ccm_HalfedgePrevID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).prevID;
        }

        int ccm_VertexToHalfedgeID(int vertexID)
        {
            return VertexToHalfedgeIDs[vertexID];
        }

        public int ccm_FaceToHalfedgeID(int faceID)
        {
            return FaceToHalfedgeIDs[faceID];
        }

        public int ccm_HalfedgeFaceID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).faceID;
        }

        public int ccm_HalfedgeVertexID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).vertexID;
        }

        public int ccm_HalfedgeUvID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).uvID;
        }

        public int ccm_HalfedgeNextID(int halfedgeID)
        {
            return ccm__Halfedge(halfedgeID).nextID;
        }

        public Vector3 ccm_VertexPoint(int vertexID)
        {
            return VertexPoints[vertexID];
        }

        public Vector2 ccm_Uv(int uvID)
        {
            return Uvs[uvID];
        }

        public Vector3 ccm_HalfedgeVertexPoint(int halfedgeID)
        {
            return ccm_VertexPoint(ccm_HalfedgeVertexID(halfedgeID));
        }

        public Vector2 ccm_HalfedgeVertexUv(int halfedgeID)
        {
            return ccm_Uv(ccm_HalfedgeUvID(halfedgeID));
        }

        public Vector3 computeVertexNormal(int vertexID)
        {
            int startHalfedge = ccm_VertexToHalfedgeID(vertexID);
            Vector3 n = Vector3.zero;
            int currentHEdge = startHalfedge;
            do {
                n += HalfedgeNormals[currentHEdge];
                currentHEdge = ccm_HalfedgeTwinID(currentHEdge);
                if (currentHEdge < 0) break;
                currentHEdge = ccm_HalfedgeNextID(currentHEdge);
            } while (currentHEdge != startHalfedge);

            // boundary, we do a backwards traversal
            if (currentHEdge != startHalfedge) {
                currentHEdge = ccm_HalfedgeTwinID(ccm_HalfedgePrevID(startHalfedge));
                while (currentHEdge >= 0) {
                    n += HalfedgeNormals[currentHEdge];
                    currentHEdge = ccm_HalfedgeTwinID(ccm_HalfedgePrevID(currentHEdge));
                }
            }

            return n.normalized;
        }

        public Vector2 computeVertexUV(int vertexID)
        {
            return ccm_HalfedgeVertexUv(ccm_VertexToHalfedgeID(vertexID));
        }

        public Vector3 computeFacePointNormal(int faceID)
        {
            int startHalfedge = ccm_FaceToHalfedgeID(faceID);
            Vector3 n = Vector3.zero;
            int currentHEdge = startHalfedge;
            do {
                n += computeVertexNormal(ccm_HalfedgeVertexID(currentHEdge));
                currentHEdge = ccm_HalfedgeNextID(currentHEdge);
            } while(currentHEdge != startHalfedge);
            return n.normalized;
        }

        public Vector2 computeFacePointUV(int faceID)
        {
            int startHalfedge = ccm_FaceToHalfedgeID(faceID);
            int n = 0;
            Vector2 uv = Vector2.zero;

            int halfedgeID = startHalfedge;
            do {
                Vector2 vertexUV = ccm_HalfedgeVertexUv(halfedgeID);
                uv += vertexUV;
                n++;

                halfedgeID = ccm_HalfedgeNextID(halfedgeID);
            } while (halfedgeID != startHalfedge);

            uv /= n;
            return uv;
        }

        public Vector3 computeBarycenter(int faceID)
        {
            int startHalfedge = ccm_FaceToHalfedgeID(faceID);
            Vector3 barycenter = Vector3.zero;
            int numVertices = 0;
            {
                int currentHEdge = startHalfedge;
                do {
                    Vector3 v = ccm_HalfedgeVertexPoint(currentHEdge);
                    barycenter += v;
                    numVertices += 1;
                    currentHEdge = ccm_HalfedgeNextID(currentHEdge);
                } while (currentHEdge != startHalfedge);
            }
            barycenter /= numVertices;

            return barycenter;
        }

        // Implement IDisposable to release unmanaged resources
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources if any
                }

                // Release unmanaged resources
                if (_nativeMeshPtr != IntPtr.Zero)
                {
                    Bindings.Htex_releaseMesh(_nativeMeshPtr);
                }

                _disposed = true;
            }
        }

        ~MeshData()
        {
            Dispose(false);
        }
    }
}
