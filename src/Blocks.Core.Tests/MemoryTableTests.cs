using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Blocks.Core.Tests
{
    [TestFixture]
    public class MemoryTableTests
    {
        [Test]
        public void ReturnsNothingIfNotFound()
        {
            MemoryTable table = new MemoryTable("/tmp/blocks/", 1024, 24);
            ValueInfo data = table.Get(KeyInfo.From(new byte[] { 1, 2 }));

            Assert.True(data.Data == null);
            Assert.That(table.GetSize(), Is.EqualTo(0));
        }

        [Test]
        public void ReturnsAlreadyInsertedValue()
        {
            MemoryTable table = new MemoryTable("/tmp/blocks/", 1024, 24);
            table.Set(KeyValueInfo.From(new byte[] { 1, 2, 20, 30, 40 }, 2));

            ValueInfo data = table.Get(KeyInfo.From(new byte[] { 1, 2 }));
            Assert.True(data.AsSpan().SequenceEqual(new byte[] { 20, 30, 40 }));

            Assert.That(table.GetSize(), Is.EqualTo(1));
        }

        [Test]
        public void ReturnsAlreadyOverriddendValueWithShorter()
        {
            MemoryTable table = new MemoryTable("/tmp/blocks/", 1024, 24);
            table.Set(KeyValueInfo.From(new byte[] { 1, 2, 20, 30, 40 }, 2));
            table.Set(KeyValueInfo.From(new byte[] { 1, 2, 50, 60 }, 2));

            ValueInfo data = table.Get(KeyInfo.From(new byte[] { 1, 2 }));
            Assert.True(data.AsSpan().SequenceEqual(new byte[] { 50, 60 }));

            Assert.That(table.GetSize(), Is.EqualTo(1));
        }

        [Test]
        public void ReturnsAlreadyOverriddendValueWithLonger()
        {
            MemoryTable table = new MemoryTable("/tmp/blocks/", 1024, 24);
            table.Set(KeyValueInfo.From(new byte[] { 1, 2, 20, 30, 40 }, 2));
            table.Set(KeyValueInfo.From(new byte[] { 1, 2, 50, 60, 70, 80 }, 2));

            ValueInfo data = table.Get(KeyInfo.From(new byte[] { 1, 2 }));
            Assert.True(data.AsSpan().SequenceEqual(new byte[] { 50, 60, 70, 80 }));

            Assert.That(table.GetSize(), Is.EqualTo(1));
        }

        [Test]
        public void ResizesWhenCapacityCrossed()
        {
            Random random = new Random(0);
            MemoryTable table = new MemoryTable("/tmp/blocks/", 65536, 24);
            var native = new Dictionary<byte[], byte[]>();

            for (int i = 0; i < 150; i++)
            {
                byte[] data =  new byte[16];

                random.NextBytes(data.AsSpan(0, 8));
                random.NextBytes(data.AsSpan(8, 8));

                table.Set(new KeyValueInfo(data, 0, 8, 8));
                native.Add(data, data);
            }

            Assert.That(table.GetSize(), Is.EqualTo(150));
            Assert.That(table.GetCapacity(), Is.EqualTo(256));

            int counter = 0;
            foreach (byte[] key in native.Keys)
                if (table.Get(new KeyInfo(key, 0, 8)).Data != null) counter++;

            Assert.That(counter, Is.EqualTo(150));

            foreach (byte[] key in native.Keys)
            {
                ValueInfo fetched = table.Get(new KeyInfo(key, 0, 8));
                Assert.True(fetched.Data != null && fetched.Length == 8);
                Assert.True(fetched.AsSpan().SequenceEqual(key.AsSpan(8, 8)));
            }
        }
    }
}