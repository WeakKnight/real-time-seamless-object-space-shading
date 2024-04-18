using System;
using Unity.Collections;
using UnityEngine;

namespace GPUScene
{
    public class DistanceField : ScriptableObject
    {
        public const float VoxelDensity = 40.0f;
        public const int BrickSize = 8;
        public const int UniqueBrickSize = BrickSize - 1;
        public const int AssetDataByteSize = 36;

        public Mesh mesh;
        public int assetIndex;
        public int brickOffset; // base brick index in Brick Texture
        public Vector3Int resolutionInBricks;
        public Vector2 scaleBias;
        public Vector3 volumeExtent;

        public void WriteToUIntArray(ref NativeArray<uint> dstArr, int elementIndex)
        {
            //! TODO: Pack ResolutonInBricks into R11G11B10
            int startIndex = elementIndex * (AssetDataByteSize / 4);
            dstArr[startIndex + 0] = (uint)brickOffset;
            dstArr[startIndex + 1] = (uint)resolutionInBricks.x;
            dstArr[startIndex + 2] = (uint)resolutionInBricks.y;
            dstArr[startIndex + 3] = (uint)resolutionInBricks.z;
            dstArr[startIndex + 4] = BitConverter.ToUInt32(BitConverter.GetBytes(scaleBias.x));
            dstArr[startIndex + 5] = BitConverter.ToUInt32(BitConverter.GetBytes(scaleBias.y));
            dstArr[startIndex + 6] = BitConverter.ToUInt32(BitConverter.GetBytes(volumeExtent.x));
            dstArr[startIndex + 7] = BitConverter.ToUInt32(BitConverter.GetBytes(volumeExtent.y));
            dstArr[startIndex + 8] = BitConverter.ToUInt32(BitConverter.GetBytes(volumeExtent.z));
        }
    }
}
