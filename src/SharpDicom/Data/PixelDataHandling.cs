namespace SharpDicom.Data;

/// <summary>
/// Specifies how pixel data should be handled during DICOM file reading.
/// </summary>
public enum PixelDataHandling
{
    /// <summary>
    /// Load pixel data into memory immediately during file parsing.
    /// </summary>
    /// <remarks>
    /// Use this for small files or when pixel data will always be accessed.
    /// This is the default behavior.
    /// </remarks>
    LoadInMemory = 0,

    /// <summary>
    /// Keep a stream reference and load pixel data on first access.
    /// </summary>
    /// <remarks>
    /// Requires a seekable stream. The stream must remain open until pixel data
    /// is accessed. Use this for large files where pixel data may not always be needed.
    /// </remarks>
    LazyLoad = 1,

    /// <summary>
    /// Skip pixel data entirely during file parsing.
    /// </summary>
    /// <remarks>
    /// Use this for metadata-only operations where pixel data is never needed.
    /// This is the most efficient option when only DICOM headers are required.
    /// </remarks>
    Skip = 2,

    /// <summary>
    /// Let a callback decide how to handle pixel data based on context.
    /// </summary>
    /// <remarks>
    /// The callback receives information about the pixel data (dimensions, estimated size)
    /// and can decide per-instance whether to load, skip, or lazy-load.
    /// </remarks>
    Callback = 3
}
