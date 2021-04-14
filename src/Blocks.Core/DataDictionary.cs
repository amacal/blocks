using System;
using System.Runtime.InteropServices;

namespace Blocks.Core
{
    public class DataDictionary
    {
        private int size;
        private int[] nodes;
        private DataHasher hasher;
        private DataBlock[] blocks;
        private DataNodeBitmap bitmap;

        public DataDictionary()
        {
            this.size = 0;
            this.hasher = new DataHasher();

            this.nodes = hasher.Initialize();
            this.bitmap = new DataNodeBitmap();

            this.blocks = new DataBlock[] { new DataBlock { binary = new byte[64 * 1024 * 1024]}};
        }

        public int GetSize()
        {
            return size;
        }

        public string Describe()
        {
            long used = 0, wasted = 0;
            long depth = 0, overhead = 0;
            long memory = GC.GetTotalMemory(false);

            for (int i = 0; i < blocks.Length; i++)
            {
                used += blocks[i].GetUsed();
                wasted += blocks[i].getWasted();
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                int current = 0;
                int index = nodes[i];

                while (index > 0)
                {
                    index = bitmap.Next(index);
                    current++;
                }

                depth = Math.Max(current, depth);
            }

            int[] depths = new int[depth+1];
            depths[0] = size;

            for (int i = 0; i < nodes.Length; i++)
            {
                int current = 0;
                int index = nodes[i];

                while (index > 0)
                {
                    index = bitmap.Next(index);
                    depths[current++]--;
                    depths[current]++;
                }
            }

            overhead += 4 * (long)nodes.Length;
            overhead += 16 * (long)blocks.Length;
            overhead += Marshal.SizeOf<DataNode>() * (long)bitmap.Size();

            return $"size: {size}, depth: {depth} ({String.Join(',', depths)}), nodes: {nodes.Length}, blocks: {blocks.Length}, used: {used/1048576L}M, wasted: {wasted/1048576L}M, overhead: {overhead/1048576L}M, memory: {memory/1048576L}M";
        }

        public byte[] Get(byte[] key)
        {
            int hash = hasher.Hash(key);
            int index = nodes[hash];

            while (index > 0)
            {
                ref DataNode node = ref bitmap.Get(index);
                ref DataBlock block = ref blocks[node.Reference.Block];
                bool equals = block.Verify(ref node.Reference, key);

                if (equals == false) index = node.Next;
                else return block.Extract(node.Reference);
            }

            return null;
        }

        public void Set(byte[] key, byte[] value)
        {
            int hash = hasher.Hash(key);
            int index = nodes[hash];

            while (index > 0)
            {
                ref DataNode node = ref bitmap.Get(index);
                ref DataBlock block = ref blocks[node.Reference.Block];

                bool equals = block.Verify(ref node.Reference, key);
                if (equals) break;
                
                index = node.Next;
            }

            if (index == 0) Insert(0, hash, key, value);
            else Update(index, hash, key, value);
        }

        private void Update(int index, int hash, byte[] key, byte[] value)
        {
            ref DataNode node = ref bitmap.Get(index);
            int currentLength = node.Reference.ValueLength;
            ref DataBlock block = ref blocks[node.Reference.Block];

            if (currentLength >= value.Length)
            {
                block.Update(node.Reference, value);
                node.Reference.ValueLength = (ushort)value.Length;
            }
            else
            {
                block.Remove(node.Reference);
                Insert(index, hash, key, value);
            }
        }

        private void Insert(int index, int hash, byte[] key, byte[] value)
        {
            ref DataBlock found = ref blocks[0];
            int length = key.Length + value.Length;
            int position = -1;

            for (int i = blocks.Length - 1; i >= 0 && position == -1; i--)
                if (blocks[i].GetLeft() >= length)
                    found = ref blocks[position = i];

            if (position == -1) {
                Array.Resize(ref blocks, blocks.Length + 1);
                found = new DataBlock { binary = new byte[64 * 1024 * 1024] };
                position = blocks.Length - 1;
                blocks[position] = found;
            }

            DataReference reference = new DataReference
            {
                Block = (byte)(position),
                Offset = found.Insert(key, value),
                KeyLength = (byte)key.Length,
                ValueLength = (ushort)value.Length
            };

            if (index > 0)
            {
                ref DataNode node = ref bitmap.Get(index);
                node.Reference = reference;
            }
            else
            {
                int allocated = bitmap.Allocate();
                ref DataNode node = ref bitmap.Get(allocated);

                node.Next = nodes[hash];
                node.Reference = reference;

                size = size + 1;
                nodes[hash] = allocated;

                if (hasher.GetCapacity() <= size)
                {
                    hasher = hasher.Next();
                    nodes = Rebuild();
                }
            }
        }

        private int[] Rebuild()
        {
            int[] result = hasher.Initialize();

            for (int i = 0; i < nodes.Length; i++)
            {
                int index = nodes[i];

                while (index > 0)
                {
                    ref DataNode node = ref bitmap.Get(index);
                    DataBlock block = blocks[node.Reference.Block];
                    int hash = block.Hash(node.Reference, hasher);

                    index = node.Next;
                    node.Next = result[hash];
                }
            }

            return result;
        }
    }
}
