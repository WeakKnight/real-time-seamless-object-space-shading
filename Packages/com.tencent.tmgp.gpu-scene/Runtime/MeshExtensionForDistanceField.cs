using Unity.Mathematics;
using UnityEngine;

namespace GPUScene
{
    public static class MeshExtensionForDistanceField
    {
        /*
            Brick Size          : 8
            Unique Brick Size   : 7
            Unique Brick        :|-|-|-|-|-|-|-|-|
            Brick #0            : * * * * * * * *
            Brick #1            :               * * * * * * * *
            Brick #2            :                             * * * * * * * *
            Brick #3            :                                           * * * * * * * *
            Brick #N Offset     : (N * (Unique Brick Size - 1) + 0.5f) * (Voxel Size)   
            e.g. #0             : 0.5  ¡Ì
                 #1             : 7.5  ¡Ì
                 #2             : 14.5 ¡Ì
        */

        public static float DistanceFieldVoxelNumPerUnit(this Mesh mesh, float resolutionScale = 1.0f)
        {
            return resolutionScale * DistanceField.VoxelDensity;
        }

        public static float DistanceFieldVoxelSize(this Mesh mesh, float resolutionScale = 1.0f)
        {
            return 1.0f / mesh.DistanceFieldVoxelNumPerUnit(resolutionScale);
        }

        public static Vector3 DistanceFieldBoundSize(this Mesh mesh, float resolutionScale = 1.0f)
        {
            float voxelSize = mesh.DistanceFieldVoxelSize(resolutionScale);
            return new Vector3(
                    Mathf.Max(0.01f, mesh.bounds.size.x + 2.0f * voxelSize), Mathf.Max(0.01f, mesh.bounds.size.y + 2.0f * voxelSize), Mathf.Max(0.01f, mesh.bounds.size.z + 2.0f * voxelSize));
        }

        public static Vector3Int DistanceFieldResolutionInBricks(this Mesh mesh, float resolutionScale = 1.0f)
        {
            float VoxelNumPerUnit = mesh.DistanceFieldVoxelNumPerUnit(resolutionScale);
            Vector3 BoundSize = mesh.DistanceFieldBoundSize(resolutionScale);
            Vector3Int result = new Vector3Int(Mathf.CeilToInt(BoundSize.x * VoxelNumPerUnit / (float)DistanceField.UniqueBrickSize),
                    Mathf.CeilToInt(BoundSize.y * VoxelNumPerUnit / (float)DistanceField.UniqueBrickSize),
                    Mathf.CeilToInt(BoundSize.z * VoxelNumPerUnit / (float)DistanceField.UniqueBrickSize));
            result.x = System.Math.Clamp(result.x, 1, 256);
            result.y = System.Math.Clamp(result.y, 1, 256);
            result.z = System.Math.Clamp(result.z, 1, 256);
            return result;
        }

        public static Vector3Int DistanceFieldResolution(this Mesh mesh, float resolutionScale = 1.0f)
        {
            Vector3Int ResolutionInBricks = mesh.DistanceFieldResolutionInBricks(resolutionScale);
            return DistanceField.BrickSize * ResolutionInBricks;
        }

        public static Vector3 DistanceFieldResolutionF(this Mesh mesh, float resolutionScale = 1.0f)
        {
            return mesh.DistanceFieldResolution(resolutionScale);
        }

        public static Vector3 DistanceFieldGridSize(this Mesh mesh, float resolutionScale = 1.0f)
        {
            Vector3 brickDim = mesh.DistanceFieldResolutionInBricks(resolutionScale);
            float voxelSize = mesh.DistanceFieldVoxelSize(resolutionScale);
            return (float)(DistanceField.UniqueBrickSize) * brickDim * voxelSize + Vector3.one * 1.0f * voxelSize;
        }

        public static Vector3 DistanceFieldBrickOffset(this Mesh mesh, Vector3 brickIndex, float resolutionScale = 1.0f)
        {
            float voxelSize = mesh.DistanceFieldVoxelSize(resolutionScale);
            return (brickIndex * (DistanceField.UniqueBrickSize) + Vector3.one * 0.5f) * voxelSize;
        }

        public static float3 DistanceFieldPositionOffset(this Mesh mesh, float resolutionScale = 1.0f)
        {
            Vector3 voxelSize = Vector3.one * mesh.DistanceFieldVoxelSize(resolutionScale);
            float3 posOffset = mesh.bounds.center + /*Why????*/ 1.0f * voxelSize;
            return posOffset;
        }
    }
}
