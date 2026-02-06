using System;
using System.IO;
using System.Text.Json;
using SharpDicom.Data;

namespace SharpDicom.Cli.Output;

/// <summary>
/// Renders DICOM elements as structured JSON using <see cref="Utf8JsonWriter"/>.
/// </summary>
internal sealed class JsonFormatter : IOutputFormatter, IDisposable
{
    private Utf8JsonWriter? _writer;
    private MemoryStream? _ms;
    private bool _firstFile;

    /// <inheritdoc />
    public string FormatName => "json";

    /// <inheritdoc />
    public void WriteBatchHeader(TextWriter output)
    {
        output.Write('[');
        _firstFile = true;
    }

    /// <inheritdoc />
    public void WriteBatchFooter(TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(']');
    }

    /// <inheritdoc />
    public void WriteFileHeader(string filePath, TextWriter output)
    {
        if (!_firstFile)
            output.Write(',');
        _firstFile = false;

        output.WriteLine();

        _ms = new MemoryStream();
        _writer = new Utf8JsonWriter(_ms, new JsonWriterOptions { Indented = true });
        _writer.WriteStartObject();
        _writer.WriteString("file", filePath);
        _writer.WriteStartArray("elements");
    }

    /// <inheritdoc />
    public void WriteFileFooter(TextWriter output)
    {
        if (_writer == null || _ms == null) return;

        _writer.WriteEndArray(); // elements
        _writer.WriteEndObject(); // file object
        _writer.Flush();

        _ms.Position = 0;
        using var reader = new StreamReader(_ms);
        output.Write(reader.ReadToEnd());

        _writer.Dispose();
        _ms.Dispose();
        _writer = null;
        _ms = null;
    }

    /// <inheritdoc />
    public void WriteElement(IDicomElement element, DicomDictionary dictionary, DicomDataset dataset, int depth, TextWriter output)
    {
        if (_writer == null) return;

        // Sequences are handled via WriteSequenceStart/End; skip here to avoid duplicate output
        if (element is DicomSequence)
            return;

        var tag = element.Tag;
        var vr = element.VR;
        var entry = dictionary.GetEntry(tag);
        var keyword = entry?.Keyword ?? "Unknown";

        _writer.WriteStartObject();
        _writer.WriteString("tag", $"{tag.Group:X4}{tag.Element:X4}");
        _writer.WriteString("vr", vr.ToString());
        _writer.WriteString("keyword", keyword);

        if (tag == DicomTag.PixelData)
        {
            _writer.WriteNumber("length", element.Length);
        }
        else if (element is DicomStringElement se)
        {
            var val = se.GetString(dataset.Encoding);
            _writer.WriteString("value", val);
        }
        else if (element is DicomSequence)
        {
            // Sequences are handled via WriteSequenceStart/End
        }
        else
        {
            _writer.WriteNumber("length", element.Length);
        }

        _writer.WriteEndObject();
    }

    /// <inheritdoc />
    public void WriteSequenceStart(DicomTag tag, string keyword, int depth, TextWriter output)
    {
        if (_writer == null) return;

        _writer.WriteStartObject();
        _writer.WriteString("tag", $"{tag.Group:X4}{tag.Element:X4}");
        _writer.WriteString("vr", "SQ");
        _writer.WriteString("keyword", keyword);
        _writer.WriteStartArray("items");
    }

    /// <inheritdoc />
    public void WriteSequenceItemStart(int itemIndex, int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteStartObject();
        _writer.WriteStartArray("elements");
    }

    /// <inheritdoc />
    public void WriteSequenceItemEnd(int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteEndArray(); // elements
        _writer.WriteEndObject(); // item
    }

    /// <inheritdoc />
    public void WriteSequenceEnd(int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteEndArray(); // items
        _writer.WriteEndObject(); // sequence element
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writer?.Dispose();
        _ms?.Dispose();
    }
}
