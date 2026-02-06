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
using SharpDicom.Cli.Helpers;
using SharpDicom.Data;
using SharpDicom.IO;
using SharpDicom.Validation;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm lint</c> subcommand for validating DICOM files.
/// </summary>
internal static class LintCommand
{
    /// <summary>
    /// Creates the <c>lint</c> command with all arguments and options.
    /// </summary>
    public static Command Create()
    {
        var filesArg = new Argument<FileSystemInfo[]>("files")
        {
            Description = "DICOM files or directories to validate",
            Arity = ArgumentArity.OneOrMore,
        };

        var profileOption = new Option<string>("--profile", "-p")
        {
            Description = "Validation profile: strict, lenient, permissive",
            DefaultValueFactory = _ => "strict",
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Output format: text, json",
            DefaultValueFactory = _ => "text",
        };

        var continueOnErrorOption = new Option<bool>("--continue-on-error")
        {
            Description = "Continue validating remaining files after errors",
        };

        var severityOption = new Option<string?>("--severity")
        {
            Description = "Minimum severity to report: error, warning, info",
        };

        var command = new Command("lint", "Validate DICOM files against the standard");
        command.Arguments.Add(filesArg);
        command.Options.Add(profileOption);
        command.Options.Add(formatOption);
        command.Options.Add(continueOnErrorOption);
        command.Options.Add(severityOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var files = parseResult.GetValue(filesArg)!;
            var profileName = parseResult.GetValue(profileOption)!;
            var format = parseResult.GetValue(formatOption)!;
            var continueOnError = parseResult.GetValue(continueOnErrorOption);
            var severityFilter = parseResult.GetValue(severityOption);

            return await RunAsync(files, profileName, format, continueOnError, severityFilter, ct)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<int> RunAsync(
        FileSystemInfo[] inputs,
        string profileName,
        string format,
        bool continueOnError,
        string? severityFilter,
        CancellationToken ct)
    {
        var profile = profileName.ToLowerInvariant() switch
        {
            "strict" => ValidationProfile.Strict,
            "lenient" => ValidationProfile.Lenient,
            "permissive" => ValidationProfile.Permissive,
            _ => throw new ArgumentException($"Unknown profile: {profileName}"),
        };

        ValidationSeverity? minSeverity = severityFilter?.ToLowerInvariant() switch
        {
            "error" => ValidationSeverity.Error,
            "warning" => ValidationSeverity.Warning,
            "info" => ValidationSeverity.Info,
            null => null,
            _ => throw new ArgumentException($"Unknown severity: {severityFilter}"),
        };

        var readerOptions = new DicomReaderOptions
        {
            ValidationProfile = profile,
            CollectValidationIssues = true,
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

        var fileResults = new List<FileResult>();
        int filesChecked = 0;
        int filesValid = 0;
        int filesInvalid = 0;
        int totalErrors = 0;
        int totalWarnings = 0;
        int totalInfos = 0;
        bool hasRuntimeError = false;

        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            filesChecked++;

            try
            {
                var file = await DicomFile.OpenAsync(path, readerOptions, ct).ConfigureAwait(false);
                var result = file.ValidationResult ?? new ValidationResult();

                var issues = result.Issues.AsEnumerable();
                if (minSeverity.HasValue)
                {
                    issues = issues.Where(i => i.Severity >= minSeverity.Value);
                }

                var filteredIssues = issues.ToList();

                var hasErrors = filteredIssues.Any(i => i.Severity == ValidationSeverity.Error);
                if (hasErrors)
                    filesInvalid++;
                else
                    filesValid++;

                totalErrors += filteredIssues.Count(i => i.Severity == ValidationSeverity.Error);
                totalWarnings += filteredIssues.Count(i => i.Severity == ValidationSeverity.Warning);
                totalInfos += filteredIssues.Count(i => i.Severity == ValidationSeverity.Info);

                fileResults.Add(new FileResult(path, !hasErrors, filteredIssues));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                hasRuntimeError = true;
                filesInvalid++;

                var errorIssue = new ValidationIssue(
                    "DICOM-000",
                    ValidationSeverity.Error,
                    null, null, null, null,
                    $"Failed to read file: {ex.Message}",
                    null, default);

                totalErrors++;
                fileResults.Add(new FileResult(path, false, new List<ValidationIssue> { errorIssue }));

                if (!continueOnError)
                {
                    OutputResults(format, fileResults, filesChecked, filesValid, filesInvalid,
                        totalErrors, totalWarnings, totalInfos);
                    return ExitCodes.RuntimeError;
                }
            }
        }

        if (filesChecked == 0)
        {
            Console.Error.WriteLine("No DICOM files found.");
            return ExitCodes.RuntimeError;
        }

        OutputResults(format, fileResults, filesChecked, filesValid, filesInvalid,
            totalErrors, totalWarnings, totalInfos);

        if (hasRuntimeError)
            return ExitCodes.RuntimeError;

        return totalErrors > 0 ? ExitCodes.ValidationError : ExitCodes.Success;
    }

    private static void OutputResults(
        string format,
        List<FileResult> fileResults,
        int filesChecked,
        int filesValid,
        int filesInvalid,
        int totalErrors,
        int totalWarnings,
        int totalInfos)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            OutputJson(fileResults, filesChecked, filesValid, filesInvalid,
                totalErrors, totalWarnings, totalInfos);
        }
        else
        {
            OutputText(fileResults);
        }

        // Summary always to stderr
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Checked {filesChecked} files: {filesValid} valid, {filesInvalid} invalid ({totalErrors} errors, {totalWarnings} warnings)"));
    }

