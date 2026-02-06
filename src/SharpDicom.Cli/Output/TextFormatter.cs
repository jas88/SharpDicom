using System;
using System.IO;
using SharpDicom.Data;
using Spectre.Console;

namespace SharpDicom.Cli.Output;

/// <summary>
/// Renders DICOM elements in dcmdump-style text:
/// <c>(GGGG,EEEE) VR [value] # keyword</c>
/// </summary>
internal sealed class TextFormatter : IOutputFormatter
{
    private readonly bool _useColor;

    /// <inheritdoc />
    public string FormatName => "text";

    /// <summary>
    /// Creates a new <see cref="TextFormatter"/>.
    /// </summary>
    /// <param name="useColor">
    /// When <c>true</c> and stdout is a TTY, emit Spectre.Console markup for colour.
    /// </param>
    public TextFormatter(bool useColor = true)
    {
        _useColor = useColor && AnsiConsole.Profile.Capabilities.Ansi;
    }

    /// <inheritdoc />
    public void WriteBatchHeader(TextWriter output) { }

    /// <inheritdoc />
    public void WriteBatchFooter(TextWriter output) { }

    /// <inheritdoc />
    public void WriteFileHeader(string filePath, TextWriter output)
    {
        output.WriteLine();
        if (_useColor)
            output.WriteLine($"\x1b[1m# {filePath}\x1b[0m");
        else
            output.WriteLine($"# {filePath}");
        output.WriteLine();
    }

    /// <inheritdoc />
    public void WriteFileFooter(TextWriter output) { }

    /// <inheritdoc />
    public void WriteElement(IDicomElement element, DicomDictionary dictionary, DicomDataset dataset, int depth, TextWriter output)
    {
        var indent = new string('>', depth);
        if (depth > 0) indent += " ";

        var tag = element.Tag;
        var vr = element.VR;
        var entry = dictionary.GetEntry(tag);
        var keyword = entry?.Keyword ?? "Unknown";

        var tagStr = $"({tag.Group:X4},{tag.Element:X4})";
        var vrStr = vr.ToString();
        string valueStr;

        // Pixel data: show length only
        if (tag == DicomTag.PixelData)
        {
            valueStr = $"(pixel data, length={element.Length})";
        }
        else if (element is DicomStringElement se)
        {
            var val = se.GetString(dataset.Encoding) ?? string.Empty;
            // Known UIDs displayed as =Name
            if (vr == DicomVR.UI && entry != null)
            {
                var uidName = LookupUidName(val);
                if (uidName != null)
                    val = $"={uidName}";
            }
            valueStr = $"[{val}]";
        }
        else if (element is DicomSequence)
        {
            // Handled via WriteSequenceStart
            return;
        }
        else
        {
            valueStr = $"({element.Length} bytes)";
        }

        // Private tag vendor annotation
        var vendorSuffix = string.Empty;
        if (tag.IsPrivate && !tag.IsPrivateCreator)
        {
            var creator = dataset.PrivateCreators.GetCreator(tag);
            if (creator != null)
            {
                var info = VendorDictionary.GetInfo(creator, tag.Element);
                if (info != null)
                    vendorSuffix = $" ({info.Value.Name})";
            }
        }

        if (_useColor)
        {
            // Tag in cyan, VR in yellow, keyword in green
            output.WriteLine($"{indent}\x1b[36m{tagStr}\x1b[0m \x1b[33m{vrStr}\x1b[0m {valueStr} \x1b[32m# {keyword}\x1b[0m{vendorSuffix}");
        }
        else
        {
            output.WriteLine($"{indent}{tagStr} {vrStr} {valueStr} # {keyword}{vendorSuffix}");
        }
    }

    /// <inheritdoc />
    public void WriteSequenceStart(DicomTag tag, string keyword, int depth, TextWriter output)
    {
        var indent = new string('>', depth);
        if (depth > 0) indent += " ";
        var tagStr = $"({tag.Group:X4},{tag.Element:X4})";

        if (_useColor)
            output.WriteLine($"{indent}\x1b[36m{tagStr}\x1b[0m \x1b[33mSQ\x1b[0m \x1b[32m# {keyword}\x1b[0m");
        else
            output.WriteLine($"{indent}{tagStr} SQ # {keyword}");
    }

    /// <inheritdoc />
    public void WriteSequenceItemStart(int itemIndex, int depth, TextWriter output)
    {
        var indent = new string('>', depth);
        if (depth > 0) indent += " ";
        output.WriteLine($"{indent}(FFFE,E000) na (Item #{itemIndex})");
    }

    /// <inheritdoc />
    public void WriteSequenceItemEnd(int depth, TextWriter output)
    {
        var indent = new string('>', depth);
        if (depth > 0) indent += " ";
        output.WriteLine($"{indent}(FFFE,E00D) na (ItemDelimitationItem)");
    }

    /// <inheritdoc />
    public void WriteSequenceEnd(int depth, TextWriter output)
    {
        var indent = new string('>', depth);
        if (depth > 0) indent += " ";
        output.WriteLine($"{indent}(FFFE,E0DD) na (SequenceDelimitationItem)");
    }

    /// <summary>
    /// Reverse-lookup a UID value to its well-known name using the generated <see cref="DicomUIDs"/> fields.
    /// </summary>
    private static string? LookupUidName(string uidValue)
    {
        // Walk the static fields of DicomUIDs to find a matching value.
        // This is intentionally reflection-based at this layer; a compiled lookup
        // can be added if profiling shows this is a bottleneck.
        var fields = typeof(DicomUIDs).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var field in fields)
        {
            if (field.FieldType != typeof(DicomUID))
                continue;

            var uid = (DicomUID)field.GetValue(null)!;
            if (uid.ToString() == uidValue)
                return field.Name;
        }

        return null;
    }
}
