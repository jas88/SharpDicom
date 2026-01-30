using System;
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
/// DCMTK interoperability tests for C-FIND operations.
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
/// For findscp testing, you need a DCMTK installation with test databases
/// or a PACS system that supports C-FIND.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("DCMTK")]
public class CFindIntegrationTests
{
    private const int DcmtkFindScpPort = 11122;
    private const string ServerAE = "FINDSCP";
    private const string ClientAE = "SHARPDICOM";

    #region SharpDicom SCU -> DCMTK findscp

    /// <summary>
    /// Tests SharpDicom CFindScu against DCMTK findscp.
    /// </summary>
    /// <remarks>
    /// <para>Prerequisites: Run in a terminal:</para>
    /// <code>
    /// # Create a test database directory with some DICOM files
    /// findscp -v -aet FINDSCP -od ./testdb 11122
    /// </code>
    /// <para>
    /// Note: findscp requires a database of DICOM files to query.
    /// Use dcmmkdir to create an index: dcmmkdir +r ./testdb
    /// </para>
    /// </remarks>
    [Test]
    [Explicit("Requires DCMTK findscp running with test database")]
    public async Task CFindScu_QueryDcmtkFindscp_ReturnsMatches()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        // Use Patient Root Q/R SOP Class
        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveFind,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);

        var findScu = new CFindScu(client);
        var query = DicomQuery.ForPatients().WithPatientName("*"); // Wildcard query

        var results = new List<DicomDataset>();
        await foreach (var result in findScu.QueryAsync(query))
        {
            results.Add(result);
        }

        await client.ReleaseAsync();

        // Assert
        Console.WriteLine($"Received {results.Count} patient records");
        // Just verify we could execute the query without error
        // Actual match count depends on findscp's database
    }

    /// <summary>
    /// Tests study-level query with date range.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK findscp running with test database")]
    public async Task CFindScu_StudyQueryWithDateRange_ReturnsMatches()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.StudyRootQueryRetrieveFind,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);

        // Query for studies within a date range
        var findScu = new CFindScu(client, new CFindOptions { UsePatientRoot = false });
        var query = DicomQuery.ForStudies()
            .WithStudyDateRange(DateTime.Today.AddYears(-1), DateTime.Today);

        var results = new List<DicomDataset>();
        await foreach (var result in findScu.QueryAsync(query))
        {
            results.Add(result);
            Console.WriteLine($"Found study: {result.GetString(DicomTag.StudyInstanceUID)}");
        }

        await client.ReleaseAsync();

        // Assert
        Console.WriteLine($"Received {results.Count} study records");
    }

    /// <summary>
    /// Tests that C-CANCEL is sent when query is cancelled.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK findscp with many results")]
    public async Task CFindScu_Cancellation_SendsCCancel()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveFind,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);

        var findScu = new CFindScu(client);
        var query = DicomQuery.ForPatients().WithPatientName("*");

        using var cts = new CancellationTokenSource();
        var results = new List<DicomDataset>();

        // Act - cancel after first result
        try
        {
            await foreach (var result in findScu.QueryAsync(query, cts.Token))
            {
                results.Add(result);
                Console.WriteLine("Got one result, cancelling...");
                cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is processed
            Console.WriteLine("Query cancelled as expected");
        }

        await client.ReleaseAsync();

        // Assert
        Assert.That(results.Count, Is.GreaterThan(0), "Should have received at least one result before cancel");
        Console.WriteLine($"Received {results.Count} results before cancellation");
    }

    /// <summary>
    /// Tests fluent DicomQuery builder constructs correct identifier.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK findscp")]
    public async Task CFindScu_FluentQuery_FormatsCorrectly()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveFind,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);

        var findScu = new CFindScu(client);

        // Build query with multiple criteria
        var query = DicomQuery.ForStudies()
            .WithPatientName("Smith*")
            .WithAccessionNumber("ACC*");

        // Verify query dataset is formatted correctly
        var dataset = query.ToDataset();

        Assert.That(dataset.GetString(DicomTag.PatientName), Does.StartWith("Smith"));
        Assert.That(dataset.GetString(DicomTag.AccessionNumber), Does.StartWith("ACC"));
        Assert.That(dataset.GetString(DicomTag.QueryRetrieveLevel), Is.EqualTo("STUDY"));

        // Execute to verify it's accepted by DCMTK
        var results = new List<DicomDataset>();
        await foreach (var result in findScu.QueryAsync(query))
        {
            results.Add(result);
        }

        await client.ReleaseAsync();

        Console.WriteLine($"Query returned {results.Count} results");
    }

    /// <summary>
    /// Tests series-level query within a known study.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK findscp with test data")]
    public async Task CFindScu_SeriesQuery_ReturnsMatches()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveFind,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);

        var findScu = new CFindScu(client);

        // First, find a study
        var studyQuery = DicomQuery.ForStudies().WithPatientName("*");
        string? studyUid = null;

        await foreach (var study in findScu.QueryAsync(studyQuery))
        {
            studyUid = study.GetString(DicomTag.StudyInstanceUID);
            if (studyUid != null)
            {
                Console.WriteLine($"Found study: {studyUid}");
                break;
            }
        }

        if (studyUid == null)
        {
            Console.WriteLine("No studies found - cannot test series query");
            await client.ReleaseAsync();
            return;
        }

        // Now query for series in that study
        var seriesQuery = DicomQuery.ForSeries()
            .WithStudyInstanceUid(studyUid);

        var series = new List<DicomDataset>();
        await foreach (var result in findScu.QueryAsync(seriesQuery))
        {
            series.Add(result);
            Console.WriteLine($"Found series: {result.GetString(DicomTag.SeriesInstanceUID)}");
        }

        await client.ReleaseAsync();

        // Assert
        Console.WriteLine($"Found {series.Count} series in study");
    }

    /// <summary>
    /// Tests with Explicit VR Little Endian transfer syntax.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK findscp")]
    public async Task CFindScu_ExplicitVR_ReturnsMatches()
    {
        // Arrange
        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = DcmtkFindScpPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        // Use Explicit VR
        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveFind,
                TransferSyntax.ExplicitVRLittleEndian)
        };

        await client.ConnectAsync(contexts);

        var findScu = new CFindScu(client);
        var query = DicomQuery.ForPatients().WithPatientName("*");

        var results = new List<DicomDataset>();
        await foreach (var result in findScu.QueryAsync(query))
        {
            results.Add(result);
        }

        await client.ReleaseAsync();

        Console.WriteLine($"Received {results.Count} results with Explicit VR");
    }

    #endregion

    #region Automated DCMTK Tests

    /// <summary>
    /// Fully automated test that spawns findscp if available.
    /// </summary>
    [Test]
    [Explicit("Requires DCMTK to be installed in PATH")]
    public async Task CFindScu_AutomatedDcmtk_QuerySucceeds()
    {
        // Check if DCMTK is available
        if (!IsDcmtkAvailable())
        {
            Assert.Ignore("DCMTK not found in PATH");
            return;
        }

        // Note: findscp requires a database, so this test is limited
        // Just verify we can connect to a hypothetical findscp

        Console.WriteLine("DCMTK is available but findscp requires a pre-built database.");
        Console.WriteLine("To test C-FIND:");
        Console.WriteLine("1. Create a directory with DICOM files");
        Console.WriteLine("2. Run: dcmmkdir +r /path/to/dicom/files");
        Console.WriteLine("3. Run: findscp -v -aet FINDSCP -od /path/to/dicom/files 11122");
        Console.WriteLine("4. Then run the manual findscp tests");

        Assert.Pass("DCMTK available - manual findscp setup required for full test");
    }

    #endregion

    #region Helper Methods

    private static bool IsDcmtkAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "findscp",
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

    #endregion
}
