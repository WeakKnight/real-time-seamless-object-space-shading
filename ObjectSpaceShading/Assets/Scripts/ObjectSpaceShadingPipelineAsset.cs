using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/ObjectSpaceShadingPipelineAsset")]
public class ObjectSpaceShadingPipelineAsset : RenderPipelineAsset<ObjectSpaceShadingPipeline>
{
    protected override RenderPipeline CreatePipeline()
    {
        return new ObjectSpaceShadingPipeline();
    }
}
