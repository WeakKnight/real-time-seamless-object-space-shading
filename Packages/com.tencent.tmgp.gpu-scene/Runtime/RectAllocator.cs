using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace GPUScene
{
    public class RectAllocator
    {
        public const uint INDEX_NONE = ~0u;

        public RectAllocator(uint tileSize, uint borderSize, uint dimInTiles)
        {
            TileSize = tileSize;
            BorderSize = borderSize;
            TileSizeWithBorder = tileSize + 2 * borderSize;
            DimInTiles = dimInTiles;
            DimInTexels = DimInTiles * TileSizeWithBorder;

            TexelSize = 1.0f / DimInTexels;

            LevelOffsets = new();
            SubAllocInfos = new();

            uint NumBits = 0;
            for (uint Level = 1; Level <= DimInTiles; Level <<= 1)
            {
                uint NumQuadsInLevel = Level * Level;
                LevelOffsets.Add(NumBits);
                NumBits += NumQuadsInLevel;
            }

            MarkerQuadTree = new BitArray();
            MarkerQuadTree.Add(false, NumBits);
        }

        public uint Alloc(uint SizeX, uint SizeY)
        {
            static uint DivideRoundUp(uint numerator, uint denominator)
            {
                return System.Math.Max(0, (numerator + denominator - 1) / denominator);
            }

            static uint ReverseMortonCode2(uint x)
            {
                x &= 0x55555555;
                x = (x ^ (x >> 1)) & 0x33333333;
                x = (x ^ (x >> 2)) & 0x0f0f0f0f;
                x = (x ^ (x >> 4)) & 0x00ff00ff;
                x = (x ^ (x >> 8)) & 0x0000ffff;
                return x;
            }

            uint NumTiles1D = DivideRoundUp(math.max(SizeX, SizeY), TileSize);
            uint NumLevels = (uint)LevelOffsets.Count;
            uint Level = NumLevels - (uint)math.ceillog2(NumTiles1D) - 1u;
            uint LevelOffet = LevelOffsets[(int)Level];
            uint QuadsInLevel1D = 1u << (int)Level;
            uint SearchEnd = LevelOffet + QuadsInLevel1D * QuadsInLevel1D;

            uint QuadIdx = LevelOffet;
            for (; QuadIdx < SearchEnd; ++QuadIdx)
            {
                if (!MarkerQuadTree[QuadIdx])
                {
                    break;
                }
            }

            if (QuadIdx != SearchEnd)
            {
                uint QuadIdxInLevel = QuadIdx - LevelOffet;

                uint ParentLevel = Level;
                uint ParentQuadIdxInLevel = QuadIdxInLevel;
                for (; ParentLevel != ~0u; --ParentLevel)
                {
                    uint ParentLevelOffset = LevelOffsets[(int)ParentLevel];
                    uint ParentQuadIdx = ParentLevelOffset + ParentQuadIdxInLevel;
                    if (MarkerQuadTree[ParentQuadIdx])
                    {
                        break;
                    }
                    MarkerQuadTree[ParentQuadIdx] = true;
                    ParentQuadIdxInLevel >>= 2;
                }

                uint ChildLevel = Level + 1;
                uint ChildQuadIdxInLevel = QuadIdxInLevel << 2;
                uint NumChildren = 4;
                for (; ChildLevel < NumLevels; ++ChildLevel)
                {
                    uint ChildQuadIdx = ChildQuadIdxInLevel + LevelOffsets[(int)ChildLevel];
                    for (uint Idx = 0; Idx < NumChildren; ++Idx)
                    {
                        MarkerQuadTree[ChildQuadIdx + Idx] = true;
                    }
                    ChildQuadIdxInLevel <<= 2;
                    NumChildren <<= 2;
                }

                uint QuadX = ReverseMortonCode2(QuadIdxInLevel);
                uint QuadY = ReverseMortonCode2(QuadIdxInLevel >> 1);
                uint QuadSizeInTiles1D = DimInTiles >> (int)Level;
                uint TileX = QuadX * QuadSizeInTiles1D;
                uint TileY = QuadY * QuadSizeInTiles1D;

                SubAllocInfo subAllocInfo = new();
                subAllocInfo.Level = Level;
                subAllocInfo.QuadIdx = QuadIdx;
                subAllocInfo.UVScaleBias = new float4(SizeX * TexelSize, SizeY * TexelSize,
                    TileX / (float)DimInTiles + BorderSize * TexelSize, TileY / (float)DimInTiles + BorderSize * TexelSize);

                return SubAllocInfos.Add(subAllocInfo);
            }

            return INDEX_NONE;
        }

        public void Free(uint handle)
        {
            SubAllocInfo subAllocInfo = SubAllocInfos.GetData(handle);
            SubAllocInfos.Free(handle);

            uint Level = subAllocInfo.Level;
            uint QuadIdx = subAllocInfo.QuadIdx;

            uint ChildLevel = Level;
            uint ChildIdxInLevel = QuadIdx - LevelOffsets[(int)Level];
            uint NumChildren = 1;
            uint NumLevels = (uint)LevelOffsets.Count;
            for (; ChildLevel < NumLevels; ++ChildLevel)
            {
                uint ChildIdx = ChildIdxInLevel + LevelOffsets[(int)ChildLevel];
                for (uint Idx = 0; Idx < NumChildren; ++Idx)
                {
                    MarkerQuadTree[ChildIdx + Idx] = false;
                }
                ChildIdxInLevel <<= 2;
                NumChildren <<= 2;
            }

            uint TestIdxInLevel = (QuadIdx - LevelOffsets[(int)Level]) & ~3u;
            uint ParentLevel = Level - 1;
            for (; ParentLevel != ~0u; --ParentLevel)
            {
                uint TestIdx = TestIdxInLevel + LevelOffsets[(int)ParentLevel + 1];
                bool bParentFree = !MarkerQuadTree[TestIdx] && !MarkerQuadTree[TestIdx + 1] && !MarkerQuadTree[TestIdx + 2] && !MarkerQuadTree[TestIdx + 3];
                if (!bParentFree)
                {
                    break;
                }
                uint ParentIdxInLevel = TestIdxInLevel >> 2;
                uint ParentIdx = ParentIdxInLevel + LevelOffsets[(int)ParentLevel];
                MarkerQuadTree[ParentIdx] = false;
                TestIdxInLevel = ParentIdxInLevel & ~3u;
            }
        }

        public float4 GetScaleBias(uint Handle)
        {
            return SubAllocInfos.GetData(Handle).UVScaleBias;
        }

        public uint4 GetSizePosition(uint Handle)
        {
            Vector4 scaleBias = GetScaleBias(Handle);
            return new uint4((uint)(scaleBias.x / TexelSize), (uint)(scaleBias.y / TexelSize), (uint)(scaleBias.z / TexelSize), (uint)(scaleBias.w / TexelSize));
        }

        struct SubAllocInfo
        {
            public uint Level;
            public uint QuadIdx;
            public float4 UVScaleBias;
        };

        uint TileSize;
        uint BorderSize;
        uint TileSizeWithBorder;
        uint DimInTiles;
        uint DimInTexels;

        float TexelSize;

        // 0: Free, 1: Allocated
        BitArray MarkerQuadTree;
        List<uint> LevelOffsets;

        SparseArray<SubAllocInfo> SubAllocInfos;
    }
}