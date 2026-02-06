using System;

namespace Dicom
{
    /// <summary>
    /// Exception type matching fo-dicom 4.x DicomDataException for compatibility.
    /// </summary>
    public class DicomDataException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataException"/> class.
        /// </summary>
        public DicomDataException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public DicomDataException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDataException"/> class with a message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public DicomDataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
