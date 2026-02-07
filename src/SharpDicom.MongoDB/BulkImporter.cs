using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.MongoDB;

/// <summary>
/// Bulk import utility for inserting multiple DICOM datasets into MongoDB.
/// </summary>
/// <remarks>
/// Uses batched operations for efficient throughput. Serializes each dataset
/// to raw BSON using <see cref="BsonDocumentAdapter"/> and inserts via
/// MongoDB bulk write operations.
/// </remarks>
public static class BulkImporter
{
    /// <summary>
    /// Bulk inserts DICOM datasets into a MongoDB collection.
    /// Uses <see cref="RawBsonDocument"/> for minimal serialization overhead.
    /// </summary>
    /// <param name="collection">Target collection.</param>
    /// <param name="datasets">Datasets to insert.</param>
    /// <param name="options">BSON serialization options, or <c>null</c> for defaults.</param>
    /// <param name="batchSize">Number of documents per batch. Default is 1000.</param>
    /// <param name="progress">Optional progress reporter (reports cumulative count of inserted documents).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of documents inserted.</returns>
    public static async Task<int> BulkInsertAsync(
        IMongoCollection<BsonDocument> collection,
        IEnumerable<DicomDataset> datasets,
        BsonSerializationOptions? options = null,
        int batchSize = 1000,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalInserted = 0;
        var batch = new List<BsonDocument>(batchSize);

        foreach (var dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = BsonDocumentAdapter.ToRawBsonDocument(dataset, options);
            batch.Add(raw.ToBsonDocument());

            if (batch.Count >= batchSize)
            {
                await collection.InsertManyAsync(batch,
                    new InsertManyOptions { IsOrdered = false },
                    cancellationToken).ConfigureAwait(false);
                totalInserted += batch.Count;
                progress?.Report(totalInserted);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await collection.InsertManyAsync(batch,
                new InsertManyOptions { IsOrdered = false },
                cancellationToken).ConfigureAwait(false);
            totalInserted += batch.Count;
            progress?.Report(totalInserted);
        }

        return totalInserted;
    }

    /// <summary>
    /// Bulk upserts DICOM datasets using SOPInstanceUID (0008,0018) as the unique key.
    /// Existing documents with matching SOPInstanceUID are replaced; new ones are inserted.
    /// </summary>
    /// <param name="collection">Target collection.</param>
    /// <param name="datasets">Datasets to upsert.</param>
    /// <param name="options">BSON serialization options, or <c>null</c> for defaults.</param>
    /// <param name="batchSize">Number of documents per batch. Default is 1000.</param>
    /// <param name="progress">Optional progress reporter (reports cumulative count of processed documents).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of documents processed.</returns>
    public static async Task<int> BulkUpsertAsync(
        IMongoCollection<BsonDocument> collection,
        IEnumerable<DicomDataset> datasets,
        BsonSerializationOptions? options = null,
        int batchSize = 1000,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var totalProcessed = 0;
        var batch = new List<WriteModel<BsonDocument>>(batchSize);

        foreach (var dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = BsonDocumentAdapter.ToBsonDocument(dataset, options);

            // Use SOPInstanceUID as unique key for upsert
            var sopUid = doc.GetValue("00080018", BsonNull.Value);
            BsonValue filterValue;
            if (sopUid is BsonDocument sopDoc && sopDoc.Contains("Value"))
                filterValue = sopDoc["Value"];
            else
                filterValue = sopUid;

            var filter = Builders<BsonDocument>.Filter.Eq("00080018.Value", filterValue);
            batch.Add(new ReplaceOneModel<BsonDocument>(filter, doc) { IsUpsert = true });

            if (batch.Count >= batchSize)
            {
                await collection.BulkWriteAsync(batch,
                    new BulkWriteOptions { IsOrdered = false },
                    cancellationToken).ConfigureAwait(false);
                totalProcessed += batch.Count;
                progress?.Report(totalProcessed);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await collection.BulkWriteAsync(batch,
                new BulkWriteOptions { IsOrdered = false },
                cancellationToken).ConfigureAwait(false);
            totalProcessed += batch.Count;
            progress?.Report(totalProcessed);
        }

        return totalProcessed;
    }
}
