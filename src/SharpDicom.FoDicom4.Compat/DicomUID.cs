using System;

namespace Dicom
{
    /// <summary>
    /// Type of DICOM UID.
    /// </summary>
    public enum DicomUidType
    {
        /// <summary>SOP Class.</summary>
        SOPClass,
        /// <summary>Transfer Syntax.</summary>
        TransferSyntax,
        /// <summary>Well-known SOP Instance.</summary>
        SOPInstance,
        /// <summary>Meta SOP Class.</summary>
        MetaSOPClass,
        /// <summary>Service Class.</summary>
        ServiceClass,
        /// <summary>Application Context Name.</summary>
        ApplicationContextName,
        /// <summary>Coding Scheme.</summary>
        CodingScheme,
        /// <summary>Unknown or unclassified.</summary>
        Unknown
    }

    /// <summary>
    /// Wrapper for DICOM UID values, matching fo-dicom 4.x DicomUID.
    /// </summary>
    public sealed class DicomUID : IEquatable<DicomUID>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomUID"/> class.
        /// </summary>
        /// <param name="uid">The UID string.</param>
        /// <param name="name">The human-readable name.</param>
        /// <param name="type">The UID type.</param>
        public DicomUID(string uid, string name, DicomUidType type)
        {
            UID = uid ?? throw new ArgumentNullException(nameof(uid));
            Name = name ?? "";
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomUID"/> class with just a UID string.
        /// </summary>
        /// <param name="uid">The UID string.</param>
        public DicomUID(string uid)
            : this(uid, "", DicomUidType.Unknown)
        {
        }

        /// <summary>
        /// Gets the UID string value.
        /// </summary>
        public string UID { get; }

        /// <summary>
        /// Gets the human-readable name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the UID type.
        /// </summary>
        public DicomUidType Type { get; }

        /// <inheritdoc />
        public bool Equals(DicomUID? other)
        {
            if (other is null) return false;
            return string.Equals(UID, other.UID, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is DicomUID other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => UID.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => UID;

        /// <summary>
        /// Determines whether two UIDs are equal.
        /// </summary>
        public static bool operator ==(DicomUID? left, DicomUID? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two UIDs are not equal.
        /// </summary>
        public static bool operator !=(DicomUID? left, DicomUID? right) => !(left == right);

        // Well-known UIDs

        /// <summary>Verification SOP Class.</summary>
        public static readonly DicomUID Verification =
            new DicomUID("1.2.840.10008.1.1", "Verification SOP Class", DicomUidType.SOPClass);

        /// <summary>Study Root Query/Retrieve Information Model - FIND.</summary>
        public static readonly DicomUID StudyRootQueryRetrieveInformationModelFind =
            new DicomUID("1.2.840.10008.5.1.4.1.2.2.1", "Study Root Query/Retrieve Information Model - FIND", DicomUidType.SOPClass);

        /// <summary>Explicit VR Little Endian transfer syntax.</summary>
        public static readonly DicomUID ExplicitVRLittleEndian =
            new DicomUID("1.2.840.10008.1.2.1", "Explicit VR Little Endian", DicomUidType.TransferSyntax);

        /// <summary>Implicit VR Little Endian transfer syntax.</summary>
        public static readonly DicomUID ImplicitVRLittleEndian =
            new DicomUID("1.2.840.10008.1.2", "Implicit VR Little Endian", DicomUidType.TransferSyntax);
    }
}
