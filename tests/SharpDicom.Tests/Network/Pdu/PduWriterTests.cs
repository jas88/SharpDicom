using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Tests.Network.Pdu
{
    [TestFixture]
    public class PduWriterTests
    {
        #region A-ASSOCIATE-RJ Tests

        [Test]
        public void WriteAssociateReject_ProducesCorrectBytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            writer.WriteAssociateReject(
                RejectResult.PermanentRejection,
                RejectSource.ServiceUser,
                RejectReason.CalledAETitleNotRecognized);

            var bytes = buffer.WrittenSpan;

            // A-ASSOCIATE-RJ is exactly 10 bytes
            Assert.That(bytes.Length, Is.EqualTo(10));

            // Header
            Assert.That(bytes[0], Is.EqualTo(0x03)); // PDU type
            Assert.That(bytes[1], Is.EqualTo(0x00)); // Reserved
            // Length = 4 (Big-Endian)
            Assert.That(bytes[2], Is.EqualTo(0x00));
            Assert.That(bytes[3], Is.EqualTo(0x00));
            Assert.That(bytes[4], Is.EqualTo(0x00));
            Assert.That(bytes[5], Is.EqualTo(0x04));

            // Body
            Assert.That(bytes[6], Is.EqualTo(0x00)); // Reserved
            Assert.That(bytes[7], Is.EqualTo(0x01)); // Result: PermanentRejection
            Assert.That(bytes[8], Is.EqualTo(0x01)); // Source: ServiceUser
            Assert.That(bytes[9], Is.EqualTo(0x07)); // Reason: CalledAETitleNotRecognized
        }

        #endregion

        #region A-RELEASE Tests

        [Test]
        public void WriteReleaseRequest_ProducesCorrect10Bytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            writer.WriteReleaseRequest();

            var bytes = buffer.WrittenSpan;

            Assert.That(bytes.Length, Is.EqualTo(10));
            Assert.That(bytes[0], Is.EqualTo(0x05)); // PDU type: A-RELEASE-RQ
            Assert.That(bytes[1], Is.EqualTo(0x00)); // Reserved
            // Length = 4 (Big-Endian)
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(2)), Is.EqualTo(4u));
            // Body: 4 reserved bytes
            Assert.That(bytes[6], Is.EqualTo(0x00));
            Assert.That(bytes[7], Is.EqualTo(0x00));
            Assert.That(bytes[8], Is.EqualTo(0x00));
            Assert.That(bytes[9], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteReleaseResponse_ProducesCorrect10Bytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            writer.WriteReleaseResponse();

            var bytes = buffer.WrittenSpan;

            Assert.That(bytes.Length, Is.EqualTo(10));
            Assert.That(bytes[0], Is.EqualTo(0x06)); // PDU type: A-RELEASE-RP
            Assert.That(bytes[1], Is.EqualTo(0x00)); // Reserved
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(2)), Is.EqualTo(4u));
        }

        #endregion

        #region A-ABORT Tests

        [Test]
        public void WriteAbort_ProducesCorrectBytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            writer.WriteAbort(AbortSource.ServiceProvider, AbortReason.UnexpectedPdu);

            var bytes = buffer.WrittenSpan;

            Assert.That(bytes.Length, Is.EqualTo(10));
            Assert.That(bytes[0], Is.EqualTo(0x07)); // PDU type: A-ABORT
            Assert.That(bytes[6], Is.EqualTo(0x00)); // Reserved
            Assert.That(bytes[7], Is.EqualTo(0x00)); // Reserved
            Assert.That(bytes[8], Is.EqualTo(0x02)); // Source: ServiceProvider
            Assert.That(bytes[9], Is.EqualTo(0x02)); // Reason: UnexpectedPdu
        }

        [Test]
        public void WriteAbort_ServiceUser_ProducesCorrectBytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            writer.WriteAbort(AbortSource.ServiceUser, AbortReason.NotSpecified);

            var bytes = buffer.WrittenSpan;

            Assert.That(bytes[8], Is.EqualTo(0x00)); // Source: ServiceUser
            Assert.That(bytes[9], Is.EqualTo(0x00)); // Reason: NotSpecified
        }

        #endregion

        #region AE Title Padding Tests

        [Test]
        public void WriteAssociateRequest_AeTitle_PaddedTo16Bytes()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            var contexts = new List<PresentationContext>
            {
                new PresentationContext(1, new DicomUID("1.2.840.10008.5.1.4.1.1.1"), TransferSyntax.ExplicitVRLittleEndian)
            };

            writer.WriteAssociateRequest("SHORT", "TEST", contexts, UserInformation.Default);

            var bytes = buffer.WrittenSpan;

            // Called AE Title starts at offset 10 (after header + protocol version + reserved)
            var calledAE = bytes.Slice(10, 16);
            // Should be "SHORT" followed by 11 spaces
            Assert.That(calledAE[0], Is.EqualTo((byte)'S'));
            Assert.That(calledAE[1], Is.EqualTo((byte)'H'));
            Assert.That(calledAE[2], Is.EqualTo((byte)'O'));
            Assert.That(calledAE[3], Is.EqualTo((byte)'R'));
            Assert.That(calledAE[4], Is.EqualTo((byte)'T'));
            for (int i = 5; i < 16; i++)
            {
                Assert.That(calledAE[i], Is.EqualTo((byte)' '), $"Byte {i} should be space");
            }

            // Calling AE Title starts at offset 26
            var callingAE = bytes.Slice(26, 16);
            Assert.That(callingAE[0], Is.EqualTo((byte)'T'));
            Assert.That(callingAE[1], Is.EqualTo((byte)'E'));
            Assert.That(callingAE[2], Is.EqualTo((byte)'S'));
            Assert.That(callingAE[3], Is.EqualTo((byte)'T'));
            for (int i = 4; i < 16; i++)
            {
                Assert.That(callingAE[i], Is.EqualTo((byte)' '));
            }
        }

        #endregion

        #region Big-Endian Length Tests

        [Test]
        public void WritePdu_BigEndianLength_EncodedCorrectly()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            // Write a release request (fixed 10 bytes, length = 4)
            writer.WriteReleaseRequest();

            var bytes = buffer.WrittenSpan;

            // Verify length field is Big-Endian
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(2));
            Assert.That(length, Is.EqualTo(4u));

            // Individual bytes should be 0x00, 0x00, 0x00, 0x04
            Assert.That(bytes[2], Is.EqualTo(0x00));
            Assert.That(bytes[3], Is.EqualTo(0x00));
            Assert.That(bytes[4], Is.EqualTo(0x00));
            Assert.That(bytes[5], Is.EqualTo(0x04));
        }

        #endregion

        #region Roundtrip Tests

        [Test]
        public void AssociateReject_Roundtrip_MatchesOriginal()
        {
            var result = RejectResult.TransientRejection;
            var source = RejectSource.ServiceProviderPresentation;
            var reason = RejectReason.ApplicationContextNotSupported;

            // Write
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateReject(result, source, reason);

            // Read header
            var reader = new PduReader(buffer.WrittenSpan);
            Assert.That(reader.TryReadPduHeader(out PduType type, out uint length), Is.True);
            Assert.That(type, Is.EqualTo(PduType.AssociateReject));
            Assert.That(length, Is.EqualTo(4u));

            // Read body
            Assert.That(reader.TryReadAssociateReject(out var parsedResult, out var parsedSource, out var parsedReason), Is.True);
            Assert.That(parsedResult, Is.EqualTo(result));
            Assert.That(parsedSource, Is.EqualTo(source));
            Assert.That(parsedReason, Is.EqualTo(reason));
        }

        [Test]
        public void Abort_Roundtrip_MatchesOriginal()
        {
            var source = AbortSource.ServiceProvider;
            var reason = AbortReason.UnrecognizedPduParameter;

            // Write
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAbort(source, reason);

            // Read header
            var reader = new PduReader(buffer.WrittenSpan);
            Assert.That(reader.TryReadPduHeader(out PduType type, out uint length), Is.True);
            Assert.That(type, Is.EqualTo(PduType.Abort));
            Assert.That(length, Is.EqualTo(4u));

            // Read body
            Assert.That(reader.TryReadAbort(out var parsedSource, out var parsedReason), Is.True);
            Assert.That(parsedSource, Is.EqualTo(source));
            Assert.That(parsedReason, Is.EqualTo(reason));
        }

        [Test]
        public void ReleaseRequest_Roundtrip()
        {
            // Write
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteReleaseRequest();

            // Read header
            var reader = new PduReader(buffer.WrittenSpan);
            Assert.That(reader.TryReadPduHeader(out PduType type, out uint length), Is.True);
            Assert.That(type, Is.EqualTo(PduType.ReleaseRequest));
            Assert.That(length, Is.EqualTo(4u));

            // Read body
            Assert.That(reader.TryReadReleaseRequest(), Is.True);
        }

        [Test]
        public void ReleaseResponse_Roundtrip()
        {
            // Write
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteReleaseResponse();

            // Read header
            var reader = new PduReader(buffer.WrittenSpan);
            Assert.That(reader.TryReadPduHeader(out PduType type, out uint length), Is.True);
            Assert.That(type, Is.EqualTo(PduType.ReleaseResponse));
            Assert.That(length, Is.EqualTo(4u));

            // Read body
            Assert.That(reader.TryReadReleaseResponse(), Is.True);
        }

        [Test]
        public void AssociateRequest_Roundtrip_AeTitlesMatch()
        {
            const string calledAE = "PACS_SERVER";
            const string callingAE = "WORKSTATION";

            var contexts = new List<PresentationContext>
            {
                new PresentationContext(1, new DicomUID("1.2.840.10008.5.1.4.1.1.1"), TransferSyntax.ExplicitVRLittleEndian)
            };

            // Write
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateRequest(calledAE, callingAE, contexts, UserInformation.Default);

            // Read header
            var reader = new PduReader(buffer.WrittenSpan);
            Assert.That(reader.TryReadPduHeader(out PduType type, out _), Is.True);
            Assert.That(type, Is.EqualTo(PduType.AssociateRequest));

            // Read body
            Assert.That(reader.TryReadAssociateRequest(
                out ushort protocolVersion,
                out string parsedCalled,
                out string parsedCalling,
                out _), Is.True);

            Assert.That(protocolVersion, Is.EqualTo(0x0001));
            Assert.That(parsedCalled, Is.EqualTo(calledAE));
            Assert.That(parsedCalling, Is.EqualTo(callingAE));
        }

        #endregion

        #region P-DATA Tests

        [Test]
        public void WritePData_SinglePdv_CorrectFormat()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId: 1,
                isCommand: true,
                isLastFragment: true,
                data: new byte[] { 0x01, 0x02, 0x03, 0x04 });

            writer.WritePData(new[] { pdv });

            var bytes = buffer.WrittenSpan;

            // Header
            Assert.That(bytes[0], Is.EqualTo(0x04)); // P-DATA-TF type
            Assert.That(bytes[1], Is.EqualTo(0x00)); // Reserved

            // Total length: 4 (PDV length field) + 1 (context ID) + 1 (header) + 4 (data) = 10
            uint pduLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(2));
            Assert.That(pduLength, Is.EqualTo(10u));

            // PDV length = 6 (context ID + header + 4 data bytes)
            uint pdvLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(6));
            Assert.That(pdvLength, Is.EqualTo(6u));

            // Context ID
            Assert.That(bytes[10], Is.EqualTo(1));

            // Message control header: Command (0x01) + Last (0x02) = 0x03
            Assert.That(bytes[11], Is.EqualTo(0x03));

            // Data
            Assert.That(bytes[12], Is.EqualTo(0x01));
            Assert.That(bytes[13], Is.EqualTo(0x02));
            Assert.That(bytes[14], Is.EqualTo(0x03));
            Assert.That(bytes[15], Is.EqualTo(0x04));
        }

        [Test]
        public void WritePData_MultiplePdvs_CorrectFormat()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            var pdvs = new[]
            {
                new PresentationDataValue(1, true, true, new byte[] { 0xAA }),
                new PresentationDataValue(3, false, false, new byte[] { 0xBB, 0xCC })
            };

            writer.WritePData(pdvs);

            var bytes = buffer.WrittenSpan;

            // Header type
            Assert.That(bytes[0], Is.EqualTo(0x04));

            // First PDV at offset 6: Length(4) + ContextID(1) + Header(1) + Data(1) = 7 bytes total
            // Second PDV: Length(4) + ContextID(1) + Header(1) + Data(2) = 8 bytes total
            // Total PDU length = 7 + 8 = 15
            uint pduLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(2));
            Assert.That(pduLength, Is.EqualTo(15u));

            // First PDV length = 3 (ContextID + Header + 1 data byte)
            uint pdv1Length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(6));
            Assert.That(pdv1Length, Is.EqualTo(3u));

            // First PDV context ID
            Assert.That(bytes[10], Is.EqualTo(1));

            // First PDV header (command + last = 0x03)
            Assert.That(bytes[11], Is.EqualTo(0x03));

            // First PDV data
            Assert.That(bytes[12], Is.EqualTo(0xAA));

            // Second PDV starts at offset 13
            uint pdv2Length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(13));
            Assert.That(pdv2Length, Is.EqualTo(4u)); // ContextID + Header + 2 data bytes

            // Second PDV context ID
            Assert.That(bytes[17], Is.EqualTo(3));

            // Second PDV header (data, not last = 0x00)
            Assert.That(bytes[18], Is.EqualTo(0x00));

            // Second PDV data
            Assert.That(bytes[19], Is.EqualTo(0xBB));
            Assert.That(bytes[20], Is.EqualTo(0xCC));
        }

        #endregion

        #region Application Context Tests

        [Test]
        public void WriteAssociateRequest_ContainsApplicationContext()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            var contexts = new List<PresentationContext>
            {
                new PresentationContext(1, new DicomUID("1.2.840.10008.5.1.4.1.1.1"), TransferSyntax.ExplicitVRLittleEndian)
            };

            writer.WriteAssociateRequest("CALLED", "CALLING", contexts, UserInformation.Default);

            var bytes = buffer.WrittenSpan;

            // Variable items start at offset 74 (6 header + 68 fixed fields)
            // Application Context item should be first
            Assert.That(bytes[74], Is.EqualTo(0x10)); // Item type: Application Context

            // Length of Application Context UID (21 bytes for "1.2.840.10008.3.1.1.1")
            ushort acLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(76));
            Assert.That(acLength, Is.EqualTo(21));

            // Verify Application Context UID
            var uidBytes = bytes.Slice(78, 21);
            var uid = System.Text.Encoding.ASCII.GetString(uidBytes.ToArray());
            Assert.That(uid, Is.EqualTo("1.2.840.10008.3.1.1.1"));
        }

        #endregion
    }
}
