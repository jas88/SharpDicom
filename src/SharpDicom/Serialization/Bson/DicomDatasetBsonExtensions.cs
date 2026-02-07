using System;
using System.Buffers;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Extension and convenience methods for BSON serialization of <see cref="DicomDataset"/>.
/// </summary>
public static class DicomDatasetBsonExtensions
{
    /// <summary>
    /// Serializes this dataset to a BSON document byte array.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A BSON document as a byte array.</returns>
    public static byte[] ToBson(this DicomDataset dataset, BsonSerializationOptions? options = null)
        => BsonDicomWriter.Serialize(dataset, options);

    /// <summary>
    /// Serializes this dataset to BSON and writes it to the specified <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="writer">The target buffer writer.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    public static void ToBson(this DicomDataset dataset, IBufferWriter<byte> writer, BsonSerializationOptions? options = null)
        => BsonDicomWriter.Serialize(dataset, writer, options);

    /// <summary>
    /// Deserializes a BSON document byte array to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="bson">The BSON document bytes.</param>
    /// <param name="options">Deserialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    /// <remarks>
    /// This is a static method (not an extension method) placed here for discoverability
    /// alongside <see cref="ToBson(DicomDataset, BsonSerializationOptions?)"/>.
    /// </remarks>
    public static DicomDataset FromBson(byte[] bson, BsonSerializationOptions? options = null)
        => BsonDicomReader.Deserialize(bson, options);

    /// <summary>
    /// Deserializes a BSON document from a <see cref="ReadOnlyMemory{T}"/> to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="bson">The BSON document memory.</param>
    /// <param name="options">Deserialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    /// <remarks>
    /// This is a static method (not an extension method) placed here for discoverability
    /// alongside <see cref="ToBson(DicomDataset, BsonSerializationOptions?)"/>.
    /// </remarks>
    public static DicomDataset FromBson(ReadOnlyMemory<byte> bson, BsonSerializationOptions? options = null)
        => BsonDicomReader.Deserialize(bson, options);
}
