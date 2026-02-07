using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.MongoDB;

/// <summary>
/// Helpers for creating and configuring MongoDB collections for DICOM metadata storage.
/// </summary>
public static class DicomCollectionHelper
{
    /// <summary>
    /// Gets or creates a DICOM metadata collection with recommended indexes.
    /// </summary>
    /// <param name="database">The MongoDB database.</param>
    /// <param name="collectionName">Collection name. Defaults to <c>"dicom_metadata"</c>.</param>
    /// <param name="indexes">
    /// Index definitions to create, or <c>null</c> to use
    /// <see cref="IndexRecommendations.AllRadiologyIndexes"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configured MongoDB collection.</returns>
    public static async Task<IMongoCollection<BsonDocument>> GetOrCreateCollectionAsync(
        IMongoDatabase database,
        string collectionName = "dicom_metadata",
        IReadOnlyList<CreateIndexModel<BsonDocument>>? indexes = null,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var indexModels = indexes ?? IndexRecommendations.AllRadiologyIndexes();
        await collection.Indexes.CreateManyAsync(indexModels, cancellationToken)
            .ConfigureAwait(false);
        return collection;
    }

    /// <summary>
    /// Inserts a single <see cref="DicomDataset"/> into a MongoDB collection.
    /// </summary>
    /// <param name="collection">Target collection.</param>
    /// <param name="dataset">The DICOM dataset to insert.</param>
    /// <param name="options">BSON serialization options, or <c>null</c> for defaults.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    public static async Task InsertAsync(
        IMongoCollection<BsonDocument> collection,
        DicomDataset dataset,
        BsonSerializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var raw = BsonDocumentAdapter.ToRawBsonDocument(dataset, options);
        await collection.InsertOneAsync(raw.ToBsonDocument(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
