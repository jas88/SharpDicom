using System;

namespace SharpDicom.Data
{
    /// <summary>
    /// Abstract base class for DICOM data elements, providing common functionality.
    /// </summary>
    public abstract class DicomElement : IDicomElement
    {
        /// <summary>
        /// Gets the DICOM tag that identifies this element.
        /// </summary>
        public DicomTag Tag { get; }

        /// <summary>
        /// Gets the Value Representation (VR) that defines the data type of this element.
        /// </summary>
        public DicomVR VR { get; }

        /// <summary>
        /// Gets the raw byte value of this element.
        /// </summary>
        public ReadOnlyMemory<byte> RawValue { get; }

        /// <summary>
        /// Gets the length of the element value in bytes.
        /// </summary>
        public int Length => RawValue.Length;

        /// <summary>
        /// Gets a value indicating whether this element has no value.
        /// </summary>
        public bool IsEmpty => RawValue.IsEmpty;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomElement"/> class.
        /// </summary>
        /// <param name="tag">The DICOM tag.</param>
        /// <param name="vr">The Value Representation.</param>
        /// <param name="value">The raw byte value.</param>
        protected DicomElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
        {
            Tag = tag;
            VR = vr;
            RawValue = value;
        }

        /// <summary>
        /// Creates a deep copy of this element that owns its memory.
        /// </summary>
        /// <returns>A new element with independently owned data.</returns>
        public abstract IDicomElement ToOwned();
    }
}
