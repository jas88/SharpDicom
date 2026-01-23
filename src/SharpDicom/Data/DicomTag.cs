using System;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM tag as a compact 4-byte structure containing group and element numbers.
    /// </summary>
    /// <remarks>
    /// DICOM tags uniquely identify data elements and are represented as (GGGG,EEEE) where
    /// GGGG is the group number and EEEE is the element number, both in hexadecimal.
    /// Tags with odd group numbers are private tags.
    /// </remarks>
    public readonly partial struct DicomTag : IEquatable<DicomTag>, IComparable<DicomTag>
    {
        private readonly uint _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTag"/> struct from group and element numbers.
        /// </summary>
        /// <param name="group">The group number (0x0000-0xFFFF).</param>
        /// <param name="element">The element number (0x0000-0xFFFF).</param>
        public DicomTag(ushort group, ushort element)
        {
            _value = ((uint)group << 16) | element;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTag"/> struct from a 32-bit value.
        /// </summary>
        /// <param name="value">The combined tag value (group in high 16 bits, element in low 16 bits).</param>
        public DicomTag(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// Gets the group number of this tag.
        /// </summary>
        public ushort Group => (ushort)(_value >> 16);

        /// <summary>
        /// Gets the element number of this tag.
        /// </summary>
        public ushort Element => (ushort)_value;

        /// <summary>
        /// Gets the combined 32-bit value of this tag.
        /// </summary>
        public uint Value => _value;

        /// <summary>
        /// Gets a value indicating whether this tag is a private tag (odd group number).
        /// </summary>
        public bool IsPrivate => (Group & 1) == 1;

        /// <summary>
        /// Gets a value indicating whether this tag is a private creator element.
        /// Private creator elements are located at (GGGG,00xx) where GGGG is odd and xx is 0x10-0xFF.
        /// </summary>
        public bool IsPrivateCreator => IsPrivate && Element > 0x0000 && Element <= 0x00FF;

        /// <summary>
        /// Gets the private creator slot (0x10-0xFF) for private data elements.
        /// For private data elements (GGGG,xxyy where xx >= 0x10), returns the xx byte.
        /// Returns 0 for non-private or private creator elements.
        /// </summary>
        public byte PrivateCreatorSlot => IsPrivate && Element > 0x00FF ? (byte)(Element >> 8) : (byte)0;

        /// <summary>
        /// Gets the key for looking up the private creator in a PrivateCreatorDictionary.
        /// Combines the group number and creator slot: (GGGG &lt;&lt; 16) | (xx &lt;&lt; 8).
        /// Returns 0 for non-private or private creator elements.
        /// </summary>
        public uint PrivateCreatorKey => IsPrivate && Element > 0x00FF
            ? ((uint)Group << 16) | (uint)(Element >> 8)
            : 0;

        /// <summary>
        /// Parses a DICOM tag from a string representation.
        /// </summary>
        /// <param name="value">The string to parse. Accepted formats: "(GGGG,EEEE)" or "GGGGEEEE".</param>
        /// <returns>The parsed DICOM tag.</returns>
        /// <exception cref="DicomTagException">Thrown when the string cannot be parsed as a valid tag.</exception>
        public static DicomTag Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DicomTagException("Tag string cannot be null or whitespace");

            value = value.Trim();

            // Format: (GGGG,EEEE)
            if (value.StartsWith("(") && value.EndsWith(")") && value.Contains(","))
            {
                value = value.Substring(1, value.Length - 2); // Remove parentheses
                var parts = value.Split(',');
                if (parts.Length != 2)
                    throw new DicomTagException($"Invalid tag format: expected (GGGG,EEEE), got {value}");

                if (!ushort.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.HexNumber, null, out var group))
                    throw new DicomTagException($"Invalid group number: {parts[0]}");

                if (!ushort.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.HexNumber, null, out var element))
                    throw new DicomTagException($"Invalid element number: {parts[1]}");

                return new DicomTag(group, element);
            }

            // Format: GGGGEEEE
            if (value.Length == 8)
            {
                if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var tagValue))
                    throw new DicomTagException($"Invalid tag format: {value}");

                return new DicomTag(tagValue);
            }

            throw new DicomTagException($"Invalid tag format: expected (GGGG,EEEE) or GGGGEEEE, got {value}");
        }

        /// <summary>
        /// Attempts to parse a DICOM tag from a string representation.
        /// </summary>
        /// <param name="value">The string to parse.</param>
        /// <param name="tag">When this method returns, contains the parsed tag if successful; otherwise, the default value.</param>
        /// <returns>true if parsing succeeded; otherwise, false.</returns>
        public static bool TryParse(string value, out DicomTag tag)
        {
            try
            {
                tag = Parse(value);
                return true;
            }
            catch
            {
                tag = default;
                return false;
            }
        }

        /// <summary>
        /// Determines whether this tag equals another tag.
        /// </summary>
        /// <param name="other">The tag to compare with.</param>
        /// <returns>true if the tags are equal; otherwise, false.</returns>
        public bool Equals(DicomTag other) => _value == other._value;

        /// <summary>
        /// Determines whether this tag equals another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the object is a DicomTag and equals this tag; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is DicomTag other && Equals(other);

        /// <summary>
        /// Returns the hash code for this tag.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode() => (int)_value;

        /// <summary>
        /// Compares this tag with another tag for ordering purposes.
        /// </summary>
        /// <param name="other">The tag to compare with.</param>
        /// <returns>A value indicating the relative order of the tags.</returns>
        public int CompareTo(DicomTag other) => _value.CompareTo(other._value);

        /// <summary>
        /// Returns a string representation of this tag in the format (GGGG,EEEE).
        /// </summary>
        /// <returns>A string representation of this tag.</returns>
        public override string ToString() => $"({Group:X4},{Element:X4})";

        /// <summary>
        /// Determines whether two tags are equal.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if the tags are equal; otherwise, false.</returns>
        public static bool operator ==(DicomTag left, DicomTag right) => left.Equals(right);

        /// <summary>
        /// Determines whether two tags are not equal.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if the tags are not equal; otherwise, false.</returns>
        public static bool operator !=(DicomTag left, DicomTag right) => !left.Equals(right);

        /// <summary>
        /// Determines whether one tag is less than another.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if left is less than right; otherwise, false.</returns>
        public static bool operator <(DicomTag left, DicomTag right) => left._value < right._value;

        /// <summary>
        /// Determines whether one tag is greater than another.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if left is greater than right; otherwise, false.</returns>
        public static bool operator >(DicomTag left, DicomTag right) => left._value > right._value;

        /// <summary>
        /// Determines whether one tag is less than or equal to another.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if left is less than or equal to right; otherwise, false.</returns>
        public static bool operator <=(DicomTag left, DicomTag right) => left._value <= right._value;

        /// <summary>
        /// Determines whether one tag is greater than or equal to another.
        /// </summary>
        /// <param name="left">The first tag.</param>
        /// <param name="right">The second tag.</param>
        /// <returns>true if left is greater than or equal to right; otherwise, false.</returns>
        public static bool operator >=(DicomTag left, DicomTag right) => left._value >= right._value;
    }
}
