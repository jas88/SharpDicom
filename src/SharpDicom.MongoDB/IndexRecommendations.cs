using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SharpDicom.MongoDB;

/// <summary>
/// Predefined MongoDB index definitions optimized for common DICOM query patterns.
/// </summary>
/// <remarks>
/// Tag keys use 8-char hex format (the default <see cref="Serialization.Bson.BsonTagKeyFormat.Hex8"/>).
/// Each index targets the ".Value" sub-field within the element sub-document, matching
/// the BSON structure produced by <see cref="Serialization.Bson.BsonDicomWriter"/>.
/// </remarks>
public static class IndexRecommendations
{
    /// <summary>
    /// Patient-level index: PatientID (0010,0020) + PatientName (0010,0010).
    /// </summary>
    /// <returns>A compound index on PatientID and PatientName values.</returns>
    public static CreateIndexModel<BsonDocument> PatientIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("00100020.Value")   // PatientID
            .Ascending("00100010.Value"),   // PatientName
            new CreateIndexOptions { Name = "idx_patient" });

    /// <summary>
    /// Study-level index: StudyInstanceUID (0020,000D) with uniqueness constraint.
    /// </summary>
    /// <returns>A unique index on StudyInstanceUID.</returns>
    public static CreateIndexModel<BsonDocument> StudyIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("0020000D.Value"),   // StudyInstanceUID
            new CreateIndexOptions { Name = "idx_study_uid", Unique = true });

    /// <summary>
    /// Study date range query index: StudyDate (0008,0020).
    /// </summary>
    /// <returns>An index on StudyDate for date-range queries.</returns>
    public static CreateIndexModel<BsonDocument> StudyDateIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("00080020.Value"),   // StudyDate
            new CreateIndexOptions { Name = "idx_study_date" });

    /// <summary>
    /// Series-level index: SeriesInstanceUID (0020,000E) with uniqueness constraint.
    /// </summary>
    /// <returns>A unique index on SeriesInstanceUID.</returns>
    public static CreateIndexModel<BsonDocument> SeriesIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("0020000E.Value"),   // SeriesInstanceUID
            new CreateIndexOptions { Name = "idx_series_uid", Unique = true });

    /// <summary>
    /// Instance-level index: SOPInstanceUID (0008,0018) with uniqueness constraint.
    /// </summary>
    /// <returns>A unique index on SOPInstanceUID.</returns>
    public static CreateIndexModel<BsonDocument> InstanceIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("00080018.Value"),   // SOPInstanceUID
            new CreateIndexOptions { Name = "idx_sop_uid", Unique = true });

    /// <summary>
    /// Modality index: Modality (0008,0060) for filtering by imaging modality.
    /// </summary>
    /// <returns>An index on Modality.</returns>
    public static CreateIndexModel<BsonDocument> ModalityIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("00080060.Value"),   // Modality
            new CreateIndexOptions { Name = "idx_modality" });

    /// <summary>
    /// Accession number index: AccessionNumber (0008,0050) for RIS/HIS integration.
    /// </summary>
    /// <returns>An index on AccessionNumber.</returns>
    public static CreateIndexModel<BsonDocument> AccessionNumberIndex()
        => new(Builders<BsonDocument>.IndexKeys
            .Ascending("00080050.Value"),   // AccessionNumber
            new CreateIndexOptions { Name = "idx_accession" });

    /// <summary>
    /// Returns all recommended indexes for a typical radiology PACS.
    /// </summary>
    /// <returns>Seven index definitions covering patient, study, series, instance, modality, and accession queries.</returns>
    public static IReadOnlyList<CreateIndexModel<BsonDocument>> AllRadiologyIndexes()
        => new[]
        {
            PatientIndex(),
            StudyIndex(),
            StudyDateIndex(),
            SeriesIndex(),
            InstanceIndex(),
            ModalityIndex(),
            AccessionNumberIndex()
        };
}
