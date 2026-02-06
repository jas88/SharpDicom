using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Storage;

namespace SharpDicom.Tests.Storage
{
    /// <summary>
    /// Tests for <see cref="FileSystemDicomStore"/>.
    /// </summary>
    [TestFixture]
    public class FileSystemDicomStoreTests
    {
        private string _tempDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "SharpDicom_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        #region Store and Retrieve Tests

        [Test]
        public async Task StoreAndRetrieve_RoundTrip()
        {
            // Arrange
            using var store = CreateStore();
            var dataset = CreateTestDataset("Smith^John", "PAT001", "1.2.3.4.5", "1.2.3.4.5.1", "1.2.3.4.5.1.1", "CT");
            var context = CreateCStoreContext("1.2.3.4.5.1.1");

            // Act - Store
            var storeStatus = await store.StoreAsync(context, dataset, CancellationToken.None);
            Assert.That(storeStatus.IsSuccess, Is.True, "Store should succeed");

            // Act - Retrieve by creating a match dataset with the SOP Instance UID
            var matchDs = new DicomDataset();
            matchDs.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4.5.1.1"));

            var retrieved = await store.RetrieveAsync(matchDs, CancellationToken.None);

            // Assert
            Assert.That(retrieved, Is.Not.Null, "Retrieved file should not be null");
            Assert.That(retrieved!.Dataset.GetString(DicomTag.PatientName), Does.Contain("Smith"));
        }

        [Test]
        public async Task Store_CreatesHierarchicalDirectories()
        {
            // Arrange
            using var store = CreateStore();
            var dataset = CreateTestDataset("Smith^John", "PAT001", "1.2.3.4.5", "1.2.3.4.5.1", "1.2.3.4.5.1.1", "CT");
            var context = CreateCStoreContext("1.2.3.4.5.1.1");

            // Act
            await store.StoreAsync(context, dataset, CancellationToken.None);

            // Assert - Check directory structure exists
            var expectedPath = Path.Combine(_tempDir, "PAT001", "1.2.3.4.5", "1.2.3.4.5.1", "1.2.3.4.5.1.1.dcm");
            Assert.That(File.Exists(expectedPath), Is.True,
                $"Expected file at {expectedPath}");
        }

        [Test]
        public async Task StoreMultiple_GetInstanceCount_ReturnsCorrectCount()
        {
            // Arrange
            using var store = CreateStore();

            // Act - Store 5 files
            for (int i = 0; i < 5; i++)
            {
                var dataset = CreateTestDataset(
                    $"Patient{i}^Test", $"PAT{i:000}",
                    $"1.2.3.4.{i}", $"1.2.3.4.{i}.1", $"1.2.3.4.{i}.1.1", "CT");
                var context = CreateCStoreContext($"1.2.3.4.{i}.1.1");
                var status = await store.StoreAsync(context, dataset, CancellationToken.None);
                Assert.That(status.IsSuccess, Is.True, $"Store {i} should succeed");
            }

            // Assert
            Assert.That(store.GetInstanceCount(), Is.EqualTo(5));
        }

        #endregion

        #region FindAsync Tests

