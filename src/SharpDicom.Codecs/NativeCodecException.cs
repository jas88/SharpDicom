using System;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Exception thrown when native codec operations fail.
    /// </summary>
    public class NativeCodecException : DicomCodecException
    {
        /// <summary>
        /// Gets the native error code from the underlying library.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Gets the error category.
        /// </summary>
        public NativeCodecErrorCategory Category { get; }

        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NativeCodecException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message and native error code.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="nativeErrorCode">The native library error code.</param>
        public NativeCodecException(string message, int nativeErrorCode)
            : base(message)
        {
            NativeErrorCode = nativeErrorCode;
            Category = CategorizeError(nativeErrorCode);
        }

        /// <summary>
        /// Initializes a new instance with a message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public NativeCodecException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message, native error code, and native message.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="nativeErrorCode">The native library error code.</param>
        /// <param name="nativeMessage">The message from the native library.</param>
        public NativeCodecException(string message, int nativeErrorCode, string nativeMessage)
            : base($"{message}: {nativeMessage}")
        {
            NativeErrorCode = nativeErrorCode;
            Category = CategorizeError(nativeErrorCode);
        }

        private static NativeCodecErrorCategory CategorizeError(int code) => code switch
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
    /// Categories of native codec errors for easier handling.
    /// </summary>
    public enum NativeCodecErrorCategory
    {
        /// <summary>Unknown error category.</summary>
        Unknown,

        /// <summary>Invalid input data or parameters.</summary>
        InvalidInput,

        /// <summary>Output buffer is too small.</summary>
        BufferTooSmall,

        /// <summary>Decode operation failed.</summary>
        DecodeFailed,

        /// <summary>Encode operation failed.</summary>
        EncodeFailed,

        /// <summary>Requested feature or format is unsupported.</summary>
        Unsupported,

        /// <summary>Out of memory.</summary>
        OutOfMemory,

        /// <summary>Operation timed out.</summary>
        Timeout,

        /// <summary>GPU is not available.</summary>
        GpuUnavailable
    }
}
