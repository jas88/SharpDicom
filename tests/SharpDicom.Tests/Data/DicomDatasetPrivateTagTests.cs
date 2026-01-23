using NUnit.Framework;
using SharpDicom.Data;
using System;
using System.Linq;
using System.Text;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomDatasetPrivateTagTests
    {
        [Test]
        public void StripPrivateTags_RemovesAllPrivate()
        {
            var dataset = new DicomDataset();

            // Add public tag
            dataset.Add(new DicomStringElement(
                DicomTag.PatientName, DicomVR.PN, Encoding.ASCII.GetBytes("Test^Patient  ")));

            // Add private creator and data
            dataset.AddPrivateString(0x0019, "MY CREATOR", 0x01, DicomVR.LO, "Private Value");

            // Verify private tag exists
            Assert.That(dataset.Count(), Is.EqualTo(3)); // PatientName + creator + data

            // Strip
            dataset.StripPrivateTags();

            // Verify only public remains
            Assert.That(dataset.Count(), Is.EqualTo(1));
            Assert.That(dataset[DicomTag.PatientName], Is.Not.Null);
        }

        [Test]
        public void StripPrivateTags_WithFilter_KeepsMatchingCreator()
        {
            var dataset = new DicomDataset();

            // Add two private creators
            dataset.AddPrivateString(0x0019, "KEEP ME", 0x01, DicomVR.LO, "Keep This");
            dataset.AddPrivateString(0x0019, "REMOVE ME", 0x01, DicomVR.LO, "Remove This");

            // Strip with filter that keeps "KEEP ME"
            dataset.StripPrivateTags(creator => creator == "KEEP ME");

            // Verify KEEP ME data remains
            var creators = dataset.PrivateCreators.GetAll().ToList();
            Assert.That(creators.Count, Is.EqualTo(1));
            Assert.That(creators[0].Creator, Is.EqualTo("KEEP ME"));
        }

        [Test]
        public void StripPrivateTags_ProcessesSequences()
        {
            var dataset = new DicomDataset();
            var item = new DicomDataset();

            // Add private tag to sequence item
            item.AddPrivateString(0x0019, "NESTED CREATOR", 0x01, DicomVR.LO, "Nested Value");

            // Add sequence to main dataset - use ContentSequence which is (0040,A730)
            var contentSequenceTag = new DicomTag(0x0040, 0xA730);
            var sequence = new DicomSequence(contentSequenceTag, new[] { item });
            dataset.Add(sequence);

            // Strip private tags
            dataset.StripPrivateTags();

            // Verify sequence item's private tags are also stripped
            var seq = dataset.GetSequence(contentSequenceTag);
            Assert.That(seq, Is.Not.Null);
            var strippedItem = seq!.Items.First();
            Assert.That(strippedItem.Any(e => e.Tag.IsPrivate), Is.False);
        }

        [Test]
        public void StripPrivateTags_WithFilter_ProcessesSequences()
        {
            var dataset = new DicomDataset();
            var item = new DicomDataset();

            // Add private tags to sequence item
            item.AddPrivateString(0x0019, "KEEP ME", 0x01, DicomVR.LO, "Keep This");
            item.AddPrivateString(0x0019, "REMOVE ME", 0x01, DicomVR.LO, "Remove This");

            // Add sequence to main dataset
            var contentSequenceTag = new DicomTag(0x0040, 0xA730);
            var sequence = new DicomSequence(contentSequenceTag, new[] { item });
            dataset.Add(sequence);

            // Strip with filter
            dataset.StripPrivateTags(creator => creator == "KEEP ME");

            // Verify nested item kept the right creator
            var seq = dataset.GetSequence(contentSequenceTag);
            var seqItem = seq!.Items.First();
            var creators = seqItem.PrivateCreators.GetAll().ToList();
            Assert.That(creators.Count, Is.EqualTo(1));
            Assert.That(creators[0].Creator, Is.EqualTo("KEEP ME"));
        }

        [Test]
        public void AddPrivateElement_AllocatesSlot()
        {
            var dataset = new DicomDataset();

            var tag = dataset.AddPrivateString(0x0019, "MY CREATOR", 0x10, DicomVR.LO, "Test Value");

            Assert.That(tag.Group, Is.EqualTo(0x0019));
            Assert.That(tag.Element, Is.EqualTo(0x1010)); // Slot 0x10 + offset 0x10

            // Verify creator was added
            var creatorTag = new DicomTag(0x0019, 0x0010);
            Assert.That(dataset[creatorTag], Is.Not.Null);
        }

        [Test]
        public void AddPrivateElement_ReusesSameCreatorSlot()
        {
            var dataset = new DicomDataset();

            var tag1 = dataset.AddPrivateString(0x0019, "MY CREATOR", 0x01, DicomVR.LO, "Value 1");
            var tag2 = dataset.AddPrivateString(0x0019, "MY CREATOR", 0x02, DicomVR.LO, "Value 2");

            // Both should use same slot (0x10)
            Assert.That(tag1.Element >> 8, Is.EqualTo(0x10));
            Assert.That(tag2.Element >> 8, Is.EqualTo(0x10));

            // Different offsets
            Assert.That(tag1.Element & 0xFF, Is.EqualTo(0x01));
            Assert.That(tag2.Element & 0xFF, Is.EqualTo(0x02));
        }

        [Test]
        public void AddPrivateElement_DifferentCreators_DifferentSlots()
        {
            var dataset = new DicomDataset();

            var tag1 = dataset.AddPrivateString(0x0019, "CREATOR A", 0x01, DicomVR.LO, "Value A");
            var tag2 = dataset.AddPrivateString(0x0019, "CREATOR B", 0x01, DicomVR.LO, "Value B");

            // Different slots
            Assert.That(tag1.Element >> 8, Is.EqualTo(0x10));
            Assert.That(tag2.Element >> 8, Is.EqualTo(0x11));
        }

        [Test]
        public void AddPrivateElement_EvenGroup_Throws()
        {
            var dataset = new DicomDataset();

            Assert.Throws<ArgumentException>(() =>
                dataset.AddPrivateString(0x0018, "CREATOR", 0x01, DicomVR.LO, "Value"));
        }

        [Test]
        public void AddPrivateElement_BinaryVR_UsesDicomBinaryElement()
        {
            var dataset = new DicomDataset();
            var value = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            var tag = dataset.AddPrivateElement(0x0019, "CREATOR", 0x01, DicomVR.OB, value);

            var element = dataset[tag];
            Assert.That(element, Is.InstanceOf<DicomBinaryElement>());
            Assert.That(element!.RawValue.ToArray(), Is.EqualTo(value));
        }

        [Test]
        public void AddPrivateElement_NumericVR_UsesDicomNumericElement()
        {
            var dataset = new DicomDataset();
            var value = BitConverter.GetBytes((ushort)42);

            var tag = dataset.AddPrivateElement(0x0019, "CREATOR", 0x01, DicomVR.US, value);

            var element = dataset[tag];
            Assert.That(element, Is.InstanceOf<DicomNumericElement>());
        }

        [Test]
        public void FindOrphanPrivateElements_DetectsOrphans()
        {
            var dataset = new DicomDataset();

            // Manually add private data without creator
            var orphanTag = new DicomTag(0x0019, 0x1010);
            dataset.Add(new DicomStringElement(orphanTag, DicomVR.LO, Encoding.ASCII.GetBytes("Orphan")));

            var orphans = dataset.FindOrphanPrivateElements();

            Assert.That(orphans.Count, Is.EqualTo(1));
            Assert.That(orphans[0], Is.EqualTo(orphanTag));
        }

        [Test]
        public void FindOrphanPrivateElements_NoOrphans_ReturnsEmpty()
        {
            var dataset = new DicomDataset();

            // Add proper private element with creator
            dataset.AddPrivateString(0x0019, "MY CREATOR", 0x01, DicomVR.LO, "Value");

            var orphans = dataset.FindOrphanPrivateElements();

            Assert.That(orphans, Is.Empty);
        }

        [Test]
        public void FindOrphanPrivateElements_IgnoresCreatorElements()
        {
            var dataset = new DicomDataset();

            // Manually add private creator without data elements
            var creatorTag = new DicomTag(0x0019, 0x0010);
            dataset.Add(new DicomStringElement(creatorTag, DicomVR.LO, Encoding.ASCII.GetBytes("CREATOR")));

            // Creator elements are not orphans
            var orphans = dataset.FindOrphanPrivateElements();
            Assert.That(orphans, Is.Empty);
        }

        [Test]
        public void PrivateCreators_ExposedOnDataset()
        {
            var dataset = new DicomDataset();

            dataset.AddPrivateString(0x0019, "MY CREATOR", 0x01, DicomVR.LO, "Value");

            Assert.That(dataset.PrivateCreators.Count, Is.EqualTo(1));
            var creator = dataset.PrivateCreators.GetCreator(new DicomTag(0x0019, 0x1001));
            Assert.That(creator, Is.EqualTo("MY CREATOR"));
        }

        [Test]
        public void AddPrivateString_PadsOddLengthValue()
        {
            var dataset = new DicomDataset();

            // "ABC" is 3 bytes, should be padded to 4
            var tag = dataset.AddPrivateString(0x0019, "CREATOR", 0x01, DicomVR.LO, "ABC");

            var element = dataset[tag];
            Assert.That(element!.RawValue.Length % 2, Is.EqualTo(0));
        }

        [Test]
        public void AddPrivateElement_UpdatesExistingElement()
        {
            var dataset = new DicomDataset();

            // Add initial value
            var tag = dataset.AddPrivateString(0x0019, "CREATOR", 0x01, DicomVR.LO, "Initial");

            // Update with new value
            dataset.AddPrivateString(0x0019, "CREATOR", 0x01, DicomVR.LO, "Updated");

            var element = dataset[tag] as DicomStringElement;
            Assert.That(element!.GetString(), Is.EqualTo("Updated"));
        }

        [Test]
        public void StripPrivateTags_ClearsPrivateCreatorDictionary()
        {
            var dataset = new DicomDataset();

            dataset.AddPrivateString(0x0019, "CREATOR", 0x01, DicomVR.LO, "Value");
            Assert.That(dataset.PrivateCreators.Count, Is.EqualTo(1));

            dataset.StripPrivateTags();

            Assert.That(dataset.PrivateCreators.Count, Is.EqualTo(0));
        }

        [Test]
        public void StripPrivateTags_WithFilter_PreservesKeptCreatorsInDictionary()
        {
            var dataset = new DicomDataset();

            dataset.AddPrivateString(0x0019, "KEEP", 0x01, DicomVR.LO, "Keep");
            dataset.AddPrivateString(0x0019, "REMOVE", 0x01, DicomVR.LO, "Remove");

            dataset.StripPrivateTags(c => c == "KEEP");

            // Note: Currently the dictionary is not cleaned up by the filter version
            // The test documents current behavior - may be enhanced in future
            var allCreators = dataset.PrivateCreators.GetAll().ToList();
            Assert.That(allCreators.Any(c => c.Creator == "KEEP"), Is.True);
        }
    }
}
