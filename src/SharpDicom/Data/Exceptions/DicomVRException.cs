using System;

namespace SharpDicom.Data.Exceptions
{
    /// <summary>
    /// Exception thrown when a DICOM Value Representation is invalid or unexpected.
    /// </summary>
    public class DicomVRException : DicomDataException
    {
        /// <summary>
        /// Gets the invalid Value Representation that caused this exception.
        /// </summary>
        public DicomVR InvalidVR { get; }

        /// <summary>
        /// Gets the expected Value Representation, if applicable.
        /// </summary>
        public DicomVR? ExpectedVR { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomVRException"/> class.
        /// </summary>
        /// <param name="invalidVR">The invalid Value Representation.</param>
        /// <param name="message">The message that describes the error.</param>
        public DicomVRException(DicomVR invalidVR, string message) : base(message)
        {
            InvalidVR = invalidVR;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomVRException"/> class with an inner exception.
        /// </summary>
        /// <param name="invalidVR">The invalid Value Representation.</param>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomVRException(DicomVR invalidVR, string message, Exception innerException) : base(message, innerException)
        {
            InvalidVR = invalidVR;
        }
    }
}
