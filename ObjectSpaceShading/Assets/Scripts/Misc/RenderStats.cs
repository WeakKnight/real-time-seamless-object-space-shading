using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera)), ExecuteInEditMode]
public class RenderStats : MonoBehaviour
{
    GUIStyle style;

    public bool toggleStats = false;
    public bool shadelVis = false;
    public bool enablePersistentLayer = true;
    public bool enableShadingIntervalVis = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.D))
        {
            toggleStats = !toggleStats;
        }

        if (Input.GetKeyDown(KeyCode.V)) 
        {
            shadelVis = !shadelVis;
        }

        if (RenderPipelineManager.currentPipeline is ObjectSpaceShadingPipeline pipeline)
        {
            pipeline.enableAtlasVisualization = shadelVis;
            pipeline.enableShadingIntervalVisualization = enableShadingIntervalVis;

            pipeline.enablePersistentLayer = enablePersistentLayer;
        }
    }

    private void OnGUI()
    {
        if (!toggleStats)
        {
            return;
        }

        if (style == null)
        {
            style = new GUIStyle();
            style.normal.textColor = 1.4f * Color.gray;
        }
        if (RenderPipelineManager.currentPipeline is ObjectSpaceShadingPipeline pipeline)
        {
            var camera = GetComponent<Camera>();
            var cameraContext = pipeline.cameraContexts[camera];
            if (cameraContext != null)
            {
                cameraContext.debug = true;

                float w = camera.pixelWidth;
                float h = camera.pixelHeight;

                float verticalPos = h;

                float scale = 0.5f;
                if (cameraContext.RemapBufferVisTexture.width * scale > (w * 0.5f))
                {
                    scale = 0.2f;
                }

                float texWidth = cameraContext.RemapBufferVisTexture.width * scale;
                {

                    float texHeight = cameraContext.RemapBufferVisTexture.height * scale;

                    verticalPos -= texHeight;

                    GUI.DrawTexture(new Rect(w - texWidth, verticalPos, texWidth, texHeight), cameraContext.RemapBufferVisTexture);
                    GUI.Label(new Rect(w - texWidth, verticalPos, 128, 64), " Virtual Atlas");
                }

                {
                    verticalPos -= texWidth;
                    GUI.DrawTexture(new Rect(w - texWidth, verticalPos, texWidth, texWidth), cameraContext.virtualRenderTexture.StorageBuffer);
                    GUI.Label(new Rect(w - texWidth, verticalPos, 128, 64), " Sparse Memory");
                }

                {
                    float progress = cameraContext.shadelAllocator.allocatedShadelCount / (float)VirtualRenderTexture.ShadelCapacity;

                    float barHeight = 32.0f;
                    verticalPos -= barHeight;
                    GUI.DrawTexture(new Rect(w - texWidth, verticalPos, texWidth, barHeight), Texture2D.grayTexture);
                    GUI.DrawTexture(new Rect(w - texWidth, verticalPos, texWidth * progress, barHeight), Texture2D.whiteTexture);
                    
                    GUI.Label(new Rect(w - texWidth, verticalPos, 128, 64), " Shadel Usage", style);
                }
            }
        }
    }
}
