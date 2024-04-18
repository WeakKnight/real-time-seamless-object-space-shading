using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

namespace GPUScene
{
    public class BVH
    {
        List<AABB> primitiveAABBs;

        public NativeArray<AABB> nodeAABBs;

        int currentPosition;

        /*
        Non-Leaf
        Left Index
        Right Index
        Leaf
        Leaf Bit | Start
        Count
        */
        public NativeArray<uint2> nodes;

        public NativeArray<uint> primitives;

        // 1: Leaf
        const uint NodeLeafBit = 0x80000000;
        const uint NodeValueMask = 0x7FFFFFFF;

        public ComputeBuffer primitiveBuffer;
        public ComputeBuffer nodeBuffer;
        public ComputeBuffer aabbBuffer;

        public BVH(int capacity = 4096)
        {
            currentPosition = 0;
            nodeAABBs = new NativeArray<AABB>(capacity, Allocator.Persistent);
            nodes = new NativeArray<uint2>(capacity, Allocator.Persistent);
            nodes[0] = new uint2(~0u);
        }

        public void Build(List<AABB> aabbs)
        {
            currentPosition = 0;
            primitiveAABBs = aabbs;

            AABB rootAABB = AABB.Empty();
            foreach (AABB aabb in aabbs)
            {
                rootAABB.Encapsulate(aabb);
            }

            NativeArray<uint2> mortenPairs = new NativeArray<uint2>(aabbs.Count, Allocator.Temp);
            for (int i = 0; i < aabbs.Count; i++)
            {
                mortenPairs[i] = new uint2((uint)i, MortonCode(aabbs[i].center, rootAABB));
            }
            MortonPairComparer mortonPairComparer = new MortonPairComparer();
            mortenPairs.Sort(mortonPairComparer);

            primitives = new NativeArray<uint>(aabbs.Count, Allocator.Persistent);
            for (int i = 0; i < mortenPairs.Length; i++)
            {
                primitives[i] = mortenPairs[i].x;
            }

            ProcessNode(new uint2(0, (uint)mortenPairs.Length), rootAABB, aabbs);

            if (primitiveBuffer == null || primitiveBuffer.count < aabbs.Count)
            {
                primitiveBuffer?.Release();
                primitiveBuffer = new ComputeBuffer(math.max(1024, aabbs.Count * 2), 4 * 1, ComputeBufferType.Structured);
            }
            primitiveBuffer.SetData(primitives, 0, 0, aabbs.Count);

            if (nodeBuffer == null || nodeBuffer.count < currentPosition)
            {
                nodeBuffer?.Release();
                nodeBuffer = new ComputeBuffer(math.max(1024, currentPosition * 2), 4 * 2, ComputeBufferType.Structured);
            }
            nodeBuffer.SetData(nodes, 0, 0, currentPosition);

            if (aabbBuffer == null || aabbBuffer.count < currentPosition)
            {
                aabbBuffer?.Release();
                aabbBuffer = new ComputeBuffer(math.max(1024, currentPosition * 2), 4 * 6, ComputeBufferType.Raw);
            }
            aabbBuffer.SetData(nodeAABBs, 0, 0, currentPosition);
        }

        float LeafCost(uint count)
        {
            return count;
        }

        float InternalCost(uint leftCount, AABB leftAABB, uint rightCount, AABB rightAABB, AABB parentAABB)
        {
            float left = leftAABB.area / parentAABB.area;
            float right = rightAABB.area / parentAABB.area;
            return 1.0f + leftCount * left + rightCount * right;
        }

        int ProcessNode(uint2 range, AABB aabb, List<AABB> primitiveAABBs)
        {
            int nodeIndex = AllocateAABB(aabb);
            float costAsLeafNode = LeafCost(range.y);

            uint2 node = new();

            float costAsInternalNode = float.PositiveInfinity;
            uint leftLengthOp = 0;
            AABB leftBoundsOp = AABB.Empty();
            AABB rightBoundsOp = AABB.Empty();

            // Surface Area Heuristic
            uint stepSize = 1u;
            if (range.y > 20)
            {
                stepSize = range.y / 10;
            }

            for (uint leftLength = 0; leftLength <= range.y; leftLength += stepSize)
            {
                leftLength = math.clamp(leftLength, 0, range.y);
                uint rightLength = range.y - leftLength;

                uint2 leftRange = new uint2(range.x, leftLength);
                uint2 rightRange = new uint2(range.x + leftLength, rightLength);

                AABB leftBounds = AABB.Empty();
                NativeSlice<uint> leftChildren = primitives.Slice((int)leftRange.x, (int)leftRange.y);
                foreach (var primitiveIndex in leftChildren)
                {
                    leftBounds.Encapsulate(primitiveAABBs[(int)primitiveIndex]);
                }

                AABB rightBounds = AABB.Empty();
                NativeSlice<uint> rightChildren = primitives.Slice((int)rightRange.x, (int)rightRange.y);
                foreach (var primitiveIndex in rightChildren)
                {
                    rightBounds.Encapsulate(primitiveAABBs[(int)primitiveIndex]);
                }

                float cost = InternalCost(leftLength, leftBounds, rightLength, rightBounds, aabb);
                if (costAsInternalNode > cost)
                {
                    costAsInternalNode = cost;
                    leftLengthOp = leftLength;
                    leftBoundsOp = leftBounds;
                    rightBoundsOp = rightBounds;
                }
            }

            if (costAsLeafNode < costAsInternalNode)
            {
                node.x = NodeLeafBit | range.x;
                node.y = range.y;
            }
            else
            {
                uint rightLengthOp = range.y - leftLengthOp;
                uint2 leftRangeOp = new uint2(range.x, leftLengthOp);
                uint2 rightRangeOp = new uint2(range.x + leftLengthOp, rightLengthOp);

                if (leftRangeOp.y == 0)
                {
                    node.x = NodeValueMask;
                }
                else
                {
                    node.x = (uint)ProcessNode(leftRangeOp, leftBoundsOp, primitiveAABBs);
                }

                if (rightRangeOp.y == 0)
                {
                    node.y = NodeValueMask;
                }
                else
                {
                    node.y = (uint)ProcessNode(rightRangeOp, rightBoundsOp, primitiveAABBs);
                }
            }

            nodes[nodeIndex] = node;

            return nodeIndex;
        }

