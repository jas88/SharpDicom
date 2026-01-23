using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomTagTests
    {
        [Test]
        public void Constructor_FromGroupAndElement_CreatesCorrectTag()
        {
            var tag = new DicomTag(0x0010, 0x0020);
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
            Assert.That(tag.Value, Is.EqualTo(0x00100020u));
        }

        [Test]
        public void Constructor_FromUInt_CreatesCorrectTag()
        {
            var tag = new DicomTag(0x00100020u);
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
            Assert.That(tag.Value, Is.EqualTo(0x00100020u));
        }

        [Test]
        public void Equality_SameValues_AreEqual()
        {
            var tag1 = new DicomTag(0x0010, 0x0020);
            var tag2 = new DicomTag(0x00100020u);
            Assert.That(tag1, Is.EqualTo(tag2));
            Assert.That(tag1 == tag2, Is.True);
            Assert.That(tag1 != tag2, Is.False);
        }

        [Test]
        public void Equality_DifferentValues_AreNotEqual()
        {
            var tag1 = new DicomTag(0x0010, 0x0020);
            var tag2 = new DicomTag(0x0010, 0x0030);
            Assert.That(tag1, Is.Not.EqualTo(tag2));
            Assert.That(tag1 == tag2, Is.False);
            Assert.That(tag1 != tag2, Is.True);
        }

        [Test]
        public void Comparison_OrdersByValue()
        {
            var tag1 = new DicomTag(0x0008, 0x0000);
            var tag2 = new DicomTag(0x0010, 0x0000);
            var tag3 = new DicomTag(0x0010, 0x0020);

            Assert.That(tag1.CompareTo(tag2), Is.LessThan(0));
            Assert.That(tag2.CompareTo(tag1), Is.GreaterThan(0));
            Assert.That(tag2.CompareTo(tag3), Is.LessThan(0));
            Assert.That(tag1.CompareTo(tag1), Is.EqualTo(0));

            Assert.That(tag1 < tag2, Is.True);
            Assert.That(tag2 > tag1, Is.True);
            Assert.That(tag1 <= tag2, Is.True);
            Assert.That(tag2 >= tag1, Is.True);
        }

        [Test]
        public void GetHashCode_SameValues_SameHash()
        {
            var tag1 = new DicomTag(0x0010, 0x0020);
            var tag2 = new DicomTag(0x00100020u);
            Assert.That(tag1.GetHashCode(), Is.EqualTo(tag2.GetHashCode()));
        }

        [Test]
        public void ToString_ReturnsCorrectFormat()
        {
            var tag = new DicomTag(0x0010, 0x0020);
            Assert.That(tag.ToString(), Is.EqualTo("(0010,0020)"));
        }

        [Test]
        public void Parse_ParenthesizedFormat_ParsesCorrectly()
        {
            var tag = DicomTag.Parse("(0010,0020)");
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
        }

        [Test]
        public void Parse_CompactFormat_ParsesCorrectly()
        {
            var tag = DicomTag.Parse("00100020");
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
        }

        [Test]
        public void Parse_WithWhitespace_ParsesCorrectly()
        {
            var tag = DicomTag.Parse(" ( 0010 , 0020 ) ");
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
        }

        [Test]
        public void Parse_InvalidFormat_ThrowsException()
        {
            Assert.Throws<DicomTagException>(() => DicomTag.Parse("invalid"));
            Assert.Throws<DicomTagException>(() => DicomTag.Parse("(0010)"));
            Assert.Throws<DicomTagException>(() => DicomTag.Parse("001000"));
        }

        [Test]
        public void TryParse_ValidFormat_ReturnsTrue()
        {
            var success = DicomTag.TryParse("(0010,0020)", out var tag);
            Assert.That(success, Is.True);
            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
        }

        [Test]
        public void TryParse_InvalidFormat_ReturnsFalse()
        {
            var success = DicomTag.TryParse("invalid", out var tag);
            Assert.That(success, Is.False);
            Assert.That(tag, Is.EqualTo(default(DicomTag)));
        }

        [Test]
        public void IsPrivate_EvenGroup_ReturnsFalse()
        {
            var tag = new DicomTag(0x0010, 0x0020); // Even group
            Assert.That(tag.IsPrivate, Is.False);
        }

        [Test]
        public void IsPrivate_OddGroup_ReturnsTrue()
        {
            var tag = new DicomTag(0x0009, 0x0010); // Odd group
            Assert.That(tag.IsPrivate, Is.True);
        }

        [Test]
        public void IsPrivateCreator_PrivateCreatorElement_ReturnsTrue()
        {
            var tag = new DicomTag(0x0009, 0x0010); // (odd,00xx) where xx in 10-FF
            Assert.That(tag.IsPrivateCreator, Is.True);

            tag = new DicomTag(0x0009, 0x00FF);
            Assert.That(tag.IsPrivateCreator, Is.True);
        }

        [Test]
        public void IsPrivateCreator_NonPrivateCreator_ReturnsFalse()
        {
            var tag = new DicomTag(0x0009, 0x0000); // Element 0x00
            Assert.That(tag.IsPrivateCreator, Is.False);

            tag = new DicomTag(0x0009, 0x1000); // Element > 0xFF
            Assert.That(tag.IsPrivateCreator, Is.False);

            tag = new DicomTag(0x0010, 0x0010); // Even group
            Assert.That(tag.IsPrivateCreator, Is.False);
        }

        [Test]
        public void PrivateCreatorSlot_PrivateDataElement_ReturnsSlot()
        {
            var tag = new DicomTag(0x0009, 0x1000); // Slot 0x10
            Assert.That(tag.PrivateCreatorSlot, Is.EqualTo(0x10));

            tag = new DicomTag(0x0009, 0x10FF); // Slot 0x10
            Assert.That(tag.PrivateCreatorSlot, Is.EqualTo(0x10));

            tag = new DicomTag(0x0009, 0xFF00); // Slot 0xFF
            Assert.That(tag.PrivateCreatorSlot, Is.EqualTo(0xFF));
        }

        [Test]
        public void PrivateCreatorSlot_NonPrivateDataElement_ReturnsZero()
        {
            var tag = new DicomTag(0x0009, 0x0010); // Private creator
            Assert.That(tag.PrivateCreatorSlot, Is.EqualTo(0));

            tag = new DicomTag(0x0010, 0x1000); // Even group
            Assert.That(tag.PrivateCreatorSlot, Is.EqualTo(0));
        }

        [Test]
        public void PrivateCreatorKey_PrivateDataElement_ReturnsKey()
        {
            var tag = new DicomTag(0x0009, 0x1020);
            var expected = (0x0009u << 16) | 0x10u;
            Assert.That(tag.PrivateCreatorKey, Is.EqualTo(expected));
        }

        [Test]
        public void PrivateCreatorKey_NonPrivateDataElement_ReturnsZero()
        {
            var tag = new DicomTag(0x0010, 0x0020);
            Assert.That(tag.PrivateCreatorKey, Is.EqualTo(0u));
        }

        [Test]
        public void EdgeCases_MinMaxValues()
        {
            var minTag = new DicomTag(0x0000, 0x0000);
            Assert.That(minTag.Group, Is.EqualTo(0));
            Assert.That(minTag.Element, Is.EqualTo(0));

            var maxTag = new DicomTag(0xFFFF, 0xFFFF);
            Assert.That(maxTag.Group, Is.EqualTo(0xFFFF));
            Assert.That(maxTag.Element, Is.EqualTo(0xFFFF));
        }
    }
}
