namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// JPEG marker constants as defined in ITU-T T.81 (JPEG specification).
    /// </summary>
    /// <remarks>
    /// JPEG markers are two-byte sequences starting with 0xFF followed by a marker code.
    /// Markers are used to delimit image segments and identify their types.
    /// </remarks>
    public static class JpegMarkers
    {
        /// <summary>
        /// Marker prefix byte. All JPEG markers start with this byte.
        /// </summary>
        public const byte Prefix = 0xFF;

        // Start/End of Image
        /// <summary>Start of Image marker.</summary>
        public const byte SOI = 0xD8;

        /// <summary>End of Image marker.</summary>
        public const byte EOI = 0xD9;

        // Start of Frame markers (non-differential, Huffman coding)
        /// <summary>Start of Frame, Baseline DCT (lossy, 8-bit).</summary>
        public const byte SOF0 = 0xC0;

        /// <summary>Start of Frame, Extended sequential DCT (lossy, 8/12-bit).</summary>
        public const byte SOF1 = 0xC1;

        /// <summary>Start of Frame, Progressive DCT (lossy).</summary>
        public const byte SOF2 = 0xC2;

        /// <summary>Start of Frame, Lossless (sequential, Huffman).</summary>
        public const byte SOF3 = 0xC3;

        // Start of Frame markers (differential, Huffman coding)
        /// <summary>Start of Frame, Differential sequential DCT.</summary>
        public const byte SOF5 = 0xC5;

        /// <summary>Start of Frame, Differential progressive DCT.</summary>
        public const byte SOF6 = 0xC6;

        /// <summary>Start of Frame, Differential lossless (sequential).</summary>
        public const byte SOF7 = 0xC7;

        // Start of Frame markers (non-differential, arithmetic coding)
        /// <summary>Start of Frame, Reserved for JPEG extensions (arithmetic coding).</summary>
        public const byte JPG = 0xC8;

        /// <summary>Start of Frame, Extended sequential DCT, arithmetic coding.</summary>
        public const byte SOF9 = 0xC9;

        /// <summary>Start of Frame, Progressive DCT, arithmetic coding.</summary>
        public const byte SOF10 = 0xCA;

        /// <summary>Start of Frame, Lossless (sequential), arithmetic coding.</summary>
        public const byte SOF11 = 0xCB;

        // Start of Frame markers (differential, arithmetic coding)
        /// <summary>Start of Frame, Differential sequential DCT, arithmetic coding.</summary>
        public const byte SOF13 = 0xCD;

        /// <summary>Start of Frame, Differential progressive DCT, arithmetic coding.</summary>
        public const byte SOF14 = 0xCE;

        /// <summary>Start of Frame, Differential lossless (sequential), arithmetic coding.</summary>
        public const byte SOF15 = 0xCF;

        // Table/Miscellaneous markers
        /// <summary>Define Huffman Table marker.</summary>
        public const byte DHT = 0xC4;

        /// <summary>Define Arithmetic Coding conditioning marker.</summary>
        public const byte DAC = 0xCC;

        /// <summary>Define Quantization Table marker.</summary>
        public const byte DQT = 0xDB;

        /// <summary>Define Number of Lines marker.</summary>
        public const byte DNL = 0xDC;

        /// <summary>Define Restart Interval marker.</summary>
        public const byte DRI = 0xDD;

        /// <summary>Define Hierarchical Progression marker.</summary>
        public const byte DHP = 0xDE;

        /// <summary>Expand Reference Component marker.</summary>
        public const byte EXP = 0xDF;

        // Scan marker
        /// <summary>Start of Scan marker.</summary>
        public const byte SOS = 0xDA;

        // Restart markers (RST0-RST7)
        /// <summary>Restart marker 0.</summary>
        public const byte RST0 = 0xD0;

        /// <summary>Restart marker 1.</summary>
        public const byte RST1 = 0xD1;

        /// <summary>Restart marker 2.</summary>
        public const byte RST2 = 0xD2;

        /// <summary>Restart marker 3.</summary>
        public const byte RST3 = 0xD3;

        /// <summary>Restart marker 4.</summary>
        public const byte RST4 = 0xD4;

        /// <summary>Restart marker 5.</summary>
        public const byte RST5 = 0xD5;

        /// <summary>Restart marker 6.</summary>
        public const byte RST6 = 0xD6;

        /// <summary>Restart marker 7.</summary>
        public const byte RST7 = 0xD7;

        // Application markers (APP0-APP15)
        /// <summary>Application marker 0 (JFIF).</summary>
        public const byte APP0 = 0xE0;

        /// <summary>Application marker 1 (EXIF).</summary>
        public const byte APP1 = 0xE1;

        /// <summary>Application marker 2 (ICC profile).</summary>
        public const byte APP2 = 0xE2;

        /// <summary>Application marker 14 (Adobe).</summary>
        public const byte APP14 = 0xEE;

        /// <summary>Application marker 15.</summary>
        public const byte APP15 = 0xEF;

        // JPEG extensions and reserved markers
        /// <summary>JPEG extension markers (JPG0-JPG13).</summary>
        public const byte JPG0 = 0xF0;

        /// <summary>Comment marker.</summary>
        public const byte COM = 0xFE;

        /// <summary>
        /// Determines whether the specified marker is a Start of Frame marker.
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker is a SOF marker; otherwise, false.</returns>
        public static bool IsSOF(byte marker)
        {
            // SOF markers are 0xC0-0xCF, excluding DHT (0xC4), JPG (0xC8), and DAC (0xCC)
            return marker >= 0xC0 && marker <= 0xCF && marker != DHT && marker != JPG && marker != DAC;
        }

        /// <summary>
        /// Determines whether the specified marker is a restart marker (RST0-RST7).
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker is a restart marker; otherwise, false.</returns>
        public static bool IsRST(byte marker)
        {
            return marker >= RST0 && marker <= RST7;
        }

        /// <summary>
        /// Determines whether the specified marker is an application marker (APP0-APP15).
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker is an application marker; otherwise, false.</returns>
        public static bool IsAPP(byte marker)
        {
            return marker >= APP0 && marker <= APP15;
        }

        /// <summary>
        /// Determines whether the specified marker indicates lossless compression.
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker indicates lossless compression; otherwise, false.</returns>
        public static bool IsLossless(byte marker)
        {
            // SOF3, SOF7, SOF11, SOF15 are lossless modes
            return marker == SOF3 || marker == SOF7 || marker == SOF11 || marker == SOF15;
        }

        /// <summary>
        /// Determines whether the specified marker indicates progressive compression.
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker indicates progressive compression; otherwise, false.</returns>
        public static bool IsProgressive(byte marker)
        {
            // SOF2, SOF6, SOF10, SOF14 are progressive modes
            return marker == SOF2 || marker == SOF6 || marker == SOF10 || marker == SOF14;
        }

        /// <summary>
        /// Determines whether the specified marker uses arithmetic coding.
        /// </summary>
        /// <param name="marker">The marker byte (without 0xFF prefix).</param>
        /// <returns>true if the marker uses arithmetic coding; otherwise, false.</returns>
        public static bool IsArithmetic(byte marker)
        {
            // SOF9-SOF11, SOF13-SOF15 use arithmetic coding
            return (marker >= SOF9 && marker <= SOF11) || (marker >= SOF13 && marker <= SOF15);
        }
    }
}
