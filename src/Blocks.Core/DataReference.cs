using System.Runtime.InteropServices;

namespace Blocks.Core
{
    [StructLayout(LayoutKind.Explicit, Size=7)]
    public struct KeyReference
    {
        [FieldOffset(0)] public short Block;
        [FieldOffset(2)] public int Offset;
        [FieldOffset(6)] public byte Length;

        public bool IsTail(int offset)
        {
            return offset == Offset + Length;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size=8)]
    public struct ValueReference
    {
        [FieldOffset(0)] public short Block;
        [FieldOffset(2)] public int Offset;
        [FieldOffset(6)] public ushort Length;

        public bool IsTail(int offset)
        {
            return offset == Offset + Length;
        }
    }
}
