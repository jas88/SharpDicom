using NUnit.Framework;
using System;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

#if NETSTANDARD2_0 || NETFRAMEWORK
using SharpDicom.Internal;
#endif

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomElementTests
    {
        [Test]
        public void DicomStringElement_GetString_ReturnsDecodedValue()
        {
            var bytes = Encoding.ASCII.GetBytes("12345678");
            var element = new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO, bytes);

            var value = element.GetString();

            Assert.That(value, Is.EqualTo("12345678"));
        }

        [Test]
        public void DicomStringElement_GetString_HandlesEmptyValue()
        {
            var element = new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO, ReadOnlyMemory<byte>.Empty);

            var value = element.GetString();

            Assert.That(value, Is.Null);
        }

        [Test]
        public void DicomStringElement_GetStrings_SplitsOnBackslash()
        {
            var bytes = Encoding.ASCII.GetBytes("VALUE1\\VALUE2\\VALUE3");
            var element = new DicomStringElement(new DicomTag(0x0008, 0x0070), DicomVR.LO, bytes);

            var values = element.GetStrings();

            Assert.That(values, Is.Not.Null);
            Assert.That(values!.Length, Is.EqualTo(3));
            Assert.That(values[0], Is.EqualTo("VALUE1"));
            Assert.That(values[1], Is.EqualTo("VALUE2"));
            Assert.That(values[2], Is.EqualTo("VALUE3"));
        }

        [Test]
        public void DicomStringElement_GetDate_ParsesDAFormat()
        {
            var bytes = Encoding.ASCII.GetBytes("20240115");
            var element = new DicomStringElement(new DicomTag(0x0008, 0x0020), DicomVR.DA, bytes);

            var date = element.GetDate();

            Assert.That(date, Is.Not.Null);
            Assert.That(date!.Value.Year, Is.EqualTo(2024));
            Assert.That(date.Value.Month, Is.EqualTo(1));
            Assert.That(date.Value.Day, Is.EqualTo(15));
        }

        [Test]
        public void DicomStringElement_GetTime_ParsesTMFormat()
        {
            var bytes = Encoding.ASCII.GetBytes("143052");
            var element = new DicomStringElement(new DicomTag(0x0008, 0x0030), DicomVR.TM, bytes);

            var time = element.GetTime();

            Assert.That(time, Is.Not.Null);
            Assert.That(time!.Value.Hour, Is.EqualTo(14));
            Assert.That(time.Value.Minute, Is.EqualTo(30));
            Assert.That(time.Value.Second, Is.EqualTo(52));
        }

        [Test]
        public void DicomStringElement_GetDateTime_ParsesDTFormat()
        {
            var bytes = Encoding.ASCII.GetBytes("20240115143052");
            var element = new DicomStringElement(new DicomTag(0x0008, 0x002A), DicomVR.DT, bytes);

            var dateTime = element.GetDateTime();

            Assert.That(dateTime, Is.Not.Null);
            Assert.That(dateTime!.Value.Year, Is.EqualTo(2024));
            Assert.That(dateTime.Value.Month, Is.EqualTo(1));
            Assert.That(dateTime.Value.Day, Is.EqualTo(15));
            Assert.That(dateTime.Value.Hour, Is.EqualTo(14));
            Assert.That(dateTime.Value.Minute, Is.EqualTo(30));
            Assert.That(dateTime.Value.Second, Is.EqualTo(52));
        }

        [Test]
        public void DicomStringElement_GetInt32_ParsesISFormat()
        {
            var bytes = Encoding.ASCII.GetBytes("12345");
            var element = new DicomStringElement(new DicomTag(0x0020, 0x0013), DicomVR.IS, bytes);

            var value = element.GetInt32();

            Assert.That(value, Is.EqualTo(12345));
        }

        [Test]
        public void DicomStringElement_GetFloat64_ParsesDSFormat()
        {
            var bytes = Encoding.ASCII.GetBytes("1.5\\2.5");
            var element = new DicomStringElement(new DicomTag(0x0028, 0x0030), DicomVR.DS, bytes);

            var value = element.GetFloat64();

            Assert.That(value, Is.EqualTo(1.5));
        }

        [Test]
        public void DicomStringElement_InvalidFormat_ReturnsNull()
        {
            var bytes = Encoding.ASCII.GetBytes("INVALID");
            var element = new DicomStringElement(new DicomTag(0x0020, 0x0013), DicomVR.IS, bytes);

            var value = element.GetInt32();

            Assert.That(value, Is.Null);
        }

        [Test]
        public void DicomStringElement_GetStringOrThrow_ThrowsForInvalid()
        {
            var element = new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO, ReadOnlyMemory<byte>.Empty);

            Assert.Throws<DicomDataException>(() => element.GetStringOrThrow());
        }

        [Test]
        public void DicomNumericElement_GetInt16_ReadsSSValue()
        {
            var bytes = new byte[] { 0x39, 0x30 }; // 12345 in little-endian
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0103), DicomVR.SS, bytes);

            var value = element.GetInt16();

            Assert.That(value, Is.EqualTo(12345));
        }

        [Test]
        public void DicomNumericElement_GetUInt16_ReadsUSValue()
        {
            var bytes = new byte[] { 0x00, 0x01 }; // 256 in little-endian
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0010), DicomVR.US, bytes);

            var value = element.GetUInt16();

            Assert.That(value, Is.EqualTo(256));
        }

        [Test]
        public void DicomNumericElement_GetInt32_ReadsSLValue()
        {
            var bytes = new byte[] { 0x15, 0xCD, 0x5B, 0x07 }; // 123456789 in little-endian
            var element = new DicomNumericElement(new DicomTag(0x0020, 0x0013), DicomVR.SL, bytes);

            var value = element.GetInt32();

            Assert.That(value, Is.EqualTo(123456789));
        }

        [Test]
        public void DicomNumericElement_GetUInt32_ReadsULValue()
        {
            var bytes = new byte[] { 0x00, 0x00, 0x00, 0x01 }; // 16777216 in little-endian
            var element = new DicomNumericElement(new DicomTag(0x0004, 0x1400), DicomVR.UL, bytes);

            var value = element.GetUInt32();

            Assert.That(value, Is.EqualTo(16777216));
        }

        [Test]
        public void DicomNumericElement_GetFloat32_ReadsFLValue()
        {
            var bytes = BitConverter.GetBytes(1.5f);
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0009), DicomVR.FL, bytes);

            var value = element.GetFloat32();

            Assert.That(value, Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void DicomNumericElement_GetFloat64_ReadsFDValue()
        {
            var bytes = BitConverter.GetBytes(2.5);
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0009), DicomVR.FD, bytes);

            var value = element.GetFloat64();

            Assert.That(value, Is.EqualTo(2.5).Within(0.001));
        }

        [Test]
        public void DicomNumericElement_GetTag_ReadsATValue()
        {
            var bytes = new byte[] { 0x10, 0x00, 0x20, 0x00 }; // (0010,0020)
            var element = new DicomNumericElement(new DicomTag(0x0000, 0x0000), DicomVR.AT, bytes);

            var tag = element.GetTag();

            Assert.That(tag, Is.Not.Null);
            Assert.That(tag!.Value.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Value.Element, Is.EqualTo(0x0020));
        }

        [Test]
        public void DicomNumericElement_GetInt16Array_ReturnsMultipleValues()
        {
            var bytes = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 }; // [1, 2, 3]
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0103), DicomVR.SS, bytes);

            var values = element.GetInt16Array();

            Assert.That(values, Is.Not.Null);
            Assert.That(values!.Length, Is.EqualTo(3));
            Assert.That(values[0], Is.EqualTo(1));
            Assert.That(values[1], Is.EqualTo(2));
            Assert.That(values[2], Is.EqualTo(3));
        }

        [Test]
        public void DicomNumericElement_EmptyValue_ReturnsNull()
        {
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0010), DicomVR.US, ReadOnlyMemory<byte>.Empty);

            var value = element.GetUInt16();

            Assert.That(value, Is.Null);
        }

        [Test]
        public void DicomNumericElement_ToOwned_CreatesIndependentCopy()
        {
            var bytes = new byte[] { 0x01, 0x00 };
            var element = new DicomNumericElement(new DicomTag(0x0028, 0x0010), DicomVR.US, bytes);

            var owned = element.ToOwned() as DicomNumericElement;

            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.Tag, Is.EqualTo(element.Tag));
            Assert.That(owned.VR, Is.EqualTo(element.VR));
            Assert.That(owned.GetUInt16(), Is.EqualTo(element.GetUInt16()));

            // Modify original bytes - owned should be unaffected
            // Original value: [0x01, 0x00] = 0x0001 (little-endian)
            // After modification: [0xFF, 0x00] = 0x00FF = 255 (little-endian)
            bytes[0] = 0xFF;
            Assert.That(element.GetUInt16(), Is.EqualTo(0x00FF));  // 255 in little-endian
            Assert.That(owned.GetUInt16(), Is.EqualTo(0x0001));    // Still 1 (independent copy)
        }

        [Test]
        public void DicomBinaryElement_GetBytes_ReturnsRawValue()
        {
            var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var element = new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, bytes);

            var value = element.GetBytes();

            Assert.That(value.Length, Is.EqualTo(4));
            Assert.That(value.Span[0], Is.EqualTo(0x01));
            Assert.That(value.Span[3], Is.EqualTo(0x04));
        }

        [Test]
        public void DicomBinaryElement_ToOwned_CopiesBytes()
        {
            var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var element = new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, bytes);

            var owned = element.ToOwned() as DicomBinaryElement;

            Assert.That(owned, Is.Not.Null);
            Assert.That(owned!.GetBytes().Length, Is.EqualTo(4));
        }
    }
}
