using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// Tests for SCP services: handler wiring, option validation, and C-FIND callback behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fixture covers the SCP handler configuration, callback invocation,
    /// and return-key filtering logic. Full end-to-end roundtrip tests that require
    /// correct P-DATA PDV interleaving between command and dataset are marked as
    /// <c>[Explicit]</c> and <c>[Category("Integration")]</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ScpIntegrationTests
    {
        private const string ServerAE = "TESTFIND";
        private const string ClientAE = "TESTCLIENT";

        #region C-FIND Callback Tests

        [Test]
        public async Task CFindCallback_InvokedWithQueryIdentifier()
        {
            // Verify that the OnCFind callback receives the correct query identifier
            DicomDataset? receivedQuery = null;

            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) =>
                {
                    receivedQuery = query;
                    return EmptyResults(ct);
                }
            };

            // Invoke the callback directly (no network) to validate wiring
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));

            var cts = new CancellationTokenSource();
            await foreach (var _ in serverOptions.OnCFind!(queryDs, cts.Token))
            {
                // No results expected
            }

            Assert.That(receivedQuery, Is.Not.Null);
            Assert.That(receivedQuery!.GetString(DicomTag.PatientName), Does.Contain("Smith"));
        }

        [Test]
        public async Task CFindCallback_StreamsMultipleResults()
        {
            // Verify the callback can yield multiple results
            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(5, ct)
            };

            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));

            var results = new List<DicomDataset>();
            await foreach (var result in serverOptions.OnCFind!(queryDs, CancellationToken.None))
            {
                results.Add(result);
            }

            Assert.That(results.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task CFindCallback_WildcardFiltering_MatchesCorrectly()
        {
            // Test that a callback using DicomQueryMatcher correctly filters results
            var allPatients = new[]
            {
                ("Smith^John", "PAT001", "1.2.3.1"),
                ("Smith^Jane", "PAT002", "1.2.3.2"),
                ("Jones^Bob", "PAT003", "1.2.3.3"),
                ("Smithson^Alice", "PAT004", "1.2.3.4")
            };

            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => FilteredStudyResults(allPatients, query, ct)
            };

            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));

            var results = new List<DicomDataset>();
            await foreach (var result in serverOptions.OnCFind!(queryDs, CancellationToken.None))
            {
                results.Add(result);
            }

            // "Smith*" matches Smith^John, Smith^Jane, Smithson^Alice (3 matches)
            Assert.That(results.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task CFindCallback_ReturnKeyFiltering_Correct()
        {
            // Test that FilterReturnKeys produces correct output for C-FIND SCP
            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateFullStudyResults(1, ct)
            };

            // Request only PatientName and StudyDate
            var requestDs = new DicomDataset();
            requestDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            requestDs.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, Array.Empty<byte>()));
            requestDs.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Array.Empty<byte>()));

            // Get unfiltered results from callback
            var unfilteredResults = new List<DicomDataset>();
            await foreach (var result in serverOptions.OnCFind!(requestDs, CancellationToken.None))
            {
                unfilteredResults.Add(result);
            }

            Assert.That(unfilteredResults.Count, Is.EqualTo(1));

            // Apply FilterReturnKeys (as the SCP does internally in HandleCFindStreamingAsync)
            var filtered = DicomQueryMatcher.FilterReturnKeys(unfilteredResults[0], requestDs);

            // Should contain QR level + PatientName + StudyDate = 3 tags
            Assert.That(filtered[DicomTag.QueryRetrieveLevel], Is.Not.Null, "QR level always included");
            Assert.That(filtered[DicomTag.PatientName], Is.Not.Null, "Requested tag included");
            Assert.That(filtered[DicomTag.StudyDate], Is.Not.Null, "Requested tag included");

            // Should NOT contain tags not in request
            Assert.That(filtered[DicomTag.Modality], Is.Null, "Not-requested tag excluded");
            Assert.That(filtered[DicomTag.AccessionNumber], Is.Null, "Not-requested tag excluded");
            Assert.That(filtered[DicomTag.PatientID], Is.Null, "Not-requested tag excluded");
        }

        [Test]
        public async Task CFindCallback_Cancellation_StopsEnumeration()
        {
            // Verify that cancelling the token stops the enumeration
            var yieldedCount = 0;

            async IAsyncEnumerable<DicomDataset> InfiniteResults(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    yieldedCount++;
                    var ds = new DicomDataset();
                    ds.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                    ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, $"Patient{yieldedCount}"));
                    yield return ds;
                    await Task.Yield();
                }
            }

            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => InfiniteResults(ct)
            };

            using var cts = new CancellationTokenSource();
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));

            var collected = 0;
            try
            {
                await foreach (var result in serverOptions.OnCFind!(queryDs, cts.Token))
                {
                    collected++;
                    if (collected >= 3)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            Assert.That(collected, Is.GreaterThanOrEqualTo(3));
            Assert.That(yieldedCount, Is.LessThan(100), "Should not yield many results after cancel");
        }

        #endregion

        #region C-STORE Callback Tests

        [Test]
        public async Task CStoreCallback_InvokedWithDataset()
        {
            DicomDataset? receivedDataset = null;
            CStoreRequestContext? receivedContext = null;

            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCStoreRequest = (ctx, dataset, ct) =>
                {
                    receivedContext = ctx;
                    receivedDataset = dataset;
                    return new ValueTask<DicomStatus>(DicomStatus.Success);
                }
            };

            // Invoke directly
            var testContext = new CStoreRequestContext(
                "SCU", "SCP", DicomUID.CTImageStorage,
                new DicomUID("1.2.3.4"), 1, 1);

            var testDataset = CreateTestDataset("Smith^John", "PAT001", "1.2.3.4.5", "CT");

            var status = await serverOptions.OnCStoreRequest!(testContext, testDataset, CancellationToken.None);

            Assert.That(status.IsSuccess, Is.True);
            Assert.That(receivedDataset, Is.Not.Null);
            Assert.That(receivedContext, Is.SameAs(testContext));
            Assert.That(receivedDataset!.GetString(DicomTag.PatientName), Does.Contain("Smith"));
        }

        [Test]
        public async Task CStoreAndCFind_InMemory_Roundtrip()
        {
            // Simulate a C-STORE + C-FIND roundtrip using callbacks directly (no network)
            var storedDatasets = new List<DicomDataset>();

            var serverOptions = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCStoreRequest = (ctx, dataset, ct) =>
                {
                    storedDatasets.Add(dataset);
                    return new ValueTask<DicomStatus>(DicomStatus.Success);
                },
                OnCFind = (query, ct) => FindInStoredDatasets(storedDatasets, query, ct)
            };

            // Store two datasets
            var ctx1 = new CStoreRequestContext("SCU", "SCP", DicomUID.CTImageStorage,
                new DicomUID("1.2.3.1.1.1"), 1, 1);
            var ds1 = CreateTestDataset("Smith^John", "PAT001", "1.2.3.1", "CT");
            await serverOptions.OnCStoreRequest!(ctx1, ds1, CancellationToken.None);

            var ctx2 = new CStoreRequestContext("SCU", "SCP", DicomUID.CTImageStorage,
                new DicomUID("1.2.3.2.1.1"), 2, 1);
            var ds2 = CreateTestDataset("Jones^Bob", "PAT002", "1.2.3.2", "MR");
            await serverOptions.OnCStoreRequest(ctx2, ds2, CancellationToken.None);

            Assert.That(storedDatasets.Count, Is.EqualTo(2));

            // Query for PAT001
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT001"));
            queryDs.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, Array.Empty<byte>()));

            var results = new List<DicomDataset>();
            await foreach (var result in serverOptions.OnCFind!(queryDs, CancellationToken.None))
            {
                results.Add(result);
            }

            Assert.That(results.Count, Is.EqualTo(1));
            var pn = results[0].GetString(DicomTag.PatientName);
            Assert.That(pn, Does.Contain("Smith"));
        }

        #endregion

        #region Handler Wiring / Options Tests

        [Test]
        public void DicomServerOptions_HasCFindHandler_ReflectsOnCFind()
        {
            var optionsWithHandler = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(0, ct)
            };

            Assert.That(optionsWithHandler.HasCFindHandler, Is.True);

            var optionsWithout = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112
            };

            Assert.That(optionsWithout.HasCFindHandler, Is.False);
        }

        [Test]
        public void DicomServerOptions_HasCMoveHandler_RequiresAllThree()
        {
            var options = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(0, ct),
                OnCMoveRetrieve = (match, ct) => new ValueTask<DicomFile?>((DicomFile?)null),
                OnResolveMoveDestination = ae => null
            };

            Assert.That(options.HasCMoveHandler, Is.True);

            // Missing OnResolveMoveDestination
            var optionsMissing = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(0, ct),
                OnCMoveRetrieve = (match, ct) => new ValueTask<DicomFile?>((DicomFile?)null)
            };

            Assert.That(optionsMissing.HasCMoveHandler, Is.False);
        }

        [Test]
        public void DicomServerOptions_HasCGetHandler_RequiresFindAndRetrieve()
        {
            var options = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(0, ct),
                OnCGetRetrieve = (match, ct) => new ValueTask<DicomFile?>((DicomFile?)null)
            };

            Assert.That(options.HasCGetHandler, Is.True);

            // Missing OnCGetRetrieve
            var optionsMissing = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCFind = (query, ct) => GenerateStudyResults(0, ct)
            };

            Assert.That(optionsMissing.HasCGetHandler, Is.False);
        }

        [Test]
        public void DicomServerOptions_HasCStoreHandler_ReflectsDelegate()
        {
            var optionsWith = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112,
                OnCStoreRequest = (ctx, ds, ct) => new ValueTask<DicomStatus>(DicomStatus.Success)
            };

            Assert.That(optionsWith.HasCStoreHandler, Is.True);

            var optionsWithout = new DicomServerOptions
            {
                AETitle = ServerAE,
                Port = 11112
            };

            Assert.That(optionsWithout.HasCStoreHandler, Is.False);
        }

        #endregion

        #region End-to-End Network Tests (Explicit)

        [Test]
        [Category("Integration")]
        public async Task CFindScp_Network_ReturnsMatchingResults()
        {
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnCFind = (query, ct) => GenerateStudyResults(3, ct)
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);
            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian),
                new PresentationContext(3, DicomUID.StudyRootQueryRetrieveFind, TransferSyntax.ImplicitVRLittleEndian)
            };

            await client.ConnectAsync(contexts);

            var findScu = new CFindScu(client, new CFindOptions { UsePatientRoot = false });
            var queryDs = new DicomDataset();
            queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            queryDs.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, Array.Empty<byte>()));

            var results = new List<DicomDataset>();
            await foreach (var result in findScu.QueryAsync(QueryRetrieveLevel.Study, queryDs))
            {
                results.Add(result);
            }

            Assert.That(results.Count, Is.EqualTo(3));
        }

        [Test]
        [Category("Integration")]
        public async Task CStoreThenCFind_Network_Roundtrip()
        {
            var port = GetFreePort();
            var storedDatasets = new List<DicomDataset>();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnCStoreRequest = (ctx, dataset, ct) =>
                {
                    storedDatasets.Add(dataset);
                    return new ValueTask<DicomStatus>(DicomStatus.Success);
                },
                OnCFind = (query, ct) => FindInStoredDatasets(storedDatasets, query, ct)
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            // Store
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    CalledAE = ServerAE,
                    CallingAE = ClientAE
                };
                await using var storeClient = new DicomClient(clientOptions);
                var storeContexts = new[]
                {
                    new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian),
                    new PresentationContext(3, DicomUID.CTImageStorage, TransferSyntax.ImplicitVRLittleEndian)
                };
                await storeClient.ConnectAsync(storeContexts);

                var dataset = CreateTestDataset("Smith^John", "PAT001", "1.2.3.4.5", "CT");
                var file = new DicomFile(dataset, TransferSyntax.ImplicitVRLittleEndian);
                var storeScu = new CStoreScu(storeClient);
                var storeResponse = await storeScu.SendAsync(file);
                Assert.That(storeResponse.Status.IsSuccess, Is.True);
                await storeClient.ReleaseAsync();
            }

            // Find
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    CalledAE = ServerAE,
                    CallingAE = ClientAE
                };
                await using var findClient = new DicomClient(clientOptions);
                var findContexts = new[]
                {
                    new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian),
                    new PresentationContext(3, DicomUID.StudyRootQueryRetrieveFind, TransferSyntax.ImplicitVRLittleEndian)
                };
                await findClient.ConnectAsync(findContexts);

                var findScu = new CFindScu(findClient, new CFindOptions { UsePatientRoot = false });
                var queryDs = new DicomDataset();
                queryDs.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                queryDs.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT001"));
                queryDs.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, Array.Empty<byte>()));

                var results = new List<DicomDataset>();
                await foreach (var result in findScu.QueryAsync(QueryRetrieveLevel.Study, queryDs))
                {
                    results.Add(result);
                }

                Assert.That(results.Count, Is.EqualTo(1));
                await findClient.ReleaseAsync();
            }
        }

        #endregion

        #region Helper Methods

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static DicomDataset CreateTestDataset(string patientName, string patientId, string studyUid, string modality)
        {
            var ds = new DicomDataset();
            ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, patientName));
            ds.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, patientId));
            ds.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid));
            ds.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, studyUid + ".1"));
            ds.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, studyUid + ".1.1"));
            ds.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, DicomUID.CTImageStorage.ToString()));
            ds.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, modality));
            ds.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
            return ds;
        }

        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                bytes.CopyTo(padded, 0);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)0 : (byte)' ';
                bytes = padded;
            }
            return new DicomStringElement(tag, vr, bytes);
        }

        private static async IAsyncEnumerable<DicomDataset> EmptyResults(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async IAsyncEnumerable<DicomDataset> GenerateStudyResults(
            int count,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var ds = new DicomDataset();
                ds.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, $"Patient{i}^Test"));
                ds.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
                ds.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, $"1.2.3.{i}"));
                yield return ds;
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<DicomDataset> GenerateFullStudyResults(
            int count,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var ds = new DicomDataset();
                ds.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "FullTest^Patient"));
                ds.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT_FULL"));
                ds.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
                ds.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, $"1.2.3.full.{i}"));
                ds.Add(CreateStringElement(DicomTag.AccessionNumber, DicomVR.SH, "ACC001"));
                ds.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
                yield return ds;
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<DicomDataset> FilteredStudyResults(
            (string Name, string Id, string StudyUid)[] patients,
            DicomDataset query,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var namePattern = (query[DicomTag.PatientName] as DicomStringElement)?.GetString();

            foreach (var (name, id, studyUid) in patients)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(namePattern) &&
                    DicomQueryMatcher.HasDicomWildcard(namePattern))
                {
                    if (!DicomQueryMatcher.MatchesWildcard(name, namePattern!, caseInsensitive: true))
                        continue;
                }
                else if (!string.IsNullOrEmpty(namePattern) &&
                         !string.Equals(name, namePattern, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ds = new DicomDataset();
                ds.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                ds.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, name));
                ds.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, id));
                ds.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid));
                yield return ds;
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<DicomDataset> FindInStoredDatasets(
            List<DicomDataset> stored,
            DicomDataset query,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var queryPatientId = (query[DicomTag.PatientID] as DicomStringElement)?.GetString();

            foreach (var ds in stored)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(queryPatientId))
                {
                    var storedPatientId = ds.GetString(DicomTag.PatientID);
                    if (!string.Equals(storedPatientId, queryPatientId, StringComparison.Ordinal))
                        continue;
                }

                var result = new DicomDataset();
                result.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
                foreach (var element in ds)
                {
                    if (element.Tag != DicomTag.QueryRetrieveLevel)
                        result.Add(element);
                }
                yield return result;
            }

            await Task.CompletedTask;
        }

        #endregion
    }
}
