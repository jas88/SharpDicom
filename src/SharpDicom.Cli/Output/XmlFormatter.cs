using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using SharpDicom.Data;

namespace SharpDicom.Cli.Output;

/// <summary>
/// Renders DICOM elements as XML using <see cref="XmlWriter"/> for proper escaping.
/// </summary>
internal sealed class XmlFormatter : IOutputFormatter, IDisposable
{
    private XmlWriter? _writer;
    private StringWriter? _sw;

    /// <inheritdoc />
    public string FormatName => "xml";

    /// <inheritdoc />
    public void WriteBatchHeader(TextWriter output)
    {
        output.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        output.WriteLine("<DicomFiles>");
    }

    /// <inheritdoc />
    public void WriteBatchFooter(TextWriter output)
    {
        output.WriteLine("</DicomFiles>");
    }

    /// <inheritdoc />
    public void WriteFileHeader(string filePath, TextWriter output)
    {
        _sw = new StringWriter();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
        };
        _writer = XmlWriter.Create(_sw, settings);
        _writer.WriteStartElement("DicomFile");
        _writer.WriteAttributeString("path", filePath);
    }

    /// <inheritdoc />
    public void WriteFileFooter(TextWriter output)
    {
        if (_writer == null || _sw == null) return;

        _writer.WriteEndElement(); // DicomFile
        _writer.Flush();

        output.WriteLine(_sw.ToString());

        _writer.Dispose();
        _sw.Dispose();
        _writer = null;
        _sw = null;
    }

    /// <inheritdoc />
    public void WriteElement(IDicomElement element, DicomDictionary dictionary, DicomDataset dataset, int depth, TextWriter output)
    {
        if (_writer == null) return;

        var tag = element.Tag;
        var vr = element.VR;
        var entry = dictionary.GetEntry(tag);
        var keyword = entry?.Keyword ?? "Unknown";

        _writer.WriteStartElement("Element");
        _writer.WriteAttributeString("tag", $"{tag.Group:X4}{tag.Element:X4}");
        _writer.WriteAttributeString("vr", vr.ToString());
        _writer.WriteAttributeString("keyword", keyword);

        if (tag == DicomTag.PixelData)
        {
            _writer.WriteAttributeString("length", element.Length.ToString(CultureInfo.InvariantCulture));
        }
        else if (element is DicomStringElement se)
        {
            var val = se.GetString(dataset.Encoding) ?? string.Empty;
            _writer.WriteString(val);
        }
        else if (element is DicomSequence)
        {
            // Handled by WriteSequenceStart/End
        }
        else
        {
            _writer.WriteAttributeString("length", element.Length.ToString(CultureInfo.InvariantCulture));
        }

        _writer.WriteEndElement();
    }

    /// <inheritdoc />
    public void WriteSequenceStart(DicomTag tag, string keyword, int depth, TextWriter output)
    {
        if (_writer == null) return;

        _writer.WriteStartElement("Sequence");
        _writer.WriteAttributeString("tag", $"{tag.Group:X4}{tag.Element:X4}");
        _writer.WriteAttributeString("keyword", keyword);
    }

    /// <inheritdoc />
    public void WriteSequenceItemStart(int itemIndex, int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteStartElement("Item");
    }

    /// <inheritdoc />
    public void WriteSequenceItemEnd(int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteEndElement(); // Item
    }

    /// <inheritdoc />
    public void WriteSequenceEnd(int depth, TextWriter output)
    {
        if (_writer == null) return;
        _writer.WriteEndElement(); // Sequence
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writer?.Dispose();
        _sw?.Dispose();
    }
}
