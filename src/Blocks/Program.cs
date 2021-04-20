using System;
using Blocks.Core;

namespace Blocks
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            long counter = 0;
            Random randomizer = new Random();
            DataDictionary dictionary = new DataDictionary();

            for (int i = 0; i < 500000000; i++)
            {
                int reads = randomizer.Next(0, 10000000);
                byte[] key = new byte[randomizer.Next(4, 4)];
                byte[] value = new byte[randomizer.Next(32, 76)];

                randomizer.NextBytes(key);
                randomizer.NextBytes(value);

                dictionary.Set(key, value);

                if (i % 10000000 + 1 == 10000000)
                {
                    for (int j = 0; j < 10000000; j++)
                    {
                        key = new byte[randomizer.Next(4, 4)];
                        randomizer.NextBytes(key);

                        if (dictionary.Get(key) != null)
                            counter++;
                    }

                    Console.Write(dictionary.Describe());
                    Console.WriteLine($" {counter}");
                }
            }

            Console.Write(dictionary.Describe());
            Console.WriteLine($" {counter}");
        }
    }
}
