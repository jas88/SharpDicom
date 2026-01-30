using System;
using System.Text.RegularExpressions;

namespace SharpDicom.Network.Items
{
    /// <summary>
    /// Represents the User Information sub-items exchanged during DICOM association negotiation.
    /// </summary>
    /// <remarks>
    /// User Information is part of the A-ASSOCIATE-RQ and A-ASSOCIATE-AC PDUs.
    /// It contains the maximum PDU length and implementation identification.
    /// </remarks>
    public sealed class UserInformation
    {
        private static readonly Lazy<UserInformation> s_default = new(CreateDefault);
        // Per DICOM PS3.5 section 9.1, UID components must not have leading zeros (except "0" itself)
        // Pattern: component is either "0" or a non-zero digit followed by any digits
        private static readonly Regex s_uidPattern = new(@"^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))*$", RegexOptions.Compiled);

        /// <summary>
        /// Gets the maximum PDU length that the entity can receive.
        /// </summary>
        /// <remarks>
        /// The receiver uses this value to determine the maximum size of P-DATA-TF PDUs to send.
        /// A value of 0 indicates no limit, though this is rarely used in practice.
        /// </remarks>
        public uint MaxPduLength { get; }

        /// <summary>
        /// Gets the Implementation Class UID identifying the implementation.
        /// </summary>
        /// <remarks>
        /// This UID uniquely identifies the DICOM implementation.
        /// Per DICOM PS3.7, this is a mandatory field.
        /// </remarks>
        public string ImplementationClassUid { get; }

        /// <summary>
        /// Gets the optional Implementation Version Name.
        /// </summary>
        /// <remarks>
        /// An optional string (max 16 characters) that identifies the version of the implementation.
        /// </remarks>
        public string? ImplementationVersionName { get; }

        /// <summary>
        /// Gets the default <see cref="UserInformation"/> for SharpDicom.
        /// </summary>
        public static UserInformation Default => s_default.Value;

        /// <summary>
        /// Initializes a new instance of <see cref="UserInformation"/>.
        /// </summary>
        /// <param name="maxPduLength">The maximum PDU length (must be at least <see cref="PduConstants.MinMaxPduLength"/> or 0 for unlimited).</param>
        /// <param name="implementationClassUid">The implementation class UID (must be valid UID format).</param>
        /// <param name="implementationVersionName">The optional implementation version name (max 16 characters).</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="maxPduLength"/> is non-zero but less than <see cref="PduConstants.MinMaxPduLength"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="implementationClassUid"/> is null, empty, or not a valid UID format,
        /// or when <paramref name="implementationVersionName"/> exceeds 16 characters.
        /// </exception>
        public UserInformation(uint maxPduLength, string implementationClassUid, string? implementationVersionName = null)
        {
            if (maxPduLength != 0 && maxPduLength < PduConstants.MinMaxPduLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPduLength),
                    maxPduLength,
                    $"Maximum PDU length must be 0 (unlimited) or at least {PduConstants.MinMaxPduLength}.");
            }

            if (string.IsNullOrWhiteSpace(implementationClassUid))
                throw new ArgumentException("Implementation Class UID cannot be null or empty.", nameof(implementationClassUid));

            if (!IsValidUid(implementationClassUid))
                throw new ArgumentException("Implementation Class UID is not a valid UID format.", nameof(implementationClassUid));

            if (implementationVersionName != null && implementationVersionName.Length > PduConstants.MaxImplementationVersionNameLength)
            {
                throw new ArgumentException(
                    $"Implementation Version Name cannot exceed {PduConstants.MaxImplementationVersionNameLength} characters.",
                    nameof(implementationVersionName));
            }

            MaxPduLength = maxPduLength;
            ImplementationClassUid = implementationClassUid;
            ImplementationVersionName = implementationVersionName;
        }

        /// <summary>
        /// Creates a <see cref="UserInformation"/> instance with just the max PDU length changed.
        /// </summary>
        /// <param name="maxPduLength">The new maximum PDU length.</param>
        /// <returns>A new <see cref="UserInformation"/> with the specified PDU length.</returns>
        public UserInformation WithMaxPduLength(uint maxPduLength)
        {
            return new UserInformation(maxPduLength, ImplementationClassUid, ImplementationVersionName);
        }

        private static UserInformation CreateDefault()
        {
            // Generate a stable implementation UID using the 2.25 prefix (UUID-derived)
            // We use a fixed GUID-based UID for SharpDicom
            // 2.25.{decimal representation of a fixed UUID for SharpDicom}
            const string implementationUid = "2.25.329800735698586629295641978511506172928";

            return new UserInformation(
                PduConstants.DefaultMaxPduLength,
                implementationUid,
                "SharpDicom");
        }

        private static bool IsValidUid(string uid)
        {
            if (string.IsNullOrEmpty(uid) || uid.Length > 64)
                return false;

            return s_uidPattern.IsMatch(uid);
        }
    }
}
