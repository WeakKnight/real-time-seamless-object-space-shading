using System;
using System.Collections.Generic;

namespace GPUScene
{
    public class BitArray
    {
        public struct Visitor
        {
            BitArray bitArray;
            uint position;
            bool targetBitValue;
            bool initialized;

            uint SkipValue
            {
                get
                {
                    return (targetBitValue ? 0u : 1u);
                }
            }

            public Visitor(BitArray bitArray, bool targetBitValue = true)
            {
                this.bitArray = bitArray;
                position = 0;
                this.targetBitValue = targetBitValue;
                initialized = false;
            }

            public uint Current
            {
                get
                {
                    return position;
                }
            }

            public bool MoveNext()
            {
                if (!initialized)
                {
                    initialized = true;
                    if (bitArray[position] == targetBitValue)
                    {
                        return position < bitArray.Count;
                    }
                }

                while (position < bitArray.Count)
                {
                    uint dataIndex = position / 32u;
                    if (bitArray.data[(int)dataIndex] == SkipValue)
                    {
                        uint dst = dataIndex * 32u + 32u;
                        if (dst >= (bitArray.count - 1))
                        {
                            return false;
                        }
                        position = dst;
                    }
                    else
                    {
                        if (position >= (bitArray.count - 1))
                        {
                            return false;
                        }

                        position++;
                        if (bitArray[position] == targetBitValue)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public Visitor MakeVisitor(bool targetBitValue = true)
        {
            return new Visitor(this, targetBitValue);
        }

        List<uint> data = new();

        uint count = 0;

        public uint Add(bool bitValue)
        {
            uint index = count;
            count++;

            uint dataIndex = index / 32;
            uint dataCount = dataIndex + 1;
            if (data.Count < dataCount)
            {
                data.Add(0);
            }

            this[index] = bitValue;

            return index;
        }

        public void Add(bool bitValue, uint numBits)
        {
            uint targetCount = count + numBits;
            while (count < targetCount)
            {
                if (((targetCount - count) >= 32) && ((count % 32) == 0))
                {
                    count += 32;
                    data.Add(bitValue ? 1u : 0u);
                }
                else
                {
                    Add(bitValue);
                }
            }
        }

        static bool Get32(uint index, uint data) => (data & (1u << (int)index)) != 0u;
        static void Set32(uint index, ref uint data, bool value) => data = (value ? (data | (1u << (int)index)) : (data & ~(1u << (int)index)));

        public bool this[uint index]
        {
            get
            {
                uint dataIndex = index / 32;
                uint bits = data[(int)dataIndex];
                return Get32(index % 32, bits);
            }
            set
            {
                uint dataIndex = index / 32;
                uint bits = data[(int)dataIndex];
                Set32(index % 32, ref bits, value);
                data[(int)dataIndex] = bits;
            }
        }

        public uint Count
        {
            get
            {
                return count;
            }
        }
    }
}