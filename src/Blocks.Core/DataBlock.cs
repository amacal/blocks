using System;
using System.IO;

namespace Blocks.Core
{
    public class InMemory
    {
        private readonly int size;

        public InMemory(int size)
        {
            this.size = size;
        }

        public KeyBlock Keys()
        {
            return new KeyBlock(size);
        }

        public ValueBlock Values()
        {
            return new ValueBlock(size);
        }
    }

    public class KeyBlock
    {
        private readonly byte[] binary;
        private int offset;
        private int wasted;

        public KeyBlock(int size)
        {
            this.binary = new byte[size];
        }

        public int GetUsed()
        {
            return offset - wasted;
        }

        public int GetWasted()
        {
            return wasted;
        }

        public int GetLeft()
        {
            return binary.Length - offset;
        }

        public int Hash(KeyReference reference, DataHasher hasher)
        {
            return hasher.Hash(new Span<byte>(binary, reference.Offset, reference.Length));
        }

        public bool Verify(KeyReference reference, byte[] key)
        {
            if (key.Length != reference.Length)
                return false;

            for (int i = 0, j = reference.Offset; i < key.Length; i++, j++)
                if (key[i] != binary[j]) return false; 

            return true;
        }

        public int Insert(byte[] data)
        {
            Buffer.BlockCopy(data, 0, binary, offset, data.Length);
            offset += data.Length;
            return offset - data.Length;
        }
    }

    public class ValueBlock
    {
        private readonly byte[] binary;
        private int offset;
        private int wasted;

        public ValueBlock(int size)
        {
            this.binary = new byte[size];
        }

        public int GetUsed()
        {
            return offset - wasted;
        }

        public int GetWasted()
        {
            return wasted;
        }

        public int GetLeft()
        {
            return binary.Length - offset;
        }

        public byte[] Extract(ValueReference reference)
        {
            int length = reference.Length;
            byte[] value = new byte[length];

            Buffer.BlockCopy(binary, reference.Offset, value, 0, length);
            return value;
        }

        public void Remove(ValueReference reference)
        {
            bool tail = reference.IsTail(offset);
            if (tail) offset -= reference.Length;
            else wasted += reference.Length;
        }

        public void Update(ValueReference reference, byte[] value)
        {
            bool tail = reference.IsTail(offset);
            int diff = reference.Length - value.Length;

            int position = reference.Offset;
            Buffer.BlockCopy(value, 0, binary, position, value.Length);

            if (tail) offset -= diff;
            else wasted += diff;
        }

        public int Insert(byte[] data)
        {
            if (offset + data.Length > binary.Length) return -1;
            Buffer.BlockCopy(data, 0, binary, offset, data.Length);
            
            offset += data.Length;
            return offset - data.Length;
        }
    }
    public class InFile
    {
        private readonly int size;

        public InFile(int size)
        {
            this.size = size;
        }

        public ValueBuilder Builder()
        {
            return new ValueBuilder($"/tmp/blocks/${Path.GetRandomFileName()}.tmp", size);
        }
    }

    public class ValueBuilder
    {
        private readonly string path;
        private readonly byte[] binary;
        private int offset;

        public ValueBuilder(string path, int size)
        {
            this.path = path;
            this.binary = new byte[size];
        }

        public bool IsEmpty()
        {
            return offset == 0;
        }

        public int Insert(byte[] data)
        {
            if (offset + data.Length > binary.Length) return -1;
            Buffer.BlockCopy(data, 0, binary, offset, data.Length);

            offset += data.Length;
            return offset - data.Length;
        }

        public ValueFile Flush()
        {
            FileMode mode = FileMode.Create;
            FileStream stream = new FileStream(path, mode);

            stream.Write(binary, 0, offset);
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

        public byte[] Extract(ValueReference reference)
        {
            int length = reference.Length;
            byte[] value = new byte[length];

            stream.Seek(reference.Offset, SeekOrigin.Begin);
            
            int read = 0, loops = 0;
            while (read < value.Length && loops < 10)
            {
                loops++;
                read += stream.Read(value, read, value.Length - read);
            }

            return read == value.Length ? value : throw new InvalidOperationException($"{reference.Offset}:{reference.Length}:{read}:{loops}:{stream.Length}:{size}");
        }
    }
}
