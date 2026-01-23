using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using System;
using System.Linq;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class PrivateCreatorDictionaryTests
    {
        private PrivateCreatorDictionary _dict = null!;

        [SetUp]
        public void SetUp()
        {
            _dict = new PrivateCreatorDictionary();
        }

        [Test]
        public void Register_PrivateCreatorTag_Succeeds()
        {
            var tag = new DicomTag(0x0019, 0x0010);
            _dict.Register(tag, "MY CREATOR");

            Assert.That(_dict.Count, Is.EqualTo(1));
        }

        [Test]
        public void Register_NonPrivateCreatorTag_Throws()
        {
            var tag = new DicomTag(0x0019, 0x1000); // Private data, not creator
            Assert.Throws<ArgumentException>(() => _dict.Register(tag, "MY CREATOR"));
        }

        [Test]
        public void Register_EvenGroupTag_Throws()
        {
            var tag = new DicomTag(0x0018, 0x0010); // Even group
            Assert.Throws<ArgumentException>(() => _dict.Register(tag, "MY CREATOR"));
        }

        [Test]
        public void GetCreator_RegisteredElement_ReturnsCreator()
        {
            var creatorTag = new DicomTag(0x0019, 0x0010);
            _dict.Register(creatorTag, "MY CREATOR");

            var dataTag = new DicomTag(0x0019, 0x1005); // Slot 0x10, offset 0x05
            var creator = _dict.GetCreator(dataTag);

            Assert.That(creator, Is.EqualTo("MY CREATOR"));
        }

        [Test]
        public void GetCreator_UnregisteredSlot_ReturnsNull()
        {
            var dataTag = new DicomTag(0x0019, 0x1005);
            var creator = _dict.GetCreator(dataTag);

            Assert.That(creator, Is.Null);
        }

        [Test]
        public void GetCreator_PrivateCreatorTag_ReturnsNull()
        {
            var creatorTag = new DicomTag(0x0019, 0x0010);
            _dict.Register(creatorTag, "MY CREATOR");

            // GetCreator should return null for creator tags themselves
            var creator = _dict.GetCreator(creatorTag);
            Assert.That(creator, Is.Null);
        }

        [Test]
        public void AllocateSlot_FirstCreator_ReturnsSlot10()
        {
            var tag = _dict.AllocateSlot(0x0019, "FIRST CREATOR");

            Assert.That(tag.Group, Is.EqualTo(0x0019));
            Assert.That(tag.Element, Is.EqualTo(0x0010));
        }

        [Test]
        public void AllocateSlot_SecondCreator_ReturnsSlot11()
        {
            _dict.AllocateSlot(0x0019, "FIRST CREATOR");
            var tag = _dict.AllocateSlot(0x0019, "SECOND CREATOR");

            Assert.That(tag.Element, Is.EqualTo(0x0011));
        }

        [Test]
        public void AllocateSlot_SameCreator_ReturnsSameSlot()
        {
            var tag1 = _dict.AllocateSlot(0x0019, "MY CREATOR");
            var tag2 = _dict.AllocateSlot(0x0019, "MY CREATOR");

            Assert.That(tag2, Is.EqualTo(tag1));
        }

        [Test]
        public void AllocateSlot_SameCreatorDifferentGroup_ReturnsDifferentSlots()
        {
            var tag1 = _dict.AllocateSlot(0x0019, "MY CREATOR");
            var tag2 = _dict.AllocateSlot(0x0021, "MY CREATOR");

            Assert.That(tag2.Group, Is.EqualTo(0x0021));
            Assert.That(tag2.Element, Is.EqualTo(0x0010)); // First slot in new group
        }

        [Test]
        public void AllocateSlot_EvenGroup_Throws()
        {
            Assert.Throws<ArgumentException>(() => _dict.AllocateSlot(0x0018, "CREATOR"));
        }

        [Test]
        public void AllocateSlot_EmptyCreator_Throws()
        {
            Assert.Throws<ArgumentException>(() => _dict.AllocateSlot(0x0019, ""));
        }

        [Test]
        public void AllocateSlot_NullCreator_Throws()
        {
            Assert.Throws<ArgumentException>(() => _dict.AllocateSlot(0x0019, null!));
        }

        [Test]
        public void GetSlotForCreator_ExistingCreator_ReturnsSlot()
        {
            _dict.AllocateSlot(0x0019, "MY CREATOR");

            var slot = _dict.GetSlotForCreator(0x0019, "MY CREATOR");
            Assert.That(slot, Is.EqualTo(0x10));
        }

        [Test]
        public void GetSlotForCreator_UnknownCreator_ReturnsNull()
        {
            var slot = _dict.GetSlotForCreator(0x0019, "UNKNOWN");
            Assert.That(slot, Is.Null);
        }

        [Test]
        public void GetSlotForCreator_EvenGroup_Throws()
        {
            Assert.Throws<ArgumentException>(() => _dict.GetSlotForCreator(0x0018, "CREATOR"));
        }

        [Test]
        public void Compact_WithGaps_ReassignsSlots()
        {
            // Register at slots 0x10 and 0x12 (gap at 0x11)
            _dict.Register(new DicomTag(0x0019, 0x0010), "FIRST");
            _dict.Register(new DicomTag(0x0019, 0x0012), "SECOND");

            var mapping = _dict.Compact(0x0019);

            Assert.That(mapping[0x10], Is.EqualTo(0x10)); // First stays
            Assert.That(mapping[0x12], Is.EqualTo(0x11)); // Second moves to fill gap

            // Verify new slots
            var slot1 = _dict.GetSlotForCreator(0x0019, "FIRST");
            var slot2 = _dict.GetSlotForCreator(0x0019, "SECOND");

            Assert.That(slot1, Is.EqualTo(0x10));
            Assert.That(slot2, Is.EqualTo(0x11));
        }

        [Test]
        public void Compact_EmptyGroup_ReturnsEmptyMapping()
        {
            var mapping = _dict.Compact(0x0019);
            Assert.That(mapping, Is.Empty);
        }

        [Test]
        public void Compact_NoGaps_PreservesOrder()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "FIRST");
            _dict.Register(new DicomTag(0x0019, 0x0011), "SECOND");

            var mapping = _dict.Compact(0x0019);

            Assert.That(mapping[0x10], Is.EqualTo(0x10));
            Assert.That(mapping[0x11], Is.EqualTo(0x11));
        }

        [Test]
        public void Compact_EvenGroup_Throws()
        {
            Assert.Throws<ArgumentException>(() => _dict.Compact(0x0018));
        }

        [Test]
        public void ValidateHasCreator_OrphanElement_ReturnsFalse()
        {
            // Private data element with no creator registered
            var dataTag = new DicomTag(0x0019, 0x1005);

            Assert.That(_dict.ValidateHasCreator(dataTag), Is.False);
        }

        [Test]
        public void ValidateHasCreator_WithCreator_ReturnsTrue()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "MY CREATOR");
            var dataTag = new DicomTag(0x0019, 0x1005);

            Assert.That(_dict.ValidateHasCreator(dataTag), Is.True);
        }

        [Test]
        public void ValidateHasCreator_PublicTag_ReturnsTrue()
        {
            var publicTag = new DicomTag(0x0008, 0x0010); // Even group
            Assert.That(_dict.ValidateHasCreator(publicTag), Is.True);
        }

        [Test]
        public void ValidateHasCreator_PrivateCreatorTag_ReturnsTrue()
        {
            // Private creator tags themselves are always valid
            var creatorTag = new DicomTag(0x0019, 0x0010);
            Assert.That(_dict.ValidateHasCreator(creatorTag), Is.True);
        }

        [Test]
        public void GetCreatorsInGroup_ReturnsOnlyGroupCreators()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "CREATOR19A");
            _dict.Register(new DicomTag(0x0019, 0x0011), "CREATOR19B");
            _dict.Register(new DicomTag(0x0021, 0x0010), "CREATOR21");

            var creators19 = _dict.GetCreatorsInGroup(0x0019).ToList();
            var creators21 = _dict.GetCreatorsInGroup(0x0021).ToList();

            Assert.That(creators19.Count, Is.EqualTo(2));
            Assert.That(creators21.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetCreatorsInGroup_EmptyGroup_ReturnsEmpty()
        {
            var creators = _dict.GetCreatorsInGroup(0x0019).ToList();
            Assert.That(creators, Is.Empty);
        }

        [Test]
        public void GetAll_ReturnsAllCreators()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "CREATOR19");
            _dict.Register(new DicomTag(0x0021, 0x0010), "CREATOR21");

            var all = _dict.GetAll().ToList();
            Assert.That(all.Count, Is.EqualTo(2));
        }

        [Test]
        public void Clear_RemovesAllCreators()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "CREATOR");
            _dict.Clear();

            Assert.That(_dict.Count, Is.EqualTo(0));
        }

        [Test]
        public void Count_ReturnsCorrectValue()
        {
            Assert.That(_dict.Count, Is.EqualTo(0));

            _dict.Register(new DicomTag(0x0019, 0x0010), "CREATOR1");
            Assert.That(_dict.Count, Is.EqualTo(1));

            _dict.Register(new DicomTag(0x0019, 0x0011), "CREATOR2");
            Assert.That(_dict.Count, Is.EqualTo(2));
        }

        [Test]
        public void HasCreator_WithCreator_ReturnsTrue()
        {
            _dict.Register(new DicomTag(0x0019, 0x0010), "MY CREATOR");
            var dataTag = new DicomTag(0x0019, 0x1005);

            Assert.That(_dict.HasCreator(dataTag), Is.True);
        }

        [Test]
        public void HasCreator_WithoutCreator_ReturnsFalse()
        {
            var dataTag = new DicomTag(0x0019, 0x1005);
            Assert.That(_dict.HasCreator(dataTag), Is.False);
        }
    }
}
