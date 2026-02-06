using System.IO;
using SharpDicom.Data;

namespace SharpDicom.Cli.Output;

/// <summary>
/// Format-agnostic abstraction for rendering DICOM elements.
/// Implementations exist for text (dcmdump-style), JSON and XML.
/// </summary>
internal interface IOutputFormatter
{
    /// <summary>Human-readable format identifier ("text", "json", "xml").</summary>
    string FormatName { get; }

    /// <summary>Write header before the first file in a multi-file batch.</summary>
    void WriteBatchHeader(TextWriter output);

    /// <summary>Write footer after the last file in a multi-file batch.</summary>
    void WriteBatchFooter(TextWriter output);

    /// <summary>Write header before a single file's elements.</summary>
    void WriteFileHeader(string filePath, TextWriter output);

    /// <summary>Write footer after a single file's elements.</summary>
    void WriteFileFooter(TextWriter output);

    /// <summary>Write a single DICOM element.</summary>
    void WriteElement(IDicomElement element, DicomDictionary dictionary, DicomDataset dataset, int depth, TextWriter output);

    /// <summary>Write the start of a DICOM sequence.</summary>
    void WriteSequenceStart(DicomTag tag, string keyword, int depth, TextWriter output);

    /// <summary>Write the start of a sequence item.</summary>
    void WriteSequenceItemStart(int itemIndex, int depth, TextWriter output);

    /// <summary>Write the end of a sequence item.</summary>
    void WriteSequenceItemEnd(int depth, TextWriter output);

    /// <summary>Write the end of a DICOM sequence.</summary>
    void WriteSequenceEnd(int depth, TextWriter output);
}
