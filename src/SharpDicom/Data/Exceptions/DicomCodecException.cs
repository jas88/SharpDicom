using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Exception thrown when a pixel data codec operation fails.
    /// </summary>
    public class DicomCodecException : DicomException
    {
        /// <summary>
        /// Gets or sets the transfer syntax associated with this exception.
        /// </summary>
        public TransferSyntax TransferSyntax { get; init; }

        /// <summary>
        /// Gets or sets the zero-based frame index where the error occurred.
        /// </summary>
        public int? FrameIndex { get; init; }

        /// <summary>
        /// Gets or sets the byte position in the compressed data where the error occurred.
        /// </summary>
        public long? BytePosition { get; init; }

        /// <summary>
        /// Gets or sets the codec name that encountered the error.
        /// </summary>
        public string? CodecName { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomCodecException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomCodecException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomCodecException"/> class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomCodecException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a DicomCodecException for a decode failure.
        /// </summary>
        /// <param name="codecName">Name of the codec that failed.</param>
        /// <param name="transferSyntax">The transfer syntax being decoded.</param>
        /// <param name="frameIndex">The frame index where failure occurred.</param>
        /// <param name="bytePosition">The byte position where failure occurred.</param>
        /// <param name="message">Description of the failure.</param>
        /// <returns>A DicomCodecException with context set.</returns>
        public static DicomCodecException DecodeError(
            string codecName,
            TransferSyntax transferSyntax,
            int frameIndex,
            long bytePosition,
            string message) =>
            new($"Codec '{codecName}' failed to decode frame {frameIndex}: {message}")
            {
                CodecName = codecName,
                TransferSyntax = transferSyntax,
                FrameIndex = frameIndex,
                BytePosition = bytePosition
            };

        /// <summary>
        /// Creates a DicomCodecException for an encode failure.
        /// </summary>
        /// <param name="codecName">Name of the codec that failed.</param>
        /// <param name="transferSyntax">The target transfer syntax.</param>
        /// <param name="message">Description of the failure.</param>
        /// <returns>A DicomCodecException with context set.</returns>
        public static DicomCodecException EncodeError(
            string codecName,
            TransferSyntax transferSyntax,
            string message) =>
            new($"Codec '{codecName}' failed to encode: {message}")
            {
                CodecName = codecName,
                TransferSyntax = transferSyntax
            };

        /// <summary>
        /// Creates a DicomCodecException for an unsupported transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">The unsupported transfer syntax.</param>
        /// <returns>A DicomCodecException with context set.</returns>
        public static DicomCodecException UnsupportedTransferSyntax(TransferSyntax transferSyntax) =>
            new($"No codec registered for transfer syntax: {transferSyntax.UID}")
            {
                TransferSyntax = transferSyntax
            };
    }
}