    private static void OutputText(List<FileResult> fileResults)
    {
        bool isTty = !Console.IsOutputRedirected;
        var dict = DicomDictionary.Default;

        foreach (var fileResult in fileResults)
        {
            if (fileResult.Issues.Count == 0)
                continue;

            Console.Out.WriteLine($"{fileResult.Path}:");

            foreach (var issue in fileResult.Issues)
            {
                var severityStr = issue.Severity switch
                {
                    ValidationSeverity.Error => isTty ? "\u001b[31mERROR\u001b[0m" : "ERROR",
                    ValidationSeverity.Warning => isTty ? "\u001b[33mWARN \u001b[0m" : "WARN ",
                    ValidationSeverity.Info => isTty ? "\u001b[34mINFO \u001b[0m" : "INFO ",
                    _ => "     ",
                };

                var tagStr = "";
                var keywordStr = "";
                if (issue.Tag.HasValue)
                {
                    tagStr = $" {issue.Tag.Value}";
                    var entry = dict.GetEntry(issue.Tag.Value);
                    if (entry.HasValue)
                        keywordStr = $" {entry.Value.Keyword}:";
                }

                Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {severityStr}  {issue.Code}{tagStr}{keywordStr} {issue.Message}"));
            }
        }
    }

    private static void OutputJson(
        List<FileResult> fileResults,
        int filesChecked,
        int filesValid,
        int filesInvalid,
        int totalErrors,
        int totalWarnings,
        int totalInfos)
    {
        var dict = DicomDictionary.Default;

        using var stream = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteStartArray("files");
        foreach (var fileResult in fileResults)
        {
            writer.WriteStartObject();
            writer.WriteString("path", fileResult.Path);
            writer.WriteBoolean("valid", fileResult.Valid);

            writer.WriteStartArray("issues");
            foreach (var issue in fileResult.Issues)
            {
                writer.WriteStartObject();
                writer.WriteString("severity", issue.Severity.ToString().ToLowerInvariant());
                writer.WriteString("code", issue.Code);
                if (issue.Tag.HasValue)
                {
                    writer.WriteString("tag", string.Create(CultureInfo.InvariantCulture,
                        $"{issue.Tag.Value.Group:X4}{issue.Tag.Value.Element:X4}"));
                    var entry = dict.GetEntry(issue.Tag.Value);
                    if (entry.HasValue)
                        writer.WriteString("keyword", entry.Value.Keyword);
                }
                writer.WriteString("message", issue.Message);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartObject("summary");
        writer.WriteNumber("filesChecked", filesChecked);
        writer.WriteNumber("filesValid", filesValid);
        writer.WriteNumber("filesInvalid", filesInvalid);
        writer.WriteNumber("totalErrors", totalErrors);
        writer.WriteNumber("totalWarnings", totalWarnings);
        writer.WriteNumber("totalInfos", totalInfos);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        // Ensure newline after JSON
        Console.Out.WriteLine();
    }

    private sealed record FileResult(string Path, bool Valid, List<ValidationIssue> Issues);
}
