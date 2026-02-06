using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Configuration;
using SharpDicom.Cli.Helpers;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;
using SharpDicom.Network.Tls;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm store</c> subcommand for sending DICOM files to a PACS server via C-STORE.
/// </summary>
internal static class StoreCommand
{
    /// <summary>
    /// Creates the <c>store</c> command with all arguments and options.
    /// </summary>
    public static Command Create()
    {
        var filesArgument = new Argument<FileSystemInfo[]>("files")
        {
            Description = "DICOM files or directories to send",
            Arity = ArgumentArity.OneOrMore,
        };

        var hostOption = new Option<string?>("--host", "-H")
        {
            Description = "Remote PACS hostname",
        };

        var portOption = new Option<int>("--port", "-p")
        {
            Description = "Remote PACS port",
            DefaultValueFactory = _ => 104,
        };

        var calledAeOption = new Option<string?>("--called-ae", "-c")
        {
            Description = "Called AE title",
        };

        var callingAeOption = new Option<string?>("--calling-ae")
        {
            Description = "Calling AE title",
            DefaultValueFactory = _ => "SHARPDCM",
        };

        var connectionOption = new Option<string?>("--connection", "-C")
        {
            Description = "Connection string (pacs://AET@host:port)",
        };

        var profileOption = new Option<string?>("--profile")
        {
            Description = "Named PACS profile from config",
        };

        var retryOption = new Option<int>("--retry", "-r")
        {
            Description = "Number of retries on failure",
            DefaultValueFactory = _ => 0,
        };

        var tlsOption = new Option<bool>("--tls")
        {
            Description = "Use TLS encryption",
            DefaultValueFactory = _ => false,
        };

        var continueOnErrorOption = new Option<bool>("--continue-on-error")
        {
            Description = "Continue sending remaining files after an error",
            DefaultValueFactory = _ => false,
        };

        var command = new Command("store", "Send DICOM files to a PACS server (C-STORE)");
        command.Arguments.Add(filesArgument);
        command.Options.Add(hostOption);
        command.Options.Add(portOption);
        command.Options.Add(calledAeOption);
        command.Options.Add(callingAeOption);
        command.Options.Add(connectionOption);
        command.Options.Add(profileOption);
        command.Options.Add(retryOption);
        command.Options.Add(tlsOption);
        command.Options.Add(continueOnErrorOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var files = parseResult.GetValue(filesArgument)!;
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var calledAe = parseResult.GetValue(calledAeOption);
            var callingAe = parseResult.GetValue(callingAeOption) ?? "SHARPDCM";
            var connection = parseResult.GetValue(connectionOption);
            var profile = parseResult.GetValue(profileOption);
            var retryCount = parseResult.GetValue(retryOption);
            var useTls = parseResult.GetValue(tlsOption);
            var continueOnError = parseResult.GetValue(continueOnErrorOption);

            return await ExecuteAsync(
                files, host, port, calledAe, callingAe,
                connection, profile, retryCount, useTls,
                continueOnError, ct).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileSystemInfo[] inputs,
        string? host,
        int port,
        string? calledAe,
        string callingAe,
        string? connection,
        string? profileName,
        int retryCount,
        bool useTls,
        bool continueOnError,
        CancellationToken ct)
    {
        // Clamp retryCount to non-negative to prevent skipping the send loop
        if (retryCount < 0)
            retryCount = 0;

        // 1. Resolve PACS connection (precedence: flags > connection string > profile > error)
        if (!TryResolveConnection(
                host, port, calledAe, callingAe, connection, profileName, useTls,
                out var resolvedHost, out var resolvedPort, out var resolvedCalledAe,
                out var resolvedCallingAe, out var resolvedTls))
        {
            Console.Error.WriteLine("No PACS connection specified. Use --host, --connection, or --profile.");
            return ExitCodes.UsageError;
        }

        // 2. Enumerate files
        List<string> filePaths;
        try
        {
            filePaths = FileEnumerator.EnumerateFiles(inputs, recursive: true, allFiles: true).ToList();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.UsageError;
        }

        // 3. Validate at least one file found
        if (filePaths.Count == 0)
        {
            Console.Error.WriteLine("No DICOM files found.");
            return ExitCodes.UsageError;
        }

        // 4. First pass: read file headers to collect unique SOP Class UIDs
        Console.Error.WriteLine($"Scanning {filePaths.Count} file(s) for SOP classes...");
        var fileInfos = new List<(string Path, DicomUID SopClassUid)>();
        var uniqueSopClasses = new HashSet<DicomUID>();

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = await DicomFile.OpenAsync(filePath, ct: ct).ConfigureAwait(false);
                var sopClassUid = GetSOPClassUID(file);
                if (sopClassUid.IsEmpty)
                {
                    Console.Error.WriteLine($"{filePath}: Missing SOP Class UID, skipping.");
                    continue;
                }

                fileInfos.Add((filePath, sopClassUid));
                uniqueSopClasses.Add(sopClassUid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"{filePath}: Failed to read: {ex.Message}");
                if (!continueOnError)
                    return ExitCodes.RuntimeError;
            }
        }

