using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpDicom.Data;

/// <summary>
/// DICOM sequence element (SQ VR) containing nested datasets.
/// </summary>
public sealed class DicomSequence : IDicomElement
{
    /// <inheritdoc />
    public DicomTag Tag { get; }

    /// <inheritdoc />
    public DicomVR VR => DicomVR.SQ;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawValue => ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc />
    public int Length => -1;  // Undefined length for sequences

    /// <inheritdoc />
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Gets the nested datasets in this sequence.
    /// </summary>
    public IReadOnlyList<DicomDataset> Items { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomSequence"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="items">The nested datasets.</param>
    public DicomSequence(DicomTag tag, IEnumerable<DicomDataset> items)
    {
        Tag = tag;
        Items = items.ToList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomSequence"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="items">The nested datasets.</param>
    public DicomSequence(DicomTag tag, params DicomDataset[] items)
    {
        Tag = tag;
        Items = items.ToList();
    }

    /// <inheritdoc />
    public IDicomElement ToOwned() =>
        new DicomSequence(Tag, Items.Select(ds => ds.ToOwned()));
}
