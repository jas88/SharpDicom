using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomMaskedTagTests
    {
        [Test]
        public void Constructor_CreatesMaskedTag()
        {
            var maskedTag = new DicomMaskedTag(0xFFFF0000u, 0x50000000u);
            Assert.That(maskedTag.Mask, Is.EqualTo(0xFFFF0000u));
            Assert.That(maskedTag.Card, Is.EqualTo(0x50000000u));
        }

        [Test]
        public void FromPattern_FullySpecified_CreatesCorrectMask()
        {
            var maskedTag = DicomMaskedTag.FromPattern("(0010,0020)");
            var testTag = new DicomTag(0x0010, 0x0020);

            Assert.That(maskedTag.Matches(testTag), Is.True);
            Assert.That(maskedTag.ToString(), Is.EqualTo("(0010,0020)"));
        }

        [Test]
        public void FromPattern_GroupWildcard_MatchesMultipleTags()
        {
            var maskedTag = DicomMaskedTag.FromPattern("(50xx,0010)");

            Assert.That(maskedTag.Matches(new DicomTag(0x5000, 0x0010)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x5010, 0x0010)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x50FF, 0x0010)), Is.True);

            Assert.That(maskedTag.Matches(new DicomTag(0x5000, 0x0020)), Is.False);
            Assert.That(maskedTag.Matches(new DicomTag(0x6000, 0x0010)), Is.False);
        }

        [Test]
        public void FromPattern_ElementWildcard_MatchesMultipleTags()
        {
            var maskedTag = DicomMaskedTag.FromPattern("(0010,00xx)");

            Assert.That(maskedTag.Matches(new DicomTag(0x0010, 0x0000)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x0010, 0x0010)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x0010, 0x00FF)), Is.True);

            Assert.That(maskedTag.Matches(new DicomTag(0x0010, 0x0100)), Is.False);
            Assert.That(maskedTag.Matches(new DicomTag(0x0020, 0x0010)), Is.False);
        }

        [Test]
        public void FromPattern_MultipleWildcards_MatchesCorrectly()
        {
            var maskedTag = DicomMaskedTag.FromPattern("(60xx,3xxx)");

            Assert.That(maskedTag.Matches(new DicomTag(0x6000, 0x3000)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x6010, 0x3FFF)), Is.True);
            Assert.That(maskedTag.Matches(new DicomTag(0x60FF, 0x3ABC)), Is.True);

            Assert.That(maskedTag.Matches(new DicomTag(0x6100, 0x3000)), Is.False);
            Assert.That(maskedTag.Matches(new DicomTag(0x6000, 0x4000)), Is.False);
        }

        [Test]
        public void FromPattern_CaseInsensitive_ParsesCorrectly()
        {
            var maskedTag1 = DicomMaskedTag.FromPattern("(50xx,0010)");
            var maskedTag2 = DicomMaskedTag.FromPattern("(50XX,0010)");

            Assert.That(maskedTag1, Is.EqualTo(maskedTag2));
        }

        [Test]
        public void FromPattern_InvalidFormat_ThrowsException()
        {
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("invalid"));
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("(0010)"));
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("0010,0020"));
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("(001,0020)"));
        }

        [Test]
        public void FromPattern_InvalidCharacter_ThrowsException()
        {
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("(00G0,0020)"));
            Assert.Throws<DicomTagException>(() => DicomMaskedTag.FromPattern("(0010,00?0)"));
        }

        [Test]
        public void Matches_VariousPatterns_CorrectResults()
        {
            var tag = new DicomTag(0x5010, 0x0020);

            var exactMatch = DicomMaskedTag.FromPattern("(5010,0020)");
            Assert.That(exactMatch.Matches(tag), Is.True);

            var groupWildcard = DicomMaskedTag.FromPattern("(50xx,0020)");
            Assert.That(groupWildcard.Matches(tag), Is.True);

            var elementWildcard = DicomMaskedTag.FromPattern("(5010,00xx)");
            Assert.That(elementWildcard.Matches(tag), Is.True);

            var noMatch = DicomMaskedTag.FromPattern("(5020,0020)");
            Assert.That(noMatch.Matches(tag), Is.False);
        }

        [Test]
        public void Equality_SameMaskAndCard_AreEqual()
        {
            var masked1 = new DicomMaskedTag(0xFFFF00FFu, 0x50000010u);
            var masked2 = new DicomMaskedTag(0xFFFF00FFu, 0x50000010u);

            Assert.That(masked1, Is.EqualTo(masked2));
            Assert.That(masked1 == masked2, Is.True);
            Assert.That(masked1 != masked2, Is.False);
        }

        [Test]
        public void Equality_DifferentMaskOrCard_AreNotEqual()
        {
            var masked1 = new DicomMaskedTag(0xFFFF00FFu, 0x50000010u);
            var masked2 = new DicomMaskedTag(0xFFFF00FFu, 0x50000020u);
            var masked3 = new DicomMaskedTag(0xFFFF0000u, 0x50000010u);

            Assert.That(masked1, Is.Not.EqualTo(masked2));
            Assert.That(masked1, Is.Not.EqualTo(masked3));
        }

        [Test]
        public void GetHashCode_SameMaskAndCard_SameHash()
        {
            var masked1 = new DicomMaskedTag(0xFFFF00FFu, 0x50000010u);
            var masked2 = new DicomMaskedTag(0xFFFF00FFu, 0x50000010u);

            Assert.That(masked1.GetHashCode(), Is.EqualTo(masked2.GetHashCode()));
        }

        [Test]
        public void ToString_FormatsCorrectly()
        {
            var fullTag = DicomMaskedTag.FromPattern("(0010,0020)");
            Assert.That(fullTag.ToString(), Is.EqualTo("(0010,0020)"));

            var groupWildcard = DicomMaskedTag.FromPattern("(50xx,0010)");
            Assert.That(groupWildcard.ToString(), Is.EqualTo("(50xx,0010)"));

            var elementWildcard = DicomMaskedTag.FromPattern("(0010,00xx)");
            Assert.That(elementWildcard.ToString(), Is.EqualTo("(0010,00xx)"));

            var multiWildcard = DicomMaskedTag.FromPattern("(60xx,3xxx)");
            Assert.That(multiWildcard.ToString(), Is.EqualTo("(60xx,3xxx)"));
        }

        [Test]
        public void ToString_LowercaseX_UsesLowercase()
        {
            var maskedTag = DicomMaskedTag.FromPattern("(50xx,0010)");
            var str = maskedTag.ToString();
            Assert.That(str, Does.Contain("xx"));
            Assert.That(str, Does.Not.Contain("XX"));
        }
    }
}
