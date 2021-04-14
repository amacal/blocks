using System;

namespace Blocks.Core
{
    public struct DataNode
    {
        public int Next;
        public DataReference Reference;
    }

    public class DataNodeBitmap
    {
        private int offset;
        private DataNode[][] values;

        public DataNodeBitmap()
        {
            this.offset = 0;
            this.values = new DataNode[0][];
        }

        public int Size()
        {
            return values.Length * 1048576;
        }

        public int Allocate()
        {
            int result = ++offset;
            int batch = result / 1048576;

            if (batch >= values.Length)
            {
                Array.Resize(ref values, values.Length + 1);
                values[^1] = new DataNode[1048576];
            }

            return result;
        }

        public ref DataNode Get(int index)
        {
            return ref values[index / 1048576][index % 1048576];
        }

        public int Next(int index)
        {
            return values[index / 1048576][index % 1048576].Next;
        }
    }
}
