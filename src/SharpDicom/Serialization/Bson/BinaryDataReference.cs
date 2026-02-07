using System;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Represents an external reference to binary data that was too large to inline in BSON.
/// </summary>
/// <remarks>
/// When binary element data exceeds <see cref="BsonSerializationOptions.BinaryInlineThreshold"/>,
/// the <see cref="BsonSerializationOptions.ExternalBinaryHandler"/> callback is invoked to store the
/// data externally. This class holds the resulting reference metadata.
/// </remarks>
public sealed class BinaryDataReference
{
    /// <summary>
    /// Gets the reference type identifying where the binary data is stored.
    /// </summary>
    /// <example>"gridfs", "file", "s3"</example>
    public string ReferenceType { get; }

    /// <summary>
    /// Gets the identifier for the stored binary data, or null if not applicable.
    /// </summary>
    /// <example>A MongoDB ObjectId string for GridFS references.</example>
    public string? Id { get; }

    /// <summary>
    /// Gets the path to the stored binary data, or null if not applicable.
    /// </summary>
    /// <example>A file system path or S3 key.</example>
    public string? Path { get; }

    private BinaryDataReference(string referenceType, string? id, string? path)
    {
        ReferenceType = referenceType ?? throw new ArgumentNullException(nameof(referenceType));
        Id = id;
        Path = path;
    }

    /// <summary>
    /// Creates a reference for binary data stored in MongoDB GridFS.
    /// </summary>
    /// <param name="objectId">The GridFS ObjectId as a hex string.</param>
    /// <returns>A new <see cref="BinaryDataReference"/> for GridFS.</returns>
    public static BinaryDataReference ForGridFs(string objectId)
    {
        if (string.IsNullOrEmpty(objectId))
            throw new ArgumentException("ObjectId cannot be null or empty.", nameof(objectId));

        return new BinaryDataReference("gridfs", objectId, null);
    }

    /// <summary>
    /// Creates a reference for binary data stored as a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>A new <see cref="BinaryDataReference"/> for file storage.</returns>
    public static BinaryDataReference ForFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        return new BinaryDataReference("file", null, path);
    }
}
