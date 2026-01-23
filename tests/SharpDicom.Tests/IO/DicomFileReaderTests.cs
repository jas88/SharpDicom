using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Unit tests for <see cref="DicomFileReader"/>.
    /// </summary>
    [TestFixture]
    public class DicomFileReaderTests
    {
        #region ReadFileMetaInfoAsync Tests

        [Test]
        public async Task ReadFileMetaInfoAsync_ParsesHeader()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            await reader.ReadFileMetaInfoAsync();

            Assert.That(reader.FileMetaInfo, Is.Not.Null);
            Assert.That(reader.TransferSyntax.IsKnown, Is.True);
        }

        [Test]
        public async Task ReadFileMetaInfoAsync_EmptyFile_Throws()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());

            await using var reader = new DicomFileReader(stream);

            Assert.ThrowsAsync<DicomFileException>(async () =>
                await reader.ReadFileMetaInfoAsync());
        }

        [Test]
        public async Task ReadFileMetaInfoAsync_CalledTwice_DoesNotThrow()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            await reader.ReadFileMetaInfoAsync();
            await reader.ReadFileMetaInfoAsync(); // Second call should be no-op

            Assert.That(reader.FileMetaInfo, Is.Not.Null);
        }

        #endregion

        #region ReadElementsAsync Tests

        [Test]
        public async Task ReadElementsAsync_StreamsElements()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var elements = new List<IDicomElement>();

            await foreach (var element in reader.ReadElementsAsync())
            {
                elements.Add(element);
            }

            Assert.That(elements, Is.Not.Empty);
        }

        [Test]
        public async Task ReadElementsAsync_AutoParsesHeader()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            Assert.That(reader.IsHeaderParsed, Is.False);

            await foreach (var element in reader.ReadElementsAsync())
            {
                // First iteration should have parsed header
                break;
            }

            Assert.That(reader.IsHeaderParsed, Is.True);
        }

        [Test]
        public async Task ReadElementsAsync_RespectsCancellation()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);
            using var cts = new CancellationTokenSource();

            await using var reader = new DicomFileReader(stream);

            cts.Cancel();

            // TaskCanceledException is a subclass of OperationCanceledException
            var ex = Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in reader.ReadElementsAsync(cts.Token))
                {
                }
            });

            Assert.That(ex, Is.Not.Null);
        }

        [Test]
        public async Task ReadElementsAsync_ContainsExpectedTags()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var tags = new List<DicomTag>();

            await foreach (var element in reader.ReadElementsAsync())
            {
                tags.Add(element.Tag);
            }

            // Should contain the tags we added in CreateTestDicomFile
            Assert.That(tags, Has.Member(DicomTag.SpecificCharacterSet)); // (0008,0005)
            Assert.That(tags, Has.Member(new DicomTag(0x0008, 0x0060))); // Modality
            Assert.That(tags, Has.Member(new DicomTag(0x0010, 0x0010))); // PatientName
        }

        #endregion

        #region ReadDatasetAsync Tests

        [Test]
        public async Task ReadDatasetAsync_ReturnsCompleteDataset()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var dataset = await reader.ReadDatasetAsync();

            Assert.That(dataset, Is.Not.Null);
            Assert.That(dataset.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ReadDatasetAsync_ContainsCorrectValues()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var dataset = await reader.ReadDatasetAsync();

            var patientName = dataset.GetString(new DicomTag(0x0010, 0x0010));
            Assert.That(patientName, Is.EqualTo("Doe^John"));
        }

        #endregion

        #region Disposal Tests

        [Test]
        public async Task DisposeAsync_ReleasesResources()
        {
            var data = CreateTestDicomFile();
            var stream = new MemoryStream(data);

            var reader = new DicomFileReader(stream, leaveOpen: false);
            await reader.DisposeAsync();

            // Stream should be disposed
            Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        }

        [Test]
        public async Task LeaveOpen_KeepsStreamOpen()
        {
            var data = CreateTestDicomFile();
            var stream = new MemoryStream(data);

            var reader = new DicomFileReader(stream, leaveOpen: true);
            await reader.DisposeAsync();

            // Stream should still be accessible
            stream.Position = 0;
            Assert.That(stream.ReadByte(), Is.Not.EqualTo(-1));
        }

        [Test]
        public async Task DisposeAsync_CalledTwice_DoesNotThrow()
        {
            var data = CreateTestDicomFile();
            var stream = new MemoryStream(data);

            var reader = new DicomFileReader(stream);
            await reader.DisposeAsync();
            await reader.DisposeAsync(); // Second call should be no-op

            // No assertion needed - just verify no exception
        }

        [Test]
        public async Task AfterDispose_ReadThrows()
        {
            var data = CreateTestDicomFile();
            var stream = new MemoryStream(data);

            var reader = new DicomFileReader(stream);
            await reader.DisposeAsync();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await reader.ReadFileMetaInfoAsync());
        }

        #endregion

        #region Preamble Tests

        [Test]
        public async Task Preamble_AvailableAfterHeaderParsed()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            await reader.ReadFileMetaInfoAsync();

            Assert.That(reader.Preamble.Length, Is.EqualTo(128));
        }

        [Test]
        public async Task Preamble_EmptyBeforeHeaderParsed()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            Assert.That(reader.Preamble.Length, Is.EqualTo(0));
        }

        #endregion

        #region TransferSyntax Tests

        [Test]
        public async Task TransferSyntax_ExplicitVRLE_Recognized()
        {
            var data = CreateTestDicomFile();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            await reader.ReadFileMetaInfoAsync();

            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.True);
            Assert.That(reader.TransferSyntax.IsLittleEndian, Is.True);
        }

        #endregion

        #region Helper Methods

        private static byte[] CreateTestDicomFile()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information (Explicit VR LE)
            // Transfer Syntax UID
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElement(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Dataset elements
            WriteElement(ms, 0x0008, 0x0005, "CS", PadToEven("ISO_IR 100"u8.ToArray()));
            WriteElement(ms, 0x0008, 0x0016, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.5.1.4.1.1.2\0"));
            WriteElement(ms, 0x0008, 0x0060, "CS", PadToEven("CT"u8.ToArray()));
            WriteElement(ms, 0x0010, 0x0010, "PN", PadToEven("Doe^John"u8.ToArray()));
            WriteElement(ms, 0x0010, 0x0020, "LO", PadToEven("PATIENT001"u8.ToArray()));

            return ms.ToArray();
        }

        private static void WriteElement(MemoryStream ms, ushort group, ushort element,
            string vr, byte[] value)
        {
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));

            var dicomVR = new DicomVR(vr);
            if (dicomVR.Is32BitLength)
            {
                ms.Write(new byte[2]); // Reserved
                ms.Write(BitConverter.GetBytes((uint)value.Length));
            }
            else
            {
                ms.Write(BitConverter.GetBytes((ushort)value.Length));
            }

            ms.Write(value);
        }

        private static byte[] PadToEven(byte[] value)
        {
            if (value.Length % 2 == 0)
                return value;

            var padded = new byte[value.Length + 1];
            Array.Copy(value, padded, value.Length);
            padded[padded.Length - 1] = (byte)' ';
            return padded;
        }

        #endregion
    }
}
