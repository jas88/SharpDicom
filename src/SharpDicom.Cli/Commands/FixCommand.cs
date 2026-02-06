using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Diagnostics;
using SharpDicom.Cli.Helpers;
using SharpDicom.Data;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm fix</c> subcommand for repairing common DICOM issues.
/// </summary>
internal static class FixCommand
{
    /// <summary>
    /// Creates the <c>fix</c> command with all arguments and options.
    /// </summary>
    public static Command Create()
    {
        var filesArg = new Argument<FileSystemInfo[]>("files")
        {
            Description = "DICOM files or directories to fix",
            Arity = ArgumentArity.OneOrMore,
        };

        var dryRunOption = new Option<bool>("--dry-run", "-n")
        {
            Description = "Show what would change without modifying files",
        };

        var forceOption = new Option<bool>("--force", "-F")
        {
            Description = "Overwrite original files instead of creating .fixed.dcm",
        };

        var outputDirOption = new Option<string?>("--output-dir", "-o")
        {
            Description = "Write fixed files to a specific directory",
        };

        var fixUidsOption = new Option<bool>("--fix-uids")
        {
            Description = "Fix invalid UIDs",
            DefaultValueFactory = _ => true,
        };

        var fixDatesOption = new Option<bool>("--fix-dates")
        {
            Description = "Fix invalid dates and times",
            DefaultValueFactory = _ => true,
        };

        var removeInvalidOption = new Option<bool>("--remove-invalid")
        {
            Description = "Remove elements that fail validation",
        };

        var fixEncodingOption = new Option<bool>("--fix-encoding")
        {
            Description = "Fix missing character set declarations",
            DefaultValueFactory = _ => true,
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Output format for changes: text, json",
            DefaultValueFactory = _ => "text",
        };

        var continueOnErrorOption = new Option<bool>("--continue-on-error")
        {
            Description = "Continue fixing remaining files after errors",
        };

        var command = new Command("fix", "Repair common issues in DICOM files");
        command.Arguments.Add(filesArg);
        command.Options.Add(dryRunOption);
        command.Options.Add(forceOption);
        command.Options.Add(outputDirOption);
        command.Options.Add(fixUidsOption);
        command.Options.Add(fixDatesOption);
        command.Options.Add(removeInvalidOption);
        command.Options.Add(fixEncodingOption);
        command.Options.Add(formatOption);
        command.Options.Add(continueOnErrorOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var files = parseResult.GetValue(filesArg)!;
            var dryRun = parseResult.GetValue(dryRunOption);
            var force = parseResult.GetValue(forceOption);
            var outputDir = parseResult.GetValue(outputDirOption);
            var fixUids = parseResult.GetValue(fixUidsOption);
            var fixDates = parseResult.GetValue(fixDatesOption);
            var removeInvalid = parseResult.GetValue(removeInvalidOption);
            var fixEncoding = parseResult.GetValue(fixEncodingOption);
            var format = parseResult.GetValue(formatOption)!;
            var continueOnError = parseResult.GetValue(continueOnErrorOption);

            return await RunAsync(
                files, dryRun, force, outputDir,
                fixUids, fixDates, removeInvalid, fixEncoding,
                format, continueOnError, ct).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<int> RunAsync(
        FileSystemInfo[] inputs,
        bool dryRun,
        bool force,
        string? outputDir,
        bool fixUids,
        bool fixDates,
        bool removeInvalid,
        bool fixEncoding,
        string format,
        bool continueOnError,
        CancellationToken ct)
    {
        var fixOptions = new FixOptions
        {
            FixInvalidUids = fixUids,
            FixInvalidDates = fixDates,
            FixInvalidTimes = fixDates, // dates flag also covers times
            RemoveInvalidElements = removeInvalid,
            FixCharacterEncoding = fixEncoding,
        };

        IEnumerable<string> filePaths;
        try
        {
            filePaths = FileEnumerator.EnumerateFiles(inputs, allFiles: true);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.RuntimeError;
        }

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

        var fileResults = new List<FileFixResult>();
        int filesProcessed = 0;
        int totalChanges = 0;
        int filesUnchanged = 0;
        bool hasError = false;

        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            filesProcessed++;

            try
            {
                var file = await DicomFile.OpenAsync(path, ct: ct).ConfigureAwait(false);
                var actions = DicomFixer.Fix(file.Dataset, fixOptions);

                string? outputPath = null;

                if (actions.Count > 0 && !dryRun)
                {
                    outputPath = DetermineOutputPath(path, force, outputDir);
                    await file.SaveAsync(outputPath, ct: ct).ConfigureAwait(false);
                }

                if (actions.Count == 0)
                    filesUnchanged++;

                totalChanges += actions.Count;
                fileResults.Add(new FileFixResult(path, outputPath, actions));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                hasError = true;
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"Error processing {path}: {ex.Message}"));

                if (!continueOnError)
                    return ExitCodes.RuntimeError;
            }
        }

