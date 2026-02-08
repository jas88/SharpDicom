using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Helpers;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm convert</c> subcommand for batch transfer syntax transcoding.
/// </summary>
internal static class ConvertCommand
{
    /// <summary>
    /// Mapping from short names to transfer syntax UIDs.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, TransferSyntax> TransferSyntaxAliases =
        new Dictionary<string, TransferSyntax>(StringComparer.OrdinalIgnoreCase)
        {
            ["htj2k-lossless"] = TransferSyntax.HTJ2KLossless,
            ["htj2k-lossless-rpcl"] = TransferSyntax.HTJ2KLosslessRPCL,
            ["htj2k-lossy"] = TransferSyntax.HTJ2KLossy,
            ["j2k-lossless"] = TransferSyntax.JPEG2000Lossless,
            ["j2k-lossy"] = TransferSyntax.JPEG2000Lossy,
            ["jpeg-baseline"] = TransferSyntax.JPEGBaseline,
            ["jpeg-lossless"] = TransferSyntax.JPEGLossless,
            ["jpeg-ls-lossless"] = TransferSyntax.JPEGLSLossless,
            ["rle"] = TransferSyntax.RLELossless,
            ["explicit-le"] = TransferSyntax.ExplicitVRLittleEndian,
        };

    /// <summary>
    /// Mapping from preset names to <see cref="HtEncoderOptions"/> values.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, HtEncoderOptions> PresetMap =
        new Dictionary<string, HtEncoderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["diagnostic"] = HtEncoderOptions.Diagnostic,
            ["archive"] = HtEncoderOptions.Archive,
            ["review"] = HtEncoderOptions.Review,
            ["fast"] = HtEncoderOptions.Fast,
            ["lossless"] = HtEncoderOptions.Lossless,
        };

    /// <summary>
    /// Creates the <c>convert</c> command with all arguments and options.
    /// </summary>
    public static Command Create()
    {
        var inputArg = new Argument<FileSystemInfo>("input")
        {
            Description = "DICOM file or directory to convert",
        };

        var transferSyntaxOption = new Option<string>("--transfer-syntax", "-t")
        {
            Description = "Target transfer syntax (short name or UID)",
            Required = true,
        };

        var presetOption = new Option<string>("--preset", "-p")
        {
            Description = "Quality preset: lossless, diagnostic, archive, review, fast",
            DefaultValueFactory = _ => "diagnostic",
        };

        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "Output directory (default: in-place with .converted.dcm suffix)",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite input files in-place",
        };

        var recursiveOption = new Option<bool>("--recursive", "-r")
        {
            Description = "Process directories recursively",
        };

        var parallelOption = new Option<int>("--parallel", "-j")
        {
            Description = "Max parallelism (default: CPU count)",
            DefaultValueFactory = _ => Environment.ProcessorCount,
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Show what would be converted without writing files",
        };

        var skipErrorsOption = new Option<bool>("--skip-errors")
        {
            Description = "Continue on individual file errors",
        };

        var command = new Command("convert", "Convert DICOM files to a different transfer syntax");
        command.Arguments.Add(inputArg);
        command.Options.Add(transferSyntaxOption);
        command.Options.Add(presetOption);
        command.Options.Add(outputOption);
        command.Options.Add(forceOption);
        command.Options.Add(recursiveOption);
        command.Options.Add(parallelOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(skipErrorsOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var input = parseResult.GetValue(inputArg)!;
            var transferSyntaxName = parseResult.GetValue(transferSyntaxOption)!;
            var preset = parseResult.GetValue(presetOption)!;
            var output = parseResult.GetValue(outputOption);
            var force = parseResult.GetValue(forceOption);
            var recursive = parseResult.GetValue(recursiveOption);
            var parallel = parseResult.GetValue(parallelOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var skipErrors = parseResult.GetValue(skipErrorsOption);

            return await ExecuteAsync(
                input, transferSyntaxName, preset, output,
                force, recursive, parallel, dryRun, skipErrors, ct).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// Resolves a transfer syntax from a short name or UID string.
    /// </summary>
    /// <param name="nameOrUid">Short name (e.g. "htj2k-lossless") or UID string.</param>
    /// <param name="transferSyntax">The resolved transfer syntax.</param>
    /// <returns>True if successfully resolved; false otherwise.</returns>
    internal static bool TryResolveTransferSyntax(string nameOrUid, out TransferSyntax transferSyntax)
    {
        // Try short name first
        if (TransferSyntaxAliases.TryGetValue(nameOrUid, out transferSyntax))
        {
            return true;
        }

        // Try as UID
        var uid = new DicomUID(nameOrUid);
        var ts = TransferSyntax.FromUID(uid);
        if (ts.IsKnown)
        {
            transferSyntax = ts;
            return true;
        }

        transferSyntax = default;
        return false;
    }

    /// <summary>
    /// Resolves a preset name to <see cref="HtEncoderOptions"/>.
    /// </summary>
    /// <param name="presetName">The preset name.</param>
    /// <param name="options">The resolved encoder options.</param>
    /// <returns>True if successfully resolved; false otherwise.</returns>
    internal static bool TryResolvePreset(string presetName, out HtEncoderOptions options)
    {
        return PresetMap.TryGetValue(presetName, out options);
    }

    private static async Task<int> ExecuteAsync(
        FileSystemInfo input,
        string transferSyntaxName,
        string presetName,
        string? outputDir,
        bool force,
        bool recursive,
        int parallelism,
        bool dryRun,
        bool skipErrors,
        CancellationToken ct)
    {
        // 1. Resolve target transfer syntax
        if (!TryResolveTransferSyntax(transferSyntaxName, out var targetTs))
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Unknown transfer syntax: '{transferSyntaxName}'"));
            Console.Error.WriteLine("Accepted names: " + string.Join(", ", TransferSyntaxAliases.Keys.OrderBy(k => k)));
            Console.Error.WriteLine("Or provide a numeric UID (e.g. 1.2.840.10008.1.2.4.201).");
            return ExitCodes.UsageError;
        }

        // 2. Resolve encoder preset (used for HTJ2K and similar)
        if (!TryResolvePreset(presetName, out var encoderOptions))
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Unknown preset: '{presetName}'"));
            Console.Error.WriteLine("Accepted presets: " + string.Join(", ", PresetMap.Keys.OrderBy(k => k)));
            return ExitCodes.UsageError;
        }

        // 3. Enumerate files
        var inputs = new[] { input };
        List<string> filePaths;
        try
        {
            filePaths = FileEnumerator.EnumerateFiles(inputs, recursive: recursive, allFiles: true).ToList();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.UsageError;
        }

        if (filePaths.Count == 0)
        {
            Console.Error.WriteLine("No DICOM files found.");
            return ExitCodes.UsageError;
        }

        // 4. Create output directory if specified
        if (outputDir != null && !Directory.Exists(outputDir))
        {
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"Cannot create output directory: {ex.Message}"));
                return ExitCodes.RuntimeError;
            }
        }

        // 5. Process files
        if (parallelism < 1)
            parallelism = 1;

        // Compute the base input path for preserving directory structure under --output
        string? inputBasePath = input is DirectoryInfo
            ? input.FullName
            : Path.GetDirectoryName(input.FullName);

        // 5a. Detect output path collisions before processing any files
        if (outputDir != null && filePaths.Count > 1)
        {
            var outputPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in filePaths)
            {
                var candidateOutput = DetermineOutputPath(path, force, outputDir, inputBasePath);
                var normalised = Path.GetFullPath(candidateOutput);
                if (outputPathMap.TryGetValue(normalised, out var existingSource))
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"Output path collision: both '{existingSource}' and '{path}' would write to '{normalised}'."));
                    Console.Error.WriteLine("Use --output with a directory input so relative paths are preserved, or process directories separately.");
                    return ExitCodes.UsageError;
                }

                outputPathMap[normalised] = path;
            }
        }

        int convertedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        if (dryRun)
        {
            // Dry-run mode: just list what would be converted
            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var file = await DicomFile.OpenAsync(path, ct: ct).ConfigureAwait(false);
                    var currentTs = file.TransferSyntax;

                    if (currentTs == targetTs)
                    {
                        skippedCount++;
                        continue;
                    }

                    var outputPath = DetermineOutputPath(path, force, outputDir, inputBasePath);
                    Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{path}: {currentTs.UID} -> {targetTs.UID} => {outputPath}"));
                    convertedCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{path}: error reading file: {ex.Message}"));
                    errorCount++;
                    if (!skipErrors)
                        break;
                }
            }
        }
        else
        {
            // Actual conversion
            var progressReporter = new ProgressReporter(Console.Error);

            await progressReporter.RunWithProgressAsync("Converting files", filePaths.Count, async (advance, innerCt) =>
            {
                if (parallelism == 1)
                {
                    // Sequential processing
                    foreach (var path in filePaths)
                    {
                        innerCt.ThrowIfCancellationRequested();
                        var result = await ConvertFileAsync(path, targetTs, encoderOptions, force, outputDir, inputBasePath, innerCt)
                            .ConfigureAwait(false);
                        UpdateCounts(result, ref convertedCount, ref skippedCount, ref errorCount, skipErrors, path);
                        advance(1);

                        if (result == ConvertResult.Error && !skipErrors)
                            break;
                    }
                }
                else
                {
                    // Parallel processing
                    using var semaphore = new SemaphoreSlim(parallelism, parallelism);
                    var tasks = new List<Task>();
                    var results = new ConcurrentBag<(string Path, ConvertResult Result)>();
                    var shouldStop = 0;

                    foreach (var path in filePaths)
                    {
                        if (Volatile.Read(ref shouldStop) != 0)
                        {
                            advance(1);
                            continue;
                        }

                        innerCt.ThrowIfCancellationRequested();
                        await semaphore.WaitAsync(innerCt).ConfigureAwait(false);

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var result = await ConvertFileAsync(path, targetTs, encoderOptions, force, outputDir, inputBasePath, innerCt)
                                    .ConfigureAwait(false);
                                results.Add((path, result));

                                if (result == ConvertResult.Error && !skipErrors)
                                    Interlocked.Exchange(ref shouldStop, 1);
                            }
                            finally
                            {
                                semaphore.Release();
                                advance(1);
                            }
                        }, innerCt));
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    // Aggregate results
                    foreach (var (path, result) in results)
                    {
                        switch (result)
                        {
                            case ConvertResult.Converted:
                                Interlocked.Increment(ref convertedCount);
                                break;
                            case ConvertResult.Skipped:
                                Interlocked.Increment(ref skippedCount);
                                break;
                            case ConvertResult.Error:
                                Interlocked.Increment(ref errorCount);
                                break;
                        }
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        // 6. Summary
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{convertedCount} converted, {skippedCount} skipped, {errorCount} errors"));

        return errorCount > 0 ? ExitCodes.RuntimeError : ExitCodes.Success;
    }

    private static void UpdateCounts(
        ConvertResult result,
        ref int convertedCount,
        ref int skippedCount,
        ref int errorCount,
        bool skipErrors,
        string path)
    {
        switch (result)
        {
            case ConvertResult.Converted:
                convertedCount++;
                break;
            case ConvertResult.Skipped:
                skippedCount++;
                break;
            case ConvertResult.Error:
                errorCount++;
                break;
        }
    }

    private static async Task<ConvertResult> ConvertFileAsync(
        string path,
        TransferSyntax targetTs,
        HtEncoderOptions encoderOptions,
        bool force,
        string? outputDir,
        string? inputBasePath,
        CancellationToken ct)
    {
        try
        {
            var file = await DicomFile.OpenAsync(path, ct: ct).ConfigureAwait(false);
            var currentTs = file.TransferSyntax;

            // Skip if already target TS
            if (currentTs == targetTs)
            {
                return ConvertResult.Skipped;
            }

            // Check if file has pixel data; if not, only TS change needed
            if (!file.HasPixelData)
            {
                // No pixel data: just re-save with the new transfer syntax
                var outputPath = DetermineOutputPath(path, force, outputDir, inputBasePath, ensureDirectory: true);
                var writerOptions = new DicomWriterOptions { TransferSyntax = targetTs };
                await file.SaveAsync(outputPath, writerOptions, ct).ConfigureAwait(false);
                return ConvertResult.Converted;
            }

            // Has pixel data: need codec transcoding
            var pixelData = file.PixelData!;
            var pixelInfo = Data.PixelDataInfo.FromDataset(file.Dataset);

            // Source codec: needed if current TS is encapsulated
            if (currentTs.IsEncapsulated)
            {
                var sourceCodec = CodecRegistry.GetCodec(currentTs);
                if (sourceCodec == null || !sourceCodec.Capabilities.CanDecode)
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(path)}: no codec to decode {currentTs.UID}"));
                    return ConvertResult.Error;
                }

                // Decode all frames to raw pixel data
                var fragments = pixelData.Fragments;
                if (fragments == null)
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(path)}: encapsulated pixel data has no fragments"));
                    return ConvertResult.Error;
                }

                var codecInfo = ToCodecPixelDataInfo(pixelInfo);
                int frameCount = pixelInfo.NumberOfFrames.GetValueOrDefault(1);
                int frameSize = codecInfo.FrameSize;
                var decodedData = new byte[frameSize * frameCount];

                for (int i = 0; i < frameCount; i++)
                {
                    var dest = new Memory<byte>(decodedData, i * frameSize, frameSize);
                    var decodeResult = sourceCodec.Decode(fragments, codecInfo, i, dest);
                    if (!decodeResult.Success)
                    {
                        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"{Path.GetFileName(path)}: decode failed for frame {i}: {decodeResult.Diagnostic?.Message}"));
                        return ConvertResult.Error;
                    }
                }

                // Remove old pixel data and add new
                file.Dataset.Remove(DicomTag.PixelData);

                if (targetTs.IsEncapsulated)
                {
                    // Encode to target compressed format
                    var targetCodec = CodecRegistry.GetCodec(targetTs);
                    if (targetCodec == null || !targetCodec.Capabilities.CanEncode)
                    {
                        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"{Path.GetFileName(path)}: no codec to encode {targetTs.UID}"));
                        return ConvertResult.Error;
                    }

                    var newFragments = targetCodec.Encode(decodedData, codecInfo, encoderOptions);
                    file.Dataset.Add(newFragments);
                }
                else
                {
                    // Target is uncompressed: store raw pixel data
                    var nativeElement = new DicomBinaryElement(DicomTag.PixelData, DicomVR.OW, decodedData);
                    file.Dataset.Add(nativeElement);
                }
            }
            else
            {
                // Source is uncompressed
                if (!targetTs.IsEncapsulated)
                {
                    // Uncompressed to uncompressed: just re-save with new TS
                    var outputPath = DetermineOutputPath(path, force, outputDir, inputBasePath, ensureDirectory: true);
                    var writerOptions = new DicomWriterOptions { TransferSyntax = targetTs };
                    await file.SaveAsync(outputPath, writerOptions, ct).ConfigureAwait(false);
                    return ConvertResult.Converted;
                }

                // Uncompressed to compressed: encode
                var targetCodec = CodecRegistry.GetCodec(targetTs);
                if (targetCodec == null || !targetCodec.Capabilities.CanEncode)
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(path)}: no codec to encode {targetTs.UID}"));
                    return ConvertResult.Error;
                }

                var codecInfo = ToCodecPixelDataInfo(pixelInfo);
                var rawData = pixelData.RawValue;

                file.Dataset.Remove(DicomTag.PixelData);
                var newFragments = targetCodec.Encode(rawData.Span, codecInfo, encoderOptions);
                file.Dataset.Add(newFragments);
            }

            // Write the result
            var finalOutputPath = DetermineOutputPath(path, force, outputDir, inputBasePath, ensureDirectory: true);
            var finalWriterOptions = new DicomWriterOptions { TransferSyntax = targetTs };
            await file.SaveAsync(finalOutputPath, finalWriterOptions, ct).ConfigureAwait(false);
            return ConvertResult.Converted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {ex.Message}"));
            return ConvertResult.Error;
        }
    }

    /// <summary>
    /// Converts the Data namespace PixelDataInfo (nullable fields) to the Codecs namespace PixelDataInfo (non-nullable).
    /// </summary>
    private static Codecs.PixelDataInfo ToCodecPixelDataInfo(Data.PixelDataInfo info)
    {
        return new Codecs.PixelDataInfo(
            Rows: info.Rows.GetValueOrDefault(),
            Columns: info.Columns.GetValueOrDefault(),
            BitsAllocated: info.BitsAllocated.GetValueOrDefault(16),
            BitsStored: info.BitsStored.GetValueOrDefault(info.BitsAllocated.GetValueOrDefault(16)),
            HighBit: info.HighBit.GetValueOrDefault((ushort)(info.BitsStored.GetValueOrDefault(info.BitsAllocated.GetValueOrDefault(16)) - 1)),
            SamplesPerPixel: info.SamplesPerPixel.GetValueOrDefault(1),
            PixelRepresentation: info.PixelRepresentation.GetValueOrDefault(),
            PlanarConfiguration: info.PlanarConfiguration.GetValueOrDefault(),
            NumberOfFrames: info.NumberOfFrames.GetValueOrDefault(1));
    }

    internal static string DetermineOutputPath(string originalPath, bool force, string? outputDir, string? inputBasePath = null, bool ensureDirectory = false)
    {
        if (outputDir != null)
        {
            if (inputBasePath != null)
            {
                var relativePath = Path.GetRelativePath(inputBasePath, originalPath);
                var outputPath = Path.Combine(outputDir, relativePath);
                if (ensureDirectory)
                {
                    var outputSubDir = Path.GetDirectoryName(outputPath);
                    if (outputSubDir != null && !Directory.Exists(outputSubDir))
                        Directory.CreateDirectory(outputSubDir);
                }
                return outputPath;
            }

            var fileName = Path.GetFileName(originalPath);
            return Path.Combine(outputDir, fileName);
        }

        if (force)
            return originalPath;

        // Default: create .converted.dcm alongside original
        var dir = Path.GetDirectoryName(originalPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(originalPath);
        return Path.Combine(dir, name + ".converted.dcm");
    }

    internal enum ConvertResult
    {
        Converted,
        Skipped,
        Error,
    }
}
