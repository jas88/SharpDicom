using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Tests.Network.Pdu
{
    [TestFixture]
    public class PduReaderTests
    {
        #region PDU Header Tests

        [Test]
        public void TryReadPduHeader_InsufficientData_ReturnsFalse()
        {
            // Only 5 bytes, need 6 for header
            var buffer = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadPduHeader(out _, out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryReadPduHeader_ValidHeader_ParsesCorrectly()
        {
            // A-ASSOCIATE-RQ with length 0x00000044 (68 bytes)
            var buffer = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x44 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadPduHeader(out PduType type, out uint length);

            Assert.That(result, Is.True);
            Assert.That(type, Is.EqualTo(PduType.AssociateRequest));
            Assert.That(length, Is.EqualTo(68u));
        }

        [Test]
        public void TryReadPduHeader_BigEndianLength_ParsesCorrectly()
        {
            // P-DATA with length 0x00010203 (66051 bytes) - Big-Endian
            var buffer = new byte[] { 0x04, 0x00, 0x00, 0x01, 0x02, 0x03 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadPduHeader(out PduType type, out uint length);

            Assert.That(result, Is.True);
            Assert.That(type, Is.EqualTo(PduType.PDataTransfer));
            Assert.That(length, Is.EqualTo(0x00010203u));
        }

        [Test]
        public void TryReadPduHeader_AllPduTypes_ParseCorrectly()
        {
            var types = new[]
            {
                (PduType.AssociateRequest, (byte)0x01),
                (PduType.AssociateAccept, (byte)0x02),
                (PduType.AssociateReject, (byte)0x03),
                (PduType.PDataTransfer, (byte)0x04),
                (PduType.ReleaseRequest, (byte)0x05),
                (PduType.ReleaseResponse, (byte)0x06),
                (PduType.Abort, (byte)0x07)
            };

            foreach (var (expectedType, typeByte) in types)
            {
                var buffer = new byte[] { typeByte, 0x00, 0x00, 0x00, 0x00, 0x04 };
                var reader = new PduReader(buffer);

                bool result = reader.TryReadPduHeader(out PduType type, out _);

                Assert.That(result, Is.True, $"Failed for {expectedType}");
                Assert.That(type, Is.EqualTo(expectedType), $"Type mismatch for {expectedType}");
            }
        }

        #endregion

        #region A-ASSOCIATE-RJ Tests

        [Test]
        public void TryReadAssociateReject_ValidData_ParsesCorrectly()
        {
            // A-ASSOCIATE-RJ body: Reserved(1) + Result(1) + Source(1) + Reason(1)
            var buffer = new byte[] { 0x00, 0x01, 0x01, 0x03 }; // PermanentRejection, ServiceUser, CallingAETitleNotRecognized
            var reader = new PduReader(buffer);

            bool result = reader.TryReadAssociateReject(out RejectResult rejectResult, out RejectSource source, out RejectReason reason);

            Assert.That(result, Is.True);
            Assert.That(rejectResult, Is.EqualTo(RejectResult.PermanentRejection));
            Assert.That(source, Is.EqualTo(RejectSource.ServiceUser));
            Assert.That(reason, Is.EqualTo(RejectReason.CallingAETitleNotRecognized));
        }

        [Test]
        public void TryReadAssociateReject_InsufficientData_ReturnsFalse()
        {
            var buffer = new byte[] { 0x00, 0x01, 0x01 }; // Only 3 bytes, need 4
            var reader = new PduReader(buffer);

            bool result = reader.TryReadAssociateReject(out _, out _, out _);

            Assert.That(result, Is.False);
        }

        #endregion

        #region A-RELEASE Tests

        [Test]
        public void TryReadReleaseRequest_ValidData_ReturnsTrue()
        {
            // A-RELEASE-RQ has 4 reserved bytes
            var buffer = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadReleaseRequest();

            Assert.That(result, Is.True);
            Assert.That(reader.Position, Is.EqualTo(4));
        }

        [Test]
        public void TryReadReleaseRequest_InsufficientData_ReturnsFalse()
        {
            var buffer = new byte[] { 0x00, 0x00, 0x00 }; // Only 3 bytes
            var reader = new PduReader(buffer);

            bool result = reader.TryReadReleaseRequest();

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryReadReleaseResponse_ValidData_ReturnsTrue()
        {
            // A-RELEASE-RP has 4 reserved bytes
            var buffer = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadReleaseResponse();

            Assert.That(result, Is.True);
            Assert.That(reader.Position, Is.EqualTo(4));
        }

        [Test]
        public void TryReadReleaseResponse_InsufficientData_ReturnsFalse()
        {
            var buffer = new byte[] { 0x00, 0x00 }; // Only 2 bytes
            var reader = new PduReader(buffer);

            bool result = reader.TryReadReleaseResponse();

            Assert.That(result, Is.False);
        }

        #endregion

        #region A-ABORT Tests

        [Test]
        public void TryReadAbort_ValidData_ParsesCorrectly()
        {
            // A-ABORT body: Reserved(2) + Source(1) + Reason(1)
            var buffer = new byte[] { 0x00, 0x00, 0x02, 0x06 }; // ServiceProvider, InvalidPduParameter
            var reader = new PduReader(buffer);

            bool result = reader.TryReadAbort(out AbortSource source, out AbortReason reason);

            Assert.That(result, Is.True);
            Assert.That(source, Is.EqualTo(AbortSource.ServiceProvider));
            Assert.That(reason, Is.EqualTo(AbortReason.InvalidPduParameter));
        }

        [Test]
        public void TryReadAbort_InsufficientData_ReturnsFalse()
        {
            var buffer = new byte[] { 0x00, 0x00, 0x02 }; // Only 3 bytes
            var reader = new PduReader(buffer);

            bool result = reader.TryReadAbort(out _, out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryReadAbort_ServiceUser_NoReason()
        {
            // A-ABORT from service user: reason should be NotSpecified
            var buffer = new byte[] { 0x00, 0x00, 0x00, 0x00 }; // ServiceUser, NotSpecified
            var reader = new PduReader(buffer);

            bool result = reader.TryReadAbort(out AbortSource source, out AbortReason reason);

            Assert.That(result, Is.True);
            Assert.That(source, Is.EqualTo(AbortSource.ServiceUser));
            Assert.That(reason, Is.EqualTo(AbortReason.NotSpecified));
        }

        #endregion

        #region AE Title Tests

        [Test]
        public void TryReadAssociateRequest_AeTitles_TrimsTrailingSpaces()
        {
            // Build A-ASSOCIATE-RQ fixed fields (68 bytes)
            var buffer = new byte[68];

            // Protocol version (Big-Endian)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), 0x0001);

            // Reserved (2 bytes) - already zero

            // Called AE Title at offset 4 (16 bytes, space-padded)
            var calledAe = System.Text.Encoding.ASCII.GetBytes("CALLED_SCP      ");
            Array.Copy(calledAe, 0, buffer, 4, 16);

            // Calling AE Title at offset 20 (16 bytes, space-padded)
            var callingAe = System.Text.Encoding.ASCII.GetBytes("CALLING_SCU     ");
            Array.Copy(callingAe, 0, buffer, 20, 16);

            // Reserved (32 bytes) - already zero

            var reader = new PduReader(buffer);

            bool result = reader.TryReadAssociateRequest(
                out ushort protocolVersion,
                out string parsedCalledAE,
                out string parsedCallingAE,
                out _);

            Assert.That(result, Is.True);
            Assert.That(protocolVersion, Is.EqualTo(0x0001));
            Assert.That(parsedCalledAE, Is.EqualTo("CALLED_SCP"));
            Assert.That(parsedCallingAE, Is.EqualTo("CALLING_SCU"));
        }

        [Test]
        public void TryReadAssociateRequest_EmptyAeTitle_ReturnsEmptyString()
        {
            // Build A-ASSOCIATE-RQ with all-space AE titles
            var buffer = new byte[68];
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), 0x0001);

            // Fill AE titles with spaces (default padding)
            for (int i = 4; i < 36; i++)
            {
                buffer[i] = (byte)' ';
            }

            var reader = new PduReader(buffer);

            reader.TryReadAssociateRequest(out _, out string calledAE, out string callingAE, out _);

            Assert.That(calledAE, Is.EqualTo(string.Empty));
            Assert.That(callingAE, Is.EqualTo(string.Empty));
        }

        #endregion

        #region Variable Item Tests

        [Test]
        public void TryReadVariableItem_ValidData_ParsesCorrectly()
        {
            // Application Context item: Type(1) + Reserved(1) + Length(2)
            var buffer = new byte[] { 0x10, 0x00, 0x00, 0x15 }; // Length = 21 (Big-Endian)
            var reader = new PduReader(buffer);

            bool result = reader.TryReadVariableItem(out ItemType type, out ushort length);

            Assert.That(result, Is.True);
            Assert.That(type, Is.EqualTo(ItemType.ApplicationContext));
            Assert.That(length, Is.EqualTo(21));
        }

        [Test]
        public void TryReadVariableItem_BigEndianLength_ParsesCorrectly()
        {
            // Item with length 0x0102 (258 bytes)
            var buffer = new byte[] { 0x20, 0x00, 0x01, 0x02 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadVariableItem(out _, out ushort length);

            Assert.That(result, Is.True);
            Assert.That(length, Is.EqualTo(0x0102));
        }

        [Test]
        public void TryReadMaxPduLength_ValidData_ParsesCorrectly()
        {
            // Max PDU Length: 0x00004000 (16384)
            var buffer = new byte[] { 0x00, 0x00, 0x40, 0x00 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadMaxPduLength(out uint maxLength);

            Assert.That(result, Is.True);
            Assert.That(maxLength, Is.EqualTo(16384u));
        }

        #endregion

        #region PDV Tests

        [Test]
        public void TryReadPresentationDataValue_ValidData_ParsesCorrectly()
        {
            // PDV: Length(4) + ContextID(1) + MessageControlHeader(1) + Data
            var buffer = new byte[10];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, 4); // PDV length = 4 (ContextID + Header + 2 data bytes)
            buffer[4] = 0x01; // Presentation Context ID
            buffer[5] = 0x03; // Command + Last fragment (bits 0 and 1 set)
            buffer[6] = 0xAA; // Data byte 1
            buffer[7] = 0xBB; // Data byte 2

            var reader = new PduReader(buffer);

            bool result = reader.TryReadPresentationDataValue(
                out byte contextId,
                out bool isCommand,
                out bool isLastFragment,
                out ReadOnlySpan<byte> data);

            Assert.That(result, Is.True);
            Assert.That(contextId, Is.EqualTo(1));
            Assert.That(isCommand, Is.True);
            Assert.That(isLastFragment, Is.True);
            Assert.That(data.Length, Is.EqualTo(2));
            Assert.That(data[0], Is.EqualTo(0xAA));
            Assert.That(data[1], Is.EqualTo(0xBB));
        }

        [Test]
        public void TryReadPresentationDataValue_DataOnly_NotLastFragment()
        {
            var buffer = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, 2); // PDV length = 2 (ContextID + Header only)
            buffer[4] = 0x03; // Presentation Context ID
            buffer[5] = 0x00; // Data (not command), not last fragment

            var reader = new PduReader(buffer);

            bool result = reader.TryReadPresentationDataValue(
                out byte contextId,
                out bool isCommand,
                out bool isLastFragment,
                out _);

            Assert.That(result, Is.True);
            Assert.That(contextId, Is.EqualTo(3));
            Assert.That(isCommand, Is.False);
            Assert.That(isLastFragment, Is.False);
        }

        [Test]
        public void TryReadPresentationDataValue_InsufficientData_ReturnsFalse()
        {
            // Only 5 bytes, need at least 6 (4 for length + context ID + header)
            var buffer = new byte[] { 0x00, 0x00, 0x00, 0x02, 0x01 };
            var reader = new PduReader(buffer);

            bool result = reader.TryReadPresentationDataValue(out _, out _, out _, out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryReadPresentationDataValue_IncompletePdvData_ReturnsFalse()
        {
            // Header claims PDV length of 10, but only 2 bytes available after length
            var buffer = new byte[6];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, 10); // Claim 10 bytes
            buffer[4] = 0x01;
            buffer[5] = 0x03;

            var reader = new PduReader(buffer);

            bool result = reader.TryReadPresentationDataValue(out _, out _, out _, out _);

            Assert.That(result, Is.False);
        }

        #endregion

        #region Position Tracking Tests

        [Test]
        public void Position_TracksCorrectly()
        {
            var buffer = new byte[20];
            var reader = new PduReader(buffer);

            Assert.That(reader.Position, Is.EqualTo(0));
            Assert.That(reader.Remaining, Is.EqualTo(20));

            reader.TrySkip(5);
            Assert.That(reader.Position, Is.EqualTo(5));
            Assert.That(reader.Remaining, Is.EqualTo(15));
        }

        [Test]
        public void TrySkip_ExceedsRemaining_ReturnsFalse()
        {
            var buffer = new byte[10];
            var reader = new PduReader(buffer);

            bool result = reader.TrySkip(11);

            Assert.That(result, Is.False);
            Assert.That(reader.Position, Is.EqualTo(0)); // Position unchanged
        }

        #endregion
    }
}
