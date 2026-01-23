namespace SharpDicom.Codecs
{
    /// <summary>
    /// Diagnostic information about a codec operation issue.
    /// </summary>
    /// <param name="FrameIndex">The zero-based frame index where the issue occurred.</param>
    /// <param name="BytePosition">The byte position in the compressed data where the issue occurred.</param>
    /// <param name="Message">A description of the issue.</param>
    /// <param name="Expected">The expected value or condition, if applicable.</param>
    /// <param name="Actual">The actual value or condition found, if applicable.</param>
    public readonly record struct CodecDiagnostic(
        int FrameIndex,
        long BytePosition,
        string Message,
        string? Expected,
        string? Actual)
    {
        /// <summary>
        /// Creates a diagnostic for a general issue at a specific frame and position.
        /// </summary>
        /// <param name="frameIndex">The zero-based frame index.</param>
        /// <param name="bytePosition">The byte position.</param>
        /// <param name="message">The diagnostic message.</param>
        /// <returns>A CodecDiagnostic instance.</returns>
        public static CodecDiagnostic At(int frameIndex, long bytePosition, string message) =>
            new(frameIndex, bytePosition, message, null, null);

        /// <summary>
        /// Creates a diagnostic with expected and actual values.
        /// </summary>
        /// <param name="frameIndex">The zero-based frame index.</param>
        /// <param name="bytePosition">The byte position.</param>
        /// <param name="message">The diagnostic message.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value.</param>
        /// <returns>A CodecDiagnostic instance.</returns>
        public static CodecDiagnostic Mismatch(int frameIndex, long bytePosition, string message, string expected, string actual) =>
            new(frameIndex, bytePosition, message, expected, actual);

        /// <inheritdoc />
        public override string ToString()
        {
            var result = $"Frame {FrameIndex} at position {BytePosition}: {Message}";
            if (Expected != null || Actual != null)
            {
                result += $" (expected: {Expected ?? "N/A"}, actual: {Actual ?? "N/A"})";
            }
            return result;
        }
    }
}
