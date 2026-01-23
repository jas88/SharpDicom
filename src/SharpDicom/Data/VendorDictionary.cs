using System;
using System.Collections.Concurrent;

namespace SharpDicom.Data
{
    /// <summary>
    /// Lookup for vendor private tag definitions.
    /// </summary>
    public static partial class VendorDictionary
    {
        private static readonly ConcurrentDictionary<(string, byte), PrivateTagInfo> s_userDictionary = new();

        /// <summary>
        /// Gets information about a private tag by creator and element offset.
        /// </summary>
        /// <param name="creator">The private creator string.</param>
        /// <param name="elementOffset">The element offset (0x00-0xFF).</param>
        /// <returns>Tag information, or null if not found.</returns>
        public static PrivateTagInfo? GetInfo(string creator, byte elementOffset)
        {
            if (string.IsNullOrEmpty(creator))
            {
                return null;
            }

            var key = (NormalizeCreator(creator), elementOffset);

            // User dictionary takes precedence
            if (s_userDictionary.TryGetValue(key, out var userInfo))
            {
                return userInfo;
            }

            // Fall back to generated
            if (s_tagLookup.TryGetValue(key, out var info))
            {
                var vr = new DicomVR(info.VR);
                return new PrivateTagInfo(creator, elementOffset, vr, info.Keyword, info.Name, false);
            }

            return null;
        }

        /// <summary>
        /// Gets information about a private tag by creator and full element number.
        /// </summary>
        /// <param name="creator">The private creator string.</param>
        /// <param name="element">The full element number (extracts low byte as offset).</param>
        /// <returns>Tag information, or null if not found.</returns>
        public static PrivateTagInfo? GetInfo(string creator, ushort element)
            => GetInfo(creator, (byte)(element & 0xFF));

        /// <summary>
        /// Checks if a creator string is known in the vendor dictionary.
        /// </summary>
        /// <param name="creator">The private creator string.</param>
        /// <returns>True if the creator is known.</returns>
        public static bool IsKnownCreator(string creator)
        {
            if (string.IsNullOrEmpty(creator))
            {
                return false;
            }

            return s_knownCreators.Contains(NormalizeCreator(creator));
        }

        /// <summary>
        /// Registers a user-defined private tag.
        /// </summary>
        /// <param name="info">The private tag information.</param>
        public static void Register(PrivateTagInfo info)
        {
            var key = (NormalizeCreator(info.Creator), info.ElementOffset);
            s_userDictionary[key] = info;
        }

        private static string NormalizeCreator(string creator)
        {
            return creator.Trim().ToUpperInvariant();
        }
    }
}
