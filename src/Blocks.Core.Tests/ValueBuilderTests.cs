using System;
using System.IO;
using NUnit.Framework;

namespace Blocks.Core.Tests
{
    [TestFixture]
    public class ValueBuilderTests
    {
        [SetUp]
        public void SetUp()
        {
            if (File.Exists("/tmp/value-builder-tests.bin"))
                File.Delete("/tmp/value-builder-tests.bin");
        }

        [Test]
        public void CanAppendTree()
        {
            InMemoryBlock block = new InMemoryBlock(13, new byte[1024]);
            KeyValueInfo keyValue = KeyValueInfo.From(new byte[] { 1, 2, 10, 20, 30 }, 2);
            TreeInsert insert = TreeReference.Insert(keyValue);

            int allocated = block.Allocate(insert.GetSize());
            InMemoryAllocation allocation = block.Extract(allocated, insert.GetSize());

            TreeReference tree = insert.Apply(allocation);
            ValueBuilder builder = new ValueBuilder(-20, "/tmp/value-builder-tests.bin", new byte[1024]);

            int index = builder.Append(new InMemoryReference(allocation.Data, allocation.Offset, allocation.Length));
            Assert.That(index, Is.GreaterThanOrEqualTo(0));

            ValueFile file = builder.Flush(null);
            Assert.That(file, Is.Not.Null);

            TreeReference fetched = file.GetTree(new TreeNode(0, index));
            Assert.That(fetched.Size(), Is.EqualTo(1));

            LinkNode node = fetched.GetLink(0);
            Assert.That(node.Verify(new byte[] { 1, 2 }));

            Assert.That(node.Value.Block, Is.EqualTo(13));
            Assert.That(node.Value.Length, Is.EqualTo(3));
        }
    }
}