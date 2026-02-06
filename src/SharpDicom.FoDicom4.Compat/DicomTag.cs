using System;

namespace Dicom
{
    /// <summary>
    /// Compatibility wrapper for DICOM tag.
    /// fo-dicom 4.x uses a class (not struct) with DictionaryEntry support.
    /// </summary>
    public sealed class DicomTag : IEquatable<DicomTag>
    {
        private readonly SharpDicom.Data.DicomTag _inner;
        private DicomDictionaryEntry? _dictionaryEntry;
        private bool _dictionaryEntryResolved;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTag"/> class.
        /// </summary>
        /// <param name="group">The group number.</param>
        /// <param name="element">The element number.</param>
        public DicomTag(ushort group, ushort element)
        {
            _inner = new SharpDicom.Data.DicomTag(group, element);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTag"/> class from an int-typed group/element pair.
        /// fo-dicom accepts int parameters for convenience.
        /// </summary>
        /// <param name="group">The group number.</param>
        /// <param name="element">The element number.</param>
        public DicomTag(int group, int element)
        {
            _inner = new SharpDicom.Data.DicomTag((ushort)group, (ushort)element);
        }

        internal DicomTag(SharpDicom.Data.DicomTag inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// Gets the group number.
        /// </summary>
        public ushort Group => _inner.Group;

        /// <summary>
        /// Gets the element number.
        /// </summary>
        public ushort Element => _inner.Element;

        /// <summary>
        /// Gets the dictionary entry for this tag, or null if unknown.
        /// </summary>
        public DicomDictionaryEntry? DictionaryEntry
        {
            get
            {
                if (!_dictionaryEntryResolved)
                {
                    var entry = SharpDicom.Data.DicomDictionary.Default.GetEntry(_inner);
                    if (entry.HasValue)
                    {
                        _dictionaryEntry = new DicomDictionaryEntry(entry.Value, this);
                    }
                    _dictionaryEntryResolved = true;
                }
                return _dictionaryEntry;
            }
        }

        /// <summary>
        /// Converts to the underlying SharpDicom tag.
        /// </summary>
        internal SharpDicom.Data.DicomTag ToSharpDicom() => _inner;

        /// <inheritdoc />
        public bool Equals(DicomTag? other)
        {
            if (other is null) return false;
            return _inner == other._inner;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is DicomTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _inner.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => _inner.ToString();

        /// <summary>
        /// Determines whether two tags are equal.
        /// </summary>
        public static bool operator ==(DicomTag? left, DicomTag? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two tags are not equal.
        /// </summary>
        public static bool operator !=(DicomTag? left, DicomTag? right) => !(left == right);

        // Well-known tags for dcm2csv and nccid usage
        // These mirror fo-dicom's static tag properties.

        /// <summary>Specific Character Set (0008,0005).</summary>
        public static readonly DicomTag SpecificCharacterSet = new DicomTag(0x0008, 0x0005);
        /// <summary>Image Type (0008,0008).</summary>
        public static readonly DicomTag ImageType = new DicomTag(0x0008, 0x0008);
        /// <summary>SOP Class UID (0008,0016).</summary>
        public static readonly DicomTag SOPClassUID = new DicomTag(0x0008, 0x0016);
        /// <summary>SOP Instance UID (0008,0018).</summary>
        public static readonly DicomTag SOPInstanceUID = new DicomTag(0x0008, 0x0018);
        /// <summary>Study Date (0008,0020).</summary>
        public static readonly DicomTag StudyDate = new DicomTag(0x0008, 0x0020);
        /// <summary>Series Date (0008,0021).</summary>
        public static readonly DicomTag SeriesDate = new DicomTag(0x0008, 0x0021);
        /// <summary>Accession Number (0008,0050).</summary>
        public static readonly DicomTag AccessionNumber = new DicomTag(0x0008, 0x0050);
        /// <summary>Modality (0008,0060).</summary>
        public static readonly DicomTag Modality = new DicomTag(0x0008, 0x0060);
        /// <summary>Referring Physician's Name (0008,0090).</summary>
        public static readonly DicomTag ReferringPhysicianName = new DicomTag(0x0008, 0x0090);
        /// <summary>Study Description (0008,1030).</summary>
        public static readonly DicomTag StudyDescription = new DicomTag(0x0008, 0x1030);
        /// <summary>Series Description (0008,103E).</summary>
        public static readonly DicomTag SeriesDescription = new DicomTag(0x0008, 0x103E);
        /// <summary>Patient's Name (0010,0010).</summary>
        public static readonly DicomTag PatientName = new DicomTag(0x0010, 0x0010);
        /// <summary>Patient ID (0010,0020).</summary>
        public static readonly DicomTag PatientID = new DicomTag(0x0010, 0x0020);
        /// <summary>Patient's Birth Date (0010,0030).</summary>
        public static readonly DicomTag PatientBirthDate = new DicomTag(0x0010, 0x0030);
        /// <summary>Patient's Sex (0010,0040).</summary>
        public static readonly DicomTag PatientSex = new DicomTag(0x0010, 0x0040);
        /// <summary>Study Instance UID (0020,000D).</summary>
        public static readonly DicomTag StudyInstanceUID = new DicomTag(0x0020, 0x000D);
        /// <summary>Series Instance UID (0020,000E).</summary>
        public static readonly DicomTag SeriesInstanceUID = new DicomTag(0x0020, 0x000E);
        /// <summary>Study ID (0020,0010).</summary>
        public static readonly DicomTag StudyID = new DicomTag(0x0020, 0x0010);
        /// <summary>Series Number (0020,0011).</summary>
        public static readonly DicomTag SeriesNumber = new DicomTag(0x0020, 0x0011);
        /// <summary>Instance Number (0020,0013).</summary>
        public static readonly DicomTag InstanceNumber = new DicomTag(0x0020, 0x0013);
        /// <summary>Transfer Syntax UID (0002,0010).</summary>
        public static readonly DicomTag TransferSyntaxUID = new DicomTag(0x0002, 0x0010);
        /// <summary>Media Storage SOP Class UID (0002,0002).</summary>
        public static readonly DicomTag MediaStorageSOPClassUID = new DicomTag(0x0002, 0x0002);
        /// <summary>Media Storage SOP Instance UID (0002,0003).</summary>
        public static readonly DicomTag MediaStorageSOPInstanceUID = new DicomTag(0x0002, 0x0003);
    }
}
