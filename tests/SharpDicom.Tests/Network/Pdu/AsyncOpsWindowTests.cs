using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Tests.Network.Pdu
{
    /// <summary>
    /// Tests for Async Operations Window (0x53 sub-item) PDU encoding/decoding
    /// and UserInformation async operations properties.
    /// </summary>
    [TestFixture]
    public class AsyncOpsWindowTests
    {
        #region UserInformation Property Tests

        [Test]
        public void HasAsyncOperations_DefaultValues_ReturnsFalse()
        {
            var info = UserInformation.Default;

            Assert.That(info.MaxOperationsInvoked, Is.EqualTo(1));
            Assert.That(info.MaxOperationsPerformed, Is.EqualTo(1));
            Assert.That(info.HasAsyncOperations, Is.False);
        }

        [Test]
        public void HasAsyncOperations_NonDefaultInvoked_ReturnsTrue()
        {
            var info = UserInformation.Default.WithAsyncOperations(5, 1);

            Assert.That(info.HasAsyncOperations, Is.True);
        }

        [Test]
        public void HasAsyncOperations_NonDefaultPerformed_ReturnsTrue()
        {
            var info = UserInformation.Default.WithAsyncOperations(1, 5);

            Assert.That(info.HasAsyncOperations, Is.True);
        }

        [Test]
        public void HasAsyncOperations_Unlimited_ReturnsTrue()
        {
            var info = UserInformation.Default.WithAsyncOperations(0, 0);

            Assert.That(info.HasAsyncOperations, Is.True);
            Assert.That(info.MaxOperationsInvoked, Is.EqualTo(0));
            Assert.That(info.MaxOperationsPerformed, Is.EqualTo(0));
        }

        [Test]
        public void WithAsyncOperations_CreatesNewInstance()
        {
            var original = UserInformation.Default;
            var modified = original.WithAsyncOperations(10, 20);

            Assert.That(modified.MaxOperationsInvoked, Is.EqualTo(10));
            Assert.That(modified.MaxOperationsPerformed, Is.EqualTo(20));
            // Original should be unchanged
            Assert.That(original.MaxOperationsInvoked, Is.EqualTo(1));
            Assert.That(original.MaxOperationsPerformed, Is.EqualTo(1));
            // Other properties preserved
            Assert.That(modified.MaxPduLength, Is.EqualTo(original.MaxPduLength));
            Assert.That(modified.ImplementationClassUid, Is.EqualTo(original.ImplementationClassUid));
            Assert.That(modified.ImplementationVersionName, Is.EqualTo(original.ImplementationVersionName));
        }

        #endregion

        #region PduWriter/PduReader Roundtrip Tests

        [Test]
        public void AsyncOpsWindow_WriteThenRead_RoundtripsCorrectly()
        {
            var userInfo = UserInformation.Default.WithAsyncOperations(5, 10);
            var contexts = CreateSingleContext();

            // Write an A-ASSOCIATE-RQ
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateRequest("CALLED", "CALLING", contexts, userInfo);

            // Scan the written bytes for the 0x53 sub-item and verify values
            var bytes = buffer.WrittenSpan;
            var found = FindAsyncOpsSubItem(bytes, out ushort invoked, out ushort performed);

            Assert.That(found, Is.True, "0x53 sub-item should be present for non-default async ops");
            Assert.That(invoked, Is.EqualTo(5));
            Assert.That(performed, Is.EqualTo(10));
        }

        [Test]
        public void AsyncOpsWindow_DefaultValues_NotWritten()
        {
            var userInfo = UserInformation.Default; // (1, 1) = default
            var contexts = CreateSingleContext();

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateRequest("CALLED", "CALLING", contexts, userInfo);

            var bytes = buffer.WrittenSpan;
            var found = FindAsyncOpsSubItem(bytes, out _, out _);

            Assert.That(found, Is.False, "0x53 sub-item should NOT be present when async ops are default (1, 1)");
        }

        [Test]
        public void AsyncOpsWindow_Unlimited_WritesZeros()
        {
            var userInfo = UserInformation.Default.WithAsyncOperations(0, 0);
            var contexts = CreateSingleContext();

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateRequest("CALLED", "CALLING", contexts, userInfo);

            var bytes = buffer.WrittenSpan;
            var found = FindAsyncOpsSubItem(bytes, out ushort invoked, out ushort performed);

            Assert.That(found, Is.True, "0x53 sub-item should be present for unlimited (0, 0)");
            Assert.That(invoked, Is.EqualTo(0));
            Assert.That(performed, Is.EqualTo(0));
        }

        #endregion

        #region DicomClientOptions Tests

        [Test]
        public void AsyncOperationsInvoked_DefaultIsOne()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "TEST",
                CallingAE = "TEST"
            };

            Assert.That(options.AsyncOperationsInvoked, Is.EqualTo(1));
        }

        [Test]
        public void AsyncOperationsPerformed_DefaultIsOne()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "TEST",
                CallingAE = "TEST"
            };

            Assert.That(options.AsyncOperationsPerformed, Is.EqualTo(1));
        }

        #endregion

        #region Helpers

        private static List<PresentationContext> CreateSingleContext()
        {
            return new List<PresentationContext>
            {
                new PresentationContext(1, new DicomUID("1.2.840.10008.5.1.4.1.1.1"), TransferSyntax.ExplicitVRLittleEndian)
            };
        }

        /// <summary>
        /// Scans the raw PDU bytes for the 0x53 (Asynchronous Operations Window) sub-item
        /// and extracts the invoked/performed values.
        /// </summary>
        private static bool FindAsyncOpsSubItem(System.ReadOnlySpan<byte> bytes, out ushort invoked, out ushort performed)
        {
            invoked = 0;
            performed = 0;

            // Search for the 0x53 item type byte followed by reserved byte and 2-byte length = 0x0004
            for (int i = 0; i < bytes.Length - 7; i++)
            {
                if (bytes[i] == 0x53 && bytes[i + 1] == 0x00)
                {
                    ushort length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 2));
                    if (length == 4)
                    {
                        invoked = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 4));
                        performed = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i + 6));
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
