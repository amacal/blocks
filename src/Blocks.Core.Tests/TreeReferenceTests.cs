using System;
using NUnit.Framework;

namespace Blocks.Core.Tests
{
    [TestFixture]
    public class TreeReferenceTests
    {
        private static readonly byte[] SingleNodeTreeData = new byte[]
        {
            255, 255,
            1, 3,
            100, 0, 0, 0, 1, 0, 200, 0,
            10, 11, 12,
        };

        private static readonly byte[] MultipleNodeTreeData = new byte[]
        {
            255, 255, 255,
            3, 3, 4, 7,
            100, 0, 0, 0, 1, 0, 200, 0,
            101, 1, 0, 1, 0, 0, 201, 0,
            102, 2, 7, 0, 0, 0, 202, 0,
            10, 11, 12,
            20, 21, 22, 23,
            30, 31, 32, 33, 34, 35, 36,
        };

        [Test]
        public void HandlesEmptyTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 0), new byte[] { 0 });

            Assert.That(reference.Size(), Is.EqualTo(0));
        }

        [Test]
        public void HandlesSingleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);

            Assert.That(reference.Size(), Is.EqualTo(1));
            LinkNode node = reference.GetLink(0);

            Assert.True(node.Verify(new byte[] { 10, 11, 12 }));
            Assert.That(node.Value.Block, Is.EqualTo(100));

            Assert.That(node.Value.Offset, Is.EqualTo(65536));
            Assert.That(node.Value.Length, Is.EqualTo(200));
        }

        [Test]
        public void HandlesMultipleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);

            Assert.That(reference.Size(), Is.EqualTo(3));
            LinkNode node0 = reference.GetLink(0);

            LinkNode node1 = reference.GetLink(1);
            LinkNode node2 = reference.GetLink(2);

            Assert.True(node0.Verify(new byte[] { 10, 11, 12 }));
            Assert.That(node0.Value.Block, Is.EqualTo(100));

            Assert.That(node0.Value.Offset, Is.EqualTo(65536));
            Assert.That(node0.Value.Length, Is.EqualTo(200));

            Assert.True(node1.Verify(new byte[] { 20, 21, 22, 23 }));
            Assert.That(node1.Value.Block, Is.EqualTo(357));

            Assert.That(node1.Value.Offset, Is.EqualTo(256));
            Assert.That(node1.Value.Length, Is.EqualTo(201));

            Assert.True(node2.Verify(new byte[] { 30, 31, 32, 33, 34, 35, 36 }));
            Assert.That(node2.Value.Block, Is.EqualTo(614));

            Assert.That(node2.Value.Offset, Is.EqualTo(7));
            Assert.That(node2.Value.Length, Is.EqualTo(202));
        }

        [Test]
        public void SplitsSingleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);

            DataHasher hasher = new DataHasher(4, 4);
            TreeSplit split = reference.Split(hasher);

            Assert.That(split.LeftSize(), Is.EqualTo(0));
            Assert.That(split.RightSize(), Is.EqualTo(13));
        }

        [Test]
        public void SplitsRightSingleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);

            DataHasher hasher = new DataHasher(4, 4);
            TreeSplit split = reference.Split(hasher);

            var allocation = new InMemoryAllocation(13, new byte[33], 20, 13);
            TreeReference right = split.Right(allocation);

            Assert.That(right.Size(), Is.EqualTo(1));
            Assert.That(right.SizeInBytes(), Is.EqualTo(13));

            LinkNode node = right.GetLink(0);
            Assert.True(node.Verify(new byte[] { 10, 11, 12 }));
            Assert.That(node.Value.Block, Is.EqualTo(100));

            Assert.That(node.Value.Offset, Is.EqualTo(65536));
            Assert.That(node.Value.Length, Is.EqualTo(200));
        }

        [Test]
        public void SplitsMultipleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);

            DataHasher hasher = new DataHasher(4, 4);
            TreeSplit split = reference.Split(hasher);

            Assert.That(split.LeftSize(), Is.EqualTo(2 + 8 + 4));
            Assert.That(split.RightSize(), Is.EqualTo(3 + 16 + 3 + 7));
       }

        [Test]
        public void SplitsRightMultipleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);

            DataHasher hasher = new DataHasher(4, 4);
            TreeSplit split = reference.Split(hasher);

            var allocation = new InMemoryAllocation(13, new byte[49], 20, 29);
            TreeReference right = split.Right(allocation);

            Assert.That(right.Size(), Is.EqualTo(2));
            Assert.That(right.SizeInBytes(), Is.EqualTo(29));

            LinkNode node0 = right.GetLink(0);
            LinkNode node2 = right.GetLink(1);

            Assert.True(node0.Verify(new byte[] { 10, 11, 12 }));
            Assert.That(node0.Value.Block, Is.EqualTo(100));

            Assert.That(node0.Value.Offset, Is.EqualTo(65536));
            Assert.That(node0.Value.Length, Is.EqualTo(200));

            Assert.True(node2.Verify(new byte[] { 30, 31, 32, 33, 34, 35, 36 }));
            Assert.That(node2.Value.Block, Is.EqualTo(614));

            Assert.That(node2.Value.Offset, Is.EqualTo(7));
            Assert.That(node2.Value.Length, Is.EqualTo(202));
        }

        [Test]
        public void SplitsLeftMultipleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);

            DataHasher hasher = new DataHasher(4, 4);
            TreeSplit split = reference.Split(hasher);

            var allocation = new InMemoryAllocation(13, new byte[34], 20, 14);
            TreeReference left = split.Left(allocation);

            Assert.That(left.Size(), Is.EqualTo(1));
            Assert.That(left.SizeInBytes(), Is.EqualTo(14));

            LinkNode node1 = left.GetLink(0);

            Assert.True(node1.Verify(new byte[] { 20, 21, 22, 23 }));
            Assert.That(node1.Value.Block, Is.EqualTo(357));

            Assert.That(node1.Value.Offset, Is.EqualTo(256));
            Assert.That(node1.Value.Length, Is.EqualTo(201));
        }

        [Test]
        public void InsertsSingleNodeTree()
        {
            KeyValueInfo keyValue = KeyValueInfo.From(new byte[] { 20, 30, 40, 100, 200 }, 3);
            TreeInsert insert = TreeReference.Insert(keyValue);

            Assert.That(insert.GetSize(), Is.EqualTo(15));
            var allocation = new InMemoryAllocation(13, new byte[35], 20, 15);

            TreeReference tree = insert.Apply(allocation);
            Assert.That(tree.Node.Block, Is.EqualTo(13));

            Assert.That(tree.SizeInBytes(), Is.EqualTo(13));
            Assert.That(tree.Size(), Is.EqualTo(1));

            LinkNode node = tree.GetLink(0);

            Assert.True(node.Verify(new byte[] { 20, 30, 40 }));
            Assert.That(node.Value.Block, Is.EqualTo(13));

            Assert.That(node.Value.Offset, Is.EqualTo(33));
            Assert.That(node.Value.Length, Is.EqualTo(2));
        }

        [Test]
        public void AppendsSingleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);
            KeyValueInfo keyValue = KeyValueInfo.From(new byte[] { 20, 30, 40, 100, 200 }, 3);

            TreeAppend append = reference.Append(keyValue);
            Assert.That(append.GetSize(), Is.EqualTo(27));

            var allocation = new InMemoryAllocation(13, new byte[47], 20, 27);
            TreeReference tree = append.Apply(allocation);

            Assert.That(tree.Node.Block, Is.EqualTo(13));
            Assert.That(tree.SizeInBytes(), Is.EqualTo(25));
            Assert.That(tree.Size(), Is.EqualTo(2));

            LinkNode node = tree.GetLink(1);

            Assert.True(node.Verify(new byte[] { 20, 30, 40 }));
            Assert.That(node.Value.Block, Is.EqualTo(13));

            Assert.That(node.Value.Offset, Is.EqualTo(45));
            Assert.That(node.Value.Length, Is.EqualTo(2));
        }

        [Test]
        public void AppendsMultiNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);
            KeyValueInfo keyValue = KeyValueInfo.From(new byte[] { 20, 30, 40, 100, 200 }, 3);

            TreeAppend append = reference.Append(keyValue);
            Assert.That(append.GetSize(), Is.EqualTo(56));

            var allocation = new InMemoryAllocation(13, new byte[76], 20, 56);
            TreeReference tree = append.Apply(allocation);

            Assert.That(tree.Node.Block, Is.EqualTo(13));
            Assert.That(tree.SizeInBytes(), Is.EqualTo(54));
            Assert.That(tree.Size(), Is.EqualTo(4));

            LinkNode node = tree.GetLink(3);

            Assert.True(node.Verify(new byte[] { 20, 30, 40 }));
            Assert.That(node.Value.Block, Is.EqualTo(13));

            Assert.That(node.Value.Offset, Is.EqualTo(74));
            Assert.That(node.Value.Length, Is.EqualTo(2));
        }

        [Test]
        public void ModifiesSingleNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);
            TreeModify modify = reference.Modify(0, ValueInfo.From(new byte[] { 100, 200 }));

            Assert.That(modify.GetSize(), Is.EqualTo(15));

            var allocation = new InMemoryAllocation(13, new byte[35], 20, 15);
            TreeReference tree = modify.Apply(allocation);

            Assert.That(tree.Node.Block, Is.EqualTo(13));
            Assert.That(tree.SizeInBytes(), Is.EqualTo(13));
            Assert.That(tree.Size(), Is.EqualTo(1));

            LinkNode node = tree.GetLink(0);

            Assert.True(node.Verify(new byte[] { 10, 11, 12 }));
            Assert.That(node.Value.Block, Is.EqualTo(13));

            Assert.That(node.Value.Offset, Is.EqualTo(33));
            Assert.That(node.Value.Length, Is.EqualTo(2));
        }

        [Test]
        public void ModifiesMultiNodeTree()
        {
            TreeReference reference = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);
            TreeModify modify = reference.Modify(1, ValueInfo.From(new byte[] { 100, 200 }));

            Assert.That(modify.GetSize(), Is.EqualTo(44));

            var allocation = new InMemoryAllocation(13, new byte[64], 20, 44);
            TreeReference tree = modify.Apply(allocation);

            Assert.That(tree.Node.Block, Is.EqualTo(13));
            Assert.That(tree.SizeInBytes(), Is.EqualTo(42));
            Assert.That(tree.Size(), Is.EqualTo(3));

            LinkNode node = tree.GetLink(1);

            Assert.True(node.Verify(new byte[] { 20, 21, 22, 23 }));
            Assert.That(node.Value.Block, Is.EqualTo(13));

            Assert.That(node.Value.Offset, Is.EqualTo(62));
            Assert.That(node.Value.Length, Is.EqualTo(2));
        }

        [Test]
        public void DetectSingleNodeTreeMemoryResidence()
        {
            TreeReference tree = new TreeReference(new TreeNode(1, 2), SingleNodeTreeData);

            Assert.That(tree.Size(), Is.EqualTo(1));
            Assert.That(tree.InMemory(0), Is.True);
        }

        [Test]
        public void DetectSingleNodeTreeMemoryResidenceHighest()
        {
            TreeReference tree = new TreeReference(new TreeNode(1, 2), (byte[])SingleNodeTreeData.Clone());
            Assert.That(tree.Size(), Is.EqualTo(1));

            ref ValueNode node = ref tree.GetValue(0);
            node.Block = Int16.MaxValue;

            Assert.That(tree.InMemory(0), Is.True);
        }

        [Test]
        public void DetectMultipleNodeTreeMemoryResidence()
        {
            TreeReference tree = new TreeReference(new TreeNode(1, 3), MultipleNodeTreeData);
            int size = tree.Size();

            for (int i = 0; i < size; i++)
                Assert.That(tree.InMemory(i), Is.True);
        }

        [Test]
        public void DetectSingleNodeTreeFileResidence()
        {
            TreeReference tree = new TreeReference(new TreeNode(1, 2), (byte[])SingleNodeTreeData.Clone());
            Assert.That(tree.Size(), Is.EqualTo(1));

            ref ValueNode node = ref tree.GetValue(0);
            node.Block = -17;

            Assert.That(tree.InMemory(0), Is.False);
        }
    }
}