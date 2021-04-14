using System;
using Blocks.Core;

namespace Blocks
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Random randomizer = new Random();
            DataDictionary dictionary = new DataDictionary();

            for (int i = 0; i < 200000000; i++)
            {
                byte[] key = new byte[randomizer.Next(4, 4)];
                byte[] value = new byte[randomizer.Next(32, 76)];

                randomizer.NextBytes(key);
                randomizer.NextBytes(value);

                dictionary.Set(key, value);

                if (i % 10000000 == 0)
                    Console.WriteLine(dictionary.Describe());
            }

            Console.WriteLine(dictionary.Describe());
            Console.WriteLine("Completed");
        }
    }
}
