using System;
using System.IO;

namespace Blocks.Core
{
    public class InMemory
    {
        private readonly string path;
        private readonly int size;

        public InMemory(string path, int size)
        {
            this.path = path;
            this.size = size;
        }

        public int GetSize()
        {
            return size;
        }

        public InMemoryBlock Block(int index)
        {
            return new InMemoryBlock(index, size);
        }

        public InMemoryManager Manager()
        {
            return new InMemoryManager(this);
        }

        public ValueBuilder Builder(short index)
        {
            return new ValueBuilder(index, Path.Combine(path, $"{Path.GetRandomFileName()}.tmp"), size);
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

    public class InMemoryManager
    {
        private readonly InMemory memory;
        private InMemoryBlock[] blocks;
        private ValueFile[] files;
        private ValueBuilder builder;

        public InMemoryManager(InMemory memory)
        {
            this.memory = memory;
            this.files = new ValueFile[0];
            this.builder = memory.Builder(-1);
            this.blocks = new InMemoryBlock[] { memory.Block(0) };
        }

        private InMemoryManager(InMemory memory, ValueBuilder builder, ValueFile[] files)
        {
            this.memory = memory;
            this.builder = builder;
            this.files = files;
            this.blocks = new InMemoryBlock[] { memory.Block(0) };
        }

        public void Dispose()
        {
        }

        public long GetDeclaredMemory()
        {
            return blocks.LongLength * memory.GetSize();
        }

        public long GetUsedMemory()
        {
            long memory = 0;

            for (int i = 0; i < blocks.Length; i++)
                memory += blocks[i].GetUsed();

            return memory;
        }

        public InMemoryManager Evolve()
        {
            return new InMemoryManager(memory, builder, files);
        }

        public InMemoryAllocation Allocate(int size)
        {
            InMemoryBlock block = blocks[^1];
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
            for (int i = blocks.Length - 2; i >= 0; i--)
                if (blocks[i].GetLeft() >= size)
                    return blocks[i];

            Array.Resize(ref blocks, blocks.Length + 1);
            blocks[^1] = memory.Block(blocks.Length - 1);

            return blocks[^1];
        }

        public TreeReference Extract(ref TreeNode node)
        {
            if (node.Block >= 0) return blocks[node.Block].GetTree(node);
            else if (node.Block == -files.Length - 1) return builder.GetTree(node);
            else return files[-node.Block - 1].GetTree(node);
        }

        public ValueInfo Extract(ref ValueNode node)
        {
            if (node.Block >= 0) return blocks[node.Block].Extract(node);
            else if (node.Block == -files.Length - 1) return builder.Extract(node);
            else return files[-node.Block - 1].Extract(node);
        }

        public void Overwrite(ref ValueNode node, ValueInfo value)
        {
            if (node.Block >= 0) blocks[node.Block].Overwrite(node, value);
            else if (node.Block == -files.Length - 1) builder.Overwrite(node, value);
            else
            {
                node.Offset = Prepare(node.Length).Append(value);
                node.Block = builder.GetIndex();
            }
        }

        public void Remove(TreeNode node)
        {
            if (node.Block >= 0) blocks[node.Block].Remove(node);
            else if (node.Block == -files.Length - 1) builder.Remove(node);
        }

        public void Remove(ValueNode node)
        {
            if (node.Block >= 0) blocks[node.Block].Remove(node);
            else if (node.Block == -files.Length - 1) builder.Remove(node);
        }

        public int Archive(ref TreeNode node)
        {
            var reference = blocks[node.Block].Remove(node);
            int offset = Prepare(reference.Length).Append(reference);

            node.Offset = offset;
            node.Block = builder.GetIndex();

            return reference.Length;
        }

        public int Archive(ref ValueNode node)
        {
            var reference = blocks[node.Block].Remove(node);
            int offset = Prepare(reference.Length).Append(reference);

            node.Offset = offset;
            node.Block = builder.GetIndex();

            return reference.Length;
        }

        private ValueBuilder Prepare(int size)
        {
            if (builder.GetLeft() >= size) return builder;

            Array.Resize(ref files, files.Length + 1);
            files[^1] = builder.Flush();

            return builder = memory.Builder((short)(-files.Length - 1));
        }
    }

    public class InMemoryBlock
    {
        private readonly int block;
        private readonly byte[] data;
        private int offset;
        private int wasted;

        public InMemoryBlock(int block, int size)
        {
            this.block = block;
            this.data = new byte[size];
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

        public ValueBuilder(short block, string path, int size)
        {
            this.block = block;
            this.path = path;
            this.data = new byte[size];
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

        public ValueFile Flush()
        {
            FileMode mode = FileMode.Create;
            FileStream stream = new FileStream(path, mode);

            stream.Write(data, 0, offset);
            stream.Flush();

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
