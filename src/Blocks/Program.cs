using System;
using Blocks.Core;

namespace Blocks
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            long read = 0, written = 0, count = 0;
            long started = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Settings settings = new Settings
            {
                Path = args.Length > 0 ? args[0] : "/tmp/blocks/",
                BlockSize = 64 * 1048576,
                BlockMemory = 8 * 1024 * 1048576L,
                InitialDepth = 20,
                MaximalDepth = 26,
                OnArchiving = () => Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - started}: archiving"),
                OnRewritting = () => Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - started}: rewritting"),
                OnRewritten = () => Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - started}: rewritten"),
            };

            Random randomizer = new Random(0);
            MemoryTable table = new MemoryTable(settings);

            byte[] data = new byte[480];

            for (int i = 0; i < 1000000000; i++)
            {
                int keySize = randomizer.Next(4, 4);
                int valueSize = randomizer.Next(32, 76);

                randomizer.NextBytes(data.AsSpan(0, keySize + valueSize));
                KeyValueInfo keyValue = new KeyValueInfo(data, 0, (byte)keySize, (ushort)valueSize);

                table.Set(keyValue);
                written += valueSize;
                count++;

                if (i % 1000000 + 1 == 1000000)
                {
                    for (int j = 0; j < 1000000; j++)
                    {
                        count++;
                        randomizer.NextBytes(data.AsSpan(0, 4));
                        var value = table.Get(new KeyInfo(data, 0, 4));
                        if (value.Data != null) read += value.Length;
                    }

                    long total = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - started;
                    int speed = (int)(count / (total + 1));

                    Console.Write($"{total}, size: {table.GetSize()}, depth: {table.GetDepth()}, {speed/1024} Kop/s,");
                    Console.WriteLine($" read: {read/1048576} MB, written: {written/1048576} MB, declared: {table.SizeInBytes()/1048576L} MB");
                }
            }
        }
    }
}
