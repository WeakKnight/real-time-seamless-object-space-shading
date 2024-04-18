using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.VirtualTexturing;

public class CameraContextEditor : EditorWindow
{
    int selectedCameraIndex = 0;
    Vector2 scrollPosition = Vector2.zero;

    [MenuItem("Window/Decoupled Rendering/CameraContextEditor")]
    public static void ShowWindow()
    {
        GetWindow(typeof(CameraContextEditor));
    }

    void Update()
    {
        Repaint();
    }

    void OnGUI()
    {
        if (RenderPipelineManager.currentPipeline is ObjectSpaceShadingPipeline pipeline)
        {
            List<Camera> cameras = new();
            List<string> optionNames = new();
            foreach (var pair in pipeline.cameraContexts)
            {
                if (pair.Key)
                {
                    cameras.Add(pair.Key);
                    optionNames.Add(pair.Key.name);
                }
                pair.Value.debug = false;
            }

            if (optionNames.Count <= 0)
            {
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                selectedCameraIndex = EditorGUILayout.Popup("Target Camera", selectedCameraIndex, optionNames.ToArray());

                var cameraContext = pipeline.cameraContexts[cameras[selectedCameraIndex]];
                cameraContext.debug = true;

                if (cameraContext != null)
                {
                    if (cameraContext.debug)
                    {
                        if (cameraContext.renderTaskAllocator != null)
                        {
                            GUI.enabled = false;
                            EditorGUILayout.IntField("Task Counter", cameraContext.renderTaskAllocator.GetCounter());
                            GUI.enabled = true;
                        }

                        if (cameraContext.shadelAllocator != null)
                        {
                            EditorGUILayout.LabelField(cameraContext.shadelAllocator.allocatedShadelCount.ToString() + " / " + VirtualRenderTexture.ShadelCapacity);
                            EditorGUI.ProgressBar(GUILayoutUtility.GetAspectRect(15.0f), cameraContext.shadelAllocator.allocatedShadelCount / (float)VirtualRenderTexture.ShadelCapacity, "Shadel Allocation");
                        }

                        if (cameraContext.virtualRenderTexture != null)
                        {
                            if (cameraContext.virtualRenderTexture.StorageBuffer != null)
                            {
                                EditorGUILayout.LabelField("Storage Buffer");
                                GUI.DrawTexture(GUILayoutUtility.GetAspectRect(cameraContext.virtualRenderTexture.StorageBuffer.width / (float)cameraContext.virtualRenderTexture.StorageBuffer.height), cameraContext.virtualRenderTexture.StorageBuffer);
                            }
                        }

                        if (cameraContext.RemapBufferVisTexture != null)
                        {
                            EditorGUILayout.LabelField("Remap Buffer");
                            GUI.DrawTexture(GUILayoutUtility.GetAspectRect(cameraContext.RemapBufferVisTexture.width / (float)cameraContext.RemapBufferVisTexture.height), cameraContext.RemapBufferVisTexture);
                        }

                        if (cameraContext.HistoryRemapBufferVisTexture != null)
                        {
                            EditorGUILayout.LabelField("History Remap Buffer");
                            GUI.DrawTexture(GUILayoutUtility.GetAspectRect(cameraContext.HistoryRemapBufferVisTexture.width / (float)cameraContext.HistoryRemapBufferVisTexture.height), cameraContext.HistoryRemapBufferVisTexture);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
