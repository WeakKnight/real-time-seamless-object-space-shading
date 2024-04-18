using UnityEditor;
using UnityEngine;

namespace GPUScene
{
    [CustomEditor(typeof(GPUSceneManager))]
    public class GPUSceneManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GPUSceneManager mgr = (GPUSceneManager)target;

            if (GUILayout.Button("Build"))
            {
                mgr.Build();
            }

            var baseColortex = mgr?.MeshCardPool?.MeshCardAtlasTextureBaseColor;
            if (baseColortex != null)
            {
                GUI.DrawTexture(GUILayoutUtility.GetAspectRect(baseColortex.width / (float)baseColortex.height), baseColortex);
            }

            var emissiveTex = mgr?.MeshCardPool?.MeshCardAtlasTextureEmissive;
            if (emissiveTex != null)
            {
                GUI.DrawTexture(GUILayoutUtility.GetAspectRect(emissiveTex.width / (float)emissiveTex.height), emissiveTex);
            }
        }
    }
}