using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Exception thrown when a DICOM tag is invalid or malformed.
    /// </summary>
    public class DicomTagException : DicomDataException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTagException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomTagException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTagException"/> class with an inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomTagException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
