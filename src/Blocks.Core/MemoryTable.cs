using System;

namespace Blocks.Core
{
    public ref struct KeyInfo
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly byte Length;

        public KeyInfo(byte[] data, int offset, byte length)
        {
            this.Data = data;
            this.Offset = offset;
            this.Length = length;
        }

        public static KeyInfo From(byte[] data)
        {
            return new KeyInfo(data, 0, (byte)data.Length);
        }
    }

    public ref struct ValueInfo
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly ushort Length;

        public ValueInfo(byte[] data, int offset, ushort length)
        {
            this.Data = data;
            this.Offset = offset;
            this.Length = length;
        }

        public static ValueInfo From(byte[] data)
        {
            return new ValueInfo(data, 0, (ushort)data.Length);
        }

        public ReadOnlySpan<byte> AsSpan()
        {
            return new ReadOnlySpan<byte>(Data, Offset, Length);
        }
    }

    public ref struct KeyValueInfo
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly byte KeyLength;
        public readonly ushort ValueLength;

        public KeyValueInfo(byte[] data, int offset, byte keyLength, ushort valueLength)
        {
            this.Data = data;
            this.Offset = offset;
            this.KeyLength = keyLength;
            this.ValueLength = valueLength;
        }

        public static KeyValueInfo From(byte[] data, byte keyLength)
        {
            return new KeyValueInfo(data, 0, keyLength, (ushort)(data.Length - keyLength));
        }

        public KeyInfo GetKey()
        {
            return new KeyInfo(Data, Offset, KeyLength);
        }

        public ValueInfo GetValue()
        {
            return new ValueInfo(Data, Offset + KeyLength, ValueLength);
        }
    }

    public class MemoryTable
    {
        private int size;
        private int[] nodes;
        private InMemory inMemory;
        private DataHasher hasher;
        private InMemoryManager blocks;
        private TreeNodeBitmap bitmap;

        public MemoryTable(string path, int blockSize)
        {
            this.hasher = new DataHasher();
            this.bitmap = new TreeNodeBitmap();
            this.nodes = this.hasher.Initialize();

            this.inMemory = new InMemory(path, blockSize);
            this.blocks = this.inMemory.Manager();
        }

        public int GetSize()
        {
            return size;
        }

        public int GetCapacity()
        {
            return hasher.GetCapacity();
        }

        public ValueInfo Get(KeyInfo key)
        {
            int hash = hasher.Hash(key.Data, key.Offset, key.Length);
            int index = nodes[hash];

            if (index > 0)
            {
                ref TreeNode node = ref bitmap.Get(index);
                TreeReference tree = blocks.Extract(ref node);

                node.Accessed++;

                ref ValueNode link = ref tree.Find(key, out index);
                if (index >= 0) return blocks.Extract(ref link);
            }

            return new ValueInfo();
        }

        public void Set(KeyValueInfo keyValue)
        {
            int hash = hasher.Hash(keyValue.Data, keyValue.Offset, keyValue.KeyLength);
            int index = nodes[hash];

            if (index > 0)
            {
                ref TreeNode node = ref bitmap.Get(index);
                TreeReference tree = blocks.Extract(ref node);
                ref ValueNode link = ref tree.Find(keyValue.GetKey(), out int pos);

                if (pos < 0) Insert(tree, link, pos, hash, keyValue);
                else Update(tree, ref link, pos, hash, keyValue);
            }
            else
                Insert(default(TreeReference), null, -1, hash, keyValue);

            if (hasher.GetCapacity() < size)
            {
                hasher = hasher.Next();
                nodes = Rebuild();
            }

            if (blocks.GetDeclaredMemory() > 2048 * 1048576L)
            {
                blocks = Archive();
            }
        }

        private void Update(TreeReference tree, ref ValueNode link, int index, int hash, KeyValueInfo keyValue)
        {
            if (link.Length >= keyValue.ValueLength)
            {
                blocks.Overwrite(ref link, keyValue.GetValue());
                link.SetLength(keyValue.ValueLength);
            }
            else
            {
                blocks.Remove(link);
                blocks.Remove(tree.Node);
                Insert(tree, link, index, hash, keyValue);
            }
        }

        private void Insert(TreeReference tree, ValueNode? link, int index, int hash, KeyValueInfo keyValue)
        {
            TreeNode assigned;

            TreeNode append(TreeAppend append)
            {
                int size = append.GetSize();
                var allocation = blocks.Allocate(size);
                return append.Apply(allocation).Node;
            }

            TreeNode modify(TreeModify modify)
            {
                int size = modify.GetSize();
                var allocation = blocks.Allocate(size);
                return modify.Apply(allocation).Node;
            }

            TreeNode insert(TreeInsert insert)
            {
                int size = insert.GetSize();
                var allocation = blocks.Allocate(size);
                return insert.Apply(allocation).Node;
            }

            if (tree.Data != null && index >= 0) assigned = modify(tree.Modify(index, keyValue.GetValue()));
            else if (tree.Data != null) assigned = append(tree.Append(keyValue));
            else assigned = insert(TreeReference.Insert(keyValue));

            int bitmapIndex = nodes[hash] == 0 ? bitmap.Allocate() : nodes[hash];
            bitmap.Set(bitmapIndex, assigned);

            nodes[hash] = bitmapIndex;
            size += index >= 0 ? 0 : 1;
        }

        private int[] Rebuild()
        {
            int mask = hasher.GetMask();
            int[] result = hasher.Initialize();

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] > 0)
                {
                    ref TreeNode node = ref bitmap.Get(nodes[i]);
                    TreeReference tree = blocks.Extract(ref node);

                    TreeSplit split = tree.Split(hasher);
                    if (split.RightSize() == 0) result[i] = nodes[i];
                    else if (split.LeftSize() == 0) result[i+mask] = nodes[i];
                    else
                    {
                        InMemoryAllocation first = blocks.Allocate(split.LeftSize());
                        InMemoryAllocation second = blocks.Allocate(split.RightSize());

                        TreeReference left = split.Left(first);
                        TreeReference right = split.Right(second);

                        int allocated = bitmap.Allocate();

                        bitmap.Set(nodes[i], first.Block, first.Offset);
                        bitmap.Set(allocated, second.Block, second.Offset);

                        result[i] = nodes[i];
                        result[i+mask] = allocated;
                    }
                }
            }

            return result;
        }

        private InMemoryManager Archive()
        {
            long left = blocks.GetUsedMemory() / 2;
            for (int i = 0; i < nodes.Length && left > 0; i++)
            {
                if (nodes[i] > 0)
                {
                    ref TreeNode node = ref bitmap.Get(nodes[i]);
                    if (node.InMemory() == false || node.Accessed > 0) continue;

                    TreeReference tree = blocks.Extract(ref node);
                    int size = tree.Size();

                    for (int j = 0; j < size; j++)
                    {
                        ref ValueNode value = ref tree.GetValue(j);
                        if (value.InMemory() == false) continue;
                        left -= blocks.Archive(ref value);
                    }

                    left -= blocks.Archive(ref node);
                }
            }

            InMemoryManager evolved = blocks.Evolve();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] > 0)
                {
                    ref TreeNode node = ref bitmap.Get(nodes[i]);
                    if (node.InMemory() == false) continue;

                    TreeReference tree = blocks.Extract(ref node);
                    int size= tree.Size(), bytes = tree.SizeInBytes();

                    for (int j = 0; j < size; j++)
                    {
                        ref ValueNode value = ref tree.GetValue(j);
                        ValueInfo data = blocks.Extract(ref value);

                        InMemoryAllocation allocation = evolved.Allocate(data.Length);
                        Buffer.BlockCopy(data.Data, data.Offset, allocation.Data, allocation.Offset, data.Length);

                        value.Block = allocation.Block;
                        value.Offset = allocation.Offset;
                    }

                    InMemoryAllocation allocated = evolved.Allocate(bytes);
                    Buffer.BlockCopy(tree.Data, node.Offset, allocated.Data, allocated.Offset, bytes);

                    node.Block = allocated.Block;
                    node.Offset = allocated.Offset;
                }
            }

            this.blocks.Dispose();
            return evolved;
        }
    }
}