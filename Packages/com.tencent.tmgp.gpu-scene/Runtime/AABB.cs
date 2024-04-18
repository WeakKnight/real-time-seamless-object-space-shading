using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace GPUScene
{
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct AABB
    {
        public float minX;
        public float minY;
        public float minZ;
        public float maxX;
        public float maxY;
        public float maxZ;

        public static AABB Empty()
        {
            return new AABB(new float3(float.MaxValue), new float3(float.MinValue));
        }

        public AABB(float3 minPoint, float3 maxPoint)
        {
            minX = minPoint.x;
            minY = minPoint.y;
            minZ = minPoint.z;

            maxX = maxPoint.x;
            maxY = maxPoint.y;
            maxZ = maxPoint.z;
        }

        public AABB(Bounds bounds)
        {
            minX = bounds.min.x;
            minY = bounds.min.y;
            minZ = bounds.min.z;

            maxX = bounds.max.x;
            maxY = bounds.max.y;
            maxZ = bounds.max.z;
        }

        public void Encapsulate(AABB other)
        {
            min = math.min(min, other.min);
            max = math.max(max, other.max);
        }

        public void Encapsulate(float3 point)
        {
            min = math.min(min, point);
            max = math.max(max, point);
        }

        public void Expand(float val)
        {
            min = min - val;
            max = max + val;
        }

        public float3 min
        {
            get
            {
                return new float3(minX, minY, minZ);
            }
            set 
            {
                minX = value.x;
                minY = value.y;
                minZ = value.z;
            }
        }

        public float3 max
        {
            get
            {
                return new float3(maxX, maxY, maxZ);
            }
            set
            {
                maxX = value.x;
                maxY = value.y;
                maxZ = value.z;
            }
        }

        public float3 center
        {
            get 
            {
                return (min + max) * 0.5f;
            }
        }

        public float3 size
        {
            get 
            {
                return max - min;
            }
        }

        public float area 
        {
            get 
            {
                return 2.0f * (size.x * size.y + size.x * size.z + size.y * size.z);
            }
        }
    }
}