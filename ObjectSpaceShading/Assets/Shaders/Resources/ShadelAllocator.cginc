#ifndef CONCURRENT_ALLOCATOR_CGINC
#define CONCURRENT_ALLOCATOR_CGINC

static const uint sInvalidAllocation = ~0u;

int _ShadelCapacity;

RWStructuredBuffer<uint> _ConcurrentAllocatorCounterBufferRW;
StructuredBuffer<uint> _ConcurrentAllocatorCounterBuffer;
StructuredBuffer<uint> _HistoryConcurrentAllocatorCounterBuffer;

struct ShadelAllocator
{
    static uint Allocate(uint shadelCount)
    {
        if (shadelCount == 0)
        {
            return sInvalidAllocation;
        }

        if (shadelCount <= 8)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[0], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 16)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[1], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 24)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[2], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 32)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[3], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 40)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[4], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 48)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[5], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 56)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[6], 1u, offset);
            return offset;
        }
        else if (shadelCount <= 64)
        {
            uint offset;
            InterlockedAdd(_ConcurrentAllocatorCounterBufferRW[7], 1u, offset);
            return offset;
        }

        return sInvalidAllocation;
    }

    static void Clear()
    {
        _ConcurrentAllocatorCounterBufferRW[0] = 0u;
        _ConcurrentAllocatorCounterBufferRW[1] = 0u;
        _ConcurrentAllocatorCounterBufferRW[2] = 0u;
        _ConcurrentAllocatorCounterBufferRW[3] = 0u;
        _ConcurrentAllocatorCounterBufferRW[4] = 0u;
        _ConcurrentAllocatorCounterBufferRW[5] = 0u;
        _ConcurrentAllocatorCounterBufferRW[6] = 0u;
        _ConcurrentAllocatorCounterBufferRW[7] = 0u;
    }

    static bool IsOverflow()
    {
        uint allocatedShadelCount = 
           8 * _ConcurrentAllocatorCounterBufferRW[0] 
        + 16 * _ConcurrentAllocatorCounterBufferRW[1] 
        + 24 * _ConcurrentAllocatorCounterBufferRW[2] 
        + 32 * _ConcurrentAllocatorCounterBufferRW[3]
        + 40 * _ConcurrentAllocatorCounterBufferRW[4]
        + 48 * _ConcurrentAllocatorCounterBufferRW[5]
        + 56 * _ConcurrentAllocatorCounterBufferRW[6]
        + 64 * _ConcurrentAllocatorCounterBufferRW[7];
        return (allocatedShadelCount > _ShadelCapacity);
    }

    static uint ResolveAddress(uint baseOffset, uint2 occupancyBitfield, bool isTemporal)
    {
        uint shadelCount = countbits(occupancyBitfield.x) + countbits(occupancyBitfield.y);

        if (isTemporal)
        {
            if (shadelCount <= 8)
            {
                return baseOffset * 8;
            }
            else if (shadelCount <= 16)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + baseOffset * 16;
            }
            else if (shadelCount <= 24)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + baseOffset * 24;
            }
            else if (shadelCount <= 32)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + _HistoryConcurrentAllocatorCounterBuffer[2] * 24
                + baseOffset * 32;
            }
            else if (shadelCount <= 40)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + _HistoryConcurrentAllocatorCounterBuffer[2] * 24
                + _HistoryConcurrentAllocatorCounterBuffer[3] * 32
                + baseOffset * 40;
            }
            else if (shadelCount <= 48)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + _HistoryConcurrentAllocatorCounterBuffer[2] * 24
                + _HistoryConcurrentAllocatorCounterBuffer[3] * 32
                + _HistoryConcurrentAllocatorCounterBuffer[4] * 40
                + baseOffset * 48;
            }
            else if (shadelCount <= 56)
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + _HistoryConcurrentAllocatorCounterBuffer[2] * 24
                + _HistoryConcurrentAllocatorCounterBuffer[3] * 32
                + _HistoryConcurrentAllocatorCounterBuffer[4] * 40
                + _HistoryConcurrentAllocatorCounterBuffer[5] * 48
                + baseOffset * 56;
            }
            else
            {
                return _HistoryConcurrentAllocatorCounterBuffer[0] * 8 
                + _HistoryConcurrentAllocatorCounterBuffer[1] * 16 
                + _HistoryConcurrentAllocatorCounterBuffer[2] * 24
                + _HistoryConcurrentAllocatorCounterBuffer[3] * 32
                + _HistoryConcurrentAllocatorCounterBuffer[4] * 40
                + _HistoryConcurrentAllocatorCounterBuffer[5] * 48
                + _HistoryConcurrentAllocatorCounterBuffer[6] * 56 
                + baseOffset * 64;
            }
        }
        else
        {
            if (shadelCount <= 8)
            {
                return baseOffset * 8;
            }
            else if (shadelCount <= 16)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + baseOffset * 16;
            }
            else if (shadelCount <= 24)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + baseOffset * 24;
            }
            else if (shadelCount <= 32)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + _ConcurrentAllocatorCounterBuffer[2] * 24
                + baseOffset * 32;
            }
            else if (shadelCount <= 40)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + _ConcurrentAllocatorCounterBuffer[2] * 24
                + _ConcurrentAllocatorCounterBuffer[3] * 32
                + baseOffset * 40;
            }
            else if (shadelCount <= 48)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + _ConcurrentAllocatorCounterBuffer[2] * 24
                + _ConcurrentAllocatorCounterBuffer[3] * 32
                + _ConcurrentAllocatorCounterBuffer[4] * 40
                + baseOffset * 48;
            }
            else if (shadelCount <= 56)
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + _ConcurrentAllocatorCounterBuffer[2] * 24
                + _ConcurrentAllocatorCounterBuffer[3] * 32
                + _ConcurrentAllocatorCounterBuffer[4] * 40
                + _ConcurrentAllocatorCounterBuffer[5] * 48
                + baseOffset * 56;
            }
            else
            {
                return _ConcurrentAllocatorCounterBuffer[0] * 8 
                + _ConcurrentAllocatorCounterBuffer[1] * 16 
                + _ConcurrentAllocatorCounterBuffer[2] * 24
                + _ConcurrentAllocatorCounterBuffer[3] * 32
                + _ConcurrentAllocatorCounterBuffer[4] * 40
                + _ConcurrentAllocatorCounterBuffer[5] * 48
                + _ConcurrentAllocatorCounterBuffer[6] * 56 
                + baseOffset * 64;
            }
        }
    }
};

#endif // CONCURRENT_ALLOCATOR_CGINC