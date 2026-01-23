using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Base exception class for all DICOM-related exceptions.
    /// </summary>
    public class DicomException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomException"/> class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
