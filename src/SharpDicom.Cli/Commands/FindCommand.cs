using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Cli.Helpers;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;
using Spectre.Console;

namespace SharpDicom.Cli.Commands;

/// <summary>
/// Implements the <c>sharpdcm find</c> subcommand for querying PACS via C-FIND.
/// </summary>
internal static class FindCommand
{
    // Tags not yet in DicomTag.WellKnown - defined by hex value
    private static readonly DicomTag StudyDescription = new(0x0008, 0x1030);
    private static readonly DicomTag SeriesNumber = new(0x0020, 0x0011);
    private static readonly DicomTag SeriesDescription = new(0x0008, 0x103E);
    private static readonly DicomTag NumberOfSeriesRelatedInstances = new(0x0020, 0x1209);
    private static readonly DicomTag InstanceNumber = new(0x0020, 0x0013);

    /// <summary>
    /// Creates the <c>find</c> command with all options and handler.
    /// </summary>
    public static Command Create()
    {
        var command = new Command("find", "Query a PACS server for studies/series/instances (C-FIND)");

        // Query options
        var levelOption = new Option<string>("--level", "-l")
        {
            Description = "Query level: patient, study, series, instance",
            DefaultValueFactory = _ => "study",
        };

        var patientNameOption = new Option<string?>("--patient-name", "-n")
        {
            Description = "Patient name filter (supports wildcards: Smith*)",
        };

        var patientIdOption = new Option<string?>("--patient-id")
        {
            Description = "Patient ID filter",
        };

        var accessionOption = new Option<string?>("--accession")
        {
            Description = "Accession number filter",
        };

        var modalityOption = new Option<string?>("--modality", "-m")
        {
            Description = "Modality filter (CT, MR, US, etc.)",
        };

        var studyDateOption = new Option<string?>("--study-date")
        {
            Description = "Study date filter (YYYYMMDD or YYYYMMDD-YYYYMMDD range)",
        };

        var studyDescOption = new Option<string?>("--study-description")
        {
            Description = "Study description filter",
        };

        var returnFieldOption = new Option<string[]>("--return-field", "-r")
        {
            Description = "Additional return fields by keyword or tag (e.g., PatientBirthDate or 00100030)",
            AllowMultipleArgumentsPerToken = true,
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Output format: text, json, csv",
            DefaultValueFactory = _ => "text",
        };

        var limitOption = new Option<int?>("--limit")
        {
            Description = "Maximum number of results to return",
        };

        // PACS connection options
        var hostOption = new Option<string?>("--host")
        {
            Description = "PACS host name or IP address",
        };

        var portOption = new Option<int>("--port")
        {
            Description = "PACS port",
            DefaultValueFactory = _ => 104,
        };

        var calledAeOption = new Option<string?>("--called-ae")
        {
            Description = "Called Application Entity title",
        };

        var callingAeOption = new Option<string?>("--calling-ae")
        {
            Description = "Calling Application Entity title",
            DefaultValueFactory = _ => "SHARPDCM",
        };

        var connectionOption = new Option<string?>("--connection")
        {
            Description = "Connection string (pacs://AET@host:port)",
        };

        var profileOption = new Option<string?>("--profile")
        {
            Description = "Named PACS connection profile from config",
        };

        var tlsOption = new Option<bool>("--tls")
        {
            Description = "Use TLS for the connection",
        };

        command.Options.Add(levelOption);
        command.Options.Add(patientNameOption);
        command.Options.Add(patientIdOption);
        command.Options.Add(accessionOption);
        command.Options.Add(modalityOption);
        command.Options.Add(studyDateOption);
        command.Options.Add(studyDescOption);
        command.Options.Add(returnFieldOption);
        command.Options.Add(formatOption);
        command.Options.Add(limitOption);
        command.Options.Add(hostOption);
        command.Options.Add(portOption);
        command.Options.Add(calledAeOption);
        command.Options.Add(callingAeOption);
        command.Options.Add(connectionOption);
        command.Options.Add(profileOption);
        command.Options.Add(tlsOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var level = parseResult.GetValue(levelOption)!;
            var patientName = parseResult.GetValue(patientNameOption);
            var patientId = parseResult.GetValue(patientIdOption);
            var accession = parseResult.GetValue(accessionOption);
            var modality = parseResult.GetValue(modalityOption);
            var studyDate = parseResult.GetValue(studyDateOption);
            var studyDesc = parseResult.GetValue(studyDescOption);
            var returnFields = parseResult.GetValue(returnFieldOption);
            var format = parseResult.GetValue(formatOption)!;
            var limit = parseResult.GetValue(limitOption);

            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var calledAe = parseResult.GetValue(calledAeOption);
            var callingAe = parseResult.GetValue(callingAeOption)!;
            var connection = parseResult.GetValue(connectionOption);
            var profile = parseResult.GetValue(profileOption);
            var tls = parseResult.GetValue(tlsOption);

            // Resolve PACS connection
            var resolved = PacsConnectionResolver.Resolve(host, port, calledAe, callingAe, connection, profile);
            if (resolved == null)
            {
                Console.Error.WriteLine("Error: No PACS connection specified. Use --host/--port/--called-ae, --connection, or --profile.");
                return ExitCodes.UsageError;
            }

            var (rHost, rPort, rCalledAe, rCallingAe) = resolved.Value;

            // Parse query level
            var qrLevel = level.ToLowerInvariant() switch
            {
                "patient" => QueryRetrieveLevel.Patient,
                "study" => QueryRetrieveLevel.Study,
                "series" => QueryRetrieveLevel.Series,
                "instance" or "image" => QueryRetrieveLevel.Image,
                _ => (QueryRetrieveLevel?)null,
            };

            if (qrLevel == null)
            {
                Console.Error.WriteLine($"Error: Invalid query level '{level}'. Use: patient, study, series, instance.");
                return ExitCodes.UsageError;
            }

            // Build query
            var query = qrLevel.Value switch
            {
                QueryRetrieveLevel.Patient => DicomQuery.ForPatients(),
                QueryRetrieveLevel.Study => DicomQuery.ForStudies(),
                QueryRetrieveLevel.Series => DicomQuery.ForSeries(),
                QueryRetrieveLevel.Image => DicomQuery.ForImages(),
                _ => DicomQuery.ForStudies(),
            };

            if (patientName != null)
                query = query.WithPatientName(patientName);
            if (patientId != null)
                query = query.WithPatientId(patientId);
            if (accession != null)
                query = query.WithAccessionNumber(accession);
            if (modality != null)
                query = query.WithModality(modality);
            if (studyDate != null)
                AddStringFilter(query, DicomTag.StudyDate, studyDate, DicomVR.DA);
            if (studyDesc != null)
                AddStringFilter(query, StudyDescription, studyDesc, DicomVR.LO);

            // Determine return fields
            var fieldTags = new List<DicomTag>();

            if (returnFields != null && returnFields.Length > 0)
            {
                foreach (var field in returnFields)
                {
                    var tag = ResolveTag(field);
                    if (tag == null)
                    {
                        Console.Error.WriteLine($"Error: Unknown tag or keyword '{field}'.");
                        return ExitCodes.UsageError;
                    }
                    fieldTags.Add(tag.Value);
                    query = query.ReturnField(tag.Value);
                }
            }
            else
            {
                // Default return fields per level
                fieldTags = GetDefaultReturnFields(qrLevel.Value);
                foreach (var tag in fieldTags)
                {
                    query = query.ReturnField(tag);
                }
            }

            // Execute query
            try
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = rHost,
                    Port = rPort,
                    CalledAE = rCalledAe,
                    CallingAE = rCallingAe,
                };

                // Determine SOP Class UID for the presentation context
                var sopClassUid = qrLevel.Value.GetPatientRootFindSopClassUid();
                var contexts = new[]
                {
                    new PresentationContext(1, sopClassUid,
                        TransferSyntax.ImplicitVRLittleEndian,
                        TransferSyntax.ExplicitVRLittleEndian),
                };

                await using var client = new DicomClient(clientOptions);
                await client.ConnectAsync(contexts, ct);

                var findScu = new CFindScu(client);
                var results = new List<DicomDataset>();
                int count = 0;

                await foreach (var result in findScu.QueryAsync(query, ct))
                {
                    results.Add(result);
                    count++;
                    if (limit.HasValue && count >= limit.Value)
                        break;
                }

                // Format and output results
                FormatOutput(results, fieldTags, format, Console.Out);

                Console.Error.WriteLine(
                    FormattableString.Invariant($"Found {count} result{(count == 1 ? "" : "s")}"));
                return ExitCodes.Success;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return ExitCodes.RuntimeError;
            }
        });

        return command;
    }

    /// <summary>
    /// Resolves a tag from keyword or hex string.
    /// </summary>
    private static DicomTag? ResolveTag(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        // Try hex formats: GGGGEEEE or GGGG,EEEE
        if (input.Length == 8 && IsHexString(input))
        {
            if (DicomTag.TryParse(input, out var tag))
                return tag;
        }

        if (input.Length >= 9 && input.Contains(','))
        {
            // Format: GGGG,EEEE (possibly with parens)
            var normalized = input.Replace("(", "").Replace(")", "");
            if (DicomTag.TryParse($"({normalized})", out var tag))
                return tag;
        }

        // Try keyword lookup (case-insensitive)
        var entry = DicomDictionary.Default.GetEntryByKeyword(input);
        if (entry.HasValue)
            return entry.Value.Tag;

        return null;
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns default return fields for the specified query level.
    /// </summary>
    private static List<DicomTag> GetDefaultReturnFields(QueryRetrieveLevel level)
    {
        return level switch
        {
            QueryRetrieveLevel.Patient => new List<DicomTag>
            {
                DicomTag.PatientName,
                DicomTag.PatientID,
                DicomTag.PatientBirthDate,
                DicomTag.PatientSex,
            },
            QueryRetrieveLevel.Study => new List<DicomTag>
            {
                DicomTag.PatientName,
                DicomTag.PatientID,
                DicomTag.StudyDate,
                StudyDescription,
                DicomTag.ModalitiesInStudy,
                DicomTag.AccessionNumber,
                DicomTag.StudyInstanceUID,
            },
            QueryRetrieveLevel.Series => new List<DicomTag>
            {
                SeriesNumber,
                SeriesDescription,
                DicomTag.Modality,
                NumberOfSeriesRelatedInstances,
                DicomTag.SeriesInstanceUID,
            },
            QueryRetrieveLevel.Image => new List<DicomTag>
            {
                DicomTag.SOPClassUID,
                DicomTag.SOPInstanceUID,
                InstanceNumber,
            },
            _ => new List<DicomTag>(),
        };
    }

    /// <summary>
    /// Adds a raw string filter directly to the query's underlying dataset.
    /// </summary>
    private static void AddStringFilter(DicomQuery query, DicomTag tag, string value, DicomVR vr)
    {
        var dataset = query.ToDataset();
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length % 2 != 0)
        {
            var padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, padded, bytes.Length);
            padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)'\0' : (byte)' ';
            bytes = padded;
        }
        dataset.Add(new DicomStringElement(tag, vr, bytes));
    }

    /// <summary>
    /// Formats and writes results to the provided writer.
    /// </summary>
    private static void FormatOutput(
        List<DicomDataset> results,
        List<DicomTag> fieldTags,
        string format,
        TextWriter writer)
    {
        if (results.Count == 0)
            return;

        switch (format.ToLowerInvariant())
        {
            case "json":
                FormatJson(results, fieldTags, writer);
                break;
            case "csv":
                FormatCsv(results, fieldTags, writer);
                break;
            case "text":
            default:
                FormatText(results, fieldTags);
                break;
        }
    }

    /// <summary>
    /// Formats results as a Spectre.Console table written to stdout.
    /// </summary>
    private static void FormatText(List<DicomDataset> results, List<DicomTag> fieldTags)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);

        // Add column headers from tag keywords
        foreach (var tag in fieldTags)
        {
            var entry = DicomDictionary.Default.GetEntry(tag);
            var header = entry?.Keyword ?? tag.ToString();
            table.AddColumn(new TableColumn(Markup.Escape(header)));
        }

        // Add data rows
        foreach (var dataset in results)
        {
            var cells = new List<string>();
            foreach (var tag in fieldTags)
            {
                var value = GetStringValue(dataset, tag);
                cells.Add(Markup.Escape(value));
            }
            table.AddRow(cells.ToArray());
        }

        // Write to stdout via AnsiConsole
        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Formats results as a JSON array.
    /// </summary>
    private static void FormatJson(
        List<DicomDataset> results,
        List<DicomTag> fieldTags,
        TextWriter writer)
    {
        var options = new JsonWriterOptions { Indented = true };
        using var stream = new MemoryStream();
        using var jsonWriter = new Utf8JsonWriter(stream, options);

        jsonWriter.WriteStartArray();

        foreach (var dataset in results)
        {
            jsonWriter.WriteStartObject();
            foreach (var tag in fieldTags)
            {
                var entry = DicomDictionary.Default.GetEntry(tag);
                var key = entry?.Keyword ?? tag.ToString();
                var value = GetStringValue(dataset, tag);
                jsonWriter.WriteString(key, value);
            }
            jsonWriter.WriteEndObject();
        }

        jsonWriter.WriteEndArray();
        jsonWriter.Flush();

        writer.Write(Encoding.UTF8.GetString(stream.ToArray()));
        writer.WriteLine();
    }

    /// <summary>
    /// Formats results as RFC 4180 CSV.
    /// </summary>
    private static void FormatCsv(
        List<DicomDataset> results,
        List<DicomTag> fieldTags,
        TextWriter writer)
    {
        // Header row
        var headers = new List<string>();
        foreach (var tag in fieldTags)
        {
            var entry = DicomDictionary.Default.GetEntry(tag);
            headers.Add(entry?.Keyword ?? tag.ToString());
        }
        writer.WriteLine(string.Join(",", headers));

        // Data rows
        foreach (var dataset in results)
        {
            var cells = new List<string>();
            foreach (var tag in fieldTags)
            {
                var value = GetStringValue(dataset, tag);
                cells.Add(CsvEscape(value));
            }
            writer.WriteLine(string.Join(",", cells));
        }
    }

    /// <summary>
    /// RFC 4180 CSV escaping: double-quote values containing commas, quotes, or newlines.
    /// </summary>
    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    /// <summary>
    /// Gets the string value for a tag from a dataset.
    /// </summary>
    private static string GetStringValue(DicomDataset dataset, DicomTag tag)
    {
        var element = dataset[tag];
        if (element == null)
            return "";

        try
        {
            return dataset.GetString(tag)?.Trim('\0', ' ') ?? "";
        }
        catch
        {
            // Fall back to byte-length display if string conversion fails
            return element.RawValue.Length > 0
                ? FormattableString.Invariant($"[{element.RawValue.Length} bytes]")
                : "";
        }
    }
}
