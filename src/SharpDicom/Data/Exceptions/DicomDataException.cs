using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Exception thrown when DICOM data parsing or validation fails.
    /// </summary>
    public class DicomDataException : DicomException
    {
        /// <summary>
        /// Gets the DICOM tag associated with this exception, if available.
        /// </summary>
        public DicomTag? Tag { get; init; }

        /// <summary>
        /// Gets the DICOM Value Representation associated with this exception, if available.
        /// </summary>
        public DicomVR? VR { get; init; }

        /// <summary>
        /// Gets the stream position where the error occurred, if available.
        /// </summary>
        public long? StreamPosition { get; init; }

        /// <summary>
        /// Gets a copy of the element value that caused the error, if available.
        /// </summary>
        public byte[]? ElementValue { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomDataException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataException"/> class with an inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomDataException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
