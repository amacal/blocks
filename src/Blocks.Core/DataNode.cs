using System;
using System.Runtime.InteropServices;

namespace Blocks.Core
{
    public ref struct TreeReference
    {
        private static readonly int NegativeByteBlockOffset = BitConverter.IsLittleEndian ? 2 : 1;

        public readonly TreeNode Node;
        public readonly byte[] Data;

        public TreeReference(TreeNode node, byte[] data)
        {
            this.Node = node;
            this.Data = data;
        }

        public TreeReference(InMemoryAllocation allocation)
        {
            this.Node = new TreeNode(allocation.Block, allocation.Offset);
            this.Data = allocation.Data;
        }

        public int Size()
        {
            return Data[Node.Offset];
        }

        public int SizeInBytes()
        {
            return Node.SizeInBytes(Data, Data[Node.Offset]);
        }

        public LinkNode GetLink(int index)
        {
            return new LinkNode
            {
                Key = GetKey(index),
                Value = GetValue(index)
            };
        }

        public ReadOnlySpan<byte> GetKey(int index)
        {
            return Data.AsSpan(Node.Offset + Node.SizeInBytes(Data, index), Data[Node.Offset + index + 1]);
        }

        public ref ValueNode GetValue(int index)
        {
            return ref MemoryMarshal.AsRef<ValueNode>(Data.AsSpan(Node.Offset + 1 + Data[Node.Offset] + (index << 3), 8));
        }

        public bool InMemory(int index)
        {
            return Data[Node.Offset + NegativeByteBlockOffset + Data[Node.Offset] + (index << 3)] < 128;
        }

        public ref ValueNode Find(KeyInfo key, out int index)
        {
            int root = Data[Node.Offset];

            for (int i = 0, j = 1 + 9 * root; i < root; i++, j += Data[Node.Offset + i])
            {
                if (Data[Node.Offset + 1 + i] != key.Length)
                    continue;

                for (int k = key.Length - 1, l = key.Length + Node.Offset + j - 1; k >= 0; k--, l--)
                {
                    if (Data[l] != key.Data[k + key.Offset])
                        break;

                    if (k == 0)
                    {
                        index = i;
                        return ref GetValue(i);
                    }
                }
            }

            index = -1;
            return ref ValueNode.Nothing;
        }

        public static TreeInsert Insert(KeyValueInfo keyValue)
        {
            return new TreeInsert(keyValue);
        }

        public TreeAppend Append(KeyValueInfo keyValue)
        {
            return new TreeAppend(this, keyValue);
        }

        public TreeModify Modify(int index, ValueInfo value)
        {
            return new TreeModify(this, index, value);
        }

        public TreeSplit Split(DataHasher hasher)
        {
            int root = Data[Node.Offset];
            byte left = 1, right = 1;

            int mask = hasher.GetMask();
            bool[] splits = new bool[root];

            for (int i = 0, j = 1 + 9 * root; i < root; i++, j += Data[Node.Offset + i])
            {
                int hash = hasher.Hash(Data, Node.Offset + j, Data[Node.Offset + i + 1]);
                byte size = (byte)(Data[Node.Offset + i + 1] + 9);

                splits[i] = (hash & mask) == 0;

                if (splits[i]) left += size;
                else right += size;
            }

            if (left == 1) left = 0;
            else if (right == 1) right = 0;

            return new TreeSplit(this, splits, left, right);
        }
    }

    public ref struct TreeSplit
    {
        private readonly bool[] items;
        private readonly TreeReference tree;
        private readonly int left, right;

        public TreeSplit(TreeReference tree, bool[] items, int left, int right)
        {
            this.tree = tree;
            this.items = items;
            this.left = left;
            this.right = right;
        }

        public int LeftSize()
        {
            return left;
        }

        public int RightSize()
        {
            return right;
        }

        public TreeReference Left(InMemoryAllocation allocation)
        {
            return Apply(allocation, false);
        }

        public TreeReference Right(InMemoryAllocation allocation)
        {
            return Apply(allocation, true);
        }

        private TreeReference Apply(InMemoryAllocation allocation, bool condition)
        {
            int offset = allocation.Offset + 1;
            allocation.Data[allocation.Offset] = 0;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == condition) continue;
                allocation.Data[offset++] = tree.Data[tree.Node.Offset + i + 1];
                allocation.Data[allocation.Offset]++;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == condition) continue;

