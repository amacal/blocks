using System;
using NUnit.Framework;

namespace Blocks.Core.Tests
{
    [TestFixture]
    public class TreeBlockTests
    {
        public void CanOverrideExistingValue()
        {
            InMemoryBlock block = new InMemoryBlock(13, new byte[1024]);
            int index = block.Allocate(5);

            ValueNode node = new ValueNode { Offset = index, Length = 5 };
            block.Overwrite(node, ValueInfo.From(new byte[] { 50, 60 }));

            ValueInfo value = block.Extract(node);
            Assert.True(value.AsSpan().SequenceEqual(new byte[] { 50, 60 }));

            Assert.That(block.GetWasted(), Is.EqualTo(3));
            Assert.That(block.GetLeft(), Is.EqualTo(1024 - 5));
        }

        [Test]
        public void CanRemoveExistingValue()
        {
            InMemoryBlock block = new InMemoryBlock(13, new byte[1024]);
            int index = block.Allocate(5);

            ValueNode node = new ValueNode { Offset = index, Length = 5 };
            block.Remove(node);

            Assert.That(block.GetWasted(), Is.EqualTo(5));
            Assert.That(block.GetLeft(), Is.EqualTo(1024 - 5));
        }

        [Test]
        public void CanRemoveExistingTree()
        {
            InMemoryBlock block = new InMemoryBlock(13, new byte[1024]);
            TreeInsert insert = TreeReference.Insert(KeyValueInfo.From(new byte[] { 1, 2, 10, 20, 30 }, 2));

            int allocated = block.Allocate(insert.GetSize());
            var allocation = block.Extract(allocated, insert.GetSize());

            TreeReference tree = insert.Apply(allocation);
            block.Remove(tree.Node);

            Assert.That(block.GetWasted(), Is.EqualTo(12));
            Assert.That(block.GetLeft(), Is.EqualTo(1024 - 15));
        }
    }
}
