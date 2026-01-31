using System;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// JPEG 2000 codestream marker constants per ITU-T T.800.
    /// </summary>
    /// <remarks>
    /// All JPEG 2000 markers are two-byte sequences in big-endian format.
    /// The first byte is always 0xFF, followed by the marker code.
    /// </remarks>
    public static class J2kMarkers
    {
        // Delimiting markers

        /// <summary>Start of codestream marker.</summary>
        public const ushort SOC = 0xFF4F;

        /// <summary>Start of tile-part marker.</summary>
        public const ushort SOT = 0xFF90;

        /// <summary>Start of data marker.</summary>
        public const ushort SOD = 0xFF93;

        /// <summary>End of codestream marker.</summary>
        public const ushort EOC = 0xFFD9;

        // Fixed information markers

        /// <summary>Image and tile size marker.</summary>
        public const ushort SIZ = 0xFF51;

        /// <summary>Coding style default marker.</summary>
        public const ushort COD = 0xFF52;

        /// <summary>Coding style component marker.</summary>
        public const ushort COC = 0xFF53;

        /// <summary>Quantization default marker.</summary>
        public const ushort QCD = 0xFF5C;

        /// <summary>Quantization component marker.</summary>
        public const ushort QCC = 0xFF5D;

        // Functional markers

        /// <summary>Region of interest marker.</summary>
        public const ushort RGN = 0xFF5E;

        /// <summary>Progression order change marker.</summary>
        public const ushort POC = 0xFF5F;

        /// <summary>Packed packet headers (main header) marker.</summary>
        public const ushort PPM = 0xFF60;

        /// <summary>Packed packet headers (tile-part) marker.</summary>
        public const ushort PPT = 0xFF61;

        /// <summary>Tile-part lengths marker.</summary>
        public const ushort TLM = 0xFF55;

        /// <summary>Packet length (main header) marker.</summary>
        public const ushort PLM = 0xFF57;

        /// <summary>Packet length (tile-part) marker.</summary>
        public const ushort PLT = 0xFF58;

        /// <summary>Coding style registration marker.</summary>
        public const ushort CRG = 0xFF63;

        /// <summary>Comment marker.</summary>
        public const ushort COM = 0xFF64;

        /// <summary>
        /// Determines whether the specified marker has a variable-length segment.
        /// </summary>
        /// <param name="marker">The marker value.</param>
        /// <returns>True if the marker has a segment; otherwise, false.</returns>
        public static bool HasSegment(ushort marker)
        {
            // SOC, SOD, EOC have no segment
            // All other markers have a 2-byte length followed by segment data
            return marker != SOC && marker != SOD && marker != EOC;
        }
    }

    /// <summary>
    /// JPEG 2000 progression orders per ITU-T T.800.
    /// </summary>
    public enum ProgressionOrder
    {
        /// <summary>Layer-Resolution-Component-Position ordering.</summary>
        LRCP = 0,

        /// <summary>Resolution-Layer-Component-Position ordering.</summary>
        RLCP = 1,

        /// <summary>Resolution-Position-Component-Layer ordering.</summary>
        RPCL = 2,

        /// <summary>Position-Component-Resolution-Layer ordering.</summary>
        PCRL = 3,

        /// <summary>Component-Position-Resolution-Layer ordering.</summary>
        CPRL = 4
    }

    /// <summary>
    /// Component information from the SIZ marker.
    /// </summary>
    public readonly struct J2kComponentInfo
    {
        /// <summary>Gets the bit depth of the component (1-38).</summary>
        public int BitDepth { get; init; }

        /// <summary>Gets whether the component samples are signed.</summary>
        public bool IsSigned { get; init; }

        /// <summary>Gets the horizontal subsampling factor.</summary>
        public int SubsamplingX { get; init; }

        /// <summary>Gets the vertical subsampling factor.</summary>
        public int SubsamplingY { get; init; }
    }

    /// <summary>
    /// Parsed JPEG 2000 codestream header information.
    /// </summary>
    /// <remarks>
    /// This class parses the main header of a JPEG 2000 codestream (Part 1, ITU-T T.800).
    /// It extracts information from the SIZ (image size) and COD (coding style) markers.
    /// </remarks>
    public sealed class J2kCodestream
    {
        // From SIZ marker

        /// <summary>Gets the reference grid width.</summary>
        public int ImageWidth { get; init; }

        /// <summary>Gets the reference grid height.</summary>
        public int ImageHeight { get; init; }

        /// <summary>Gets the horizontal offset from origin to left edge of image area.</summary>
        public int ImageOffsetX { get; init; }

        /// <summary>Gets the vertical offset from origin to top edge of image area.</summary>
        public int ImageOffsetY { get; init; }

        /// <summary>Gets the nominal tile width.</summary>
        public int TileWidth { get; init; }

        /// <summary>Gets the nominal tile height.</summary>
        public int TileHeight { get; init; }

        /// <summary>Gets the horizontal offset from origin to left edge of first tile.</summary>
        public int TileOffsetX { get; init; }

        /// <summary>Gets the vertical offset from origin to top edge of first tile.</summary>
        public int TileOffsetY { get; init; }

        /// <summary>Gets the number of components.</summary>
        public int ComponentCount { get; init; }

        /// <summary>Gets the bit depth of the first component (convenience property).</summary>
        public int BitDepth { get; init; }

        /// <summary>Gets whether the first component samples are signed (convenience property).</summary>
        public bool IsSigned { get; init; }

        /// <summary>Gets the component information array.</summary>
        public J2kComponentInfo[] Components { get; init; } = Array.Empty<J2kComponentInfo>();

        // From COD marker

        /// <summary>Gets the number of decomposition levels (0-32).</summary>
        public int DecompositionLevels { get; init; }

        /// <summary>Gets the code-block width (typically 64).</summary>
        public int CodeBlockWidth { get; init; }

        /// <summary>Gets the code-block height (typically 64).</summary>
        public int CodeBlockHeight { get; init; }

        /// <summary>Gets whether the reversible 5/3 wavelet transform is used (true) or irreversible 9/7 (false).</summary>
        public bool UsesReversibleTransform { get; init; }

        /// <summary>Gets the progression order.</summary>
        public ProgressionOrder Progression { get; init; }

        /// <summary>Gets the number of quality layers.</summary>
        public int NumberOfLayers { get; init; }

        /// <summary>Gets whether multiple component transform is used.</summary>
        public bool UsesMct { get; init; }

        /// <summary>Gets whether SOP markers are present in the codestream.</summary>
        public bool HasSopMarkers { get; init; }

        /// <summary>Gets whether EPH markers are present in the codestream.</summary>
        public bool HasEphMarkers { get; init; }

        /// <summary>
        /// Parses a JPEG 2000 codestream header.
        /// </summary>
        /// <param name="data">The codestream data.</param>
        /// <param name="header">The parsed header information if successful.</param>
        /// <param name="error">An error message if parsing fails.</param>
        /// <returns>True if parsing succeeded; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<byte> data, out J2kCodestream? header, out string? error)
        {
            header = null;
            error = null;

            if (data.Length < 4)
            {
                error = "Codestream too short for header";
                return false;
            }

            // Check for SOC marker
            ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data);
            if (marker != J2kMarkers.SOC)
            {
                error = $"Expected SOC marker (0xFF4F), found 0x{marker:X4}";
                return false;
            }

            int position = 2;

            // Variables to collect parsed data
            int imageWidth = 0, imageHeight = 0;
            int imageOffsetX = 0, imageOffsetY = 0;
            int tileWidth = 0, tileHeight = 0;
            int tileOffsetX = 0, tileOffsetY = 0;
            int componentCount = 0;
            int bitDepth = 0;
            bool isSigned = false;
            J2kComponentInfo[] components = Array.Empty<J2kComponentInfo>();

            int decompositionLevels = 5; // Default per ITU-T T.800
            int codeBlockWidth = 64;
            int codeBlockHeight = 64;
            bool usesReversibleTransform = false;
            ProgressionOrder progression = ProgressionOrder.LRCP;
            int numberOfLayers = 1;
            bool usesMct = false;
            bool hasSopMarkers = false;
            bool hasEphMarkers = false;

            bool foundSiz = false;
            bool foundCod = false;

            // Parse markers until we hit SOT or SOD
            while (position + 2 <= data.Length)
            {
                marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                // End of main header
                if (marker == J2kMarkers.SOT || marker == J2kMarkers.SOD)
                {
                    break;
                }

                // Markers without segments
                if (!J2kMarkers.HasSegment(marker))
                {
                    continue;
                }

                // Read segment length
                if (position + 2 > data.Length)
                {
                    error = "Unexpected end of codestream while reading marker segment length";
                    return false;
                }

                int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                if (segmentLength < 2 || position + segmentLength - 2 > data.Length)
                {
                    error = $"Invalid segment length {segmentLength} for marker 0x{marker:X4}";
                    return false;
                }

                ReadOnlySpan<byte> segmentData = data.Slice(position, segmentLength - 2);

                switch (marker)
                {
                    case J2kMarkers.SIZ:
                        if (!ParseSizMarker(segmentData, out imageWidth, out imageHeight,
                            out imageOffsetX, out imageOffsetY, out tileWidth, out tileHeight,
                            out tileOffsetX, out tileOffsetY, out componentCount,
                            out bitDepth, out isSigned, out components, out error))
                        {
                            return false;
                        }
                        foundSiz = true;
                        break;

                    case J2kMarkers.COD:
                        if (!ParseCodMarker(segmentData, out hasSopMarkers, out hasEphMarkers,
                            out progression, out numberOfLayers, out usesMct,
                            out decompositionLevels, out codeBlockWidth, out codeBlockHeight,
                            out usesReversibleTransform, out error))
                        {
                            return false;
                        }
                        foundCod = true;
                        break;

                    // Skip other markers
                    default:
                        break;
                }

                position += segmentLength - 2;
            }

            if (!foundSiz)
            {
                error = "Missing required SIZ marker";
                return false;
            }

            // COD is required but we provide sensible defaults if missing
            if (!foundCod)
            {
                // Use defaults - this is technically non-conformant but some files may be malformed
            }

            header = new J2kCodestream
            {
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                ImageOffsetX = imageOffsetX,
                ImageOffsetY = imageOffsetY,
                TileWidth = tileWidth,
                TileHeight = tileHeight,
                TileOffsetX = tileOffsetX,
                TileOffsetY = tileOffsetY,
                ComponentCount = componentCount,
                BitDepth = bitDepth,
                IsSigned = isSigned,
                Components = components,
                DecompositionLevels = decompositionLevels,
                CodeBlockWidth = codeBlockWidth,
                CodeBlockHeight = codeBlockHeight,
                UsesReversibleTransform = usesReversibleTransform,
                Progression = progression,
                NumberOfLayers = numberOfLayers,
                UsesMct = usesMct,
                HasSopMarkers = hasSopMarkers,
                HasEphMarkers = hasEphMarkers
            };

            return true;
        }

        /// <summary>
        /// Finds the start of tile data (after SOD marker) for a specific tile.
        /// </summary>
        /// <param name="data">The codestream data.</param>
        /// <param name="tileIndex">The zero-based tile index.</param>
        /// <returns>The offset to the tile data, or -1 if not found.</returns>
        public static int FindTileDataOffset(ReadOnlySpan<byte> data, int tileIndex)
        {
            if (data.Length < 4)
            {
                return -1;
            }

            int position = 0;
            int currentTile = -1;

            while (position + 2 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                if (marker == J2kMarkers.EOC)
                {
                    break;
                }

                if (!J2kMarkers.HasSegment(marker))
                {
                    continue;
                }

                if (position + 2 > data.Length)
                {
                    break;
                }

                int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                if (segmentLength < 2)
                {
                    break;
                }

                if (marker == J2kMarkers.SOT)
                {
                    // SOT segment contains tile index at bytes 0-1 (after length)
                    if (position + 4 <= data.Length)
                    {
                        currentTile = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                    }
                }
                else if (marker == J2kMarkers.SOD)
                {
                    // This shouldn't happen as SOD has no segment, but handle it
                    // The position after SOD marker is the tile data
                    if (currentTile == tileIndex)
                    {
                        return position - 2; // Return position of SOD marker
                    }
                }

                position += segmentLength - 2;

                // Check for SOD immediately after SOT segment
                if (marker == J2kMarkers.SOT && position + 2 <= data.Length)
                {
                    ushort nextMarker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                    if (nextMarker == J2kMarkers.SOD)
                    {
                        if (currentTile == tileIndex)
                        {
                            return position + 2; // Return position after SOD marker
                        }
                    }
                }
            }

            return -1;
        }

        private static bool ParseSizMarker(
            ReadOnlySpan<byte> data,
            out int imageWidth, out int imageHeight,
            out int imageOffsetX, out int imageOffsetY,
            out int tileWidth, out int tileHeight,
            out int tileOffsetX, out int tileOffsetY,
            out int componentCount, out int bitDepth, out bool isSigned,
            out J2kComponentInfo[] components,
            out string? error)
        {
            imageWidth = imageHeight = 0;
            imageOffsetX = imageOffsetY = 0;
            tileWidth = tileHeight = 0;
            tileOffsetX = tileOffsetY = 0;
            componentCount = 0;
            bitDepth = 0;
            isSigned = false;
            components = Array.Empty<J2kComponentInfo>();
            error = null;

            // SIZ marker segment structure (ITU-T T.800 Table A.9):
            // Rsiz (2 bytes) - Capabilities
            // Xsiz (4 bytes) - Width of reference grid
            // Ysiz (4 bytes) - Height of reference grid
            // XOsiz (4 bytes) - Horizontal offset to image area
            // YOsiz (4 bytes) - Vertical offset to image area
            // XTsiz (4 bytes) - Nominal tile width
            // YTsiz (4 bytes) - Nominal tile height
            // XTOsiz (4 bytes) - Horizontal offset to first tile
            // YTOsiz (4 bytes) - Vertical offset to first tile
            // Csiz (2 bytes) - Number of components
            // For each component:
            //   Ssiz (1 byte) - Component bit depth and sign
            //   XRsiz (1 byte) - Horizontal subsampling
            //   YRsiz (1 byte) - Vertical subsampling

            const int minLength = 36; // Without component info
            if (data.Length < minLength)
            {
                error = "SIZ marker segment too short";
                return false;
            }

            int offset = 0;

            // Skip Rsiz (capabilities)
            offset += 2;

            imageWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            imageHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            imageOffsetX = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            imageOffsetY = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            tileWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            tileHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            tileOffsetX = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            tileOffsetY = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            componentCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
            offset += 2;

            if (componentCount == 0 || componentCount > 16384)
            {
                error = $"Invalid component count: {componentCount}";
                return false;
            }

            // Each component has 3 bytes of info
            int expectedLength = offset + (componentCount * 3);
            if (data.Length < expectedLength)
            {
                error = "SIZ marker segment too short for component info";
                return false;
            }

            components = new J2kComponentInfo[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                byte ssiz = data[offset++];
                byte xrsiz = data[offset++];
                byte yrsiz = data[offset++];

                // Ssiz: bit 7 = signed, bits 0-6 = bit depth - 1
                bool compSigned = (ssiz & 0x80) != 0;
                int compBitDepth = (ssiz & 0x7F) + 1;

                components[i] = new J2kComponentInfo
                {
                    BitDepth = compBitDepth,
                    IsSigned = compSigned,
                    SubsamplingX = xrsiz,
                    SubsamplingY = yrsiz
                };

                // Use first component for convenience properties
                if (i == 0)
                {
                    bitDepth = compBitDepth;
                    isSigned = compSigned;
                }
            }

            return true;
        }

        private static bool ParseCodMarker(
            ReadOnlySpan<byte> data,
            out bool hasSopMarkers, out bool hasEphMarkers,
            out ProgressionOrder progression, out int numberOfLayers, out bool usesMct,
            out int decompositionLevels, out int codeBlockWidth, out int codeBlockHeight,
            out bool usesReversibleTransform,
            out string? error)
        {
            hasSopMarkers = false;
            hasEphMarkers = false;
            progression = ProgressionOrder.LRCP;
            numberOfLayers = 1;
            usesMct = false;
            decompositionLevels = 5;
            codeBlockWidth = 64;
            codeBlockHeight = 64;
            usesReversibleTransform = false;
            error = null;

            // COD marker segment structure (ITU-T T.800 Table A.13):
            // Scod (1 byte) - Coding style
            // SGcod (4 bytes) - Parameters for all components:
            //   Progression order (1 byte)
            //   Number of layers (2 bytes)
            //   Multiple component transform (1 byte)
            // SPcod (5-9 bytes) - Parameters for all components:
            //   Number of decomposition levels (1 byte)
            //   Code-block width exponent (1 byte)
            //   Code-block height exponent (1 byte)
            //   Code-block style (1 byte)
            //   Wavelet transform (1 byte)
            //   [Precinct sizes if Scod indicates]

            if (data.Length < 10)
            {
                error = "COD marker segment too short";
                return false;
            }

            int offset = 0;

            byte scod = data[offset++];
            hasSopMarkers = (scod & 0x02) != 0;
            hasEphMarkers = (scod & 0x04) != 0;

            // SGcod
            byte progOrder = data[offset++];
            if (progOrder <= 4)
            {
                progression = (ProgressionOrder)progOrder;
            }

            numberOfLayers = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
            offset += 2;

            byte mct = data[offset++];
            usesMct = mct != 0;

            // SPcod
            decompositionLevels = data[offset++];
            if (decompositionLevels > 32)
            {
                error = $"Invalid decomposition levels: {decompositionLevels}";
                return false;
            }

            byte cbWidthExp = data[offset++];
            byte cbHeightExp = data[offset++];
            codeBlockWidth = 1 << (cbWidthExp + 2);
            codeBlockHeight = 1 << (cbHeightExp + 2);

            // Limit code block size per spec
            if (codeBlockWidth > 1024 || codeBlockHeight > 1024 || codeBlockWidth * codeBlockHeight > 4096)
            {
                codeBlockWidth = Math.Min(codeBlockWidth, 64);
                codeBlockHeight = Math.Min(codeBlockHeight, 64);
            }

            // Skip code-block style
            offset++;

            if (offset >= data.Length)
            {
                error = "COD marker segment missing wavelet transform type";
                return false;
            }

            byte wavelet = data[offset++];
            // 0 = 9/7 irreversible, 1 = 5/3 reversible
            usesReversibleTransform = wavelet == 1;

            return true;
        }
    }
}
