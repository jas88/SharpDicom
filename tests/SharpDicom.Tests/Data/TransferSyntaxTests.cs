using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class TransferSyntaxTests
    {
        [Test]
        public void ImplicitVRLittleEndian_HasCorrectProperties()
        {
            var ts = TransferSyntax.ImplicitVRLittleEndian;

            Assert.That(ts.IsExplicitVR, Is.False);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.False);
            Assert.That(ts.IsLossy, Is.False);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.None));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2"));
        }

        [Test]
        public void ExplicitVRLittleEndian_HasCorrectProperties()
        {
            var ts = TransferSyntax.ExplicitVRLittleEndian;

            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.False);
            Assert.That(ts.IsLossy, Is.False);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.None));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.1"));
        }

        [Test]
        public void ExplicitVRBigEndian_HasCorrectProperties()
        {
            var ts = TransferSyntax.ExplicitVRBigEndian;

            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.False);
            Assert.That(ts.IsEncapsulated, Is.False);
            Assert.That(ts.IsLossy, Is.False);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.None));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.2"));
        }

        [Test]
        public void JPEGBaseline_HasCorrectProperties()
        {
            var ts = TransferSyntax.JPEGBaseline;

            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.True);
            Assert.That(ts.IsLossy, Is.True);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.JPEGBaseline));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.50"));
        }

        [Test]
        public void JPEG2000Lossless_HasCorrectProperties()
        {
            var ts = TransferSyntax.JPEG2000Lossless;

            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.True);
            Assert.That(ts.IsLossy, Is.False);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.JPEG2000Lossless));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.90"));
        }

        [Test]
        public void RLELossless_HasCorrectProperties()
        {
            var ts = TransferSyntax.RLELossless;

            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.True);
            Assert.That(ts.IsLossy, Is.False);
            Assert.That(ts.Compression, Is.EqualTo(CompressionType.RLE));
            Assert.That(ts.IsKnown, Is.True);
            Assert.That(ts.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.5"));
        }

        [Test]
        public void FromUID_ImplicitVRLittleEndian_ReturnsCorrectSyntax()
        {
            var uid = new DicomUID("1.2.840.10008.1.2");
            var ts = TransferSyntax.FromUID(uid);

            Assert.That(ts, Is.EqualTo(TransferSyntax.ImplicitVRLittleEndian));
        }

        [Test]
        public void FromUID_ExplicitVRLittleEndian_ReturnsCorrectSyntax()
        {
            var uid = new DicomUID("1.2.840.10008.1.2.1");
            var ts = TransferSyntax.FromUID(uid);

            Assert.That(ts, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
        }

        [Test]
        public void FromUID_JPEGBaseline_ReturnsCorrectSyntax()
        {
            var uid = new DicomUID("1.2.840.10008.1.2.4.50");
            var ts = TransferSyntax.FromUID(uid);

            Assert.That(ts, Is.EqualTo(TransferSyntax.JPEGBaseline));
        }

        [Test]
        public void FromUID_UnknownUID_ReturnsUnknownSyntax()
        {
            var uid = new DicomUID("1.2.3.4.5.6.7.8.9");
            var ts = TransferSyntax.FromUID(uid);

            Assert.That(ts.IsKnown, Is.False);
            Assert.That(ts.UID, Is.EqualTo(uid));
            // Default assumptions for unknown transfer syntax
            Assert.That(ts.IsExplicitVR, Is.True);
            Assert.That(ts.IsLittleEndian, Is.True);
            Assert.That(ts.IsEncapsulated, Is.False);
        }

        [Test]
        public void RecordEquality_SameProperties_AreEqual()
        {
            var ts1 = TransferSyntax.ExplicitVRLittleEndian;
            var ts2 = TransferSyntax.FromUID(new DicomUID("1.2.840.10008.1.2.1"));

            Assert.That(ts1, Is.EqualTo(ts2));
        }

        [Test]
        public void RecordEquality_DifferentProperties_AreNotEqual()
        {
            var ts1 = TransferSyntax.ImplicitVRLittleEndian;
            var ts2 = TransferSyntax.ExplicitVRLittleEndian;

            Assert.That(ts1, Is.Not.EqualTo(ts2));
        }
    }
}
