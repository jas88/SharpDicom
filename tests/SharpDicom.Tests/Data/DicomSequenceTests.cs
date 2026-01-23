using NUnit.Framework;
using System;
using System.Linq;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomSequenceTests
    {
        [Test]
        public void DicomSequence_EmptySequence_HasZeroItems()
        {
            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), Array.Empty<DicomDataset>());

            Assert.That(sequence.Items.Count, Is.EqualTo(0));
            Assert.That(sequence.IsEmpty, Is.True);
        }

        [Test]
        public void DicomSequence_WithItems_ReturnsItems()
        {
            var dataset1 = new DicomDataset();
            dataset1.Add(new DicomStringElement(new DicomTag(0x0008, 0x1150), DicomVR.UI, Encoding.ASCII.GetBytes("1.2.3.4")));

            var dataset2 = new DicomDataset();
            dataset2.Add(new DicomStringElement(new DicomTag(0x0008, 0x1155), DicomVR.UI, Encoding.ASCII.GetBytes("5.6.7.8")));

            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), new[] { dataset1, dataset2 });

            Assert.That(sequence.Items.Count, Is.EqualTo(2));
            Assert.That(sequence.IsEmpty, Is.False);
        }

        [Test]
        public void DicomSequence_ItemsAreReadOnly()
        {
            var dataset = new DicomDataset();
            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), new[] { dataset });

            Assert.That(sequence.Items, Is.InstanceOf<System.Collections.Generic.IReadOnlyList<DicomDataset>>());
        }

        [Test]
        public void DicomSequence_ToOwned_CreatesDeepCopy()
        {
            var dataset = new DicomDataset();
            dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x1150), DicomVR.UI, Encoding.ASCII.GetBytes("1.2.3.4")));

            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), new[] { dataset });

            var owned = sequence.ToOwned() as DicomSequence;

            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.Tag, Is.EqualTo(sequence.Tag));
            Assert.That(owned.Items.Count, Is.EqualTo(sequence.Items.Count));

            // Verify deep copy - modify original dataset
            dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN, Encoding.ASCII.GetBytes("Test")));

            Assert.That(sequence.Items[0].Count, Is.EqualTo(2)); // Original modified
            Assert.That(owned.Items[0].Count, Is.EqualTo(1));    // Copy unchanged
        }

        [Test]
        public void DicomSequence_NestedSequences_WorkCorrectly()
        {
            var innerDataset = new DicomDataset();
            innerDataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x1150), DicomVR.UI, Encoding.ASCII.GetBytes("1.2.3.4")));

            var innerSequence = new DicomSequence(new DicomTag(0x0040, 0x0260), new[] { innerDataset });

            var outerDataset = new DicomDataset();
            outerDataset.Add(innerSequence);

            var outerSequence = new DicomSequence(new DicomTag(0x0040, 0x0275), new[] { outerDataset });

            Assert.That(outerSequence.Items.Count, Is.EqualTo(1));
            Assert.That(outerSequence.Items[0].Count, Is.EqualTo(1));

            var retrievedInnerSeq = outerSequence.Items[0].GetSequence(new DicomTag(0x0040, 0x0260));
            Assert.That(retrievedInnerSeq, Is.Not.Null);
            Assert.That(retrievedInnerSeq!.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void DicomSequence_VR_IsSQ()
        {
            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), Array.Empty<DicomDataset>());

            Assert.That(sequence.VR, Is.EqualTo(DicomVR.SQ));
        }

        [Test]
        public void DicomSequence_Length_IsUndefined()
        {
            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), Array.Empty<DicomDataset>());

            Assert.That(sequence.Length, Is.EqualTo(-1));
        }

        [Test]
        public void DicomSequence_RawValue_IsEmpty()
        {
            var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), Array.Empty<DicomDataset>());

            Assert.That(sequence.RawValue.IsEmpty, Is.True);
        }

        [Test]
        public void DicomFragmentSequence_Properties_AreCorrect()
        {
            var offsetTable = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var fragment1 = new byte[] { 0x01, 0x02, 0x03 };
            var fragment2 = new byte[] { 0x04, 0x05, 0x06 };

            var sequence = new DicomFragmentSequence(
                new DicomTag(0x7FE0, 0x0010),
                DicomVR.OB,
                offsetTable,
                new ReadOnlyMemory<byte>[] { fragment1, fragment2 });

            Assert.That(sequence.Tag.Group, Is.EqualTo(0x7FE0));
            Assert.That(sequence.Tag.Element, Is.EqualTo(0x0010));
            Assert.That(sequence.VR, Is.EqualTo(DicomVR.OB));
            Assert.That(sequence.OffsetTable.Length, Is.EqualTo(4));
            Assert.That(sequence.Fragments.Count, Is.EqualTo(2));
        }

        [Test]
        public void DicomFragmentSequence_IsEmpty_ChecksFragments()
        {
            var emptySequence = new DicomFragmentSequence(
                new DicomTag(0x7FE0, 0x0010),
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                Array.Empty<ReadOnlyMemory<byte>>());

            Assert.That(emptySequence.IsEmpty, Is.True);

            var nonEmptySequence = new DicomFragmentSequence(
                new DicomTag(0x7FE0, 0x0010),
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new ReadOnlyMemory<byte>[] { new byte[] { 0x01 } });

            Assert.That(nonEmptySequence.IsEmpty, Is.False);
        }

        [Test]
        public void DicomFragmentSequence_ToOwned_CopiesFragments()
        {
            var offsetTable = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var fragment = new byte[] { 0x01, 0x02, 0x03 };

            var sequence = new DicomFragmentSequence(
                new DicomTag(0x7FE0, 0x0010),
                DicomVR.OB,
                offsetTable,
                new ReadOnlyMemory<byte>[] { fragment });

            var owned = sequence.ToOwned() as DicomFragmentSequence;

            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.Fragments.Count, Is.EqualTo(1));

            // Modify original - owned should be unaffected
            fragment[0] = 0xFF;
            Assert.That(sequence.Fragments[0].Span[0], Is.EqualTo(0xFF));
            Assert.That(owned.Fragments[0].Span[0], Is.EqualTo(0x01));
        }
    }
}
