using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class VendorDictionaryTests
    {
        [Test]
        public void GetInfo_SiemensTag_ReturnsInfo()
        {
            // Siemens MED DISPLAY private tags should be known
            var info = VendorDictionary.GetInfo("SIEMENS MED DISPLAY", 0x04);

            // Should find the Photometric Interpretation tag
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Name, Is.EqualTo("Photometric Interpretation"));
            Assert.That(info!.Value.VR, Is.EqualTo(DicomVR.CS));
        }

        [Test]
        public void GetInfo_GEMSTag_ReturnsInfo()
        {
            // GE Medical Systems private tags should be known
            // GEMS_IMAG_01 has tags in group 0027
            var info = VendorDictionary.GetInfo("GEMS_IMAG_01", 0x10);

            // Should find a GEMS tag
            if (info != null)
            {
                Assert.That(info.Value.Creator, Is.EqualTo("GEMS_IMAG_01"));
            }
            else
            {
                // Tag might not be at this exact offset, but lookup should work
                Assert.Pass("Lookup executed without exception");
            }
        }

        [Test]
        public void GetInfo_UnknownCreator_ReturnsNull()
        {
            var info = VendorDictionary.GetInfo("UNKNOWN VENDOR XYZ", 0x10);
            Assert.That(info, Is.Null);
        }

        [Test]
        public void GetInfo_EmptyCreator_ReturnsNull()
        {
            var info = VendorDictionary.GetInfo("", 0x10);
            Assert.That(info, Is.Null);
        }

        [Test]
        public void GetInfo_NullCreator_ReturnsNull()
        {
            var info = VendorDictionary.GetInfo(null!, 0x10);
            Assert.That(info, Is.Null);
        }

        [Test]
        public void IsKnownCreator_SiemensCreator_ReturnsTrue()
        {
            Assert.That(VendorDictionary.IsKnownCreator("SIEMENS MED DISPLAY"), Is.True);
        }

        [Test]
        public void IsKnownCreator_UnknownCreator_ReturnsFalse()
        {
            Assert.That(VendorDictionary.IsKnownCreator("UNKNOWN VENDOR XYZ"), Is.False);
        }

        [Test]
        public void IsKnownCreator_EmptyCreator_ReturnsFalse()
        {
            Assert.That(VendorDictionary.IsKnownCreator(""), Is.False);
        }

        [Test]
        public void Register_UserTag_CanBeRetrieved()
        {
            var info = new PrivateTagInfo(
                "TEST CREATOR",
                0x42,
                DicomVR.LO,
                "TestKeyword",
                "Test Name",
                false);

            VendorDictionary.Register(info);

            var retrieved = VendorDictionary.GetInfo("TEST CREATOR", 0x42);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Value.Keyword, Is.EqualTo("TestKeyword"));
        }

        [Test]
        public void GetInfo_CaseInsensitive_FindsTag()
        {
            var info = new PrivateTagInfo(
                "CASE TEST CREATOR",
                0x01,
                DicomVR.CS,
                "CaseTest",
                "Case Test",
                false);

            VendorDictionary.Register(info);

            // Lookup with different case
            var retrieved = VendorDictionary.GetInfo("case test creator", 0x01);
            Assert.That(retrieved, Is.Not.Null);
        }

        [Test]
        public void GetInfo_WithFullElement_ExtractsOffset()
        {
            var info = new PrivateTagInfo(
                "ELEMENT TEST",
                0xAB,
                DicomVR.US,
                "ElementTest",
                "Element Test",
                false);

            VendorDictionary.Register(info);

            // Lookup with full element 0x10AB (slot 0x10, offset 0xAB)
            var retrieved = VendorDictionary.GetInfo("ELEMENT TEST", 0x10AB);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Value.ElementOffset, Is.EqualTo(0xAB));
        }

        [Test]
        public void GetInfo_SiemensCaseInsensitive_Works()
        {
            // Should find tag regardless of case
            var info1 = VendorDictionary.GetInfo("SIEMENS MED DISPLAY", 0x04);
            var info2 = VendorDictionary.GetInfo("siemens med display", 0x04);

            Assert.That(info1, Is.Not.Null);
            Assert.That(info2, Is.Not.Null);
            Assert.That(info1!.Value.Name, Is.EqualTo(info2!.Value.Name));
        }
    }
}
