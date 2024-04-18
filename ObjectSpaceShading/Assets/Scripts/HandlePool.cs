using System.Collections.Generic;
using UnityEngine;

public class HandlePool
{
    public struct Handle
    {
        public int index;
        public int generation;

        public static Handle Invalid()
        {
            var result = new Handle();
            result.index = -1;
            result.generation = -1;
            return result;
        }

        public static implicit operator int(Handle handle) => handle.index;
    }

    public int capacity;
    public int count;
    List<int> freeSlots;
    List<int> generations;

    public List<uint> allocatedSlots;

    public ComputeBuffer indexBuffer;

    public HandlePool(int capacity)
    {
        this.capacity = capacity;
        this.count = 0;
        freeSlots = new List<int>(capacity);
        allocatedSlots = new List<uint>(capacity);
        generations = new List<int>(capacity);
        for (int i = 0; i < capacity; i++)
        {
            freeSlots.Add(capacity - i - 1);
            generations.Add(0);
        }
    }

    public void Release()
    {
        // indexBuffer?.Release();
    }

    public Handle Allocate()
    {
        count++;

        Handle handle = new Handle();
        handle.index = freeSlots[freeSlots.Count - 1];
        handle.generation = generations[handle.index];

        freeSlots.RemoveAt(freeSlots.Count - 1);

        allocatedSlots.Add((uint)handle.index);

        UpdateBuffer();

        return handle;
    }

    public void Free(Handle handle)
    {
        if (!IsValid(handle))
        {
            return;
        }

        count--;

        generations[handle.index]++;

        freeSlots.Add(handle.index);

        allocatedSlots.Remove((uint)handle.index);
    }

    private void UpdateBuffer()
    {
        if (indexBuffer == null)
        {
            indexBuffer = new ComputeBuffer(capacity, 4, ComputeBufferType.Structured | ComputeBufferType.Raw);
        }

        allocatedSlots.Sort();

        indexBuffer.SetData(allocatedSlots, 0, 0, allocatedSlots.Count);
    }

    public bool IsValid(Handle handle)
    {
        return handle.index != -1 && generations[handle.index] == handle.generation;
    }
}
