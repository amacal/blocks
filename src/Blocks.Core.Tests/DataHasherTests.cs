using NUnit.Framework;

namespace Blocks.Core.Tests
{
    [TestFixture]
    public class DataHasherTests
    {
        [Test]
        public void DistributesHashesEqually()
        {
            DataHasher hasher = new DataHasher(24, 24);
            int[] occurences = new int[hasher.GetCapacity()];

            for (int i = 0; i < 256 * 256 * 256; i++)
            {
                occurences[hasher.Hash(new byte[] { (byte)((i & 0xff0000) >> 16), (byte)((i & 0xff00) >> 8), (byte)(i % 0x00ff) }, 0, 3)]++;
            }

            for (int i = 0; i < occurences.Length; i++)
            {
                Assert.That(occurences[i], Is.LessThanOrEqualTo(10));
            }
        }
    }
}
