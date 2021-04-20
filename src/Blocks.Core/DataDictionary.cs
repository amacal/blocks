using System;
using System.Runtime.InteropServices;

namespace Blocks.Core
{
    public class DataDictionary
    {
        private long offset;
        private int size;
        private int[] nodes;
        private InFile inFile;
        private InMemory inMemory;
        private DataHasher hasher;
        private KeyBlock[] keys;
        private ValueBlock[] values;
        private ValueFile[] files;
        private DataNodeBitmap bitmap;
        private int[] seconds;

        public DataDictionary()
        {
            this.size = 0;
            this.hasher = new DataHasher();

            this.nodes = hasher.Initialize();
            this.bitmap = new DataNodeBitmap();

            this.inFile = new InFile(64 * 1024 * 1024);
            this.inMemory = new InMemory(64 * 1024 * 1024);

            this.offset = DateTimeOffset.Now.ToUnixTimeSeconds();
            this.seconds = new int[3600];

            this.files = new ValueFile[0];
            this.keys = new KeyBlock[] { inMemory.Keys() };
            this.values = new ValueBlock[] { inMemory.Values() };
        }

        public int GetSize()
        {
            return size;
        }

        public string Describe()
        {
            long used = 0, wasted = 0;
            long depth = 0, overhead = 0;

            GC.Collect();
            long memory = GC.GetTotalMemory(false);
            long time = DateTimeOffset.Now.ToUnixTimeSeconds() - offset;

            for (int i = 0; i < keys.Length; i++)
            {
                used += keys[i].GetUsed();
                wasted += keys[i].GetWasted();
            }

            for (int i = 0; i < values.Length; i++)
            {
                used += values[i].GetUsed();
                wasted += values[i].GetWasted();
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
            overhead += Marshal.SizeOf<DataNode>() * (long)bitmap.Size();

            return $"{time}, size: {size}, depth: {depth} ({String.Join(',', depths)}), nodes: {nodes.Length}, blocks: {keys.Length}/{values.Length}/{files.Length}, used: {used/1048576L}M, wasted: {wasted/1048576L}M, overhead: {overhead/1048576L}M, memory: {memory/1048576L}M";
        }

        public byte[] Get(byte[] key)
        {
            int hash = hasher.Hash(key);
            int index = nodes[hash];

            while (index > 0)
            {
                ref DataNode node = ref bitmap.Get(index);
                ref KeyBlock keyBlock = ref keys[node.Key.Block];

                if (keyBlock.Verify(node.Key, key))
                {
                    node.Fetched = (int)(DateTimeOffset.Now.ToUnixTimeSeconds() - offset);
                    seconds[node.Fetched]++;

                    if (node.Value.Block >= 0)
                        return values[node.Value.Block].Extract(node.Value);
                    
                    byte[] data = files[-node.Value.Block-1].Extract(node.Value);
                    node.Value = InsertValue(data);

                    return data;
                }

                index = node.Next;
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
                ref KeyBlock keyBlock = ref keys[node.Key.Block];

                if (keyBlock.Verify(node.Key, key)) break;
                else index = node.Next;
            }

            if (index == 0) Insert(0, hash, key, value);
            else Update(index, hash, key, value);

            if (hasher.GetCapacity() <= size)
            {
                hasher = hasher.Next();
                nodes = Rebuild();
            }

            if (values.Length >= 25)
            {
                int count = bitmap.Count();
                ValueBuilder builder = inFile.Builder();

                for (int i = 1; i <= count; i++)
                {
                    ref DataNode node = ref bitmap.Get(i);
                    if (node.Fetched > 0 || node.Value.Block < 0) continue;

                    ref ValueBlock valueBlock = ref values[node.Value.Block];
                    byte[] data = valueBlock.Extract(node.Value);

                    int inserted = builder.Insert(data);
                    int next = -files.Length - 1;

                    if (inserted == -1)
                    {
                        Array.Resize(ref files, files.Length + 1);
                        files[^1] = builder.Flush();

                        builder = inFile.Builder();
                        inserted = builder.Insert(data);
                        next = -files.Length - 1;
                    }

                    node.Value.Block = (short)next;
                    node.Value.Offset = inserted;
                }

                if (builder.IsEmpty() == false)
                {
                    Array.Resize(ref files, files.Length + 1);
                    files[^1] = builder.Flush();
                }

                ValueBlock[] rewritten = new ValueBlock[] { inMemory.Values() };
                ref ValueBlock current = ref rewritten[0];

                for (int i = 1; i <= count; i++)
                {
                    ref DataNode node = ref bitmap.Get(i);
                    if (node.Value.Block < 0) continue;

                    ref ValueBlock valueBlock = ref values[node.Value.Block];
                    byte[] data = valueBlock.Extract(node.Value);

                    int inserted = current.Insert(data);
                    if (inserted == -1)
                    {
                        Array.Resize(ref rewritten, rewritten.Length + 1);
                        rewritten[^1] = inMemory.Values();

                        current = ref rewritten[^1];
                        inserted = current.Insert(data);
                    }

                    node.Value.Block = (short)(rewritten.Length - 1);
                    node.Value.Offset = inserted;
                }

                values = rewritten;
            }
        }

        private void Update(int index, int hash, byte[] key, byte[] value)
        {
            ref DataNode node = ref bitmap.Get(index);
            int currentLength = node.Value.Length;

            bool accessible = node.Value.Block >= 0;
            ValueBlock valueBlock = accessible ? values[node.Value.Block] : null;

            if (currentLength >= value.Length)
            {
                valueBlock?.Update(node.Value, value);
                node.Value.Length = (ushort)value.Length;
            }
            else
            {
                valueBlock?.Remove(node.Value);
                Insert(index, hash, key, value);
            }
        }

        private void Insert(int index, int hash, byte[] key, byte[] value)
        {
            ref KeyBlock foundKeys = ref keys[0];
            ref ValueBlock foundValues = ref values[0];
            int positionKeys = -1, positionValues = -1;

            for (int i = keys.Length - 1; i >= 0 && positionKeys == -1; i--)
                if (keys[i].GetLeft() >= key.Length)
                    foundKeys = ref keys[positionKeys = i];

            for (int i = values.Length - 1; i >= 0 && positionValues == -1; i--)
                if (values[i].GetLeft() >= value.Length)
                    foundValues = ref values[positionValues = i];

            if (positionKeys == -1) {
                Array.Resize(ref keys, keys.Length + 1);
                foundKeys = inMemory.Keys();
                positionKeys = keys.Length - 1;
                keys[positionKeys] = foundKeys;
            }

            if (positionValues == -1) {
                Array.Resize(ref values, values.Length + 1);
                foundValues = inMemory.Values();
                positionValues = values.Length - 1;
                values[positionValues] = foundValues;
            }

            ValueReference valueReference = new ValueReference
            {
                Block = (byte)positionValues,
                Offset = foundValues.Insert(value),
                Length = (ushort)value.Length
            };

            if (index > 0)
            {
                ref DataNode node = ref bitmap.Get(index);
                node.Value = valueReference;
            }
            else
            {
                KeyReference keyReference = new KeyReference
                {
                    Block = (byte)positionKeys,
                    Offset = foundKeys.Insert(key),
                    Length = (byte)key.Length,
                };

                int allocated = bitmap.Allocate();
                ref DataNode node = ref bitmap.Get(allocated);

                node.Fetched = 0;
                node.Next = nodes[hash];
                node.Key = keyReference;
                node.Value = valueReference;

                size = size + 1;
                nodes[hash] = allocated;
            }
        }

        private ValueReference InsertValue(byte[] value)
        {
            ref ValueBlock block = ref values[0];
            int length = value.Length, position = -1;

            for (int i = values.Length - 1; i >= 0 && position == -1; i--)
                if (values[i].GetLeft() >= length)
                    block = ref values[position = i];

            if (position == -1) {
                Array.Resize(ref values, values.Length + 1);
                block = inMemory.Values();
                position = values.Length - 1;
                values[position] = block;
            }

            return new ValueReference
            {
                Block = (byte)position,
                Offset = block.Insert(value),
                Length = (ushort)value.Length
            };
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
                    ref KeyBlock keyBlock = ref keys[node.Key.Block];
                    int hash = keyBlock.Hash(node.Key, hasher);

                    index = node.Next;
                    node.Next = result[hash];
                }
            }

            return result;
        }
    }
}
