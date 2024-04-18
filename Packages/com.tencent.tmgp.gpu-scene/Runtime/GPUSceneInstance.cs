using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    [ExecuteInEditMode, RequireComponent(typeof(MeshFilter))]
    public class GPUSceneInstance : MonoBehaviour
    {
        [Range(0.05f, 8.0f)]
        public float resolutionScale = 1.0f;
        public bool doubleSided = false;

        public int instanceIndex
        {
            get
            {
                return _instanceIndex;
            }

            set 
            {
                if (_instanceIndex != value)
                {
                    _instanceIndex = value;
                    var mr = GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var block = new MaterialPropertyBlock();
                        mr.GetPropertyBlock(block);
                        block.SetInt("_InstanceIndex", value);
                        mr.SetPropertyBlock(block);
                    }
                }
            }
        }
        
        private int _instanceIndex = -1;

        private MeshFilter meshFilter;

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>();

            transform.hasChanged = true;

            GPUSceneManager mgr = FindAnyObjectByType<GPUSceneManager>();
            if (mgr != null && mgr.MeshCardPool != null)
            {
                mgr.MeshCardPool.dirty = true;
            }
        }

        private void Update()
        {
            if (transform.hasChanged)
            {
                GPUSceneManager mgr = FindAnyObjectByType<GPUSceneManager>();
                if (mgr != null)
                {
                    mgr.UpdateInstanceTransform(instanceIndex, transform, meshFilter.sharedMesh, resolutionScale);
                }

                transform.hasChanged = false;
            }
        }

        public void OnDisable()
        {
            GPUSceneManager mgr = FindAnyObjectByType<GPUSceneManager>();
            if (mgr != null)
            {
                mgr.UpdateInstanceTransform(instanceIndex, transform, meshFilter.sharedMesh, 0.0f);
            }
        }

        public Matrix4x4 GetWorldToVolume()
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return Matrix4x4.identity;
            }

            float3 posOffset = meshFilter.sharedMesh.DistanceFieldPositionOffset(resolutionScale);
            Matrix4x4 worldToLocal = Matrix4x4.Translate(-posOffset) * transform.worldToLocalMatrix;
            return worldToLocal;
        }
    }
}
