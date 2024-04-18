Shader "General/Principled"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
        [Toggle] _HalfedgeMesh("Halfedge Mesh", Integer) = 0
        _MainTex("Base Color Texture", 2D) = "white" {}
        [Toggle] _MainTexUseHalfedgeTexture("Base Map Use Halfedge Texture", Integer) = 0
        _MainTex_Htex("Base Color Halfedge Texture", 2D) = "white" {}
        _MainTex_Htex_QuadSize("Base Color Htex Quas Size", Vector) = (0.0, 0.0, 0.0, 0.0)
        _MainTex_Htex_NumQuads("Base Color Htex Num Quads", Vector) = (0.0, 0.0, 0.0, 0.0)
        _BaseColor("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _EmissiveTex("Emissive", 2D) = "white" {}
        [HDR] _EmissiveColor("Emissive Color", Color) = (0.0, 0.0, 0.0, 1.0)
    }

    HLSLINCLUDE

    #pragma use_dxc
    #pragma require barycentrics
    #pragma enable_d3d11_debug_symbols

    #include "UnityCG.cginc"
    #include "Halfedge.hlsl"
    #include "RenderConfig.hlsl"
    #include "GlobalShaderVariables.hlsl"
    #include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/RandomSampler.cginc"

    uint _HalfedgeMesh;

    uint _MainTexUseHalfedgeTexture;
    Texture2D _MainTex_Htex;
    SamplerState sampler_MainTex_Htex;
    float2 _MainTex_Htex_QuadSize;
    float2 _MainTex_Htex_NumQuads;

    sampler2D _MainTex;
    float4 _BaseColor;
    sampler2D _EmissiveTex;
    float4 _EmissiveColor;

    int _FrameIndex;

    float4 sampleBaseMap(float2 uv0, uint primitiveIndex, float3 vBaryWeights, uint2 pixelCoord = 0)
    {
        if (_LightingOnly || _VisualizationMode > 0)
            return 0.33;

        if (_HalfedgeMesh && _MainTexUseHalfedgeTexture)
        {
            HalfedgeMeshTexture htex = HalfedgeMeshTexture_(_MainTex_Htex, sampler_MainTex_Htex, _MainTex_Htex_NumQuads, _MainTex_Htex_QuadSize);

#if 0
            pixelCoord = pixelCoord % 256;
            const uint seed = (pixelCoord.x + pixelCoord.y * 256);
            RandomSequence randomSequence;
            RandomSequence_Initialize(randomSequence, seed, _FrameIndex);

            float w = 0;
            float4 color = 0;

            {
                float2 dir = RandomSequence_GenerateSample2D(randomSequence) * 2 - 1;
                dir = normalize(dir);

                float d = RandomSequence_GenerateSample1D(randomSequence) * 1;

                // float2 dir = normalize(float2(1, 1));
                // float d = 7;
                float4 s = HtextureSpatialSample(htex, primitiveIndex, vBaryWeights.yz, dir, d);
                if (any(isnan(s)) || any(isinf(s)))
                {
                    return float4(1, 0, 1, 0);
                }
                color += s;
                w += 1;
            }

            color /= w;
            return color;
#endif

            return Htexture(htex, primitiveIndex, vBaryWeights.yz, 0);
        }
        else
        {
            return tex2D(_MainTex, uv0);
        }
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Tags
            {
                "LightMode" = "FinalGathering"
            }

            Cull [_Cull]
            ZWrite Off
            ZTest Equal

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "VirtualRenderTexture.cginc"
            #include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/RandomSampler.cginc"

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normalWS : NORMAL;
                float3 positionWS : POSITION;
                float4 vertex : SV_POSITION;
            };

            struct VertexData
            {
                float3 position : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normal : NORMAL;
            };

            v2f vert(VertexData vertex, in uint vertexId: SV_VertexID)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex.position);
                o.normalWS = UnityObjectToWorldNormal(vertex.normal);
                o.positionWS = mul(unity_ObjectToWorld, float4(vertex.position, 1.0f)).xyz;
                o.uv0 = vertex.uv0;
                o.uv1 = vertex.uv1;
            #if UNITY_UV_STARTS_AT_TOP
                o.uv1.y = 1.0f - o.uv1.y;
            #endif
                return o;
            }

            float4 frag(v2f i, uint primitiveIndex: SV_PrimitiveID, centroid float3 vBaryWeights: SV_Barycentrics) : SV_Target
            {
                if (_HalfedgeMesh)
                {
                    int halfedgeID = primitiveIndex;
                    i.uv0 = HalfedgeMesh_UV0(halfedgeID, vBaryWeights.yz);
                }

                int2 dimension = (int2)_DS_StartLocationAndDimension.zw;
                // return float4(lod / 10.0f, 1.0f);

                float4 irradiance = 0.0f;
                if (_HalfedgeMesh)
                {
                    float lod = ResolveTextureLod(vBaryWeights.yz);
                    irradiance = VRT_SampleHtexture(primitiveIndex, vBaryWeights.yz, lod);
                }
                else
                {
                    irradiance = VRT_ReadTextureViaLightmapUV(i.uv1);
                } 
                
                // float3 edgeIDVisColor = RandomColor(primitiveIndex);
                float4 baseColor = sampleBaseMap(i.uv0, primitiveIndex, vBaryWeights, i.vertex.xy);
                float3 emissive = _EmissiveColor.xyz * tex2Dlod(_EmissiveTex, float4(i.uv0, 0.0f, 0.0f)).xyz;
                if (_VisualizationMode > 0 || _LightingOnly)
                {
                    emissive = 0.0f;
                }
                else
                {
                    baseColor = baseColor * _BaseColor;
                }
                
                #if 1
                return float4(ToneMapAces((emissive + irradiance.xyz * baseColor.xyz) * _ExposureValue.x), 1.0f);
                #else
                return float4(baseColor.xyz, 1.0f);
                #endif
            }

            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "PreZ"
            }

            Cull [_Cull]
            ZWrite On

            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "RWVirtualRenderTexture.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct VertexData
            {
                float3 position : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            v2f vert(VertexData vertex)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex.position);
                o.uv0 = vertex.uv0;
                o.uv1 = vertex.uv1;
            #if UNITY_UV_STARTS_AT_TOP
                o.uv1.y = 1.0f - o.uv1.y;
            #endif
                return o;
            }

            [earlydepthstencil]
            fixed4 frag(v2f i, uint primitiveIndex: SV_PrimitiveID, centroid float3 vBaryWeights: SV_Barycentrics) : SV_Target
            {
                if (_HalfedgeMesh)
                {
                    int halfedgeID = primitiveIndex;
                    i.uv0 = HalfedgeMesh_UV0(halfedgeID, vBaryWeights.yz);

                    int2 dimension = (int2)_DS_StartLocationAndDimension.zw;
                    float3 lod = ResolveTextureLod(vBaryWeights.yz);

                    VRT_MarkShadel(halfedgeID, vBaryWeights.yz, lod);
                }
                else
                {
                    VRT_MarkShadelViaLightmapUV(i.uv1);
                }

                return 0.0f;
            }

            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "GBuffer"
            }

            Cull [_Cull]
            ZWrite Off
            ZTest Equal

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float3 normal : NORMAL;
                float4 posL : LOCALPOS;
                float4 lastFramePositionCS : LASTFRAMEPOSITIONCS;
            };

            struct VertexData
            {
                float4 position : POSITION;
                float2 uv0 : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct GBufferOutput
            {
                float4 posW : SV_Target0;
                float4 normalW : SV_Target1;
                float4 baseColor : SV_Target2;
                float2 motionVector : SV_Target3;
            };

            CBUFFER_START(UnityVelocityPass)
                float4x4 unity_MatrixPreviousM;
                float4x4 unity_MatrixPreviousMI;
                // X : Use last frame positions (right now skinned meshes are the only objects that use this
                // Y : Force No Motion
                // Z : Z bias value
                // W : Camera Motion Only
                float4 unity_MotionVectorsParams;
            CBUFFER_END

            float2 NdcToUv(float2 ndc)
            {
                return (ndc * 0.5 + 0.5);
            }

            float2 PerObjectMotionVector(float4 positionCS, float4 lastFramePositionCS)
            {
                float2 motionVector;
                bool forceNoMotion = (unity_MotionVectorsParams.y == 0.0f);
                if (forceNoMotion)
                {
                    motionVector = 0;
                }
                else
                {
                    // Remove jitter from motion vector
                    float2 cameraJitterOffset = 0;
                    float2 curUv = positionCS.xy / _ScreenParams.xy - cameraJitterOffset;
                    float2 prevUv = NdcToUv(lastFramePositionCS.xy / lastFramePositionCS.w);
                    motionVector = curUv - prevUv;
                }

                return motionVector;
            }

            v2f vert(VertexData vertex)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex.position);
                o.uv0 = vertex.uv0;
                o.normal = vertex.normal;
                o.posL = vertex.position;

                // TODO: support skinned mesh
                float3 lastFramePositionOS = vertex.position.xyz;
                float3 lastFramePositionWS = mul(unity_MatrixPreviousM, float4(lastFramePositionOS, 1)).xyz;
                o.lastFramePositionCS = mul(_PrevNonJitteredViewProjectionMatrix, float4(lastFramePositionWS, 1));

                return o;
            }

            GBufferOutput frag(v2f i, uint primitiveIndex: SV_PrimitiveID, centroid float3 vBaryWeights: SV_Barycentrics)
            {
                if (_HalfedgeMesh)
                {
                    int halfedgeID = primitiveIndex;
                    i.uv0 = HalfedgeMesh_UV0(halfedgeID, vBaryWeights.yz);
                }

                float4 baseColor = sampleBaseMap(i.uv0, primitiveIndex, vBaryWeights);
                float3 worldNormal = UnityObjectToWorldNormal(i.normal);

                if (!_LightingOnly)
                {
                    baseColor = baseColor * _BaseColor;
                }

                GBufferOutput gbuffer;
                gbuffer.posW = float4(mul(unity_ObjectToWorld, i.posL).xyz, 1.0f);
                gbuffer.normalW = float4(worldNormal.xyz, 1.0f);
                gbuffer.baseColor = baseColor;
                gbuffer.motionVector = PerObjectMotionVector(i.vertex, i.lastFramePositionCS);
                return gbuffer;
            }

            ENDHLSL
        }

        Pass
        {
            name "MeshCard"

            Tags
            {
                "LightMode" = "MeshCard"
            }
            
            Cull Off
            Blend Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/MeshCardRender.cginc"
            #include "Packages/com.tencent.tmgp.gpu-scene/Shaders/Resources/Packing.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;

                float2 uv0 : TEXCOORD0;
                float3 normal : NORMAL;
                float3 pos : LOCALPOS;
            };

            struct VertexData
            {
                float3 position : POSITION;
                float2 uv0 : TEXCOORD0;
                float3 normal : NORMAL;
            };

            v2f vert(VertexData vertex)
            {
                v2f o;
                o.vertex = MeshCardClipSpacePos(vertex.position.xyz, vertex.normal);
                o.uv0 = vertex.uv0;
                o.normal = vertex.normal;
                o.pos = vertex.position;
                return o;
            }

            MeshCardFragmentOutput frag(v2f i, uint primitiveIndex: SV_PrimitiveID, centroid float3 vBaryWeights: SV_Barycentrics) : SV_Target
            {
                if (_HalfedgeMesh)
                {
                    int halfedgeID = primitiveIndex;
                    i.uv0 = HalfedgeMesh_UV0(halfedgeID, vBaryWeights.yz);
                }

                MeshCardFragmentOutput output;
                output.baseColor = float4(_BaseColor.xyz * tex2Dlod(_MainTex, float4(i.uv0, 0.0f, 1.0f)).xyz, i.vertex.z);
                output.emissive = float4(_EmissiveColor.xyz * tex2Dlod(_EmissiveTex, float4(i.uv0, 0.0f, 0.0f)).xyz, 1.0f);

                float3 normal = normalize(mul(transpose((float3x3)_WorldToLocal), normalize(i.normal)).xyz);
                output.normal = encodeNormal2x8(normal);

                float3 pos = mul(_LocalToWorld, float4(i.pos, 1.0)).xyz;
                pos = mul(_WorldToVolume, float4(pos, 1.0)).xyz;
                float3 uvw = saturate((pos + _VolumeExtent.xyz * 0.5f) / _VolumeExtent.xyz);
                output.position = PackR10G11B11UnormToUINT(uvw);

                return output;
            }

            ENDHLSL
        }
    }
}
