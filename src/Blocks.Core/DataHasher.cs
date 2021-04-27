using System;

namespace Blocks.Core
{
    public class DataHasher
    {
        private readonly int level;
        private readonly int value;

        public DataHasher() : this(4) {}

        public DataHasher(int level)
        {
            this.level = level;
            this.value = (1 << level) - 1;
        }

        public int GetCapacity()
        {
            return value + 1;
        }

        public int GetMask()
        {
            return 1 << (level - 1);
        }

        public DataHasher Prev()
        {
            return new DataHasher(level - 1);
        }

        public DataHasher Next()
        {
            return new DataHasher(level + 1);
        }

        public int Hash(byte[] data, int offset, int length)
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;
                int end = length + offset;

                for (int i = offset; i < end; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;

                return hash >= 0 ? hash & value : -hash & value;
            }
        }

        public int[] Initialize()
        {
            return new int[value + 1];
        }
    }
}
