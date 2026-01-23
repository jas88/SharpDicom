using NUnit.Framework;
using SharpDicom.Data;
using System;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomVRTests
    {
        [Test]
        public void Constructor_FromBytes_CreatesCorrectVR()
        {
            var vr = new DicomVR((byte)'A', (byte)'E');
            Assert.That(vr.Char1, Is.EqualTo((byte)'A'));
            Assert.That(vr.Char2, Is.EqualTo((byte)'E'));
            Assert.That(vr.Code, Is.EqualTo(0x4145));
        }

        [Test]
        public void Constructor_FromString_CreatesCorrectVR()
        {
            var vr = new DicomVR("AE");
            Assert.That(vr.ToString(), Is.EqualTo("AE"));
        }

        [Test]
        public void Constructor_FromString_InvalidLength_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => new DicomVR("A"));
            Assert.Throws<ArgumentException>(() => new DicomVR("AEX"));
            Assert.Throws<ArgumentException>(() => new DicomVR(""));
        }

        [Test]
        public void FromBytes_CreatesCorrectVR()
        {
            ReadOnlySpan<byte> bytes = stackalloc byte[] { (byte)'D', (byte)'A' };
            var vr = DicomVR.FromBytes(bytes);
            Assert.That(vr.ToString(), Is.EqualTo("DA"));
        }

        [Test]
        public void FromBytes_InsufficientLength_ThrowsException()
        {
            var bytes = new byte[] { (byte)'A' };
            Assert.Throws<ArgumentException>(() => DicomVR.FromBytes(bytes));
        }

        [Test]
        public void Equality_SameVR_AreEqual()
        {
            var vr1 = new DicomVR("AE");
            var vr2 = new DicomVR((byte)'A', (byte)'E');
            Assert.That(vr1, Is.EqualTo(vr2));
            Assert.That(vr1 == vr2, Is.True);
            Assert.That(vr1 != vr2, Is.False);
        }

        [Test]
        public void Equality_DifferentVR_AreNotEqual()
        {
            var vr1 = new DicomVR("AE");
            var vr2 = new DicomVR("DA");
            Assert.That(vr1, Is.Not.EqualTo(vr2));
            Assert.That(vr1 == vr2, Is.False);
            Assert.That(vr1 != vr2, Is.True);
        }

        [Test]
        public void GetHashCode_SameVR_SameHash()
        {
            var vr1 = new DicomVR("AE");
            var vr2 = new DicomVR("AE");
            Assert.That(vr1.GetHashCode(), Is.EqualTo(vr2.GetHashCode()));
        }

        [Test]
        public void ToString_ReturnsCorrectString()
        {
            var vr = new DicomVR("UI");
            Assert.That(vr.ToString(), Is.EqualTo("UI"));
        }

        [TestCase("AE", true, "Application Entity")]
        [TestCase("AS", true, "Age String")]
        [TestCase("AT", true, "Attribute Tag")]
        [TestCase("CS", true, "Code String")]
        [TestCase("DA", true, "Date")]
        [TestCase("DS", true, "Decimal String")]
        [TestCase("DT", true, "Date Time")]
        [TestCase("FL", true, "Floating Point Single")]
        [TestCase("FD", true, "Floating Point Double")]
        [TestCase("IS", true, "Integer String")]
        [TestCase("LO", true, "Long String")]
        [TestCase("LT", true, "Long Text")]
        [TestCase("OB", true, "Other Byte")]
        [TestCase("OD", true, "Other Double")]
        [TestCase("OF", true, "Other Float")]
        [TestCase("OL", true, "Other Long")]
        [TestCase("OW", true, "Other Word")]
        [TestCase("PN", true, "Person Name")]
        [TestCase("SH", true, "Short String")]
        [TestCase("SL", true, "Signed Long")]
        [TestCase("SQ", true, "Sequence of Items")]
        [TestCase("SS", true, "Signed Short")]
        [TestCase("ST", true, "Short Text")]
        [TestCase("TM", true, "Time")]
        [TestCase("UC", true, "Unlimited Characters")]
        [TestCase("UI", true, "Unique Identifier")]
        [TestCase("UL", true, "Unsigned Long")]
        [TestCase("UN", true, "Unknown")]
        [TestCase("UR", true, "Universal Resource Identifier")]
        [TestCase("US", true, "Unsigned Short")]
        [TestCase("UT", true, "Unlimited Text")]
        [TestCase("XX", false, "Unknown")]
        public void IsKnown_ChecksStandardVRs(string vrCode, bool expectedKnown, string expectedName)
        {
            var vr = new DicomVR(vrCode);
            Assert.That(vr.IsKnown, Is.EqualTo(expectedKnown));

            var info = DicomVRInfo.GetInfo(vr);
            Assert.That(info.Name, Is.EqualTo(expectedName));
        }

        [Test]
        public void StaticInstances_AllStandardVRsExist()
        {
            Assert.That(DicomVR.AE.ToString(), Is.EqualTo("AE"));
            Assert.That(DicomVR.AS.ToString(), Is.EqualTo("AS"));
            Assert.That(DicomVR.AT.ToString(), Is.EqualTo("AT"));
            Assert.That(DicomVR.CS.ToString(), Is.EqualTo("CS"));
            Assert.That(DicomVR.DA.ToString(), Is.EqualTo("DA"));
            Assert.That(DicomVR.DS.ToString(), Is.EqualTo("DS"));
            Assert.That(DicomVR.DT.ToString(), Is.EqualTo("DT"));
            Assert.That(DicomVR.FL.ToString(), Is.EqualTo("FL"));
            Assert.That(DicomVR.FD.ToString(), Is.EqualTo("FD"));
            Assert.That(DicomVR.IS.ToString(), Is.EqualTo("IS"));
            Assert.That(DicomVR.LO.ToString(), Is.EqualTo("LO"));
            Assert.That(DicomVR.LT.ToString(), Is.EqualTo("LT"));
            Assert.That(DicomVR.OB.ToString(), Is.EqualTo("OB"));
            Assert.That(DicomVR.OD.ToString(), Is.EqualTo("OD"));
            Assert.That(DicomVR.OF.ToString(), Is.EqualTo("OF"));
            Assert.That(DicomVR.OL.ToString(), Is.EqualTo("OL"));
            Assert.That(DicomVR.OW.ToString(), Is.EqualTo("OW"));
            Assert.That(DicomVR.PN.ToString(), Is.EqualTo("PN"));
            Assert.That(DicomVR.SH.ToString(), Is.EqualTo("SH"));
            Assert.That(DicomVR.SL.ToString(), Is.EqualTo("SL"));
            Assert.That(DicomVR.SQ.ToString(), Is.EqualTo("SQ"));
            Assert.That(DicomVR.SS.ToString(), Is.EqualTo("SS"));
            Assert.That(DicomVR.ST.ToString(), Is.EqualTo("ST"));
            Assert.That(DicomVR.TM.ToString(), Is.EqualTo("TM"));
            Assert.That(DicomVR.UC.ToString(), Is.EqualTo("UC"));
            Assert.That(DicomVR.UI.ToString(), Is.EqualTo("UI"));
            Assert.That(DicomVR.UL.ToString(), Is.EqualTo("UL"));
            Assert.That(DicomVR.UN.ToString(), Is.EqualTo("UN"));
            Assert.That(DicomVR.UR.ToString(), Is.EqualTo("UR"));
            Assert.That(DicomVR.US.ToString(), Is.EqualTo("US"));
            Assert.That(DicomVR.UT.ToString(), Is.EqualTo("UT"));
        }

        [TestCase("AE", 0x20, 16u, true, true, false)]
        [TestCase("DA", 0x20, 8u, true, true, false)]
        [TestCase("UI", 0x00, 64u, true, true, false)]
        [TestCase("OB", 0x00, uint.MaxValue, false, false, true)]
        [TestCase("SQ", 0x00, uint.MaxValue, false, false, true)]
        [TestCase("UN", 0x00, uint.MaxValue, false, false, true)]
        public void VRInfo_HasCorrectMetadata(string vrCode, byte paddingByte, uint maxLength,
            bool isString, bool is16BitLength, bool canHaveUndefinedLength)
        {
            var vr = new DicomVR(vrCode);
            var info = DicomVRInfo.GetInfo(vr);

            Assert.That(info.VR, Is.EqualTo(vr));
            Assert.That(info.PaddingByte, Is.EqualTo(paddingByte));
            Assert.That(info.MaxLength, Is.EqualTo(maxLength));
            Assert.That(info.IsStringVR, Is.EqualTo(isString));
            Assert.That(info.Is16BitLength, Is.EqualTo(is16BitLength));
            Assert.That(info.CanHaveUndefinedLength, Is.EqualTo(canHaveUndefinedLength));
        }

        [TestCase("AE", '\\')]
        [TestCase("DA", '\\')]
        [TestCase("LT", null)]
        [TestCase("OB", null)]
        public void VRInfo_HasCorrectMultiValueDelimiter(string vrCode, char? delimiter)
        {
            var vr = new DicomVR(vrCode);
            var info = DicomVRInfo.GetInfo(vr);
            Assert.That(info.MultiValueDelimiter, Is.EqualTo(delimiter));
        }

        [Test]
        public void VRInfo_UnknownVR_ReturnsFallback()
        {
            var unknownVR = new DicomVR("XX");
            var info = DicomVRInfo.GetInfo(unknownVR);

            Assert.That(info.VR, Is.EqualTo(unknownVR));
            Assert.That(info.Name, Is.EqualTo("Unknown"));
            Assert.That(info.PaddingByte, Is.EqualTo(0x00));
            Assert.That(info.MaxLength, Is.EqualTo(uint.MaxValue));
            Assert.That(info.IsStringVR, Is.False);
            Assert.That(info.Is16BitLength, Is.False);
            Assert.That(info.CanHaveUndefinedLength, Is.True);
            Assert.That(info.MultiValueDelimiter, Is.Null);
        }
    }
}
