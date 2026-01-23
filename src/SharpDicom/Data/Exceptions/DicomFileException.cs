using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Exception thrown for DICOM file format errors.
    /// </summary>
    public class DicomFileException : DicomException
    {
        /// <summary>
        /// Gets or sets the file path, if available.
        /// </summary>
        public string? FilePath { get; init; }

        /// <summary>
        /// Gets or sets the stream position where the error occurred.
        /// </summary>
        public long? Position { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomFileException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomFileException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomFileException"/> class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomFileException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown for DICOM preamble errors.
    /// </summary>
    public class DicomPreambleException : DicomFileException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomPreambleException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomPreambleException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown for File Meta Information errors.
    /// </summary>
    public class DicomMetaInfoException : DicomFileException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomMetaInfoException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomMetaInfoException(string message) : base(message)
        {
        }
    }
}