        [Test]
        public async Task FindAsync_ByPatientName_ReturnsMatch()
        {
            // Arrange
            using var store = CreateStore();

            // Store 3 files with different patient names
            await StoreTestDataset(store, "Smith^John", "PAT001", "1.2.3.1", "1.2.3.1.1", "1.2.3.1.1.1", "CT");
            await StoreTestDataset(store, "Jones^Bob", "PAT002", "1.2.3.2", "1.2.3.2.1", "1.2.3.2.1.1", "MR");
            await StoreTestDataset(store, "Smith^Jane", "PAT003", "1.2.3.3", "1.2.3.3.1", "1.2.3.3.1.1", "CT");

            // Build query for "Smith*" at study level
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));
            queryDs.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, Array.Empty<byte>()));

            // Act
            var results = await CollectAsync(store.FindAsync(queryDs, CancellationToken.None));

            // Assert - Should match Smith^John and Smith^Jane
            Assert.That(results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task FindAsync_ByStudyDate_ReturnsMatch()
        {
            // Arrange
            using var store = CreateStore();

            await StoreTestDataset(store, "Patient1", "P001", "1.2.1", "1.2.1.1", "1.2.1.1.1", "CT", "20240101");
            await StoreTestDataset(store, "Patient2", "P002", "1.2.2", "1.2.2.1", "1.2.2.1.1", "CT", "20240115");
            await StoreTestDataset(store, "Patient3", "P003", "1.2.3", "1.2.3.1", "1.2.3.1.1", "CT", "20240201");

            // Query for date range 20240101-20240120
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240101-20240120"));
            queryDs.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, Array.Empty<byte>()));

            // Act
            var results = await CollectAsync(store.FindAsync(queryDs, CancellationToken.None));

            // Assert - Should match Patient1 (Jan 1) and Patient2 (Jan 15)
            Assert.That(results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task FindAsync_ByModality_ReturnsMatch()
        {
            // Arrange
            using var store = CreateStore();

            await StoreTestDataset(store, "Patient1", "P001", "1.2.1", "1.2.1.1", "1.2.1.1.1", "CT");
            await StoreTestDataset(store, "Patient2", "P002", "1.2.2", "1.2.2.1", "1.2.2.1.1", "MR");
            await StoreTestDataset(store, "Patient3", "P003", "1.2.3", "1.2.3.1", "1.2.3.1.1", "CT");

            // Query for CT only at series level (modality is a series-level attribute)
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "SERIES"));
            queryDs.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
            queryDs.Add(new DicomStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, Array.Empty<byte>()));

            // Act
            var results = await CollectAsync(store.FindAsync(queryDs, CancellationToken.None));

            // Assert - Should match 2 CT series
            Assert.That(results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task FindAsync_NoMatch_ReturnsEmpty()
        {
            // Arrange
            using var store = CreateStore();

            await StoreTestDataset(store, "Smith^John", "PAT001", "1.2.3.1", "1.2.3.1.1", "1.2.3.1.1.1", "CT");

            // Query for non-existent patient
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "NonExistent"));
            queryDs.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, Array.Empty<byte>()));

            // Act
            var results = await CollectAsync(store.FindAsync(queryDs, CancellationToken.None));

            // Assert
            Assert.That(results.Count, Is.EqualTo(0));
        }

        #endregion

        #region CreateServerOptions Tests

        [Test]
        public void CreateServerOptions_WiresCallbacks()
        {
            // Arrange
            using var store = CreateStore();

            // Act
            var options = store.CreateServerOptions();

            // Assert
            Assert.That(options.OnCFind, Is.Not.Null, "OnCFind should be wired");
            Assert.That(options.OnCStoreRequest, Is.Not.Null, "OnCStoreRequest should be wired");
            Assert.That(options.OnCMoveRetrieve, Is.Not.Null, "OnCMoveRetrieve should be wired");
            Assert.That(options.OnCGetRetrieve, Is.Not.Null, "OnCGetRetrieve should be wired");
            Assert.That(options.OnResolveMoveDestination, Is.Null, "OnResolveMoveDestination should be null (user must set)");
            Assert.That(options.AETitle, Is.EqualTo("SHARPDICOM"), "Default AE title");
            Assert.That(options.Port, Is.EqualTo(11112), "Default port");
        }

        [Test]
        public void CreateServerOptions_UsesCustomAETitleAndPort()
        {
            // Arrange
            var storeOptions = new FileSystemDicomStoreOptions
            {
                RootDirectory = _tempDir,
                AETitle = "MYSTORE",
                Port = 4242
            };
            using var store = new FileSystemDicomStore(storeOptions);

            // Act
            var options = store.CreateServerOptions();

            // Assert
            Assert.That(options.AETitle, Is.EqualTo("MYSTORE"));
            Assert.That(options.Port, Is.EqualTo(4242));
        }

        #endregion

        #region Options Validation Tests

        [Test]
        public void FileSystemDicomStoreOptions_Validate_RequiresRootDirectory()
        {
            var options = new FileSystemDicomStoreOptions
            {
                RootDirectory = null!
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void FileSystemDicomStoreOptions_Validate_RequiresValidPort()
        {
            var options = new FileSystemDicomStoreOptions
            {
                RootDirectory = _tempDir,
                Port = 0
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void FileSystemDicomStoreOptions_EffectiveDatabasePath_DefaultsToIndexDb()
        {
            var options = new FileSystemDicomStoreOptions
            {
                RootDirectory = _tempDir
            };

            Assert.That(options.EffectiveDatabasePath, Is.EqualTo(Path.Combine(_tempDir, "index.db")));
        }

        #endregion

        #region Helper Methods

        private FileSystemDicomStore CreateStore()
        {
            return new FileSystemDicomStore(new FileSystemDicomStoreOptions
            {
                RootDirectory = _tempDir
            });
        }

        private static CStoreRequestContext CreateCStoreContext(string sopInstanceUid)
        {
            return new CStoreRequestContext(
                callingAE: "TESTSCU",
                calledAE: "TESTSCP",
                sopClassUid: DicomUID.CTImageStorage,
                sopInstanceUid: new DicomUID(sopInstanceUid),
                messageId: 1,
                presentationContextId: 1);
        }

        private static DicomDataset CreateTestDataset(
            string patientName, string patientId,
            string studyUid, string seriesUid, string sopUid,
            string modality, string? studyDate = "20240115")
        {
            var ds = new DicomDataset();
            ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, patientName));
            ds.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, patientId));
            ds.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid));
            ds.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, seriesUid));
            ds.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, sopUid));
            ds.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, DicomUID.CTImageStorage.ToString()));
            ds.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, modality));
            if (studyDate != null)
                ds.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, studyDate));
            return ds;
        }

        private static async Task StoreTestDataset(
            FileSystemDicomStore store,
            string patientName, string patientId,
            string studyUid, string seriesUid, string sopUid,
            string modality, string? studyDate = "20240115")
        {
            var dataset = CreateTestDataset(patientName, patientId, studyUid, seriesUid, sopUid, modality, studyDate);
            var context = CreateCStoreContext(sopUid);
            var status = await store.StoreAsync(context, dataset, CancellationToken.None);
            Assert.That(status.IsSuccess, Is.True, $"Store {sopUid} should succeed");
        }

        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            // Pad to even length per DICOM spec
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                bytes.CopyTo(padded, 0);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)0 : (byte)' ';
                bytes = padded;
            }
            return new DicomStringElement(tag, vr, bytes);
        }

        private static async Task<List<DicomDataset>> CollectAsync(IAsyncEnumerable<DicomDataset> source)
        {
            var results = new List<DicomDataset>();
            await foreach (var item in source)
            {
                results.Add(item);
            }
            return results;
        }

        #endregion
    }
}
