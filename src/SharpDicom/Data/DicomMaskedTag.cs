using System;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM tag pattern with masked bits for matching multiple tags.
    /// </summary>
    /// <remarks>
    /// Masked tags are used in the DICOM standard for tag patterns like (50xx,0010)
    /// where 'xx' can match any hex digits. The mask specifies which bits must match
    /// exactly, and the card (cardinal) specifies the expected value after masking.
    /// </remarks>
    public readonly struct DicomMaskedTag : IEquatable<DicomMaskedTag>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomMaskedTag"/> struct.
        /// </summary>
        /// <param name="mask">The mask specifying which bits must match (0xFFFF for fixed nibbles, 0xFF00 for xx).</param>
        /// <param name="card">The expected value after applying the mask.</param>
        public DicomMaskedTag(uint mask, uint card)
        {
            Mask = mask;
            Card = card;
        }

        /// <summary>
        /// Gets the mask specifying which bits must match.
        /// </summary>
        /// <remarks>
        /// 0xFFFF = all bits must match (fixed)
        /// 0xFF00 = high byte must match, low byte is wildcard (xx)
        /// 0x00FF = low byte must match, high byte is wildcard
        /// </remarks>
        public uint Mask { get; }

        /// <summary>
        /// Gets the expected value after applying the mask.
        /// </summary>
        public uint Card { get; }

        /// <summary>
        /// Determines whether the specified tag matches this pattern.
        /// </summary>
        /// <param name="tag">The tag to test.</param>
        /// <returns>true if the tag matches this pattern; otherwise, false.</returns>
        public bool Matches(DicomTag tag) => (tag.Value & Mask) == Card;

        /// <summary>
        /// Creates a masked tag from a pattern string.
        /// </summary>
        /// <param name="pattern">The pattern string in format "(GGxx,EExx)" or "(GGGG,EEEE)" where x represents wildcards.</param>
        /// <returns>A DicomMaskedTag representing the pattern.</returns>
        /// <exception cref="DicomTagException">Thrown when the pattern is invalid.</exception>
        public static DicomMaskedTag FromPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new DicomTagException("Pattern cannot be null or whitespace");

            pattern = pattern.Trim();

            // Expected format: (GGxx,EExx) or variations
            if (!pattern.StartsWith("(") || !pattern.EndsWith(")") || !pattern.Contains(","))
                throw new DicomTagException($"Invalid masked tag pattern: {pattern}");

            pattern = pattern.Substring(1, pattern.Length - 2); // Remove parentheses
            var parts = pattern.Split(',');
            if (parts.Length != 2)
                throw new DicomTagException($"Invalid masked tag pattern: expected (GGGG,EEEE), got {pattern}");

            var groupPart = parts[0].Trim().ToUpperInvariant();
            var elementPart = parts[1].Trim().ToUpperInvariant();

            if (groupPart.Length != 4 || elementPart.Length != 4)
                throw new DicomTagException($"Invalid masked tag pattern: group and element must be 4 characters each");

            uint groupMask = ParseMaskedValue(groupPart, out ushort groupCard);
            uint elementMask = ParseMaskedValue(elementPart, out ushort elementCard);

            uint mask = (groupMask << 16) | elementMask;
            uint card = ((uint)groupCard << 16) | elementCard;

            return new DicomMaskedTag(mask, card);
        }

        private static uint ParseMaskedValue(string value, out ushort card)
        {
            uint mask = 0;
            ushort cardValue = 0;

            for (int i = 0; i < 4; i++)
            {
                char c = value[i];
                if (c == 'X')
                {
                    // Wildcard - don't set mask bits for this nibble
                    mask = (mask << 4);
                    cardValue = (ushort)(cardValue << 4);
                }
                else if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))
                {
                    // Hex digit - set mask bits and card value
                    mask = (mask << 4) | 0xF;
                    byte nibble = c <= '9' ? (byte)(c - '0') : (byte)(c - 'A' + 10);
                    cardValue = (ushort)((cardValue << 4) | nibble);
                }
                else
                {
                    throw new DicomTagException($"Invalid character in masked tag pattern: {c}");
                }
            }

            card = cardValue;
            return mask;
        }

        /// <summary>
        /// Determines whether this masked tag equals another masked tag.
        /// </summary>
        /// <param name="other">The masked tag to compare with.</param>
        /// <returns>true if the masked tags are equal; otherwise, false.</returns>
        public bool Equals(DicomMaskedTag other) => Mask == other.Mask && Card == other.Card;

        /// <summary>
        /// Determines whether this masked tag equals another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the object is a DicomMaskedTag and equals this masked tag; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is DicomMaskedTag other && Equals(other);

        /// <summary>
        /// Returns the hash code for this masked tag.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            unchecked
            {
                return ((int)Mask * 397) ^ (int)Card;
            }
#else
            return HashCode.Combine(Mask, Card);
#endif
        }

        /// <summary>
        /// Returns a string representation of this masked tag pattern.
        /// </summary>
        /// <returns>A string representation in the format (GGxx,EExx).</returns>
        public override string ToString()
        {
            static string FormatMaskedValue(uint value, uint mask)
            {
                var chars = new char[4];
                for (int i = 0; i < 4; i++)
                {
                    int shift = (3 - i) * 4;
                    if (((mask >> shift) & 0xF) == 0xF)
                    {
                        // Fixed nibble
                        byte nibble = (byte)((value >> shift) & 0xF);
                        chars[i] = nibble < 10 ? (char)('0' + nibble) : (char)('A' + nibble - 10);
                    }
                    else
                    {
                        // Wildcard
                        chars[i] = 'x';
                    }
                }
                return new string(chars);
            }

            var group = FormatMaskedValue(Card >> 16, Mask >> 16);
            var element = FormatMaskedValue(Card & 0xFFFF, Mask & 0xFFFF);
            return $"({group},{element})";
        }

        /// <summary>
        /// Determines whether two masked tags are equal.
        /// </summary>
        /// <param name="left">The first masked tag.</param>
        /// <param name="right">The second masked tag.</param>
        /// <returns>true if the masked tags are equal; otherwise, false.</returns>
        public static bool operator ==(DicomMaskedTag left, DicomMaskedTag right) => left.Equals(right);

        /// <summary>
        /// Determines whether two masked tags are not equal.
        /// </summary>
        /// <param name="left">The first masked tag.</param>
        /// <param name="right">The second masked tag.</param>
        /// <returns>true if the masked tags are not equal; otherwise, false.</returns>
        public static bool operator !=(DicomMaskedTag left, DicomMaskedTag right) => !left.Equals(right);
    }
}