        if (fileInfos.Count == 0)
        {
            Console.Error.WriteLine("No valid DICOM files found.");
            return ExitCodes.RuntimeError;
        }

        // 5. Build presentation contexts for all unique SOP classes
        var contexts = BuildPresentationContexts(uniqueSopClasses);

        // 6. Create DicomClient and connect
        var clientOptions = new DicomClientOptions
        {
            Host = resolvedHost,
            Port = resolvedPort,
            CalledAE = resolvedCalledAe,
            CallingAE = resolvedCallingAe,
        };

        if (resolvedTls)
        {
            clientOptions.Tls = new TlsOptions();
        }

        var progressReporter = new ProgressReporter(Console.Error);
        int successCount = 0;
        int failCount = 0;

        await using var client = new DicomClient(clientOptions);

        try
        {
            await client.ConnectAsync(contexts, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Failed to connect to {resolvedHost}:{resolvedPort}: {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        // 7. Send loop with progress
        var storeScu = new CStoreScu(client);

        await progressReporter.RunWithProgressAsync("Sending files", fileInfos.Count, async (advance, innerCt) =>
        {
            foreach (var (filePath, _) in fileInfos)
            {
                innerCt.ThrowIfCancellationRequested();

                var sent = false;
                for (int attempt = 0; attempt <= retryCount; attempt++)
                {
                    try
                    {
                        var file = await DicomFile.OpenAsync(filePath, ct: innerCt).ConfigureAwait(false);
                        var response = await storeScu.SendAsync(file, ct: innerCt).ConfigureAwait(false);

                        if (response.IsSuccessOrWarning)
                        {
                            successCount++;
                            sent = true;
                            break;
                        }

                        if (attempt == retryCount)
                        {
                            Console.Error.WriteLine(
                                $"{Path.GetFileName(filePath)}: C-STORE failed: 0x{response.Status.Code:X4} {response.ErrorComment ?? response.Status.ErrorComment ?? ""}");
                            failCount++;
                        }
                        else
                        {
                            Console.Error.WriteLine(
                                $"{Path.GetFileName(filePath)}: Attempt {attempt + 1} failed (0x{response.Status.Code:X4}), retrying...");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Propagate cancellation
                    }
                    catch (Exception ex) when (attempt < retryCount)
                    {
                        Console.Error.WriteLine(
                            $"{Path.GetFileName(filePath)}: Attempt {attempt + 1} failed: {ex.Message}");
                    }
                    catch (Exception ex) when (attempt == retryCount)
                    {
                        Console.Error.WriteLine(
                            $"{Path.GetFileName(filePath)}: Failed after {retryCount + 1} attempt(s): {ex.Message}");
                        failCount++;
                    }
                }

                if (!sent && !continueOnError && failCount > 0)
                {
                    advance(1);
                    break;
                }

                advance(1);
            }
        }, ct).ConfigureAwait(false);

        // 8. Summary
        var total = successCount + failCount;
        Console.Error.WriteLine($"Sent {successCount}/{total} file(s) ({failCount} failed)");

        return failCount > 0 ? ExitCodes.RuntimeError : ExitCodes.Success;
    }

    /// <summary>
    /// Resolves PACS connection parameters from flags, connection string, or profile.
    /// </summary>
    private static bool TryResolveConnection(
        string? host,
        int port,
        string? calledAe,
        string callingAe,
        string? connection,
        string? profileName,
        bool useTls,
        out string resolvedHost,
        out int resolvedPort,
        out string resolvedCalledAe,
        out string resolvedCallingAe,
        out bool resolvedTls)
    {
        resolvedHost = string.Empty;
        resolvedPort = 104;
        resolvedCalledAe = string.Empty;
        resolvedCallingAe = callingAe;
        resolvedTls = useTls;

        // Flags take precedence
        if (!string.IsNullOrWhiteSpace(host))
        {
            resolvedHost = host!;
            resolvedPort = port;
            resolvedCalledAe = calledAe ?? "ANY-SCP";
            return true;
        }

        // Connection string
        if (!string.IsNullOrWhiteSpace(connection))
        {
            if (ConnectionStringParser.TryParse(connection!, out var parsedHost, out var parsedPort, out var parsedAe))
            {
                resolvedHost = parsedHost;
                resolvedPort = parsedPort;
                resolvedCalledAe = parsedAe;
                return true;
            }

            Console.Error.WriteLine($"Invalid connection string: {connection}");
            Console.Error.WriteLine("Expected format: pacs://AET@host:port");
            return false;
        }

        // Profile
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var config = ConfigLoader.Load();
            if (config.Profiles.TryGetValue(profileName!, out var profile))
            {
                resolvedHost = profile.Host;
                resolvedPort = profile.Port;
                resolvedCalledAe = profile.CalledAE;
                resolvedCallingAe = profile.CallingAE ?? callingAe;
                resolvedTls = profile.UseTls || useTls;
                return true;
            }

            Console.Error.WriteLine($"Unknown profile: {profileName}");
            Console.Error.WriteLine("Available profiles can be configured in ~/.sharpdcm/config.toml");
            return false;
        }

        return false;
    }

    /// <summary>
    /// Builds presentation contexts for all unique SOP classes, proposing common transfer syntaxes.
    /// </summary>
    private static List<PresentationContext> BuildPresentationContexts(HashSet<DicomUID> sopClasses)
    {
        var contexts = new List<PresentationContext>();
        byte contextId = 1;

        foreach (var sopClass in sopClasses)
        {
            if (contextId > 255)
            {
                Console.Error.WriteLine(
                    $"Warning: Too many unique SOP classes ({sopClasses.Count}). " +
                    "Only the first 128 will be proposed.");
                break;
            }

            contexts.Add(new PresentationContext(
                contextId,
                sopClass,
                TransferSyntax.ExplicitVRLittleEndian,
                TransferSyntax.ImplicitVRLittleEndian));

            contextId += 2; // Presentation context IDs must be odd
        }

        return contexts;
    }

    /// <summary>
    /// Gets the SOP Class UID from a DicomFile.
    /// </summary>
    private static DicomUID GetSOPClassUID(DicomFile file)
    {
        // Try File Meta Information first
        var uid = file.FileMetaInfo?.GetString(DicomTag.MediaStorageSOPClassUID);
        if (!string.IsNullOrEmpty(uid))
            return new DicomUID(uid!.TrimEnd('\0', ' '));

        // Fall back to dataset
        var dsUid = file.Dataset.GetString(DicomTag.SOPClassUID);
        return dsUid != null ? new DicomUID(dsUid.TrimEnd('\0', ' ')) : default;
    }
}
