using System;

namespace Blocks.Core
{
    public class DataBlock
    {
        public byte[] binary;
        public int offset;
        public int wasted;

        public int GetUsed()
        {
            return offset - wasted;
        }

        public int getWasted()
        {
            return wasted;
        }

        public int GetLeft()
        {
            return binary.Length - offset;
        }

        public bool Verify(ref DataReference reference, byte[] key)
        {
            if (key.Length != reference.KeyLength)
                return false;

            for (int i = 0, j = reference.Offset; i < key.Length; i++, j++)
                if (key[i] != binary[j]) return false; 

            return true;
        }

        public int Hash(DataReference reference, DataHasher hasher)
        {
            return hasher.Hash(new Span<byte>(binary, reference.Offset, reference.KeyLength));
        }

        public byte[] Extract(DataReference reference)
        {
            int length = reference.ValueLength;
            byte[] value = new byte[length];

            Buffer.BlockCopy(binary, reference.Offset, value, 0, length);
            return value;
        }

        public void Remove(DataReference reference)
        {
            bool tail = offset == reference.GetEnd();
            if (tail) offset -= reference.GetLength();
            else wasted += reference.GetLength();
        }

        public void Update(DataReference reference, byte[] value)
        {
            bool tail = offset == reference.GetEnd();
            int diff = reference.ValueLength - value.Length;

            int position = reference.GetValueOffset();
            Buffer.BlockCopy(value, 0, binary, position, value.Length);

            if (tail) offset -= diff;
            else wasted += diff;
        }

        public int Insert(byte[] key, byte[] value)
        {
            Buffer.BlockCopy(key, 0, binary, offset, key.Length);
            Buffer.BlockCopy(value, 0, binary, offset + key.Length, value.Length);

            offset += key.Length + value.Length;
            return offset - key.Length - value.Length;
        }
    }
}
