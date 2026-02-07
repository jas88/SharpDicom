using MongoDB.Bson;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.MongoDB;

/// <summary>
/// Adapts between SharpDicom's raw BSON bytes and MongoDB.Bson object model.
/// </summary>
/// <remarks>
/// SharpDicom's <see cref="BsonDicomWriter"/> produces raw BSON bytes without any
/// dependency on MongoDB.Bson. This adapter bridges that output to the MongoDB driver's
/// <see cref="BsonDocument"/> and <see cref="RawBsonDocument"/> types, enabling
/// efficient insertion into MongoDB collections.
/// </remarks>
public static class BsonDocumentAdapter
{
    /// <summary>
    /// Converts a <see cref="DicomDataset"/> to a <see cref="BsonDocument"/>
    /// via raw BSON serialization.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to convert.</param>
    /// <param name="options">Serialization options, or <c>null</c> for defaults.</param>
    /// <returns>A fully materialized <see cref="BsonDocument"/>.</returns>
    public static BsonDocument ToBsonDocument(DicomDataset dataset,
        BsonSerializationOptions? options = null)
    {
        var rawBytes = BsonDicomWriter.Serialize(dataset, options);
        return new RawBsonDocument(rawBytes).ToBsonDocument();
    }

    /// <summary>
    /// Converts a <see cref="DicomDataset"/> to a <see cref="RawBsonDocument"/>
    /// without materializing the document structure. More efficient for bulk inserts.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to convert.</param>
    /// <param name="options">Serialization options, or <c>null</c> for defaults.</param>
    /// <returns>A <see cref="RawBsonDocument"/> wrapping the raw BSON bytes.</returns>
    public static RawBsonDocument ToRawBsonDocument(DicomDataset dataset,
        BsonSerializationOptions? options = null)
    {
        var rawBytes = BsonDicomWriter.Serialize(dataset, options);
        return new RawBsonDocument(rawBytes);
    }

    /// <summary>
    /// Converts a <see cref="BsonDocument"/> to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="document">The BSON document to convert.</param>
    /// <param name="options">Serialization options, or <c>null</c> for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset ToDicomDataset(BsonDocument document,
        BsonSerializationOptions? options = null)
    {
        var rawBytes = document.ToBson();
        return BsonDicomReader.Deserialize(rawBytes, options);
    }

    /// <summary>
    /// Converts a <see cref="RawBsonDocument"/> to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="document">The raw BSON document to convert.</param>
    /// <param name="options">Serialization options, or <c>null</c> for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset ToDicomDataset(RawBsonDocument document,
        BsonSerializationOptions? options = null)
    {
        var rawBytes = document.ToBson();
        return BsonDicomReader.Deserialize(rawBytes, options);
    }

    /// <summary>
    /// Converts raw BSON bytes (as produced by <see cref="BsonDicomWriter"/>) to a
    /// fully materialized <see cref="BsonDocument"/>.
    /// </summary>
    /// <param name="rawBson">Raw BSON document bytes.</param>
    /// <returns>A <see cref="BsonDocument"/>.</returns>
    public static BsonDocument BytesToBsonDocument(byte[] rawBson)
    {
        return new RawBsonDocument(rawBson).ToBsonDocument();
    }

    /// <summary>
    /// Converts raw BSON bytes to a <see cref="RawBsonDocument"/> (zero-copy wrap).
    /// </summary>
    /// <param name="rawBson">Raw BSON document bytes.</param>
    /// <returns>A <see cref="RawBsonDocument"/> wrapping the byte array.</returns>
    public static RawBsonDocument BytesToRawBsonDocument(byte[] rawBson)
    {
        return new RawBsonDocument(rawBson);
    }
}
