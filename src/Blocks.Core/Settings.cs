using System;

namespace Blocks.Core
{
    public class Settings
    {
        public string Path;
        public int BlockSize;
        public long BlockMemory;
        public int InitialDepth;
        public int MaximalDepth;

        public Action OnRewritting;
        public Action OnRewritten;
        public Action OnArchiving;
    }
}