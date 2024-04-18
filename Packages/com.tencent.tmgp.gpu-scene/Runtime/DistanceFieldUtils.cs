using System;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GPUScene
{
    public class DistanceFieldConfig
    {
        public Mesh mesh;
        public Vector3 boundingBoxMin;
        public int sampleNum;
        public Vector3Int resolution;
        public float cellSize;
        public Vector3 gridSize;
        public bool doubleSided;
    }

    public static class DistanceFieldUtils
    {
        public class DistanceFieldIntermediateResult
        {
            public NativeArray<byte> brickData;
            public float minDistance;
            public float maxDistance;
        }

        [BurstCompile]
        struct DoubleSideDistanceInit : IJobParallelFor
        {
            public uint3 Dimension;
            public float DiagonalLength;

            [WriteOnly]
            public NativeArray<float2> DoubleSideDistances;

            public void Execute(int index)
            {
                if (index >= (Dimension.x * Dimension.y * Dimension.z))
                {
                    return;
                }

                DoubleSideDistances[index] = 1.0f * new float2(DiagonalLength);
            }
        }

        [BurstCompile]
        struct PrepareRays : IJobParallelFor
        {
            public float CellSize;
            public uint SampleIndex;
            public float DiagonalLength;

            public float3 BoundingBoxMin;
            public uint3 Dimension;

            [WriteOnly]
            public NativeArray<CPURT.RayDesc> Rays;

            float2 Plastic(uint index)
            {
                const float p1 = 0.7548776662466927f;
                const float p2 = 0.5698402909980532f;
                float2 result;
                result.x = math.fmod(p1 * index, 1);
                result.y = math.fmod(p2 * index, 1);
                return result;
            }

            float3 SampleSphere(float2 u)
            {
                float phi = 2.0f * math.PI * u.y;
                float cosTheta = 1.0f - 2.0f * u.x;
                float sinTheta = math.sqrt(math.max(0.0f, 1.0f - cosTheta * cosTheta));
                return new float3(sinTheta * math.cos(phi), sinTheta * math.sin(phi), cosTheta);
            }

            float3 ComputePosW(uint3 pixel)
            {
                uint3 brickIndex = pixel / DistanceField.BrickSize;
                uint3 localCoord = pixel % DistanceField.BrickSize;
                float3 brickOffset = ((float3)brickIndex * DistanceField.UniqueBrickSize + 0.5f) * CellSize;
                float3 localOffset = (float3)localCoord * CellSize;
                return BoundingBoxMin.xyz + brickOffset + localOffset + 0.5f * CellSize;
            }

            uint3 Convert1DIndexTo3D(uint index1D, uint3 dimensions)
            {
                uint x = index1D % dimensions.x;
                uint y = (index1D / dimensions.x) % dimensions.y;
                uint z = index1D / (dimensions.x * dimensions.y);
                return new uint3(x, y, z);
            }

            public void Execute(int index)
            {
                if (index >= (Dimension.x * Dimension.y * Dimension.z))
                {
                    return;
                }

                uint3 pixel = Convert1DIndexTo3D((uint)index, Dimension);
                float3 posW = ComputePosW(pixel);
                float3 direction = SampleSphere(Plastic(SampleIndex));

                Rays[index] = new CPURT.RayDesc(posW, direction);
            }
        }

        class DispatchRays
        {
            public IntPtr RTContext;
            public NativeArray<CPURT.RayDesc> Rays;
            public NativeArray<CPURT.HitInfo> HitInfos;

            public void Execute()
            {
                CPURT.DispatchRays(RTContext, Rays, HitInfos);
            }
        }

        [BurstCompile]
        struct DoubleSideDistanceFinalize : IJobParallelFor
        {
            public uint3 Dimension;
            public bool DoubleSided;

            [ReadOnly]
            public NativeArray<CPURT.HitInfo> HitInfos;

            public NativeArray<float2> DoubleSideDistances;
            public NativeArray<uint2> BackfaceHits;

            public void Execute(int index)
            {
                if (index >= (Dimension.x * Dimension.y * Dimension.z))
                {
                    return;
                }

                CPURT.HitInfo hitInfo = HitInfos[index];
                if (hitInfo.hit == 1)
                {
                    uint backHitCount = BackfaceHits[index].x;
                    uint totalHitCount = BackfaceHits[index].y + 1;

                    float2 curDistance = DoubleSideDistances[index];

                    if ((hitInfo.hitBackFace == 1) && !DoubleSided)
                    {
                        backHitCount += 1;

                        DoubleSideDistances[index] = new float2(curDistance.x, math.min(hitInfo.t, curDistance.y));
                    }
                    else
                    {
                        DoubleSideDistances[index] = new float2(math.min(hitInfo.t, curDistance.x), curDistance.y);
                    }

                    BackfaceHits[index] = new uint2(backHitCount, totalHitCount);
                }
            }
        }

        [BurstCompile]
        struct DistanceFieldFinalize : IJobParallelFor
        {
            public uint3 Dimension;
            public float CellSize;

            [ReadOnly]
            public NativeArray<float2> DoubleSideDistances;
            [ReadOnly]
            public NativeArray<uint2> BackfaceHits;
            [WriteOnly]
            public NativeArray<float> Distances;

            float AddVirtualSurface(float dist)
            {
                float wrapValue = -4.0f * CellSize;
                float halfWrapValue = wrapValue * 0.5f;
                if (dist < halfWrapValue)
                {
                    return wrapValue - dist;
                }

                return dist;
            }

            public void Execute(int index)
            {
                if (index >= (Dimension.x * Dimension.y * Dimension.z))
                {
                    return;
                }

                float backfaceRate = (float)BackfaceHits[index].x / math.max((float)BackfaceHits[index].y, 1.0f);
                float dis;
                if (backfaceRate > 0.25f)
                {
                    dis = -DoubleSideDistances[index].y;
                }
                else
                {
                    dis = DoubleSideDistances[index].x;
                }

                dis = AddVirtualSurface(dis);

                Distances[index] = dis;
            }
        }

        public static DistanceFieldIntermediateResult GenerateDistanceField(DistanceFieldConfig sdfConfig)
        {
            int totalCellCount = sdfConfig.resolution.x * sdfConfig.resolution.y * sdfConfig.resolution.z;
            float diagonalLength = Vector3.Magnitude(sdfConfig.gridSize);
            uint3 dimension = new uint3((uint)sdfConfig.resolution.x, (uint)sdfConfig.resolution.y, (uint)sdfConfig.resolution.z);

            NativeArray<float> Distances = new NativeArray<float>(totalCellCount, Allocator.TempJob);
            NativeArray<float2> DoubleSideDistances = new NativeArray<float2>(totalCellCount, Allocator.TempJob);
            NativeArray<uint2> BackfaceHits = new NativeArray<uint2>(totalCellCount, Allocator.TempJob);
            NativeArray<CPURT.RayDesc> Rays = new NativeArray<CPURT.RayDesc>(totalCellCount, Allocator.TempJob);
            NativeArray<CPURT.HitInfo> HitInfos = new NativeArray<CPURT.HitInfo>(totalCellCount, Allocator.TempJob);

            DoubleSideDistanceInit doubleSideDistanceInit = new DoubleSideDistanceInit();
            doubleSideDistanceInit.DiagonalLength = diagonalLength;
            doubleSideDistanceInit.Dimension = dimension;
            doubleSideDistanceInit.DoubleSideDistances = DoubleSideDistances;

            PrepareRays prepareRays = new PrepareRays();
            prepareRays.DiagonalLength = diagonalLength;
            prepareRays.Dimension = dimension;
            prepareRays.BoundingBoxMin = sdfConfig.boundingBoxMin;
            prepareRays.CellSize = sdfConfig.cellSize;
            prepareRays.Rays = Rays;

            DispatchRays dispatchRays = new DispatchRays();

            dispatchRays.RTContext = CPURT.Init(sdfConfig.mesh);
            dispatchRays.Rays = Rays;
            dispatchRays.HitInfos = HitInfos;

            DoubleSideDistanceFinalize doubleSideDistanceFinalize = new DoubleSideDistanceFinalize();
            doubleSideDistanceFinalize.Dimension = dimension;
            doubleSideDistanceFinalize.DoubleSided = sdfConfig.doubleSided;
            doubleSideDistanceFinalize.DoubleSideDistances = DoubleSideDistances;
            doubleSideDistanceFinalize.BackfaceHits = BackfaceHits;
            doubleSideDistanceFinalize.HitInfos = HitInfos;

            DistanceFieldFinalize distanceFieldFinalize = new DistanceFieldFinalize();
            distanceFieldFinalize.Dimension = dimension;
            distanceFieldFinalize.CellSize = sdfConfig.cellSize;
            distanceFieldFinalize.DoubleSideDistances = DoubleSideDistances;
            distanceFieldFinalize.BackfaceHits = BackfaceHits;
            distanceFieldFinalize.Distances = Distances;

            {
                doubleSideDistanceInit.Schedule(totalCellCount, 64).Complete();

                for (uint sampleIndex = 0; sampleIndex < sdfConfig.sampleNum; sampleIndex++)
                {
                    prepareRays.SampleIndex = sampleIndex;
                    prepareRays.Schedule(totalCellCount, 64).Complete();

                    dispatchRays.Execute();

                    doubleSideDistanceFinalize.Schedule(totalCellCount, 64).Complete();
                }

                distanceFieldFinalize.Schedule(totalCellCount, 64).Complete();
            }

            CPURT.Release(dispatchRays.RTContext);

            float minVal = Mathf.Max(Distances.Min(), -diagonalLength);
            float maxVal = Mathf.Min(Distances.Max(), diagonalLength);

            NativeArray<byte> dfData = new NativeArray<byte>(totalCellCount, Allocator.TempJob);
            for (int i = 0; i < Distances.Length; i++)
            {
                float dis = Distances[i];
                // scale: maxVal - minVal, bias: minVal
                float normDis = (dis - minVal) / (maxVal - minVal);
                dfData[i] = (byte)System.Math.Truncate(normDis * 255.0f + 0.5f);
            }

            DistanceFieldIntermediateResult result = new DistanceFieldIntermediateResult
            {
                brickData = dfData,
                minDistance = minVal,
                maxDistance = maxVal
            };

            Distances.Dispose();
            DoubleSideDistances.Dispose();
            BackfaceHits.Dispose();
            Rays.Dispose();
            HitInfos.Dispose();

            return result;
        }
    }
}