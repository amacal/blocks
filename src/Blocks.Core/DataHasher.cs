using System;

namespace Blocks.Core
{
    public class DataHasher
    {
        private static readonly int[] Primes =
        {
            17, 33, 67, 131, 257, 521, 1031, 2053, 4099, 8209, 16411,
            32771, 65537, 131101, 262147, 524309, 1048583, 2097169,
            4194319, 8388617, 16777259, 33554467, 67108879, 134217757,
            268435459, 536870923, 1073741827, 2147483647
        };

        private readonly int level;
        private readonly int value;

        public DataHasher() : this(0) {}

        private DataHasher(int level)
        {
            this.level = level;
            this.value = Primes[level];
        }

        public int GetSize()
        {
            return value;
        }

        public int GetCapacity()
        {
            return value;
        }

        public DataHasher Prev()
        {
            return new DataHasher(level - 1);
        }

        public DataHasher Next()
        {
            return new DataHasher(level + 1);
        }

        public int Hash(Span<byte> data)
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;

                return hash >= 0 ? hash % value : -hash % value;
            }
        }

        public int[] Initialize()
        {
            return new int[value];
        }
    }
}
