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
        private readonly Settings settings;
        private int size;
        private int depth;
        private HashNodeBitmap nodes;
        private InMemory inMemory;
        private DataHasher hasher;
        private InMemoryManager blocks;
        private TreeNodeBitmap bitmap;

        public MemoryTable(Settings settings)
        {
            this.hasher = new DataHasher(settings.InitialDepth, settings.MaximalDepth);
            this.bitmap = new TreeNodeBitmap();

            this.nodes = new HashNodeBitmap();
            this.nodes.Resize(hasher.GetCapacity());

            this.inMemory = new InMemory(settings.Path, settings.BlockSize);
            this.blocks = this.inMemory.Manager();

            this.settings = settings;
        }

        public int GetSize()
        {
            return size;
        }

        public int GetDepth()
        {
            return depth;
        }

        public int GetCapacity()
        {
            return hasher.GetCapacity();
        }

        public long SizeInBytes()
        {
            return blocks.SizeInBytes() + nodes.SizeInBytes() + bitmap.SizeInBytes();
        }

        public ValueInfo Get(KeyInfo key)
        {
            int hash = hasher.Hash(key.Data, key.Offset, key.Length);
            int index = nodes.Get(hash);

            if (index > 0)
            {
                ref TreeNode node = ref bitmap.Get(index);
                TreeReference tree = Cache(ref node, blocks.Extract(node));

                node.Accessed++;
                depth = Math.Max(depth, tree.Size());

                ref ValueNode link = ref tree.Find(key, out index);
                if (index >= 0) return blocks.Extract(ref link);
            }

            return new ValueInfo();
        }

        public void Set(KeyValueInfo keyValue)
        {
            int hash = hasher.Hash(keyValue.Data, keyValue.Offset, keyValue.KeyLength);
            int index = nodes.Get(hash);

            if (index > 0)
            {
                ref TreeNode node = ref bitmap.Get(index);
                TreeReference tree = Cache(ref node, blocks.Extract(node));
                ref ValueNode link = ref tree.Find(keyValue.GetKey(), out int pos);

                if (pos < 0) Insert(tree, link, pos, hash, keyValue);
                else Update(tree, ref link, pos, hash, keyValue);
            }
            else
                Insert(default(TreeReference), null, -1, hash, keyValue);

            if (hasher.GetCapacity() < size && hasher.IsMaximum() == false) Rebuild();
            if (blocks.GetDeclaredMemory() > settings.BlockMemory) Archive();
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

            int node = nodes.Get(hash);
            int bitmapIndex = node == 0 ? bitmap.Allocate() : node;

            bitmap.Set(bitmapIndex, assigned);
            nodes.Set(hash, bitmapIndex);

            size += index >= 0 ? 0 : 1;
        }

        private void Rebuild()
        {
            int capacity = hasher.GetCapacity();
            hasher = hasher.Next();

            int mask = hasher.GetMask();
            nodes.Resize(hasher.GetCapacity());
            depth = 0;

            for (int i = 0; i < capacity; i++)
            {
                int index = nodes.Get(i);

                if (index > 0)
                {
                    ref TreeNode node = ref bitmap.Get(index);
                    TreeReference tree = blocks.Extract(node);

                    TreeSplit split = tree.Split(hasher);
                    if (split.RightSize() == 0) depth = Math.Max(depth, tree.Size());
                    else if (split.LeftSize() == 0)
                    {
                        nodes.Set(i, 0);
                        nodes.Set(i + mask, index);
                        depth = Math.Max(depth, tree.Size());
                    }
                    else
                    {
                        InMemoryAllocation first = blocks.Allocate(split.LeftSize());
                        InMemoryAllocation second = blocks.Allocate(split.RightSize());

                        TreeReference left = split.Left(first);
                        TreeReference right = split.Right(second);

                        int allocated = bitmap.Allocate();

                        bitmap.Set(index, first.Block, first.Offset);
                        bitmap.Set(allocated, second.Block, second.Offset);
                        nodes.Set(i + mask, allocated);

                        depth = Math.Max(depth, left.Size());
                        depth = Math.Max(depth, right.Size());
                    }
                }
            }
        }

        private TreeReference Cache(ref TreeNode node, TreeReference tree)
        {
            if (node.InMemory()) return tree;

            int size = tree.SizeInBytes();
            var allocation = blocks.Allocate(size);

            Buffer.BlockCopy(tree.Data, tree.Node.Offset, allocation.Data, allocation.Offset, size);
            blocks.Remove(node);

            node.Block = allocation.Block;
            node.Offset = allocation.Offset;

            return new TreeReference(node, allocation.Data);
        }

        private void Archive()
        {
            int length = bitmap.Count();
            long left = blocks.GetUsedMemory() / 5;

            settings.OnArchiving?.Invoke();

            ArchiveValuesNotAccessed(length, ref left);
            ArchiveValuesAccessed(length, ref left);

            ArchiveTreesNotAccessed(length, ref left);
            ArchiveTreesAccessed(length, ref left);

            RewriteMemory(length, 0.10);
        }

        private void ArchiveValuesNotAccessed(int length, ref long left)
        {
            for (int i = 1; i <= length && left > 0; i++)
            {
                TreeNode node = bitmap.Get(i);
                if (node.InMemory() == false || node.Accessed > 0) continue;

                TreeReference tree = blocks.Extract(node);
                int size = tree.Size();

                for (int j = 0; j < size; j++)
                {
                    if (tree.InMemory(j) == false) continue;
                    ref ValueNode value = ref tree.GetValue(j);
                    left -= blocks.Archive(ref value);
                }
            }
        }

        private void ArchiveValuesAccessed(int length, ref long left)
        {
            for (int i = 1; i <= length && left > 0; i++)
            {
                TreeNode node = bitmap.Get(i);
                if (node.InMemory() == false) continue;

                TreeReference tree = blocks.Extract(node);
                int size = tree.Size();

                for (int j = 0; j < size; j++)
                {
                    if (tree.InMemory(j) == false) continue;
                    ref ValueNode value = ref tree.GetValue(j);
                    left -= blocks.Archive(ref value);
                }
            }
        }

        private void ArchiveTreesNotAccessed(int length, ref long left)
        {
            for (int i = 1; i <= length && left > 0; i++)
            {
                ref TreeNode node = ref bitmap.Get(i);
                if (node.InMemory() == false || node.Accessed > 0) continue;
                left -= blocks.Archive(ref node);
            }
        }

        private void ArchiveTreesAccessed(int length, ref long left)
        {
            for (int i = 1; i <= length && left > 0; i++)
            {
                ref TreeNode node = ref bitmap.Get(i);
                if (node.InMemory() == false) continue;
                left -= blocks.Archive(ref node);
                node.Accessed = 0;
            }
        }

        private void RewriteMemory(int length, double factor)
        {
            settings.OnRewritting?.Invoke();
            InMemoryEvolution evolved = blocks.Evolve(factor);

            for (int i = 1; i <= length; i++)
            {
                ref TreeNode node = ref bitmap.Get(i);
                if (node.InMemory() == false) continue;

                TreeReference tree = blocks.Extract(node);
                int size = tree.Size(), bytes = tree.SizeInBytes();

                for (int j = 0; j < size; j++)
                {
                    if (tree.InMemory(j) == false) continue;
                    ref ValueNode value = ref tree.GetValue(j);
                    if (evolved.Contains(value.Block) == false) continue;

                    ValueInfo data = blocks.Extract(ref value);
                    InMemoryAllocation allocation = evolved.Allocate(data.Length);

                    Buffer.BlockCopy(data.Data, data.Offset, allocation.Data, allocation.Offset, data.Length);

                    value.Block = allocation.Block;
                    value.Offset = allocation.Offset;
                }

                if (evolved.Contains(node.Block))
                {
                    InMemoryAllocation allocated = evolved.Allocate(bytes);
                    Buffer.BlockCopy(tree.Data, tree.Node.Offset, allocated.Data, allocated.Offset, bytes);

                    node.Block = allocated.Block;
                    node.Offset = allocated.Offset;
                }
            }

            blocks = evolved.Create();
            settings.OnRewritten?.Invoke();
        }
    }
}