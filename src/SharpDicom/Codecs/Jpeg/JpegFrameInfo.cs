using System;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Information parsed from a JPEG frame header (SOF segment).
    /// </summary>
    /// <remarks>
    /// This structure contains the essential parameters from a Start of Frame segment
    /// that describe the image dimensions, sample precision, and compression mode.
    /// </remarks>
    public readonly record struct JpegFrameInfo
    {
        /// <summary>
        /// Gets the sample precision in bits (typically 8, 12, or 16).
        /// </summary>
        public byte Precision { get; init; }

        /// <summary>
        /// Gets the image height in pixels.
        /// </summary>
        public ushort Height { get; init; }

        /// <summary>
        /// Gets the image width in pixels.
        /// </summary>
        public ushort Width { get; init; }

        /// <summary>
        /// Gets the number of image components (1 for grayscale, 3 for color, 4 for CMYK).
        /// </summary>
        public byte ComponentCount { get; init; }

        /// <summary>
        /// Gets the SOF marker that was parsed (e.g., SOF0, SOF3).
        /// </summary>
        public byte SofMarker { get; init; }

        /// <summary>
        /// Gets a value indicating whether this is baseline DCT compression (SOF0).
        /// </summary>
        public bool IsBaseline => SofMarker == JpegMarkers.SOF0;

        /// <summary>
        /// Gets a value indicating whether this is lossless compression (SOF3, SOF7, SOF11, SOF15).
        /// </summary>
        public bool IsLossless => JpegMarkers.IsLossless(SofMarker);

        /// <summary>
        /// Gets a value indicating whether this is progressive compression (SOF2, SOF6, SOF10, SOF14).
        /// </summary>
        public bool IsProgressive => JpegMarkers.IsProgressive(SofMarker);

        /// <summary>
        /// Gets a value indicating whether this uses arithmetic coding.
        /// </summary>
        public bool IsArithmetic => JpegMarkers.IsArithmetic(SofMarker);

        /// <summary>
        /// Gets the per-component information (component ID, sampling factors, quantization table).
        /// </summary>
        public JpegComponentInfo[] Components { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JpegFrameInfo"/> struct.
        /// </summary>
        public JpegFrameInfo(
            byte precision,
            ushort height,
            ushort width,
            byte componentCount,
            byte sofMarker,
            JpegComponentInfo[]? components = null)
        {
            Precision = precision;
            Height = height;
            Width = width;
            ComponentCount = componentCount;
            SofMarker = sofMarker;
            Components = components ?? Array.Empty<JpegComponentInfo>();
        }

        /// <summary>
        /// Attempts to parse a JPEG frame header from a SOF segment.
        /// </summary>
        /// <param name="data">
        /// The SOF segment data, starting after the marker (FF Cx) and length bytes.
        /// This should be the segment payload (precision, height, width, components).
        /// </param>
        /// <param name="sofMarker">The SOF marker byte (e.g., 0xC0 for baseline).</param>
        /// <param name="info">When successful, contains the parsed frame information.</param>
        /// <returns>true if parsing was successful; otherwise, false.</returns>
        /// <remarks>
        /// SOF segment format (after marker and length):
        /// - 1 byte: Sample precision (P)
        /// - 2 bytes: Number of lines (Y) - big-endian
        /// - 2 bytes: Number of samples per line (X) - big-endian
        /// - 1 byte: Number of components (Nf)
        /// - For each component (3 bytes each):
        ///   - 1 byte: Component identifier (Ci)
        ///   - 1 byte: Sampling factors (Hi:Vi in high:low nibbles)
        ///   - 1 byte: Quantization table destination selector (Tqi)
        /// </remarks>
        public static bool TryParse(ReadOnlySpan<byte> data, byte sofMarker, out JpegFrameInfo info)
        {
            info = default;

            // Minimum SOF header: 6 bytes for fixed fields + at least 3 bytes for one component
            if (data.Length < 6)
            {
                return false;
            }

            byte precision = data[0];
            ushort height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
            ushort width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2));
            byte componentCount = data[5];

            // Validate component count and ensure we have enough data
            if (componentCount == 0 || componentCount > 4)
            {
                return false;
            }

            int expectedLength = 6 + (componentCount * 3);
            if (data.Length < expectedLength)
            {
                return false;
            }

            // Parse component information
            var components = new JpegComponentInfo[componentCount];
            int offset = 6;
            for (int i = 0; i < componentCount; i++)
            {
                byte componentId = data[offset];
                byte samplingFactors = data[offset + 1];
                byte quantTableId = data[offset + 2];

                // Validate sampling factors (1-4 per JPEG spec)
                byte hSampling = (byte)(samplingFactors >> 4);
                byte vSampling = (byte)(samplingFactors & 0x0F);
                if (hSampling < 1 || hSampling > 4 || vSampling < 1 || vSampling > 4)
                {
                    return false;
                }

                // Validate quantization table ID (0-3)
                if (quantTableId > 3)
                {
                    return false;
                }

                components[i] = new JpegComponentInfo(
                    componentId,
                    hSampling,
                    vSampling,
                    quantTableId);

                offset += 3;
            }

            info = new JpegFrameInfo(precision, height, width, componentCount, sofMarker, components);
            return true;
        }

        /// <summary>
        /// Attempts to parse a JPEG frame header from a complete SOF segment including length.
        /// </summary>
        /// <param name="segmentWithLength">
        /// The SOF segment data, starting at the length field (2-byte big-endian length
        /// followed by segment payload).
        /// </param>
        /// <param name="sofMarker">The SOF marker byte (e.g., 0xC0 for baseline).</param>
        /// <param name="info">When successful, contains the parsed frame information.</param>
        /// <returns>true if parsing was successful; otherwise, false.</returns>
        public static bool TryParseWithLength(ReadOnlySpan<byte> segmentWithLength, byte sofMarker, out JpegFrameInfo info)
        {
            info = default;

            if (segmentWithLength.Length < 2)
            {
                return false;
            }

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(segmentWithLength.Slice(0, 2));
            if (length < 2 || segmentWithLength.Length < length)
            {
                return false;
            }

            // Length includes the 2-byte length field itself, so payload starts at offset 2
            return TryParse(segmentWithLength.Slice(2, length - 2), sofMarker, out info);
        }
    }

    /// <summary>
    /// Information about a single JPEG image component from the SOF segment.
    /// </summary>
    public readonly record struct JpegComponentInfo
    {
        /// <summary>
        /// Gets the component identifier (1-4 typically, or Y/Cb/Cr identifiers).
        /// </summary>
        public byte ComponentId { get; init; }

        /// <summary>
        /// Gets the horizontal sampling factor (1-4).
        /// </summary>
        public byte HorizontalSampling { get; init; }

        /// <summary>
        /// Gets the vertical sampling factor (1-4).
        /// </summary>
        public byte VerticalSampling { get; init; }

        /// <summary>
        /// Gets the quantization table destination selector (0-3).
        /// </summary>
        public byte QuantizationTableId { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JpegComponentInfo"/> struct.
        /// </summary>
        public JpegComponentInfo(
            byte componentId,
            byte horizontalSampling,
            byte verticalSampling,
            byte quantizationTableId)
        {
            ComponentId = componentId;
            HorizontalSampling = horizontalSampling;
            VerticalSampling = verticalSampling;
            QuantizationTableId = quantizationTableId;
        }
    }
}