                int source = tree.Node.Offset + 1 + tree.Data[tree.Node.Offset] + 8 * i;
                int destination = offset;

                Buffer.BlockCopy(tree.Data, source, allocation.Data, destination, 8);
                offset = offset + 8;
            }

            for (int i = 0, j = 1 + 9 * tree.Data[tree.Node.Offset]; i < items.Length; i++, j += tree.Data[tree.Node.Offset + i])
            {
                if (items[i] == condition) continue;
                int length = tree.Data[tree.Node.Offset + i + 1];

                int source = tree.Node.Offset + j;
                int destination = offset;

                Buffer.BlockCopy(tree.Data, source, allocation.Data, destination, length);
                offset = offset + tree.Data[tree.Node.Offset + i + 1];
            }

            return new TreeReference(allocation);
        }
    }

    public ref struct TreeAppend
    {
        private readonly TreeReference tree;
        private readonly KeyValueInfo keyValue;

        public TreeAppend(TreeReference tree, KeyValueInfo keyValue)
        {
            this.tree = tree;
            this.keyValue = keyValue;
        }

        public int GetSize()
        {
            return tree.SizeInBytes() + 9 + keyValue.KeyLength + keyValue.ValueLength;
        }

        public TreeReference Apply(InMemoryAllocation allocation)
        {
            int size = allocation.Length - 9 - keyValue.KeyLength - keyValue.ValueLength;
            int metaOffset = tree.Data[tree.Node.Offset] + 1;
            int metaSize = tree.Data[tree.Node.Offset] << 3;
            int keysOffset = metaOffset + metaSize;
            int keysLength = size - keysOffset;

            Buffer.BlockCopy(keyValue.Data, keyValue.Offset, allocation.Data, allocation.Offset + allocation.Length - keyValue.ValueLength - keyValue.KeyLength, keyValue.KeyLength + keyValue.ValueLength);
            Buffer.BlockCopy(tree.Data, tree.Node.Offset, allocation.Data, allocation.Offset, metaOffset);

            allocation.Data[allocation.Offset]++;
            allocation.Data[allocation.Offset + allocation.Data[allocation.Offset]] = (byte)keyValue.KeyLength;

            Buffer.BlockCopy(tree.Data, tree.Node.Offset + metaOffset, allocation.Data, allocation.Offset + metaOffset + 1, metaSize);
            Buffer.BlockCopy(tree.Data, tree.Node.Offset + keysOffset, allocation.Data, allocation.Offset + keysOffset + 9, keysLength);

            ValueNode node = new ValueNode
            {
                Block = allocation.Block,
                Length = keyValue.ValueLength,
                Offset = allocation.Offset + allocation.Length - keyValue.ValueLength,
            };

            Span<byte> destination = allocation.Data.AsSpan(allocation.Offset + keysOffset + 1);
            MemoryMarshal.AsBytes<ValueNode>(MemoryMarshal.CreateSpan(ref node, 1)).CopyTo(destination);

            return new TreeReference(allocation);
        }
    }

    public ref struct TreeModify
    {
        private readonly TreeReference tree;
        private readonly ValueInfo value;
        private readonly int index;

        public TreeModify(TreeReference tree, int index, ValueInfo value)
        {
            this.tree = tree;
            this.index = index;
            this.value = value;
        }

        public int GetSize()
        {
            return tree.SizeInBytes() + value.Length;
        }

        public TreeReference Apply(InMemoryAllocation allocation)
        {
            int size = allocation.Length - value.Length;
            ref ValueNode node = ref MemoryMarshal.AsRef<ValueNode>(allocation.Data.AsSpan(allocation.Offset + 1 + tree.Data[tree.Node.Offset] + 8 * index, 8));

            Buffer.BlockCopy(tree.Data, tree.Node.Offset, allocation.Data, allocation.Offset, size);
            Buffer.BlockCopy(value.Data, value.Offset, allocation.Data, allocation.Offset + allocation.Length - value.Length, value.Length);

            node.Block = allocation.Block;
            node.Length = (ushort)value.Length;
            node.Offset = allocation.Offset + allocation.Length - value.Length;

            return new TreeReference(allocation);
        }
    }

    public ref struct TreeInsert
    {
        private readonly KeyValueInfo keyValue;

        public TreeInsert(KeyValueInfo keyValue)
        {
            this.keyValue = keyValue;
        }

        public int GetSize()
        {
            return 10 + keyValue.KeyLength + keyValue.ValueLength;
        }

        public TreeReference Apply(InMemoryAllocation allocation)
        {
            Buffer.BlockCopy(keyValue.Data, keyValue.Offset, allocation.Data, allocation.Offset + 10, keyValue.KeyLength + keyValue.ValueLength);

            allocation.Data[allocation.Offset] = 1;
            allocation.Data[allocation.Offset + 1] = keyValue.KeyLength;

            ValueNode node = new ValueNode
            {
                Block = allocation.Block,
                Length = keyValue.ValueLength,
                Offset = allocation.Offset + allocation.Length - keyValue.ValueLength,
            };

            Span<byte> destination = allocation.Data.AsSpan(allocation.Offset + 2);
            MemoryMarshal.AsBytes<ValueNode>(MemoryMarshal.CreateSpan(ref node, 1)).CopyTo(destination);

            return new TreeReference(allocation);
        }
    }

    public ref struct LinkNode
    {
        public ReadOnlySpan<byte> Key;
        public ValueNode Value;

        public bool Verify(byte[] key)
        {
            return Key.SequenceEqual(key);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size=8)]
    public struct ValueNode
    {
        public static ValueNode Nothing;

        [FieldOffset(0)] public short Block;
        [FieldOffset(2)] public int Offset;
        [FieldOffset(6)] public ushort Length;

        public bool InMemory()
        {
            return Block >= 0;
        }

        public void SetLength(int length)
        {
            Length = (ushort)length;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size=8)]
    public struct TreeNode
    {
        [FieldOffset(0)] public short Block;
        [FieldOffset(2)] public int Offset;
        [FieldOffset(6)] public ushort Accessed;

        public TreeNode(int block, int offset)
        {
            this.Block = (short)block;
            this.Offset = offset;
            this.Accessed = 0;
        }

        private TreeNode(int block, int offset, ushort accessed)
        {
            this.Block = (short)block;
            this.Offset = offset;
            this.Accessed = accessed;
        }

        public bool InMemory()
        {
            return Block >= 0;
        }

        public int SizeInBytes(byte[] data, int index)
        {
            int offset = data[Offset];

            for (int i = 1; i <= index; i++)
                offset += data[Offset + i];

            return 1 + 8 * data[Offset] + offset;
        }

        public TreeNode Shift(int offset)
        {
            return new TreeNode(Block, offset, Accessed);
        }
    }

    public class TreeNodeBitmap
    {
        private int offset;
        private TreeNode[][] values;

        public TreeNodeBitmap()
        {
            this.offset = 0;
            this.values = new TreeNode[0][];
        }

        public int Count()
        {
            return offset;
        }

        public long SizeInBytes()
        {
            return values.LongLength * 1048576 * 8;
        }

        public int Allocate()
        {
            int result = ++offset;
            int batch = result >> 20;

            if (batch >= values.Length)
            {
                Array.Resize(ref values, values.Length + 1);
                values[^1] = new TreeNode[1048576];
            }

            return result;
        }

        public ref TreeNode Get(int index)
        {
            return ref values[index >> 20][index & 1048575];
        }

        public void Set(int index, TreeNode node)
        {
            values[index >> 20][index & 1048575] = node;
        }

        public void Set(int index, short block, int offset)
        {
            values[index >> 20][index & 1048575] = new TreeNode
            {
                Block = block,
                Offset = offset,
            };
        }
    }

    public class HashNodeBitmap
    {
        private int[][] values;

        public HashNodeBitmap()
        {
            this.values = new int[0][];
        }

        public long SizeInBytes()
        {
            return values.LongLength * 1048576;
        }

        public void Resize(int size)
        {
            Array.Resize(ref values, (size >> 18) + 1);

            for (int i = 0; i < values.Length; i++)
                if (values[i] == null) values[i] = new int[262144];
        }

        public int Get(int index)
        {
            return values[index >> 18][index & 262143];
        }

        public void Set(int index, int value)
        {
            values[index >> 18][index & 262143] = value;
        }
    }
}
