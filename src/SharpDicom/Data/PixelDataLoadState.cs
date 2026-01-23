namespace SharpDicom.Data;

/// <summary>
/// Represents the current loading state of pixel data.
/// </summary>
public enum PixelDataLoadState
{
    /// <summary>
    /// Pixel data was skipped during initial file parsing and is not available.
    /// </summary>
    NotLoaded = 0,

    /// <summary>
    /// Asynchronous loading of pixel data is currently in progress.
    /// </summary>
    Loading = 1,

    /// <summary>
    /// Pixel data has been loaded and is available in memory.
    /// </summary>
    Loaded = 2,

    /// <summary>
    /// An error occurred while loading pixel data.
    /// </summary>
    /// <remarks>
    /// Check the associated error information for details about the failure.
    /// </remarks>
    Failed = 3
}
