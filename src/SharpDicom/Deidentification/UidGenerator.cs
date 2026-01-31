using System;
using System.Numerics;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Generates DICOM-compliant UIDs using UUID-derived 2.25.xxx format.
    /// </summary>
    /// <remarks>
    /// UIDs generated follow the 2.25 root which is reserved for UUID-derived UIDs
    /// per RFC 4122. This ensures globally unique, collision-free UIDs without
    /// requiring a registered root.
    /// </remarks>
    public static class UidGenerator
    {
        /// <summary>
        /// The root for UUID-derived UIDs as defined by ISO/IEC 9834-8:2005.
        /// </summary>
        public const string UuidRoot = "2.25.";

        /// <summary>
        /// Maximum allowed length for a DICOM UID.
        /// </summary>
        public const int MaxUidLength = 64;

        /// <summary>
        /// Generates a new unique UID using UUID-derived 2.25.xxx format.
        /// </summary>
        /// <returns>A globally unique DICOM UID.</returns>
        public static string GenerateUid()
        {
            var uuid = Guid.NewGuid();
            return UuidToUid(uuid);
        }

        /// <summary>
        /// Generates a deterministic UID from a seed value.
        /// </summary>
        /// <param name="seed">The seed string (e.g., original UID + context).</param>
        /// <returns>A deterministic UID that will be the same for the same seed.</returns>
        /// <remarks>
        /// This is useful for generating consistent UIDs across sessions when
        /// given the same input. Uses SHA-256 hash truncated to UUID format.
        /// </remarks>
        public static string GenerateDeterministicUid(string seed)
        {
            if (string.IsNullOrEmpty(seed))
            {
                throw new ArgumentNullException(nameof(seed));
            }

#if NETSTANDARD2_0
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(seed);
            var hash = sha.ComputeHash(bytes);
#else
            var bytes = System.Text.Encoding.UTF8.GetBytes(seed);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
#endif

            // Take first 16 bytes to create a UUID-like value
            var uuidBytes = new byte[16];
            Array.Copy(hash, uuidBytes, 16);

            // Set version to 5 (name-based SHA-1) - though we're using SHA-256
            uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x50);
            // Set variant to RFC 4122
            uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

            var uuid = new Guid(uuidBytes);
            return UuidToUid(uuid);
        }

        /// <summary>
        /// Converts a UUID to a DICOM UID in 2.25.xxx format.
        /// </summary>
        /// <param name="uuid">The UUID to convert.</param>
        /// <returns>The corresponding DICOM UID.</returns>
        public static string UuidToUid(Guid uuid)
        {
            // Convert UUID to 128-bit integer and then to decimal string
            var bytes = uuid.ToByteArray();

            // .NET Guid byte order is different from RFC 4122
            // We need to reverse the byte groups to get the correct integer
            // Guid stores first 3 fields in little-endian, last 2 in big-endian
            // Convert to big-endian for consistent integer representation
            var bigEndianBytes = new byte[16];
            bigEndianBytes[0] = bytes[3];
            bigEndianBytes[1] = bytes[2];
            bigEndianBytes[2] = bytes[1];
            bigEndianBytes[3] = bytes[0];
            bigEndianBytes[4] = bytes[5];
            bigEndianBytes[5] = bytes[4];
            bigEndianBytes[6] = bytes[7];
            bigEndianBytes[7] = bytes[6];
            Array.Copy(bytes, 8, bigEndianBytes, 8, 8);

#if NETSTANDARD2_0 || NET6_0 || NET7_0
            // BigInteger needs little-endian, plus unsigned flag byte
            Array.Reverse(bigEndianBytes);
            var unsigned = new byte[17];
            Array.Copy(bigEndianBytes, 0, unsigned, 0, 16);
            unsigned[16] = 0; // Ensure positive
            var value = new BigInteger(unsigned);
#else
            // BigInteger with unsigned big-endian
            var value = new BigInteger(bigEndianBytes, isUnsigned: true, isBigEndian: true);
#endif

            var uid = UuidRoot + value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Ensure UID doesn't exceed max length (shouldn't happen with 2.25. root)
            if (uid.Length > MaxUidLength)
            {
                throw new InvalidOperationException($"Generated UID exceeds maximum length of {MaxUidLength}");
            }

            return uid;
        }

        /// <summary>
        /// Validates that a string is a valid DICOM UID.
        /// </summary>
        /// <param name="uid">The UID to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValidUid(string uid)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            if (uid.Length > MaxUidLength)
            {
                return false;
            }

            // UID must start with digit (not 0 unless single component is 0)
            // and contain only digits and dots
            bool lastWasDot = true;
            bool componentStartedWithZero = false;

            foreach (var c in uid)
            {
                if (c == '.')
                {
                    if (lastWasDot)
                    {
                        return false; // Empty component
                    }
                    lastWasDot = true;
                    componentStartedWithZero = false;
                }
                else if (c >= '0' && c <= '9')
                {
                    if (lastWasDot)
                    {
                        componentStartedWithZero = (c == '0');
                        lastWasDot = false;
                    }
                    else if (componentStartedWithZero)
                    {
                        return false; // Leading zero in multi-digit component
                    }
                }
                else
                {
                    return false; // Invalid character
                }
            }

            // Cannot end with dot
            return !lastWasDot;
        }
    }
}
