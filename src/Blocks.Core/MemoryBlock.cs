using System;
using System.Collections.Generic;
using System.IO;

namespace Blocks.Core
{
    public class InMemory
    {
        private readonly Queue<byte[]> pool;
        private readonly string path;
        private readonly long size;

        public InMemory(string path, int size)
        {
            this.path = path;
            this.size = size;
            this.pool = new Queue<byte[]>();
        }

        public long SizeInBytes()
        {
            return size * pool.Count;
        }

        public long GetSize()
        {
            return size;
        }

        public InMemoryBlock Block(int index)
        {
            if (pool.TryDequeue(out var data) == false)
                data = new byte[size];

            return new InMemoryBlock(index, data);
        }

        public InMemoryManager Manager()
        {
            return new InMemoryManager(this);
        }

        public ValueBuilder Builder(short index)
        {
            if (pool.TryDequeue(out var data) == false)
                data = new byte[size];

            return new ValueBuilder(index, Path.Combine(path, $"{Path.GetRandomFileName()}.tmp"), data);
        }

        public void Release(byte[] data)
        {
            this.pool.Enqueue(data);
        }
    }

    public ref struct InMemoryReference
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly int Length;

        public InMemoryReference(byte[] data, int offset, int length)
        {
            this.Offset = offset;
            this.Length = length;
            this.Data = data;
        }
    }

    public ref struct InMemoryAllocation
    {
        public readonly short Block;
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly int Length;

        public InMemoryAllocation(int block, byte[] data, int offset, int length)
        {
            this.Block = (short)block;
            this.Offset = offset;
            this.Length = length;
            this.Data = data;
        }
    }

    public class InMemoryEvolution
    {
        private readonly InMemory memory;
        private readonly HashSet<int> obsolete;
        private InMemoryBlock[] blocks;
        private InMemoryBlock[] abandoned;
        private ValueFile[] files;
        private ValueBuilder builder;
        private InMemoryBlock current;

        public InMemoryEvolution(InMemory memory, ValueBuilder builder, InMemoryBlock[] available, InMemoryBlock[] abandoned, ValueFile[] files)
        {
            this.files = files;
            this.blocks = available;
            this.memory = memory;
            this.abandoned = abandoned;

            this.builder = builder;
            this.obsolete = new HashSet<int>();

            for (int i = 0; i < abandoned.Length; i++)
                if (abandoned[i] != null)
                    obsolete.Add(i);

            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] == null)
                {
                    blocks[i] = memory.Block(i);
                    current = available[i];
                    break;
                }
            }

            if (available == null)
            {
                Array.Resize(ref blocks, blocks.Length + 1);
                blocks[^1] = memory.Block(blocks.Length - 1);

                current = blocks[^1];
            }
        }

        public bool Contains(short block)
        {
            return obsolete.Contains(block);
        }

        public InMemoryAllocation Allocate(int size)
        {
            InMemoryBlock block = current;
            int offset = block.Allocate(size);

            if (offset == -1)
            {
                block = Find(size);
                offset = block.Allocate(size);
            }

            return block.Extract(offset, size);
        }

        private InMemoryBlock Find(int size)
        {
            for (int i = blocks.Length - 1; i >= 0; i--)
            {
                if (blocks[i] == null)
                    return current = blocks[i] = memory.Block(i);

                if (blocks[i].GetLeft() >= size)
                    return current = blocks[i];
            }

            Array.Resize(ref blocks, blocks.Length + 1);
            blocks[^1] = memory.Block(blocks.Length - 1);

            return current = blocks[^1];
        }

        public InMemoryManager Create()
        {
            for (int i = 0; i < abandoned.Length; i++)
                if (abandoned[i] != null)
                    abandoned[i].Free(memory);

            return new InMemoryManager(memory, builder, blocks, files);
        }
    }

    public class InMemoryManager
    {
        private readonly InMemory memory;
        private InMemoryBlock[] blocks;
        private ValueFile[] files;
        private ValueBuilder builder;
        private InMemoryBlock available;
        private short current;
        private long bytes;

        public InMemoryManager(InMemory memory)
        {
            this.memory = memory;
            this.files = new ValueFile[0];
            this.current = -1;
            this.builder = memory.Builder(-1);
            this.available = memory.Block(0);
            this.blocks = new InMemoryBlock[] { available };
            this.bytes = memory.GetSize();
        }

        public InMemoryManager(InMemory memory, ValueBuilder builder, InMemoryBlock[] blocks, ValueFile[] files)
        {
            this.memory = memory;
            this.builder = builder;
            this.current = builder.GetIndex();
            this.files = files;
            this.available = blocks[0];
            this.blocks = blocks;
            this.bytes = 0;

            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i] != null)
                    bytes += memory.GetSize();
        }

        public long SizeInBytes()
        {
            return bytes + memory.SizeInBytes() + (builder != null ? memory.GetSize() : 0);
        }

        public long GetDeclaredMemory()
        {
            return bytes;
        }

        public long GetUsedMemory()
        {
            long memory = 0;

            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i] != null)
                    memory += blocks[i].GetUsed();

            return memory;
        }

        public InMemoryEvolution Evolve(double factor)
        {
            InMemoryBlock[] taken = new InMemoryBlock[blocks.Length];
            InMemoryBlock[] abandoned = new InMemoryBlock[blocks.Length];

            for (int i = 0; i < taken.Length; i++)
            {
                if (1.0 * blocks[i].GetWasted() / memory.GetSize() <= factor)
                    taken[i] = blocks[i];
                else
                    abandoned[i] = blocks[i];
            }

            return new InMemoryEvolution(memory, builder, taken, abandoned, files);
        }

        public InMemoryAllocation Allocate(int size)
        {
            InMemoryBlock block = available;
            int offset = block.Allocate(size);

            if (offset == -1)
            {
                block = Find(size);
                offset = block.Allocate(size);
            }

            return block.Extract(offset, size);
        }

        private InMemoryBlock Find(int size)
        {
            for (int i = blocks.Length - 1; i >= 0; i--)
            {
                if (blocks[i] == null)
                {
                    bytes += memory.GetSize();
                    blocks[i] = memory.Block(i);;
                    return available = blocks[i];
                }

                if (blocks[i].GetLeft() >= size)
                    return available = blocks[i];
            }

            Array.Resize(ref blocks, blocks.Length + 1);
            blocks[^1] = memory.Block(blocks.Length - 1);

            bytes += memory.GetSize();
            return available = blocks[^1];
        }

        public TreeReference Extract(TreeNode node)
        {
            if (node.Block >= 0) return blocks[node.Block].GetTree(node);
            else if (node.Block == current) return builder.GetTree(node);
            else return files[-node.Block - 1].GetTree(node);
        }

        public ValueInfo Extract(ref ValueNode node)
        {
            if (node.Block >= 0) return blocks[node.Block].Extract(node);
            else if (node.Block == current) return builder.Extract(node);
            else return files[-node.Block - 1].Extract(node);
        }

        public void Overwrite(ref ValueNode node, ValueInfo value)
        {
            if (node.Block >= 0) blocks[node.Block].Overwrite(node, value);
            else if (node.Block == current) builder.Overwrite(node, value);
            else
            {
                node.Offset = Prepare(node.Length).Append(value);
                node.Block = current;
            }
        }

        public void Remove(TreeNode node)
        {
            if (node.Block >= 0) blocks[node.Block].Remove(node);
            else if (node.Block == current) builder.Remove(node);
        }

        public void Remove(ValueNode node)
        {
            if (node.Block >= 0) blocks[node.Block].Remove(node);
            else if (node.Block == current) builder.Remove(node);
        }

        public int Archive(ref TreeNode node)
        {
            var reference = blocks[node.Block].Remove(node);
            int offset = Prepare(reference.Length).Append(reference);

            node.Offset = offset;
            node.Block = current;

            return reference.Length;
        }

        public int Archive(ref ValueNode node)
        {
            var reference = blocks[node.Block].Remove(node);
            int offset = Prepare(reference.Length).Append(reference);

            node.Offset = offset;
            node.Block = current;

            return reference.Length;
        }

        private ValueBuilder Prepare(int size)
        {
            if (builder.GetLeft() >= size) return builder;

            Array.Resize(ref files, files.Length + 1);
            files[^1] = builder.Flush(memory);

            current = (short)(-files.Length - 1);
            return builder = memory.Builder(current);
        }
    }

    public class InMemoryBlock
    {
        private readonly int block;
        private readonly byte[] data;
        private int offset;
        private int wasted;

        public InMemoryBlock(int block, byte[] data)
        {
            this.block = block;
            this.data = data;
        }

        public short GetIndex()
        {
            return (short)block;
        }

        public int GetUsed()
        {
            return offset - wasted;
        }

        public int GetLeft()
        {
            return data.Length - offset;
        }

        public int GetWasted()
        {
            return wasted;
        }

        public void Free(InMemory memory)
        {
            memory.Release(data);
        }

        /// <summary>
        /// Retries the tree at the given offset. The method does not check
        /// the available data. It relies on its consistency.
        /// </summary>
        public TreeReference GetTree(TreeNode node)
        {
            return new TreeReference(node, data);
        }

        /// <summary>
        /// Extracts the value node into bytes view.
        /// </summary>
        /// <returns>
        /// The readonly binary view.
        /// </returns>
        public ValueInfo Extract(ValueNode node)
        {
            return new ValueInfo(data, node.Offset, node.Length);
        }

        public InMemoryAllocation Extract(int offset, int size)
        {
            return new InMemoryAllocation(block, data, offset, size);
        }

        public int Allocate(int size)
        {
            if (offset + size > data.Length) return -1;
            else return (offset += size) - size;
        }

        /// <summary>
        /// Overwrites value data with new values in place. The code does not
        /// check if new data has less bytes, but relies on this condition.
        /// The operation may only increase the number of wasted bytes.
        /// </summary>
        public void Overwrite(ValueNode node, ValueInfo value)
        {
            wasted += node.Length - value.Length;
            Buffer.BlockCopy(value.Data, value.Offset, data, node.Offset, value.Length);
        }

        /// <summary>
        /// Remove tree from data. The operation does not clean the space.
        /// It only marks the occupied region as wasted. The values pointed
        /// by the tree are not affected and should be removed manually.
        /// </summary>
        public InMemoryReference Remove(TreeNode node)
        {
            int size = node.SizeInBytes(data, data[node.Offset]);
            InMemoryReference reference = new InMemoryReference(data, node.Offset, size);

            wasted += size;
            return reference;
        }

        /// <summary>
        /// Removes value from data. The operation does not clean the space.
        /// It only marks the occupied region as wasted.
        /// </summary>
        public InMemoryReference Remove(ValueNode node)
        {
            wasted += node.Length;
            return new InMemoryReference(data, node.Offset, node.Length);
        }
    }

    public class ValueBuilder
    {
        private readonly short block;
        private readonly string path;
        private readonly byte[] data;
        private int offset;
        private int wasted;

        public ValueBuilder(short block, string path, byte[] data)
        {
            this.block = block;
            this.path = path;
            this.data = data;
        }

        public bool IsEmpty()
        {
            return offset == 0;
        }

        public short GetIndex()
        {
            return block;
        }

        public int GetLeft()
        {
            return data.Length - offset;
        }

        /// <summary>
        /// Retries the tree at the given offset. The method does not check
        /// the available data. It relies on its consistency.
        /// </summary>
        public TreeReference GetTree(TreeNode node)
        {
            return new TreeReference(node, data);
        }

        /// <summary>
        /// Extracts the value node into bytes view.
        /// </summary>
        /// <returns>
        /// The readonly binary view.
        /// </returns>
        public ValueInfo Extract(ValueNode node)
        {
            return new ValueInfo(data, node.Offset, node.Length);
        }

        /// <summary>
        /// Overwrites value data with new values in place. The code does not
        /// check if new data has less bytes, but relies on this condition.
        /// The operation may only increase the number of wasted bytes.
        /// </summary>
        public void Overwrite(ValueNode node, ValueInfo value)
        {
            wasted += node.Length - value.Length;
            Buffer.BlockCopy(value.Data, value.Offset, data, node.Offset, value.Length);
        }

        /// <summary>
        /// Remove tree from data. The operation does not clean the space.
        /// It only marks the occupied region as wasted. The values pointed
        /// by the tree are not affected and should be removed manually.
        /// </summary>
        public InMemoryReference Remove(TreeNode node)
        {
            int size = node.SizeInBytes(data, data[node.Offset]);
            InMemoryReference reference = new InMemoryReference(data, node.Offset, size);

            wasted += size;
            return reference;
        }

        /// <summary>
        /// Removes value from data. The operation does not clean the space.
        /// It only marks the occupied region as wasted.
        /// </summary>
        public InMemoryReference Remove(ValueNode node)
        {
            wasted += node.Length;
            return new InMemoryReference(data, node.Offset, node.Length);
        }
        public int Append(InMemoryReference reference)
        {
            Buffer.BlockCopy(reference.Data, reference.Offset, data, offset, reference.Length);
            return (offset += reference.Length) - reference.Length;
        }

        public int Append(ValueInfo value)
        {
            Buffer.BlockCopy(value.Data, value.Offset, data, offset, value.Length);
            return (offset += value.Length) - value.Length;
        }

        public ValueFile Flush(InMemory memory)
        {
            FileOptions options = FileOptions.DeleteOnClose | FileOptions.RandomAccess | FileOptions.WriteThrough;
            FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, options);

            stream.Write(data, 0, offset);
            stream.Flush();

            memory?.Release(data);
            return new ValueFile(stream, offset);
        }
    }

    public class ValueFile
    {
        private readonly int size;
        private readonly FileStream stream;

        public ValueFile(FileStream stream, int size)
        {
            this.stream = stream;
            this.size = size;
        }

        public void Close()
        {
            this.stream.Close();
            this.stream.Dispose();
        }

        public TreeReference GetTree(TreeNode node)
        {
            int length = Math.Min(4096, size - node.Offset);
            byte[] data = new byte[length];

            stream.Seek(node.Offset, SeekOrigin.Begin);

            int read = 0, loops = 0;
            while (read < length && loops < 10)
            {
                loops++;
                read += stream.Read(data, read, length - read);
            }

            return new TreeReference(node.Shift(0), data);
        }

        public ValueInfo Extract(ValueNode node)
        {
            int length = node.Length;
            byte[] data = new byte[length];

            stream.Seek(node.Offset, SeekOrigin.Begin);

            int read = 0, loops = 0;
            while (read < length && loops < 10)
            {
                loops++;
                read += stream.Read(data, read, length - read);
            }

            return new ValueInfo(data, 0, (ushort)read);
        }
    }
}
