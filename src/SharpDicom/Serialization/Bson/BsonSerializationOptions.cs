using System;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Configuration options for BSON serialization of DICOM datasets.
/// </summary>
public sealed class BsonSerializationOptions
{
    /// <summary>
    /// Gets the default serialization options singleton.
    /// </summary>
    public static BsonSerializationOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the format used for tag keys in the output BSON document.
    /// Default: <see cref="BsonTagKeyFormat.Hex8"/>.
    /// </summary>
    public BsonTagKeyFormat TagKeyFormat { get; set; } = BsonTagKeyFormat.Hex8;

    /// <summary>
    /// Gets or sets the output mode.
    /// Default: <see cref="BsonOutputMode.MongoNative"/>.
    /// </summary>
    public BsonOutputMode OutputMode { get; set; } = BsonOutputMode.MongoNative;

    /// <summary>
    /// Gets or sets whether to always include the "vr" field in element sub-documents.
    /// When false (default), "vr" is only included for private, ambiguous (multi-VR),
    /// or retired tags.
    /// </summary>
    public bool AlwaysIncludeVR { get; set; }

    /// <summary>
    /// Gets or sets the threshold in bytes below which binary data is inlined in the BSON document.
    /// Binary data at or above this size triggers the <see cref="ExternalBinaryHandler"/> callback
    /// (if provided) or is inlined with a size note.
    /// Default: 16384 (16 KB).
    /// </summary>
    public int BinaryInlineThreshold { get; set; } = 16384;

    /// <summary>
    /// Gets or sets the callback invoked when binary data exceeds <see cref="BinaryInlineThreshold"/>.
    /// The callback receives the DICOM tag and the binary data, and returns a
    /// <see cref="BinaryDataReference"/> describing where the data was stored externally.
    /// When null, large binary data is inlined regardless of size.
    /// </summary>
    public Func<DicomTag, ReadOnlyMemory<byte>, BinaryDataReference>? ExternalBinaryHandler { get; set; }

    /// <summary>
    /// Gets or sets whether to strip all private tags from the output.
    /// Default: false.
    /// </summary>
    public bool StripPrivateTags { get; set; }

    /// <summary>
    /// Gets or sets the maximum sequence nesting depth.
    /// Sequences exceeding this depth are serialized as binary blobs.
    /// Default: 16.
    /// </summary>
    public int MaxSequenceDepth { get; set; } = 16;

    /// <summary>
    /// Gets or sets the flatten profile controlling which sequences are flattened
    /// (dot-notation fields at the document root).
    /// Default: null (no flattening).
    /// </summary>
    public FlattenProfile? FlattenProfile { get; set; }
}