        if (filesProcessed == 0)
        {
            Console.Error.WriteLine("No DICOM files found.");
            return ExitCodes.RuntimeError;
        }

        OutputResults(format, fileResults, dryRun);

        // Summary to stderr
        var modeStr = dryRun ? " (dry-run)" : "";
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Fixed {filesProcessed - filesUnchanged} files ({totalChanges} changes total), {filesUnchanged} files unchanged{modeStr}"));

        return hasError ? ExitCodes.RuntimeError : ExitCodes.Success;
    }

    private static string DetermineOutputPath(string originalPath, bool force, string? outputDir)
    {
        if (outputDir != null)
        {
            var fileName = Path.GetFileName(originalPath);
            return Path.Combine(outputDir, fileName);
        }

        if (force)
            return originalPath;

        // Default: create .fixed.dcm alongside original
        var dir = Path.GetDirectoryName(originalPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        return Path.Combine(dir, $"{name}.fixed{ext}");
    }

    private static void OutputResults(string format, List<FileFixResult> fileResults, bool dryRun)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            OutputJson(fileResults, dryRun);
        }
        else
        {
            OutputText(fileResults, dryRun);
        }
    }

    private static void OutputText(List<FileFixResult> fileResults, bool dryRun)
    {
        var dict = DicomDictionary.Default;
        bool isTty = !Console.IsOutputRedirected;

        foreach (var result in fileResults)
        {
            if (result.Actions.Count == 0)
                continue;

            var prefix = dryRun ? "(dry-run) " : "";
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{prefix}{result.Path}: {result.Actions.Count} fixes {(dryRun ? "would be applied" : "applied")}"));

            foreach (var action in result.Actions)
            {
                var tagStr = action.Tag.ToString();
                var entry = dict.GetEntry(action.Tag);
                var keyword = entry.HasValue ? $" {entry.Value.Keyword}" : "";

                var fixLabel = isTty ? "\u001b[32mFIX \u001b[0m" : "FIX ";
                Console.Out.WriteLine($"  {fixLabel} {tagStr}{keyword}: {action.Description}");

                if (action.OldValue != null)
                    Console.Out.WriteLine($"    - Old: {action.OldValue}");
                if (action.NewValue != null)
                    Console.Out.WriteLine($"    + New: {action.NewValue}");
            }
        }
    }

    private static void OutputJson(List<FileFixResult> fileResults, bool dryRun)
    {
        var dict = DicomDictionary.Default;

        using var stream = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();

        foreach (var result in fileResults)
        {
            writer.WriteStartObject();
            writer.WriteString("path", result.Path);
            writer.WriteBoolean("dryRun", dryRun);
            if (result.OutputPath != null)
                writer.WriteString("outputPath", result.OutputPath);

            writer.WriteStartArray("changes");
            foreach (var action in result.Actions)
            {
                writer.WriteStartObject();
                writer.WriteString("tag", string.Create(CultureInfo.InvariantCulture,
                    $"{action.Tag.Group:X4}{action.Tag.Element:X4}"));

                var entry = dict.GetEntry(action.Tag);
                if (entry.HasValue)
                    writer.WriteString("keyword", entry.Value.Keyword);

                writer.WriteString("description", action.Description);
                if (action.OldValue != null)
                    writer.WriteString("oldValue", action.OldValue);
                if (action.NewValue != null)
                    writer.WriteString("newValue", action.NewValue);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.Flush();

        Console.Out.WriteLine();
    }

    private sealed record FileFixResult(string Path, string? OutputPath, List<FixAction> Actions);
}
