using System;

namespace Dicom
{
    /// <summary>
    /// Abstract base class for all DICOM elements in the fo-dicom 4.x compat layer.
    /// Matches fo-dicom 4.x DicomItem base type.
    /// </summary>
    public abstract class DicomItem
    {
        /// <summary>
        /// Gets the DICOM tag for this item.
        /// </summary>
        public DicomTag Tag { get; }

        /// <summary>
        /// Gets the value representation for this item.
        /// </summary>
        public abstract DicomVR ValueRepresentation { get; }

        /// <summary>
        /// Gets the underlying SharpDicom element.
        /// </summary>
        internal SharpDicom.Data.IDicomElement Inner { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomItem"/> class.
        /// </summary>
        protected DicomItem(SharpDicom.Data.IDicomElement inner)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(inner);
#else
            if (inner == null) throw new ArgumentNullException(nameof(inner));
#endif
            Inner = inner;
            Tag = new DicomTag(inner.Tag);
        }

        /// <summary>
        /// Wraps a SharpDicom element as the appropriate compat DicomItem subtype.
        /// </summary>
        internal static DicomItem Wrap(SharpDicom.Data.IDicomElement element)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(element);
#else
            if (element == null) throw new ArgumentNullException(nameof(element));
#endif

            return element switch
            {
                SharpDicom.Data.DicomSequence seq => new DicomSequence(seq),
                SharpDicom.Data.DicomNumericElement ne when ne.VR == SharpDicom.Data.DicomVR.AT
                    => new DicomAttributeTag(ne),
                SharpDicom.Data.DicomStringElement se => new DicomStringElement(se),
                _ => new DicomOtherElement(element)
            };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var name = Tag.DictionaryEntry?.Name ?? "Unknown Tag";
            return $"{Tag} {ValueRepresentation} {name}";
        }
    }
}
