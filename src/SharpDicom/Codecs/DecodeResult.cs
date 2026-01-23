namespace SharpDicom.Codecs
{
    /// <summary>
    /// Result of a decode operation on a single frame.
    /// </summary>
    /// <param name="Success">Whether the decode operation succeeded.</param>
    /// <param name="BytesWritten">Number of bytes written to the destination buffer.</param>
    /// <param name="Diagnostic">Diagnostic information if the operation failed.</param>
    public readonly record struct DecodeResult(
        bool Success,
        int BytesWritten,
        CodecDiagnostic? Diagnostic)
    {
        /// <summary>
        /// Creates a successful decode result.
        /// </summary>
        /// <param name="bytesWritten">Number of bytes written to the destination.</param>
        /// <returns>A successful DecodeResult.</returns>
        public static DecodeResult Ok(int bytesWritten) =>
            new(true, bytesWritten, null);

        /// <summary>
        /// Creates a failed decode result with diagnostic information.
        /// </summary>
        /// <param name="frameIndex">The zero-based frame index where failure occurred.</param>
        /// <param name="position">The byte position where failure occurred.</param>
        /// <param name="message">A description of the failure.</param>
        /// <param name="expected">The expected value, if applicable.</param>
        /// <param name="actual">The actual value found, if applicable.</param>
        /// <returns>A failed DecodeResult.</returns>
        public static DecodeResult Fail(int frameIndex, long position, string message, string? expected = null, string? actual = null) =>
            new(false, 0, new CodecDiagnostic(frameIndex, position, message, expected, actual));

        /// <summary>
        /// Creates a failed decode result from an existing diagnostic.
        /// </summary>
        /// <param name="diagnostic">The diagnostic information.</param>
        /// <returns>A failed DecodeResult.</returns>
        public static DecodeResult Fail(CodecDiagnostic diagnostic) =>
            new(false, 0, diagnostic);
    }
}
