using System.Collections.Generic;

namespace GPUScene
{
    public class SparseArray<T>
    {
        LinkedList<uint> freeSlots = new();
        public List<T> data = new();
        public BitArray allocationFlags = new();

        public uint Add(T value)
        {
            uint handle;

            if (freeSlots.Count > 0)
            {
                handle = freeSlots.Last.Value;
                
                allocationFlags[handle] = true;

                freeSlots.RemoveLast();
            }
            else
            {
                uint index = allocationFlags.Add(true);

                handle = index;
            }

            if (handle >= data.Count)
            {
                data.Add(value);
            }
            else
            {
                data[(int)handle] = value;
            }

            return handle;
        }

        public void Free(uint handle)
        {
            allocationFlags[handle] = false;
            freeSlots.AddLast(handle);
        }

        public T GetData(uint handle)
        {
            return data[(int)handle];
        }
    }
}