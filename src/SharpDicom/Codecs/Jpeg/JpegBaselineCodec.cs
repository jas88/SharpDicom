using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// JPEG Baseline (Process 1) codec implementing IPixelDataCodec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.50 (JPEG Baseline).
    /// It provides lossy compression for 8-bit grayscale and RGB images.
    /// </para>
    /// <para>
    /// JPEG Baseline is the most common compressed transfer syntax in DICOM,
    /// suitable for general-purpose lossy compression of medical images.
    /// </para>
    /// </remarks>
    public sealed class JpegBaselineCodec : IPixelDataCodec
    {
        /// <inheritdoc />
        public TransferSyntax TransferSyntax => TransferSyntax.JPEGBaseline;

        /// <inheritdoc />
        public string Name => "JPEG Baseline (Process 1)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: true,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8 },  // Baseline is 8-bit only
            SupportedSamplesPerPixel: new[] { 1, 3 });  // Grayscale or RGB

        /// <inheritdoc />
        public DecodeResult Decode(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination)
        {
            ThrowHelpers.ThrowIfNull(fragments, nameof(fragments));

            if (frameIndex < 0 || frameIndex >= fragments.Fragments.Count)
            {
                return DecodeResult.Fail(frameIndex, 0, $"Frame index {frameIndex} out of range [0, {fragments.Fragments.Count})");
            }

            var fragment = fragments.Fragments[frameIndex];
            return JpegBaselineDecoder.DecodeFrame(fragment.Span, info, destination.Span, frameIndex);
        }

        /// <inheritdoc />
        public ValueTask<DecodeResult> DecodeAsync(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<DecodeResult>(Decode(fragments, info, frameIndex, destination));
        }

        /// <inheritdoc />
        public DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            object? options = null)
        {
            var jpegOptions = options is JpegCodecOptions opt ? opt : JpegCodecOptions.Default;

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);
                var encoded = JpegBaselineEncoder.EncodeFrame(frameData, info, jpegOptions);
                fragments.Add(encoded);
            }

            // Build offset table for multi-frame images
            var offsetTable = frameCount > 1
                ? BuildOffsetTable(fragments)
                : ReadOnlyMemory<byte>.Empty;

            return new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                offsetTable,
                fragments);
        }

        /// <inheritdoc />
        public ValueTask<DicomFragmentSequence> EncodeAsync(
            ReadOnlyMemory<byte> pixelData,
            PixelDataInfo info,
            object? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<DicomFragmentSequence>(Encode(pixelData.Span, info, options));
        }

        /// <inheritdoc />
        public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
        {
            if (fragments == null)
            {
                return ValidationResult.Invalid(0, 0, "Fragments cannot be null");
            }

            var issues = new List<CodecDiagnostic>();

            for (int i = 0; i < fragments.Fragments.Count; i++)
            {
                var fragment = fragments.Fragments[i];
                var span = fragment.Span;

                // Check minimum length
                if (span.Length < 4)
                {
                    issues.Add(new CodecDiagnostic(i, 0, "Fragment too short for valid JPEG", ">= 4", span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    continue;
                }

                // Check SOI marker (0xFFD8)
                if (span[0] != JpegMarkers.Prefix || span[1] != JpegMarkers.SOI)
                {
                    issues.Add(new CodecDiagnostic(i, 0, "Missing SOI marker", "0xFFD8", $"0x{span[0]:X2}{span[1]:X2}"));
                    continue;
                }

                // Check EOI marker at end (0xFFD9)
                if (span[span.Length - 2] != JpegMarkers.Prefix || span[span.Length - 1] != JpegMarkers.EOI)
                {
                    // EOI might be followed by padding byte
                    if (span.Length >= 3 && span[span.Length - 3] == JpegMarkers.Prefix && span[span.Length - 2] == JpegMarkers.EOI)
                    {
                        // Has padding byte after EOI - OK
                    }
                    else
                    {
                        issues.Add(new CodecDiagnostic(i, span.Length - 2, "Missing EOI marker", "0xFFD9", $"0x{span[span.Length - 2]:X2}{span[span.Length - 1]:X2}"));
                    }
                }

                // Find and validate SOF0 marker dimensions
                int position = 2;
                bool foundSof = false;

                while (position < span.Length - 1 && !foundSof)
                {
                    if (span[position] != JpegMarkers.Prefix)
                    {
                        position++;
                        continue;
                    }

                    byte marker = span[position + 1];
                    position += 2;

                    if (marker == JpegMarkers.SOI || marker == JpegMarkers.EOI || JpegMarkers.IsRST(marker))
                    {
                        continue;
                    }

                    // Get segment length
                    if (position + 2 > span.Length)
                    {
                        break;
                    }

                    ushort length = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(position, 2));
                    if (length < 2 || position + length > span.Length)
                    {
                        break;
                    }

                    // Check for SOF0
                    if (marker == JpegMarkers.SOF0)
                    {
                        foundSof = true;

                        // Parse frame info
                        var payload = span.Slice(position + 2, length - 2);
                        if (JpegFrameInfo.TryParse(payload, marker, out var frameInfo))
                        {
                            // Validate dimensions
                            if (frameInfo.Width != info.Columns)
                            {
                                issues.Add(new CodecDiagnostic(
                                    i, position,
                                    "Width mismatch",
                                    info.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    frameInfo.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            }

                            if (frameInfo.Height != info.Rows)
                            {
                                issues.Add(new CodecDiagnostic(
                                    i, position,
                                    "Height mismatch",
                                    info.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    frameInfo.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            }
                        }
                    }
                    else if (JpegMarkers.IsSOF(marker) && marker != JpegMarkers.SOF0)
                    {
                        // Non-baseline SOF marker
                        issues.Add(new CodecDiagnostic(
                            i, position - 2,
                            "Unsupported SOF marker",
                            "0xC0 (SOF0)",
                            $"0x{marker:X2}"));
                    }

                    position += length;
                }

                if (!foundSof)
                {
                    issues.Add(new CodecDiagnostic(i, 0, "No SOF0 marker found", null, null));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        /// <summary>
        /// Builds a Basic Offset Table from the encoded fragments.
        /// </summary>
        private static ReadOnlyMemory<byte> BuildOffsetTable(List<ReadOnlyMemory<byte>> fragments)
        {
            if (fragments.Count <= 1)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            var offsets = new byte[fragments.Count * 4];
            uint offset = 0;

            for (int i = 0; i < fragments.Count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(offsets.AsSpan(i * 4), offset);
                offset += (uint)fragments[i].Length;
            }

            return offsets;
        }
    }
}
