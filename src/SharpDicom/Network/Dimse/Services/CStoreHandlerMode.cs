namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Specifies how incoming C-STORE requests are handled.
    /// </summary>
    public enum CStoreHandlerMode
    {
        /// <summary>
        /// Full dataset buffered in memory before handler invoked.
        /// Simple but uses more memory for large datasets.
        /// </summary>
        Buffered,

        /// <summary>
        /// Metadata first, then pixel data streams via CopyToAsync pattern.
        /// Memory-efficient for large images.
        /// </summary>
        Streaming
    }
}
