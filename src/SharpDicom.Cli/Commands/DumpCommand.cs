using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Configuration;
using SharpDicom.Cli.Helpers;
using SharpDicom.Cli.Output;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm dump</c> subcommand for displaying DICOM file contents.
/// </summary>
internal static class DumpCommand
{
    /// <summary>
    /// Creates the <c>dump</c> command with all arguments and options.
    /// </summary>
    public static Command Create()
    {
        var filesArg = new Argument<FileSystemInfo[]>("files")
        {
            Description = "DICOM files or directories to dump",
            Arity = ArgumentArity.OneOrMore,
        };

        var formatOption = new Option<string?>("--format", "-f")
        {
            Description = "Output format: text, json, xml",
        };

        var maxDepthOption = new Option<int?>("--max-depth", "-d")
        {
            Description = "Maximum sequence nesting depth to display",
        };

        var noPrivateOption = new Option<bool>("--no-private")
        {
            Description = "Hide private tags",
        };

        var noPixelOption = new Option<bool>("--no-pixel")
        {
            Description = "Hide pixel data element",
        };

        var tagFilterOption = new Option<string?>("--tag-filter", "-t")
        {
            Description = "Only show specific tag (e.g., 00100010)",
        };

        var command = new Command("dump", "Display DICOM file contents");
        command.Arguments.Add(filesArg);
        command.Options.Add(formatOption);
        command.Options.Add(maxDepthOption);
        command.Options.Add(noPrivateOption);
        command.Options.Add(noPixelOption);
        command.Options.Add(tagFilterOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var files = parseResult.GetValue(filesArg)!;
            var formatFlag = parseResult.GetValue(formatOption);
            var maxDepth = parseResult.GetValue(maxDepthOption);
            var noPrivate = parseResult.GetValue(noPrivateOption);
            var noPixel = parseResult.GetValue(noPixelOption);
            var tagFilter = parseResult.GetValue(tagFilterOption);
            var continueOnError = parseResult.GetValue<bool>("--continue-on-error");
            var noColor = parseResult.GetValue<bool>("--no-color");

            // Load config for defaults
            var configPath = parseResult.GetValue<string?>("--config");
            var config = ConfigLoader.Load(configPath);
            config = ConfigLoader.ApplyEnvironmentVariables(config);

            // Resolve format: flag > env var > config > "text"
            var format = formatFlag
                ?? Environment.GetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT")
                ?? config.OutputFormat
                ?? "text";

            var useColor = !noColor && config.Color;

            // Create formatter
            IOutputFormatter formatter = format.ToUpperInvariant() switch
            {
                "JSON" => new JsonFormatter(),
                "XML" => new XmlFormatter(),
                _ => new TextFormatter(useColor),
            };

            // Parse tag filter
            DicomTag? filterTag = null;
            if (!string.IsNullOrEmpty(tagFilter))
            {
                filterTag = ParseTagFilter(tagFilter);
                if (filterTag == null)
                {
                    Console.Error.WriteLine($"Error: Invalid tag filter '{tagFilter}'. Expected format: GGGGEEEE (e.g., 00100010)");
                    return ExitCodes.UsageError;
                }
            }

            // Enumerate files
            string[] filePaths;
            try
            {
                filePaths = CollectFilePaths(files);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return ExitCodes.RuntimeError;
            }

            if (filePaths.Length == 0)
            {
                Console.Error.WriteLine("Error: No DICOM files found.");
                return ExitCodes.RuntimeError;
            }

            var dictionary = DicomDictionary.Default;
            var stdout = Console.Out;
            var hasErrors = false;
            var isBatch = filePaths.Length > 1;

            if (isBatch)
                formatter.WriteBatchHeader(stdout);

            for (var i = 0; i < filePaths.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var path = filePaths[i];

                DicomFile dicomFile;
                try
                {
                    dicomFile = await DicomFile.OpenAsync(path, ct: ct).ConfigureAwait(false);
                }
                catch (DicomFileException ex)
                {
                    Console.Error.WriteLine($"Error: {path}: {ex.Message}");
                    hasErrors = true;
                    if (continueOnError) continue;
                    if (isBatch) formatter.WriteBatchFooter(stdout);
                    return ExitCodes.RuntimeError;
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Error: {path}: {ex.Message}");
                    hasErrors = true;
                    if (continueOnError) continue;
                    if (isBatch) formatter.WriteBatchFooter(stdout);
                    return ExitCodes.RuntimeError;
                }

                formatter.WriteFileHeader(path, stdout);
                WriteDataset(dicomFile.Dataset, formatter, dictionary, 0, maxDepth, noPrivate, noPixel, filterTag, stdout);
                formatter.WriteFileFooter(stdout);

                // Progress on stderr for multi-file runs
                if (isBatch)
                    Console.Error.WriteLine($"  {i + 1}/{filePaths.Length}: {Path.GetFileName(path)}");
            }

            if (isBatch)
                formatter.WriteBatchFooter(stdout);

            // Dispose formatter if it implements IDisposable
            if (formatter is IDisposable disposable)
                disposable.Dispose();

            return hasErrors ? ExitCodes.RuntimeError : ExitCodes.Success;
        });

