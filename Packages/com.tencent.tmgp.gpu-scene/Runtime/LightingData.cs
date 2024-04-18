using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUScene
{
    public class LightingData
    {
        public float4 MainLightColor = new();
        public float4 MainLightDirection = new();

        public AnalyticalSky proceduralSky = new();

        static class Constants
        {
            public static int MainLightColor = Shader.PropertyToID("GPUScene_MainLightColor");
            public static int MainLightDirection = Shader.PropertyToID("GPUScene_MainLightDirection");
        }

        public void BindUniforms(CommandBuffer commandBuffer, ComputeShader shader)
        {
            commandBuffer.SetComputeVectorParam(shader, Constants.MainLightColor, MainLightColor);
            commandBuffer.SetComputeVectorParam(shader, Constants.MainLightDirection, MainLightDirection);
            proceduralSky.Bind(commandBuffer);
        }

        public void Update()
        {
            proceduralSky.Update(new float3(MainLightDirection.x, MainLightDirection.y, MainLightDirection.z));
        }
    };
}