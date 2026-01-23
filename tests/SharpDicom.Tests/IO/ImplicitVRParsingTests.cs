using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Comprehensive tests for implicit VR parsing in DicomStreamReader.
    /// </summary>
    /// <remarks>
    /// Implicit VR Little Endian (UID 1.2.840.10008.1.2) is the default DICOM transfer syntax.
    /// In implicit VR, the Value Representation is not encoded in the byte stream.
    /// Instead, the VR must be looked up from the DICOM dictionary by tag.
    ///
    /// Format: Tag (4 bytes) + Length (4 bytes) + Value
    /// - Tag: Group (2 bytes LE) + Element (2 bytes LE)
    /// - Length: Always 32-bit unsigned integer (4 bytes LE)
    /// - Value: Raw bytes
    /// </remarks>
    [TestFixture]
    public class ImplicitVRParsingTests
    {
        #region Basic Tag and VR Lookup Tests

        [Test]
        public void ImplicitVR_PatientName_LooksUpPNFromDictionary()
        {
            // PatientName (0010,0010) - expected VR is PN
            // Implicit VR format: Tag (4 bytes) + Length (4 bytes)
            // Value: "SMITH^JOHN" (10 bytes, but we'll pad to 12 for even length)
            byte[] data =
            {
                0x10, 0x00, 0x10, 0x00,  // Tag: (0010,0010) - PatientName
                0x0C, 0x00, 0x00, 0x00,  // Length: 12 (LE)
                (byte)'S', (byte)'M', (byte)'I', (byte)'T', (byte)'H', (byte)'^',
                (byte)'J', (byte)'O', (byte)'H', (byte)'N', (byte)' ', (byte)' '
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.PatientName));
            Assert.That(vr, Is.EqualTo(DicomVR.PN), "VR should be PN from dictionary lookup");
            Assert.That(length, Is.EqualTo(12u));
        }

        [Test]
        public void ImplicitVR_SOPClassUID_LooksUpUIFromDictionary()
        {
            // SOPClassUID (0008,0016) - expected VR is UI
            string uid = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage
            byte[] uidBytes = System.Text.Encoding.ASCII.GetBytes(uid);

            // Pad to even length if needed
            int paddedLength = (uidBytes.Length % 2 == 1) ? uidBytes.Length + 1 : uidBytes.Length;

            byte[] data = new byte[8 + paddedLength];
            // Tag
            data[0] = 0x08; data[1] = 0x00;  // Group 0008
            data[2] = 0x16; data[3] = 0x00;  // Element 0016
            // Length (32-bit LE)
            data[4] = (byte)paddedLength;
            data[5] = 0x00;
            data[6] = 0x00;
            data[7] = 0x00;
            // Value
            System.Array.Copy(uidBytes, 0, data, 8, uidBytes.Length);
            // Null pad if odd
            if (uidBytes.Length < paddedLength)
                data[8 + uidBytes.Length] = 0x00;

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.SOPClassUID));
            Assert.That(vr, Is.EqualTo(DicomVR.UI), "VR should be UI from dictionary lookup");
        }

        [Test]
        public void ImplicitVR_PatientID_LooksUpLOFromDictionary()
        {
            // PatientID (0010,0020) - expected VR is LO
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag: (0010,0020) - PatientID
                0x08, 0x00, 0x00, 0x00,  // Length: 8 (LE)
                (byte)'P', (byte)'A', (byte)'T', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'1'
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.PatientID));
            Assert.That(vr, Is.EqualTo(DicomVR.LO), "VR should be LO from dictionary lookup");
            Assert.That(length, Is.EqualTo(8u));
        }

        [Test]
        public void ImplicitVR_SpecificCharacterSet_LooksUpCSFromDictionary()
        {
            // SpecificCharacterSet (0008,0005) - expected VR is CS
            byte[] data =
            {
                0x08, 0x00, 0x05, 0x00,  // Tag: (0008,0005) - SpecificCharacterSet
                0x0A, 0x00, 0x00, 0x00,  // Length: 10 (LE)
                (byte)'I', (byte)'S', (byte)'O', (byte)'_', (byte)'I', (byte)'R',
                (byte)' ', (byte)'1', (byte)'0', (byte)'0'
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.SpecificCharacterSet));
            Assert.That(vr, Is.EqualTo(DicomVR.CS), "VR should be CS from dictionary lookup");
        }

        #endregion

        #region Unknown Tag Tests (Should Default to UN)

        [Test]
        public void ImplicitVR_UnknownPrivateTag_DefaultsToUN()
        {
            // Unknown private tag (0011,0010) - private creator
            byte[] data =
            {
                0x11, 0x00, 0x10, 0x00,  // Tag: (0011,0010) - private creator slot
                0x06, 0x00, 0x00, 0x00,  // Length: 6 (LE)
                (byte)'M', (byte)'Y', (byte)'A', (byte)'P', (byte)'P', (byte)' '
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0011));
            Assert.That(tag.Element, Is.EqualTo(0x0010));
            Assert.That(tag.IsPrivate, Is.True);
            Assert.That(vr, Is.EqualTo(DicomVR.UN), "Unknown private tag should default to UN");
            Assert.That(length, Is.EqualTo(6u));
        }

        [Test]
        public void ImplicitVR_UnknownPrivateDataTag_DefaultsToUN()
        {
            // Unknown private data element (0011,1001)
            byte[] data =
            {
                0x11, 0x00, 0x01, 0x10,  // Tag: (0011,1001) - private data element
                0x04, 0x00, 0x00, 0x00,  // Length: 4 (LE)
                0x01, 0x02, 0x03, 0x04
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0011));
            Assert.That(tag.IsPrivate, Is.True);
            Assert.That(vr, Is.EqualTo(DicomVR.UN), "Unknown private data tag should default to UN");
        }

        [Test]
        public void ImplicitVR_UnknownStandardTag_DefaultsToUN()
        {
            // Hypothetical unknown tag in group 0008 (unlikely but tests fallback)
            // Use a tag that definitely doesn't exist: (0008,FFFF)
            byte[] data =
            {
                0x08, 0x00, 0xFF, 0xFF,  // Tag: (0008,FFFF) - non-existent
                0x02, 0x00, 0x00, 0x00,  // Length: 2 (LE)
                0xAB, 0xCD
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0008));
            Assert.That(tag.Element, Is.EqualTo(0xFFFF));
            Assert.That(vr, Is.EqualTo(DicomVR.UN), "Unknown tag should default to UN");
        }

        #endregion

        #region 32-bit Length Tests

        [Test]
        public void ImplicitVR_Always32BitLength_EvenForShortVRTags()
        {
            // In explicit VR, PatientID uses 16-bit length (LO is short VR).
            // In implicit VR, ALL elements use 32-bit length.
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag: (0010,0020) - PatientID
                0x10, 0x00, 0x00, 0x00,  // Length: 16 (32-bit LE, NOT 16-bit)
                (byte)'P', (byte)'A', (byte)'T', (byte)'I', (byte)'E', (byte)'N', (byte)'T',
                (byte)'_', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'0',
                (byte)'0', (byte)'1'
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(length, Is.EqualTo(16u), "Implicit VR should read 32-bit length");
            Assert.That(reader.Position, Is.EqualTo(8), "Header should be 8 bytes (tag + 32-bit length)");
        }

        [Test]
        public void ImplicitVR_ZeroLength_ParsesCorrectly()
        {
            // Empty value element
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag: (0010,0020) - PatientID
                0x00, 0x00, 0x00, 0x00   // Length: 0 (LE)
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.PatientID));
            Assert.That(length, Is.EqualTo(0u), "Zero length should parse correctly");
        }

        [Test]
        public void ImplicitVR_LargeLength_ParsesCorrectly()
        {
            // Large length value (1 MB)
            byte[] data =
            {
                0x08, 0x00, 0x16, 0x00,  // Tag: (0008,0016) - SOPClassUID
                0x00, 0x00, 0x10, 0x00   // Length: 0x00100000 = 1,048,576 (1 MB)
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(length, Is.EqualTo(1048576u), "Large length value should parse correctly");
        }

        [Test]
        public void ImplicitVR_MaxLength_IsUndefinedLength()
        {
            // Maximum length (0xFFFFFFFF) indicates undefined length
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag: (0010,0020)
                0xFF, 0xFF, 0xFF, 0xFF   // Length: 0xFFFFFFFF (undefined length)
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(length, Is.EqualTo(0xFFFFFFFFu), "Undefined length value should be preserved");
        }

        #endregion

        #region Multiple Element Sequential Parsing Tests

        [Test]
        public void ImplicitVR_MultipleElements_ParseSequentially()
        {
            // Two consecutive elements
            byte[] data =
            {
                // Element 1: SOPClassUID (0008,0016)
                0x08, 0x00, 0x16, 0x00,  // Tag
                0x1A, 0x00, 0x00, 0x00,  // Length: 26
                // Value: "1.2.840.10008.5.1.4.1.1.2" (CT Image Storage) + null pad
                (byte)'1', (byte)'.', (byte)'2', (byte)'.', (byte)'8', (byte)'4', (byte)'0',
                (byte)'.', (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)'8', (byte)'.',
                (byte)'5', (byte)'.', (byte)'1', (byte)'.', (byte)'4', (byte)'.', (byte)'1',
                (byte)'.', (byte)'1', (byte)'.', (byte)'2', 0x00,

                // Element 2: PatientName (0010,0010)
                0x10, 0x00, 0x10, 0x00,  // Tag
                0x08, 0x00, 0x00, 0x00,  // Length: 8
                (byte)'S', (byte)'M', (byte)'I', (byte)'T', (byte)'H', (byte)'^',
                (byte)'J', (byte)' '
            };

            var reader = new DicomStreamReader(data, explicitVR: false);

            // First element
            Assert.That(reader.TryReadElementHeader(out var tag1, out var vr1, out var length1), Is.True);
            Assert.That(tag1, Is.EqualTo(DicomTag.SOPClassUID));
            Assert.That(vr1, Is.EqualTo(DicomVR.UI));
            Assert.That(length1, Is.EqualTo(26u));

            Assert.That(reader.TryReadValue(length1, out _), Is.True);

            // Second element
            Assert.That(reader.TryReadElementHeader(out var tag2, out var vr2, out var length2), Is.True);
            Assert.That(tag2, Is.EqualTo(DicomTag.PatientName));
            Assert.That(vr2, Is.EqualTo(DicomVR.PN));
            Assert.That(length2, Is.EqualTo(8u));

            Assert.That(reader.TryReadValue(length2, out _), Is.True);
            Assert.That(reader.IsAtEnd, Is.True);
        }

        [Test]
        public void ImplicitVR_MixedKnownAndUnknownTags_ParsesCorrectly()
        {
            // Mix of known and unknown tags
            byte[] data =
            {
                // Element 1: PatientID (0010,0020) - known, VR=LO
                0x10, 0x00, 0x20, 0x00,
                0x04, 0x00, 0x00, 0x00,
                (byte)'T', (byte)'E', (byte)'S', (byte)'T',

                // Element 2: Unknown private (0011,0010) - unknown, VR=UN
                0x11, 0x00, 0x10, 0x00,
                0x04, 0x00, 0x00, 0x00,
                (byte)'X', (byte)'Y', (byte)'Z', (byte)' ',

                // Element 3: SOPInstanceUID (0008,0018) - known, VR=UI
                0x08, 0x00, 0x18, 0x00,
                0x08, 0x00, 0x00, 0x00,
                (byte)'1', (byte)'.', (byte)'2', (byte)'.', (byte)'3', (byte)'.', (byte)'4', 0x00
            };

            var reader = new DicomStreamReader(data, explicitVR: false);

            // First: PatientID -> LO
            Assert.That(reader.TryReadElementHeader(out var tag1, out var vr1, out var length1), Is.True);
            Assert.That(vr1, Is.EqualTo(DicomVR.LO));
            reader.TryReadValue(length1, out _);

            // Second: Unknown -> UN
            Assert.That(reader.TryReadElementHeader(out var tag2, out var vr2, out var length2), Is.True);
            Assert.That(vr2, Is.EqualTo(DicomVR.UN));
            reader.TryReadValue(length2, out _);

            // Third: SOPInstanceUID -> UI
            Assert.That(reader.TryReadElementHeader(out var tag3, out var vr3, out var length3), Is.True);
            Assert.That(vr3, Is.EqualTo(DicomVR.UI));
            reader.TryReadValue(length3, out _);

            Assert.That(reader.IsAtEnd, Is.True);
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void ImplicitVR_InsufficientDataForHeader_ReturnsFalse()
        {
            // Only 6 bytes, need 8 for header
            byte[] data = { 0x10, 0x00, 0x10, 0x00, 0x04, 0x00 };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.False);
        }

        [Test]
        public void ImplicitVR_EmptyBuffer_ReturnsFalse()
        {
            byte[] data = System.Array.Empty<byte>();

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.False);
        }

        [Test]
        public void ImplicitVR_ValueReadAfterHeader_WorksCorrectly()
        {
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag: PatientID
                0x04, 0x00, 0x00, 0x00,  // Length: 4
                (byte)'A', (byte)'B', (byte)'C', (byte)'D'  // Value
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out _, out _, out var length), Is.True);
            Assert.That(reader.TryReadValue(length, out var value), Is.True);

            Assert.That(value.Length, Is.EqualTo(4));
            Assert.That(value[0], Is.EqualTo((byte)'A'));
            Assert.That(value[1], Is.EqualTo((byte)'B'));
            Assert.That(value[2], Is.EqualTo((byte)'C'));
            Assert.That(value[3], Is.EqualTo((byte)'D'));
        }

        [Test]
        public void ImplicitVR_UndefinedLengthValue_ReadValueReturnsFalse()
        {
            // Undefined length - TryReadValue cannot handle this (sequences)
            byte[] data =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag
                0xFF, 0xFF, 0xFF, 0xFF,  // Length: undefined
                // Would need delimiters to parse
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out _, out _, out var length), Is.True);
            Assert.That(length, Is.EqualTo(0xFFFFFFFFu));

            // TryReadValue should return false for undefined length
            Assert.That(reader.TryReadValue(length, out _), Is.False);
        }

        #endregion

        #region Comparison with Explicit VR Tests

        [Test]
        public void ExplicitVR_PatientID_ReadsVRFromStream()
        {
            // For comparison: same tag in explicit VR format
            // PatientID (0010,0020) LO with 16-bit length
            byte[] explicitData =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag
                (byte)'L', (byte)'O',    // VR: LO
                0x04, 0x00,              // Length: 4 (16-bit)
                (byte)'T', (byte)'E', (byte)'S', (byte)'T'
            };

            var reader = new DicomStreamReader(explicitData, explicitVR: true);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.PatientID));
            Assert.That(vr, Is.EqualTo(DicomVR.LO), "VR should be read from stream");
            Assert.That(length, Is.EqualTo(4u));
            Assert.That(reader.Position, Is.EqualTo(8), "Explicit VR short form is 8 bytes");
        }

        [Test]
        public void ImplicitVR_PatientID_LooksUpVRFromDictionary()
        {
            // Same tag in implicit VR format
            byte[] implicitData =
            {
                0x10, 0x00, 0x20, 0x00,  // Tag
                0x04, 0x00, 0x00, 0x00,  // Length: 4 (32-bit)
                (byte)'T', (byte)'E', (byte)'S', (byte)'T'
            };

            var reader = new DicomStreamReader(implicitData, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.PatientID));
            Assert.That(vr, Is.EqualTo(DicomVR.LO), "VR should be looked up from dictionary");
            Assert.That(length, Is.EqualTo(4u));
            Assert.That(reader.Position, Is.EqualTo(8), "Implicit VR header is 8 bytes");
        }

        [Test]
        public void HeaderSize_ImplicitVR_Always8Bytes()
        {
            // Implicit VR: 4 bytes tag + 4 bytes length = 8 bytes always
            byte[] data =
            {
                0x10, 0x00, 0x10, 0x00,  // Tag
                0x00, 0x00, 0x00, 0x00   // Length
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.True);
            Assert.That(reader.Position, Is.EqualTo(8));
        }

        [Test]
        public void HeaderSize_ExplicitVR_ShortForm_8Bytes()
        {
            // Explicit VR short: 4 bytes tag + 2 bytes VR + 2 bytes length = 8 bytes
            byte[] data =
            {
                0x10, 0x00, 0x10, 0x00,  // Tag
                (byte)'P', (byte)'N',    // VR
                0x00, 0x00               // Length
            };

            var reader = new DicomStreamReader(data, explicitVR: true);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.True);
            Assert.That(reader.Position, Is.EqualTo(8));
        }

        [Test]
        public void HeaderSize_ExplicitVR_LongForm_12Bytes()
        {
            // Explicit VR long (OB, OW, OF, SQ, UC, UN, UR, UT):
            // 4 bytes tag + 2 bytes VR + 2 bytes reserved + 4 bytes length = 12 bytes
            byte[] data =
            {
                0xE0, 0x7F, 0x10, 0x00,  // Tag: PixelData
                (byte)'O', (byte)'W',    // VR: OW
                0x00, 0x00,              // Reserved
                0x00, 0x00, 0x00, 0x00   // Length (32-bit)
            };

            var reader = new DicomStreamReader(data, explicitVR: true);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.True);
            Assert.That(reader.Position, Is.EqualTo(12));
        }

        #endregion

        #region Delimiter Tag Tests

        [Test]
        public void ImplicitVR_ItemTag_DefaultsToUN()
        {
            // Item tag (FFFE,E000) - delimiter, not in dictionary, should be UN
            byte[] data =
            {
                0xFE, 0xFF, 0x00, 0xE0,  // Tag: (FFFE,E000) - Item
                0x10, 0x00, 0x00, 0x00   // Length: 16
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.Item));
            Assert.That(length, Is.EqualTo(16u));
            // Delimiter tags are not in standard dictionary, should default to UN
            Assert.That(vr, Is.EqualTo(DicomVR.UN));
        }

        [Test]
        public void ImplicitVR_ItemDelimitationTag_DefaultsToUN()
        {
            // Item Delimitation tag (FFFE,E00D)
            byte[] data =
            {
                0xFE, 0xFF, 0x0D, 0xE0,  // Tag: (FFFE,E00D) - Item Delimitation
                0x00, 0x00, 0x00, 0x00   // Length: 0 (always for delimiters)
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.ItemDelimitationItem));
            Assert.That(length, Is.EqualTo(0u));
        }

        [Test]
        public void ImplicitVR_SequenceDelimitationTag_DefaultsToUN()
        {
            // Sequence Delimitation tag (FFFE,E0DD)
            byte[] data =
            {
                0xFE, 0xFF, 0xDD, 0xE0,  // Tag: (FFFE,E0DD) - Sequence Delimitation
                0x00, 0x00, 0x00, 0x00   // Length: 0 (always)
            };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag, Is.EqualTo(DicomTag.SequenceDelimitationItem));
            Assert.That(length, Is.EqualTo(0u));
        }

        #endregion

        #region DicomReaderOptions Sequence Configuration Tests

        [Test]
        public void DicomReaderOptions_DefaultMaxSequenceDepth_Is128()
        {
            var options = new DicomReaderOptions();
            Assert.That(options.MaxSequenceDepth, Is.EqualTo(128));
        }

        [Test]
        public void DicomReaderOptions_DefaultMaxTotalItems_Is100000()
        {
            var options = new DicomReaderOptions();
            Assert.That(options.MaxTotalItems, Is.EqualTo(100_000));
        }

        [Test]
        public void DicomReaderOptions_Strict_HasStandardLimits()
        {
            var options = DicomReaderOptions.Strict;
            Assert.That(options.MaxSequenceDepth, Is.EqualTo(128));
            Assert.That(options.MaxTotalItems, Is.EqualTo(100_000));
        }

        [Test]
        public void DicomReaderOptions_Lenient_HasStandardLimits()
        {
            var options = DicomReaderOptions.Lenient;
            Assert.That(options.MaxSequenceDepth, Is.EqualTo(128));
            Assert.That(options.MaxTotalItems, Is.EqualTo(100_000));
        }

        [Test]
        public void DicomReaderOptions_Permissive_HasHigherLimits()
        {
            var options = DicomReaderOptions.Permissive;
            Assert.That(options.MaxSequenceDepth, Is.EqualTo(256));
            Assert.That(options.MaxTotalItems, Is.EqualTo(500_000));
        }

        [Test]
        public void DicomReaderOptions_CustomLimits_CanBeSet()
        {
            var options = new DicomReaderOptions
            {
                MaxSequenceDepth = 50,
                MaxTotalItems = 10_000
            };
            Assert.That(options.MaxSequenceDepth, Is.EqualTo(50));
            Assert.That(options.MaxTotalItems, Is.EqualTo(10_000));
        }

        #endregion
    }
}