        public int AllocateAABB(AABB bounds)
        {
            int result = currentPosition;
            nodeAABBs[currentPosition] = bounds;
            currentPosition++;
            return result;
        }

        public void Release()
        {
            nodeAABBs.Dispose();
            nodes.Dispose();
            primitives.Dispose();

            primitiveBuffer?.Release();
            nodeBuffer?.Release();
            aabbBuffer?.Release();
        }

        uint3 BitExpansion(uint3 x)
        {
            //https://fgiesen.wordpress.com/2009/12/13/decoding-morton-codes/
            x = (x | x << 16) & 0x30000ff;
            x = (x | x << 8) & 0x300f00f;
            x = (x | x << 4) & 0x30c30c3;
            x = (x | x << 2) & 0x9249249;
            return x;
        }

        uint MortonCode(float3 pos, AABB aabb)
        {
            float3 normPos = ((pos - new float3(aabb.min)) / aabb.size);
            uint3 quanPos = new uint3((uint)(1024 * normPos.x), (uint)(1024 * normPos.y), (uint)(1024 * normPos.z));
            quanPos = BitExpansion(quanPos);
            uint morton = quanPos.x * 4 + quanPos.y * 2 + quanPos.z;
            return morton;
        }

        public struct MortonPairComparer : IComparer<uint2>
        {
            public int Compare(uint2 a, uint2 b)
            {
                return a.y.CompareTo(b.y);
            }
        }

        float3 RandomColor(uint uniqueIndex)
        {
            var rng = Unity.Mathematics.Random.CreateFromIndex(uniqueIndex);
            float r = rng.NextFloat();

            float h = r * 360.0f;
            float s = 0.4f;
            float l = 0.5f;

            // from HSL to RGB
            float c = (1.0f - math.abs(2.0f * l - 1.0f)) * s;
            float x = c * (1.0f - math.abs(math.fmod((h / 60.0f), 2.0f) - 1.0f));

            float3 rgb;

            if (h >= 0.0f && h < 60.0f)
                rgb = new float3(c, x, 0.0f);
            else if (h >= 60.0f && h < 120.0f)
                rgb = new float3(x, c, 0.0f);
            else if (h >= 120.0f && h < 180.0f)
                rgb = new float3(0.0f, c, x);
            else if (h >= 180.0f && h < 240.0f)
                rgb = new float3(0.0f, x, c);
            else if (h >= 240.0f && h < 300.0f)
                rgb = new float3(x, 0.0f, c);
            else
                rgb = new float3(c, 0.0f, x);

            return rgb;
        }

        public void OnGizmos()
        {
            if (nodes == null)
            {
                return;
            }

            if (math.all(nodes[0] == ~0u))
            {
                return;
            }

            if (currentPosition > 0)
            {
                uint2 rootNode = nodes[0];
                AABB rootAABB = nodeAABBs[0];
                float3 color = RandomColor(0);
                Gizmos.color = new Color(color.x, color.y, color.z, math.saturate(0.5f));
                Gizmos.DrawCube(rootAABB.center, rootAABB.size);

                uint depth = 0;
                OnGizmosNode(rootNode, depth);
            }
        }

        void OnGizmosNode(uint2 node, uint depth)
        {
            if (node.x == ~0u)
            {
                return;
            }

            Gizmos.matrix = Matrix4x4.identity;

            bool isLeaf = (node.x & NodeLeafBit) != 0u;
            if (isLeaf)
            {
                Gizmos.color = new Color(0.0f, 0.4f, 0.8f, 0.9f);

                uint start = node.x & NodeValueMask;
                uint count = node.y;

                for (int i = 0; i < count; i++)
                {
                    uint instanceIndex = primitives[(int)start + i];
                    AABB aabb = primitiveAABBs[(int)instanceIndex];
                    Gizmos.DrawCube(aabb.center, aabb.size);
                }
            }
            else
            {
                uint leftNodeIndex = node.x;
                if (leftNodeIndex != NodeValueMask)
                {
                    uint2 leftNode = nodes[(int)leftNodeIndex];
                    AABB leftAABB = nodeAABBs[(int)leftNodeIndex];
                    float3 color = RandomColor(leftNodeIndex);
                    Gizmos.color = new Color(color.x, color.y, color.z, math.saturate(0.6f + depth * 0.1f));
                    Gizmos.DrawCube(leftAABB.center, leftAABB.size);
                    OnGizmosNode(leftNode, depth + 1);
                }

                uint rightNodeIndex = node.y;
                if (rightNodeIndex != NodeValueMask)
                {
                    uint2 rightNode = nodes[(int)rightNodeIndex];
                    AABB rightAABB = nodeAABBs[((int)rightNodeIndex)];
                    float3 color = RandomColor(rightNodeIndex);
                    Gizmos.color = new Color(color.x, color.y, color.z, math.saturate(0.6f + depth * 0.1f));
                    Gizmos.DrawCube(rightAABB.center, rightAABB.size);
                    OnGizmosNode(rightNode, depth + 1);
                }
            }
        }
    }
}