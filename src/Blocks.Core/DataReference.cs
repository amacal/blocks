namespace Blocks.Core
{
    public struct DataReference
    {
        public short Block;
        public int Offset;
        public byte KeyLength;
        public ushort ValueLength;

        public int GetEnd()
        {
            return Offset + GetLength();
        }

        public int GetLength()
        {
            return KeyLength + ValueLength;
        }

        public int GetValueOffset()
        {
            return Offset + KeyLength;
        }
    }
}
