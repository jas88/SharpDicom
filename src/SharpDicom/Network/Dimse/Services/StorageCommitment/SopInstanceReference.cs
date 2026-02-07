using System;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// Reference to a SOP Instance for storage commitment operations.
    /// </summary>
    /// <remarks>
    /// Encapsulates the SOP Class UID and SOP Instance UID pair that uniquely
    /// identifies a stored DICOM object. Used in Storage Commitment N-ACTION
    /// requests and N-EVENT-REPORT results per DICOM PS3.4 Annex J.
    /// </remarks>
    public readonly struct SopInstanceReference : IEquatable<SopInstanceReference>
    {
        /// <summary>
        /// Gets the SOP Class UID of the referenced instance.
        /// </summary>
        public DicomUID SOPClassUID { get; }

        /// <summary>
        /// Gets the SOP Instance UID of the referenced instance.
        /// </summary>
        public DicomUID SOPInstanceUID { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SopInstanceReference"/> struct.
        /// </summary>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="sopInstanceUid">The SOP Instance UID.</param>
        public SopInstanceReference(DicomUID sopClassUid, DicomUID sopInstanceUid)
        {
            SOPClassUID = sopClassUid;
            SOPInstanceUID = sopInstanceUid;
        }

        /// <inheritdoc />
        public bool Equals(SopInstanceReference other)
            => SOPClassUID == other.SOPClassUID && SOPInstanceUID == other.SOPInstanceUID;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SopInstanceReference other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            unchecked
            {
                return (SOPClassUID.GetHashCode() * 397) ^ SOPInstanceUID.GetHashCode();
            }
#else
            return HashCode.Combine(SOPClassUID, SOPInstanceUID);
#endif
        }

        /// <inheritdoc />
        public override string ToString() => $"{SOPClassUID} / {SOPInstanceUID}";

        /// <summary>
        /// Determines whether two SOP Instance references are equal.
        /// </summary>
        public static bool operator ==(SopInstanceReference left, SopInstanceReference right) => left.Equals(right);

        /// <summary>
        /// Determines whether two SOP Instance references are not equal.
        /// </summary>
        public static bool operator !=(SopInstanceReference left, SopInstanceReference right) => !left.Equals(right);
    }
}
