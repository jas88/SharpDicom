using System;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Exception thrown when native codec operations fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exception provides additional context specific to native codec failures,
    /// including the native error code and error category.
    /// </para>
    /// <para>
    /// Native error codes are mapped to categories for easier error handling:
    /// <list type="table">
    /// <listheader>
    /// <term>Code</term>
    /// <description>Category</description>
    /// </listheader>
    /// <item><term>-1</term><description>InvalidInput</description></item>
    /// <item><term>-2</term><description>BufferTooSmall</description></item>
    /// <item><term>-3</term><description>DecodeFailed</description></item>
    /// <item><term>-4</term><description>EncodeFailed</description></item>
    /// <item><term>-5</term><description>Unsupported</description></item>
    /// <item><term>-6</term><description>OutOfMemory</description></item>
    /// <item><term>-7</term><description>Timeout</description></item>
    /// <item><term>-8</term><description>GpuUnavailable</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public class NativeCodecException : DicomCodecException
    {
        /// <summary>
        /// Gets the native error code from the underlying library.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Gets the error category derived from the native error code.
        /// </summary>
        public NativeCodecErrorCategory Category { get; }

        /// <summary>
        /// Gets the native error message if available.
        /// </summary>
        public string? NativeMessage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCodecException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public NativeCodecException(string message)
            : base(message)
        {
            Category = NativeCodecErrorCategory.Unknown;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCodecException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="nativeErrorCode">The native error code.</param>
        public NativeCodecException(string message, int nativeErrorCode)
            : base(message)
        {
            NativeErrorCode = nativeErrorCode;
            Category = CategorizeError(nativeErrorCode);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCodecException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of this exception.</param>
        public NativeCodecException(string message, Exception innerException)
            : base(message, innerException)
        {
            Category = NativeCodecErrorCategory.Unknown;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCodecException"/> class.
        /// </summary>
        /// <param name="message">The managed error message.</param>
        /// <param name="nativeErrorCode">The native error code.</param>
        /// <param name="nativeMessage">The native error message.</param>
        public NativeCodecException(string message, int nativeErrorCode, string? nativeMessage)
            : base(FormatMessage(message, nativeMessage))
        {
            NativeErrorCode = nativeErrorCode;
            Category = CategorizeError(nativeErrorCode);
            NativeMessage = nativeMessage;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeCodecException"/> class.
        /// </summary>
        /// <param name="message">The managed error message.</param>
        /// <param name="nativeErrorCode">The native error code.</param>
        /// <param name="nativeMessage">The native error message.</param>
        /// <param name="transferSyntax">The transfer syntax associated with the error.</param>
        public NativeCodecException(string message, int nativeErrorCode, string? nativeMessage, TransferSyntax transferSyntax)
            : base(FormatMessage(message, nativeMessage))
        {
            NativeErrorCode = nativeErrorCode;
            Category = CategorizeError(nativeErrorCode);
            NativeMessage = nativeMessage;
            TransferSyntax = transferSyntax;
        }

        private static string FormatMessage(string message, string? nativeMessage)
        {
            if (string.IsNullOrEmpty(nativeMessage))
            {
                return message;
            }

            return $"{message}: {nativeMessage}";
        }

        private static NativeCodecErrorCategory CategorizeError(int code)
        {
            return code switch
            {
                -1 => NativeCodecErrorCategory.InvalidInput,
                -2 => NativeCodecErrorCategory.BufferTooSmall,
                -3 => NativeCodecErrorCategory.DecodeFailed,
                -4 => NativeCodecErrorCategory.EncodeFailed,
                -5 => NativeCodecErrorCategory.Unsupported,
                -6 => NativeCodecErrorCategory.OutOfMemory,
                -7 => NativeCodecErrorCategory.Timeout,
                -8 => NativeCodecErrorCategory.GpuUnavailable,
                _ => NativeCodecErrorCategory.Unknown
            };
        }

        /// <summary>
        /// Creates a NativeCodecException for a library not found error.
        /// </summary>
        /// <param name="libraryName">Name of the library that was not found.</param>
        /// <param name="rid">The runtime identifier.</param>
        /// <returns>A configured NativeCodecException.</returns>
        public static NativeCodecException LibraryNotFound(string libraryName, string rid)
        {
            return new NativeCodecException(
                $"Native library '{libraryName}' not found. Ensure SharpDicom.Codecs.runtime.{rid} is installed.")
            {
                CodecName = libraryName
            };
        }

        /// <summary>
        /// Creates a NativeCodecException for a version mismatch error.
        /// </summary>
        /// <param name="expected">Expected version.</param>
        /// <param name="actual">Actual version found.</param>
        /// <returns>A configured NativeCodecException.</returns>
        public static NativeCodecException VersionMismatch(int expected, int actual)
        {
            return new NativeCodecException(
                $"Native library version mismatch: expected {expected}, got {actual}");
        }

        /// <summary>
        /// Creates a NativeCodecException for a decode error.
        /// </summary>
        /// <param name="codecName">Name of the codec.</param>
        /// <param name="nativeErrorCode">The native error code.</param>
        /// <param name="nativeMessage">The native error message.</param>
        /// <param name="transferSyntax">The transfer syntax being decoded.</param>
        /// <param name="frameIndex">The frame index being decoded.</param>
        /// <returns>A configured NativeCodecException.</returns>
        public static NativeCodecException DecodeError(
            string codecName,
            int nativeErrorCode,
            string? nativeMessage,
            TransferSyntax transferSyntax,
            int frameIndex)
        {
            var ex = new NativeCodecException(
                $"Native codec '{codecName}' failed to decode frame {frameIndex}",
                nativeErrorCode,
                nativeMessage,
                transferSyntax);
            ex = new NativeCodecException(ex.Message, nativeErrorCode, nativeMessage, transferSyntax)
            {
                CodecName = codecName,
                FrameIndex = frameIndex
            };
            return ex;
        }

        /// <summary>
        /// Creates a NativeCodecException for an encode error.
        /// </summary>
        /// <param name="codecName">Name of the codec.</param>
        /// <param name="nativeErrorCode">The native error code.</param>
        /// <param name="nativeMessage">The native error message.</param>
        /// <param name="transferSyntax">The target transfer syntax.</param>
        /// <returns>A configured NativeCodecException.</returns>
        public static NativeCodecException EncodeError(
            string codecName,
            int nativeErrorCode,
            string? nativeMessage,
            TransferSyntax transferSyntax)
        {
            return new NativeCodecException(
                $"Native codec '{codecName}' failed to encode",
                nativeErrorCode,
                nativeMessage,
                transferSyntax)
            {
                CodecName = codecName
            };
        }
    }

    /// <summary>
    /// Categories of native codec errors.
    /// </summary>
    public enum NativeCodecErrorCategory
    {
        /// <summary>
        /// Unknown error category.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The input data is invalid or malformed.
        /// </summary>
        InvalidInput = 1,

        /// <summary>
        /// The output buffer is too small to hold the result.
        /// </summary>
        BufferTooSmall = 2,

        /// <summary>
        /// Decoding operation failed.
        /// </summary>
        DecodeFailed = 3,

        /// <summary>
        /// Encoding operation failed.
        /// </summary>
        EncodeFailed = 4,

        /// <summary>
        /// The requested operation or format is not supported.
        /// </summary>
        Unsupported = 5,

        /// <summary>
        /// Out of memory during codec operation.
        /// </summary>
        OutOfMemory = 6,

        /// <summary>
        /// Operation timed out.
        /// </summary>
        Timeout = 7,

        /// <summary>
        /// GPU acceleration is not available.
        /// </summary>
        GpuUnavailable = 8
    }
}