        return command;
    }

    /// <summary>
    /// Recursively writes dataset elements to the formatter, handling sequences and filters.
    /// </summary>
    private static void WriteDataset(
        DicomDataset dataset,
        IOutputFormatter formatter,
        DicomDictionary dictionary,
        int depth,
        int? maxDepth,
        bool noPrivate,
        bool noPixel,
        DicomTag? filterTag,
        TextWriter output)
    {
        foreach (var element in dataset)
        {
            // Apply filters
            if (noPrivate && element.Tag.IsPrivate)
                continue;

            if (noPixel && element.Tag == DicomTag.PixelData)
                continue;

            if (filterTag.HasValue && element.Tag != filterTag.Value && element is not DicomSequence)
            {
                // Non-sequence elements that don't match the filter are skipped.
                // Sequences are always traversed so nested matching tags can be found.
                continue;
            }

            if (element is DicomSequence seq)
            {
                if (maxDepth.HasValue && depth >= maxDepth.Value)
                {
                    // Depth limit reached - show indicator
                    var entry = dictionary.GetEntry(seq.Tag);
                    var keyword = entry?.Keyword ?? "Unknown";
                    formatter.WriteSequenceStart(seq.Tag, keyword, depth, output);
                    // Write a text indicator (the formatters handle this gracefully via item start/end)
                    output.Write(new string(' ', (depth + 1) * 2));
                    // Not ideal to write directly but the depth limit indicator is informational
                    formatter.WriteSequenceEnd(depth, output);
                    continue;
                }

                var seqEntry = dictionary.GetEntry(seq.Tag);
                var seqKeyword = seqEntry?.Keyword ?? "Unknown";
                formatter.WriteSequenceStart(seq.Tag, seqKeyword, depth, output);

                for (var itemIdx = 0; itemIdx < seq.Items.Count; itemIdx++)
                {
                    formatter.WriteSequenceItemStart(itemIdx, depth + 1, output);
                    WriteDataset(seq.Items[itemIdx], formatter, dictionary, depth + 1, maxDepth, noPrivate, noPixel, filterTag: null, output);
                    formatter.WriteSequenceItemEnd(depth + 1, output);
                }

                formatter.WriteSequenceEnd(depth, output);
            }
            else
            {
                formatter.WriteElement(element, dictionary, dataset, depth, output);
            }
        }
    }

    /// <summary>
    /// Collects all file paths from the given inputs, expanding directories.
    /// </summary>
    private static string[] CollectFilePaths(FileSystemInfo[] inputs)
    {
        var paths = new System.Collections.Generic.List<string>();
        foreach (var path in FileEnumerator.EnumerateFiles(inputs, recursive: true, allFiles: false))
        {
            paths.Add(path);
        }
        return paths.ToArray();
    }

    /// <summary>
    /// Parses a tag filter string like "00100010" into a <see cref="DicomTag"/>.
    /// </summary>
    private static DicomTag? ParseTagFilter(string filter)
    {
        // Accept formats: GGGGEEEE or GGGG,EEEE or (GGGG,EEEE)
        var cleaned = filter.Replace("(", "", StringComparison.Ordinal)
                           .Replace(")", "", StringComparison.Ordinal)
                           .Replace(",", "", StringComparison.Ordinal)
                           .Trim();

        if (cleaned.Length != 8)
            return null;

        if (!ushort.TryParse(cleaned.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var group))
            return null;

        if (!ushort.TryParse(cleaned.AsSpan(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var element))
            return null;

        return new DicomTag(group, element);
    }
}
