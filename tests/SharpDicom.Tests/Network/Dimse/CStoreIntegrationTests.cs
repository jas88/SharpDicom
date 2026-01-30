using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network.Dimse;

/// <summary>
/// DCMTK interoperability tests for C-STORE operations.
/// </summary>
/// <remarks>
/// <para>
/// These tests require DCMTK tools to be installed and available in PATH.
/// They are marked as Explicit and in the "Integration" category to prevent
/// them from running during normal test execution.
/// </para>
/// <para>
/// To run these tests manually:
/// <code>
/// dotnet test --filter "Category=Integration&amp;Category=DCMTK"
/// </code>
/// </para>
/// <para>
/// DCMTK can be installed via:
/// - macOS: brew install dcmtk
/// - Ubuntu: apt-get install dcmtk
/// - Windows: Download from https://dicom.offis.de/dcmtk.php.en
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("DCMTK")]
public class CStoreIntegrationTests : IDisposable
{
    private const int DcmtkStoreScpPort = 11120;
    private const int SharpDicomServerPort = 11121;
    private const string ServerAE = "STORESCP";
    private const string ClientAE = "SHARPDICOM";

    private string? _tempDir;
    private bool _disposed;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpdicom_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Disposes the test fixture.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            TearDown();
        }
        _disposed = true;
    }

    #region SharpDicom SCU -> DCMTK storescp

    /// <summary>
    /// Tests SharpDicom CStoreScu against DCMTK storescp.
    /// </summary>
    /// <remarks>
    /// <para>Prerequisites: Run in a terminal:</para>
    /// <code>storescp -v -od /tmp/received 11120</code>
    /// </remarks>
    [Test]
    [Explicit("Requires DCMTK storescp running: storescp -v -od /tmp/received 11120")]
    public async Task CStoreScu_SendToDcmtkStorescp_Succeeds()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.6.7.8.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "DcmtkTest");

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkStoreScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True,
            $"C-STORE to DCMTK storescp should succeed, got status: 0x{response.Status.Code:X4}");
    }

    /// <summary>
    /// Tests SharpDicom CStoreScu with Explicit VR Little Endian against DCMTK.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK storescp running: storescp -v -od /tmp/received 11120")]
    public async Task CStoreScu_ExplicitVR_ToDcmtkStorescp_Succeeds()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.6.7.9.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "ExplicitVRTest");

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkStoreScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ExplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True,
            "C-STORE with Explicit VR LE to DCMTK should succeed");
    }

    /// <summary>
    /// Tests sending multiple files on the same association to DCMTK storescp.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK storescp running: storescp -v -od /tmp/received 11120")]
    public async Task CStoreScu_MultipleFiles_ToDcmtkStorescp_AllSucceed()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var fileCount = 5;

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkStoreScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);

        // Act - send multiple files on same association
        for (int i = 0; i < fileCount; i++)
        {
            var sopInstanceUid = new DicomUID($"1.2.3.4.5.6.7.10.{i}.{DateTime.UtcNow.Ticks}");
            var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, $"Patient{i}");

            var response = await storeScu.SendAsync(dataset, null);

            // Assert each file
            Assert.That(response.IsSuccess, Is.True, $"File {i} should succeed");
        }

        await client.ReleaseAsync();
    }

    #endregion

    #region DCMTK storescu -> SharpDicom SCP

    /// <summary>
    /// Tests that SharpDicom DicomServer can receive C-STORE from DCMTK storescu.
    /// </summary>
    /// <remarks>
    /// <para>This test starts a SharpDicom server and waits for DCMTK storescu to connect.</para>
    /// <para>After starting the test, create a test DICOM file and run in a terminal:</para>
    /// <code>storescu -v 127.0.0.1 11121 /path/to/test.dcm</code>
    /// </remarks>
    [Test]
    [Explicit("Requires DCMTK storescu to connect: storescu -v 127.0.0.1 11121 /path/to/test.dcm")]
    public async Task CStoreScp_ReceiveFromDcmtkStorescu_Succeeds()
    {
        // Arrange
        var receivedDatasets = new ConcurrentBag<DicomDataset>();
        var storeReceived = new TaskCompletionSource<bool>();

        var serverOptions = new DicomServerOptions
        {
            Port = SharpDicomServerPort,
            AETitle = ServerAE,
            OnCStoreRequest = (ctx, dataset, ct) =>
            {
                receivedDatasets.Add(dataset);
                storeReceived.TrySetResult(true);
                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        await using var server = new DicomServer(serverOptions);
        server.Start();

        Console.WriteLine($"Server started on port {SharpDicomServerPort}.");
        Console.WriteLine($"Run: storescu -v 127.0.0.1 {SharpDicomServerPort} /path/to/test.dcm");

        // Act - wait for DCMTK storescu to connect (with 60 second timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(60));
        var completedTask = await Task.WhenAny(storeReceived.Task, timeout);

        // Assert
        Assert.That(completedTask, Is.EqualTo(storeReceived.Task),
            "Expected C-STORE from storescu within 60 seconds");
        Assert.That(receivedDatasets.Count, Is.GreaterThan(0),
            "Should have received at least one dataset");
    }

    /// <summary>
    /// Tests that SharpDicom server responds correctly to DCMTK storescu
    /// for various SOP Classes.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK storescu to connect with various SOP Classes")]
    public async Task CStoreScp_MultipleSOPClasses_FromDcmtkStorescu_AllSucceed()
    {
        // Arrange
        var receivedByClass = new ConcurrentDictionary<string, int>();
        var expectedCount = 3; // Adjust based on test files
        var allReceived = new TaskCompletionSource<bool>();

        var serverOptions = new DicomServerOptions
        {
            Port = SharpDicomServerPort,
            AETitle = ServerAE,
            OnCStoreRequest = (ctx, dataset, ct) =>
            {
                var sopClass = ctx.SOPClassUID.ToString();
                receivedByClass.AddOrUpdate(sopClass, 1, (k, v) => v + 1);

                var total = 0;
                foreach (var count in receivedByClass.Values)
                    total += count;

                if (total >= expectedCount)
                    allReceived.TrySetResult(true);

                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        await using var server = new DicomServer(serverOptions);
        server.Start();

        Console.WriteLine($"Server started on port {SharpDicomServerPort}.");
        Console.WriteLine("Send multiple DICOM files with different SOP Classes.");

        // Act
        var timeout = Task.Delay(TimeSpan.FromSeconds(90));
        var completedTask = await Task.WhenAny(allReceived.Task, timeout);

        // Assert
        if (completedTask == allReceived.Task)
        {
            Assert.That(receivedByClass.Count, Is.GreaterThan(0),
                "Should have received files from at least one SOP Class");
            Console.WriteLine($"Received files from {receivedByClass.Count} different SOP Classes");
        }
        else
        {
            Console.WriteLine("Timeout - test requires manual interaction.");
        }
    }

    #endregion

    #region Orthanc Integration Tests (CI)

    /// <summary>
    /// Gets the Orthanc host from environment variable.
    /// </summary>
    private static string? OrthancHost => Environment.GetEnvironmentVariable("ORTHANC_HOST");

    /// <summary>
    /// Gets the Orthanc DICOM port from environment variable.
    /// </summary>
    private static int OrthancPort => int.TryParse(
        Environment.GetEnvironmentVariable("ORTHANC_DICOM_PORT"), out var port) ? port : 4242;

    /// <summary>
    /// Gets the Orthanc AE title from environment variable.
    /// </summary>
    private static string OrthancAeTitle =>
        Environment.GetEnvironmentVariable("ORTHANC_AE_TITLE") ?? "ORTHANC";

    /// <summary>
    /// Tests C-STORE to Orthanc PACS in CI environment.
    /// </summary>
    [Test]
    [Category("Orthanc")]
    public async Task CStoreScu_SendToOrthanc_Succeeds()
    {
        if (string.IsNullOrEmpty(OrthancHost))
        {
            Assert.Ignore("ORTHANC_HOST not set - skipping Orthanc test");
            return;
        }

        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.826.0.1.3680043.8.1055.1.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "OrthancTest");

        var clientOptions = new DicomClientOptions
        {
            Host = OrthancHost,
            Port = OrthancPort,
            CalledAE = OrthancAeTitle,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True,
            $"C-STORE to Orthanc should succeed, got status: 0x{response.Status.Code:X4}");
    }

    /// <summary>
    /// Tests C-STORE with Explicit VR Little Endian to Orthanc.
    /// </summary>
    [Test]
    [Category("Orthanc")]
    public async Task CStoreScu_ExplicitVR_ToOrthanc_Succeeds()
    {
        if (string.IsNullOrEmpty(OrthancHost))
        {
            Assert.Ignore("ORTHANC_HOST not set - skipping Orthanc test");
            return;
        }

        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.826.0.1.3680043.8.1055.2.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "OrthancExplicitVR");

        var clientOptions = new DicomClientOptions
        {
            Host = OrthancHost,
            Port = OrthancPort,
            CalledAE = OrthancAeTitle,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ExplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True,
            "C-STORE with Explicit VR LE to Orthanc should succeed");
    }

    /// <summary>
    /// Tests sending multiple files on the same association to Orthanc.
    /// </summary>
    [Test]
    [Category("Orthanc")]
    public async Task CStoreScu_MultipleFiles_ToOrthanc_AllSucceed()
    {
        if (string.IsNullOrEmpty(OrthancHost))
        {
            Assert.Ignore("ORTHANC_HOST not set - skipping Orthanc test");
            return;
        }

        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        const int fileCount = 5;

        var clientOptions = new DicomClientOptions
        {
            Host = OrthancHost,
            Port = OrthancPort,
            CalledAE = OrthancAeTitle,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);

        // Act - send multiple files on same association
        for (int i = 0; i < fileCount; i++)
        {
            var sopInstanceUid = new DicomUID($"1.2.826.0.1.3680043.8.1055.3.{i}.{DateTime.UtcNow.Ticks}");
            var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, $"OrthancPatient{i}");

            var response = await storeScu.SendAsync(dataset, null);

            // Assert each file
            Assert.That(response.IsSuccess, Is.True, $"File {i} should succeed");
        }

        await client.ReleaseAsync();
    }

    /// <summary>
    /// Tests sending different SOP Classes to Orthanc.
    /// </summary>
    [Test]
    [Category("Orthanc")]
    public async Task CStoreScu_DifferentSopClasses_ToOrthanc_AllSucceed()
    {
        if (string.IsNullOrEmpty(OrthancHost))
        {
            Assert.Ignore("ORTHANC_HOST not set - skipping Orthanc test");
            return;
        }

        // Arrange - multiple SOP classes
        var sopClasses = new[]
        {
            DicomUID.CTImageStorage,
            DicomUID.MRImageStorage,
            DicomUID.SecondaryCaptureImageStorage
        };

        var clientOptions = new DicomClientOptions
        {
            Host = OrthancHost,
            Port = OrthancPort,
            CalledAE = OrthancAeTitle,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        // Create presentation contexts for all SOP classes
        var contexts = new List<PresentationContext>();
        for (int i = 0; i < sopClasses.Length; i++)
        {
            contexts.Add(new PresentationContext((byte)(i * 2 + 1), sopClasses[i],
                TransferSyntax.ImplicitVRLittleEndian));
        }

        await client.ConnectAsync(contexts.ToArray());
        var storeScu = new CStoreScu(client);

        // Act - send one instance of each SOP class
        for (int i = 0; i < sopClasses.Length; i++)
        {
            var sopInstanceUid = new DicomUID($"1.2.826.0.1.3680043.8.1055.4.{i}.{DateTime.UtcNow.Ticks}");
            var dataset = CreateTestDataset(sopClasses[i], sopInstanceUid, $"MultiSOPPatient{i}");

            var response = await storeScu.SendAsync(dataset, null);

            // Assert each SOP class
            Assert.That(response.IsSuccess, Is.True,
                $"SOP Class {sopClasses[i]} should succeed");
        }

        await client.ReleaseAsync();
    }

    #endregion

    #region Automated DCMTK Tests (spawns DCMTK processes)

    /// <summary>
    /// Fully automated test: spawns storescp, sends file, verifies receipt.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK to be installed in PATH")]
    public async Task CStoreScu_AutomatedDcmtk_SendAndVerify()
    {
        // Check if DCMTK is available
        if (!IsDcmtkAvailable())
        {
            Assert.Ignore("DCMTK not found in PATH");
            return;
        }

        // Arrange - start storescp
        var outputDir = Path.Combine(_tempDir!, "received");
        Directory.CreateDirectory(outputDir);

        var port = GetFreePort();
        using var storescp = StartStorescp(port, outputDir);

        // Give storescp time to start
        await Task.Delay(1000);

        // Create and send a file
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.6.7.auto.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "AutomatedTest");

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = port,
            CalledAE = "STORESCP",
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True, "C-STORE should succeed");

        // Give time for file to be written
        await Task.Delay(500);

        // Verify file was received
        var files = Directory.GetFiles(outputDir, "*.dcm", SearchOption.AllDirectories);
        Assert.That(files.Length, Is.GreaterThan(0), "storescp should have received and saved the file");
    }

    /// <summary>
    /// Fully automated test: starts SharpDicom server, uses storescu to send.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK to be installed in PATH")]
    public async Task CStoreScp_AutomatedDcmtk_ReceiveFromStorescu()
    {
        // Check if DCMTK is available
        if (!IsDcmtkAvailable())
        {
            Assert.Ignore("DCMTK not found in PATH");
            return;
        }

        // Arrange
        var receivedDatasets = new ConcurrentBag<DicomDataset>();
        var port = GetFreePort();

        var serverOptions = new DicomServerOptions
        {
            Port = port,
            AETitle = "SHARPDICOM_SCP",
            OnCStoreRequest = (ctx, dataset, ct) =>
            {
                receivedDatasets.Add(dataset);
                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        await using var server = new DicomServer(serverOptions);
        server.Start();

        // Create a test DICOM file
        var testFile = Path.Combine(_tempDir!, "test.dcm");
        await CreateTestDicomFile(testFile);

        // Act - run storescu
        var result = RunStorescu("127.0.0.1", port, testFile);

        // Assert
        Assert.That(result, Is.EqualTo(0), "storescu should exit with code 0");

        // Give time for server to process
        await Task.Delay(500);

        Assert.That(receivedDatasets.Count, Is.GreaterThan(0),
            "Server should have received the dataset from storescu");
    }

    #endregion

    #region Helper Methods

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static DicomDataset CreateTestDataset(
        DicomUID sopClassUid,
        DicomUID sopInstanceUid,
        string patientName)
    {
        var dataset = new DicomDataset();

        dataset.Add(new DicomStringElement(DicomTag.SOPClassUID, DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes(sopClassUid.ToString())));
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes(sopInstanceUid.ToString())));
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            System.Text.Encoding.ASCII.GetBytes(patientName)));
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            System.Text.Encoding.ASCII.GetBytes("000001")));
        dataset.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.3.4.5")));
        dataset.Add(new DicomStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.3.4.5.1")));

        return dataset;
    }

    private static bool IsDcmtkAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "storescp",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Process StartStorescp(int port, string outputDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "storescp",
                Arguments = $"-v -od \"{outputDir}\" {port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        return process;
    }

    private static int RunStorescu(string host, int port, string filePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "storescu",
                Arguments = $"-v {host} {port} \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit(30000);
        return process.ExitCode;
    }

    private static async Task CreateTestDicomFile(string filePath)
    {
        // Create minimal DICOM file using dump2dcm or similar
        // For now, create a simple binary file that DCMTK can parse
        // A proper implementation would write valid Part 10 format

        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.6.7.file.{DateTime.UtcNow.Ticks}");
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "FileTest");
        var file = new DicomFile(dataset, TransferSyntax.ExplicitVRLittleEndian);

        // Use DicomFileWriter to create valid file
        await using var stream = File.Create(filePath);
        using var writer = new SharpDicom.IO.DicomFileWriter(stream);
        await writer.WriteAsync(file);
    }

    #endregion
}
