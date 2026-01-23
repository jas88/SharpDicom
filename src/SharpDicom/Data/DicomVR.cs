using System;

namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM Value Representation (VR) as a compact 2-byte structure.
    /// </summary>
    /// <remarks>
    /// Value Representations define the data type and format of DICOM element values.
    /// Each VR is identified by a two-character ASCII code (e.g., "AE", "DA", "UI").
    /// </remarks>
    public readonly struct DicomVR : IEquatable<DicomVR>
    {
        private readonly ushort _code;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomVR"/> struct from two character bytes.
        /// </summary>
        /// <param name="char1">The first character byte.</param>
        /// <param name="char2">The second character byte.</param>
        public DicomVR(byte char1, byte char2)
        {
            _code = (ushort)((char1 << 8) | char2);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomVR"/> struct from a two-character string.
        /// </summary>
        /// <param name="code">The two-character VR code (e.g., "AE", "DA").</param>
        /// <exception cref="ArgumentException">Thrown when the code is not exactly 2 characters.</exception>
        public DicomVR(string code)
        {
            if (code == null || code.Length != 2)
                throw new ArgumentException("VR code must be exactly 2 characters", nameof(code));
            _code = (ushort)((code[0] << 8) | code[1]);
        }

        /// <summary>
        /// Creates a DicomVR from a byte span.
        /// </summary>
        /// <param name="bytes">A span containing at least 2 bytes.</param>
        /// <returns>A new DicomVR instance.</returns>
        /// <exception cref="ArgumentException">Thrown when the span has fewer than 2 bytes.</exception>
        public static DicomVR FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 2)
                throw new ArgumentException("Span must contain at least 2 bytes", nameof(bytes));
            return new DicomVR(bytes[0], bytes[1]);
        }

        /// <summary>
        /// Gets the first character byte of the VR code.
        /// </summary>
        public byte Char1 => (byte)(_code >> 8);

        /// <summary>
        /// Gets the second character byte of the VR code.
        /// </summary>
        public byte Char2 => (byte)_code;

        /// <summary>
        /// Gets the packed 16-bit code value.
        /// </summary>
        public ushort Code => _code;

        /// <summary>
        /// Gets a value indicating whether this VR is a known standard VR.
        /// </summary>
        public bool IsKnown => DicomVRInfo.IsKnown(this);

        /// <summary>
        /// Determines whether this VR equals another VR.
        /// </summary>
        /// <param name="other">The VR to compare with.</param>
        /// <returns>true if the VRs are equal; otherwise, false.</returns>
        public bool Equals(DicomVR other) => _code == other._code;

        /// <summary>
        /// Determines whether this VR equals another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the object is a DicomVR and equals this VR; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is DicomVR other && Equals(other);

        /// <summary>
        /// Returns the hash code for this VR.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode() => _code;

        /// <summary>
        /// Returns a string representation of this VR.
        /// </summary>
        /// <returns>A two-character string representation.</returns>
        public override string ToString() => $"{(char)Char1}{(char)Char2}";

        /// <summary>
        /// Determines whether two VRs are equal.
        /// </summary>
        /// <param name="left">The first VR.</param>
        /// <param name="right">The second VR.</param>
        /// <returns>true if the VRs are equal; otherwise, false.</returns>
        public static bool operator ==(DicomVR left, DicomVR right) => left.Equals(right);

        /// <summary>
        /// Determines whether two VRs are not equal.
        /// </summary>
        /// <param name="left">The first VR.</param>
        /// <param name="right">The second VR.</param>
        /// <returns>true if the VRs are not equal; otherwise, false.</returns>
        public static bool operator !=(DicomVR left, DicomVR right) => !left.Equals(right);

        // Standard DICOM Value Representations
        /// <summary>Application Entity - String identifying an Application Entity (max 16 chars).</summary>
        public static readonly DicomVR AE = new("AE");

        /// <summary>Age String - String representing patient age (nnnW/M/Y/D).</summary>
        public static readonly DicomVR AS = new("AS");

        /// <summary>Attribute Tag - Pair of 16-bit unsigned integers representing a tag.</summary>
        public static readonly DicomVR AT = new("AT");

        /// <summary>Code String - String with specific character set restrictions (max 16 chars).</summary>
        public static readonly DicomVR CS = new("CS");

        /// <summary>Date - String representing a date (YYYYMMDD).</summary>
        public static readonly DicomVR DA = new("DA");

        /// <summary>Decimal String - String representing a decimal number.</summary>
        public static readonly DicomVR DS = new("DS");

        /// <summary>Date Time - String representing a date and time (YYYYMMDDHHMMSS.FFFFFF&amp;ZZXX).</summary>
        public static readonly DicomVR DT = new("DT");

        /// <summary>Floating Point Single - 32-bit IEEE 754 floating point number.</summary>
        public static readonly DicomVR FL = new("FL");

        /// <summary>Floating Point Double - 64-bit IEEE 754 floating point number.</summary>
        public static readonly DicomVR FD = new("FD");

        /// <summary>Integer String - String representing an integer.</summary>
        public static readonly DicomVR IS = new("IS");

        /// <summary>Long String - Character string (max 64 chars).</summary>
        public static readonly DicomVR LO = new("LO");

        /// <summary>Long Text - Character string (max 10240 chars).</summary>
        public static readonly DicomVR LT = new("LT");

        /// <summary>Other Byte - Byte string.</summary>
        public static readonly DicomVR OB = new("OB");

        /// <summary>Other Double - 64-bit floating point values.</summary>
        public static readonly DicomVR OD = new("OD");

        /// <summary>Other Float - 32-bit floating point values.</summary>
        public static readonly DicomVR OF = new("OF");

        /// <summary>Other Long - 32-bit integer values.</summary>
        public static readonly DicomVR OL = new("OL");

        /// <summary>Other Word - 16-bit integer values.</summary>
        public static readonly DicomVR OW = new("OW");

        /// <summary>Other 64-bit Very Long - 64-bit integer values (DICOM 2020).</summary>
        public static readonly DicomVR OV = new("OV");

        /// <summary>Person Name - Character string representing a person name.</summary>
        public static readonly DicomVR PN = new("PN");

        /// <summary>Short String - Character string (max 16 chars).</summary>
        public static readonly DicomVR SH = new("SH");

        /// <summary>Signed Long - 32-bit signed integer.</summary>
        public static readonly DicomVR SL = new("SL");

        /// <summary>Sequence of Items - Sequence containing nested datasets.</summary>
        public static readonly DicomVR SQ = new("SQ");

        /// <summary>Signed Short - 16-bit signed integer.</summary>
        public static readonly DicomVR SS = new("SS");

        /// <summary>Short Text - Character string (max 1024 chars).</summary>
        public static readonly DicomVR ST = new("ST");

        /// <summary>Signed 64-bit Very Long - 64-bit signed integer (DICOM 2020).</summary>
        public static readonly DicomVR SV = new("SV");

        /// <summary>Time - String representing a time (HHMMSS.FFFFFF).</summary>
        public static readonly DicomVR TM = new("TM");

        /// <summary>Unlimited Characters - Character string with no length limit.</summary>
        public static readonly DicomVR UC = new("UC");

        /// <summary>Unique Identifier (UID) - String containing a UID.</summary>
        public static readonly DicomVR UI = new("UI");

        /// <summary>Unsigned Long - 32-bit unsigned integer.</summary>
        public static readonly DicomVR UL = new("UL");

        /// <summary>Unknown - Used when VR is not known or cannot be determined.</summary>
        public static readonly DicomVR UN = new("UN");

        /// <summary>Universal Resource Identifier - String containing a URI.</summary>
        public static readonly DicomVR UR = new("UR");

        /// <summary>Unsigned Short - 16-bit unsigned integer.</summary>
        public static readonly DicomVR US = new("US");

        /// <summary>Unlimited Text - Character string with no length limit.</summary>
        public static readonly DicomVR UT = new("UT");

        /// <summary>Unsigned 64-bit Very Long - 64-bit unsigned integer (DICOM 2020).</summary>
        public static readonly DicomVR UV = new("UV");

        /// <summary>
        /// Gets a value indicating whether this VR uses 32-bit length encoding in Explicit VR.
        /// </summary>
        /// <remarks>
        /// Long VRs (OB, OD, OF, OL, OV, OW, SQ, SV, UC, UN, UR, UT, UV) use a 12-byte header:
        /// Tag(4) + VR(2) + Reserved(2) + Length(4).
        /// Short VRs use an 8-byte header: Tag(4) + VR(2) + Length(2).
        /// </remarks>
        public bool Is32BitLength => this == OB || this == OD || this == OF ||
                                      this == OL || this == OV || this == OW || this == SQ ||
                                      this == SV || this == UC || this == UN || this == UR ||
                                      this == UT || this == UV;

        /// <summary>
        /// Gets a value indicating whether this VR represents string data.
        /// </summary>
        /// <remarks>
        /// String VRs are: AE, AS, CS, DA, DS, DT, IS, LO, LT, PN, SH, ST, TM, UC, UI, UR, UT
        /// </remarks>
        public bool IsStringVR => this == AE || this == AS || this == CS || this == DA ||
                                   this == DS || this == DT || this == IS || this == LO ||
                                   this == LT || this == PN || this == SH || this == ST ||
                                   this == TM || this == UC || this == UI || this == UR || this == UT;

        /// <summary>
        /// Gets a value indicating whether this VR represents binary numeric data.
        /// </summary>
        /// <remarks>
        /// Numeric VRs are: FL, FD, SL, SS, SV, UL, US, UV, AT
        /// </remarks>
        public bool IsNumericVR => this == FL || this == FD || this == SL || this == SS ||
                                    this == SV || this == UL || this == US || this == UV || this == AT;
    }
}
