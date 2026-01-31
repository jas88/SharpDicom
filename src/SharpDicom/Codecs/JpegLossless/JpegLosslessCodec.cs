using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.JpegLossless
{
    /// <summary>
    /// JPEG Lossless (Process 14, Selection Value 1) codec implementing IPixelDataCodec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.70
    /// (JPEG Lossless, Non-Hierarchical, First-Order Prediction, Process 14).
    /// </para>
    /// <para>
    /// It uses DPCM prediction with Selection Value 1 (horizontal predictor) as
    /// required by the DICOM standard, and Huffman coding of prediction residuals.
    /// </para>
    /// <para>
    /// Key characteristics:
    /// <list type="bullet">
    /// <item>Lossless compression - bit-perfect reconstruction</item>
    /// <item>Supports 2-16 bit samples per DICOM requirement</item>
    /// <item>Supports grayscale and RGB images</item>
    /// <item>Typical compression ratio: 2:1 to 3:1 for medical images</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class JpegLosslessCodec : IPixelDataCodec
    {
        /// <inheritdoc />
        public TransferSyntax TransferSyntax => TransferSyntax.JPEGLossless;

        /// <inheritdoc />
        public string Name => "JPEG Lossless (Process 14, First-Order Prediction)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: false,  // Key difference from baseline JPEG
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8, 12, 16 },  // 2-16 bit per DICOM, common values
            SupportedSamplesPerPixel: new[] { 1, 3 });

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
                return DecodeResult.Fail(frameIndex, 0,
                    $"Frame index {frameIndex} out of range [0, {fragments.Fragments.Count})");
            }

            var fragment = fragments.Fragments[frameIndex];
            return JpegLosslessDecoder.DecodeFrame(fragment.Span, info, destination.Span, frameIndex);
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
            var codecOptions = options is JpegLosslessCodecOptions opt ? opt : JpegLosslessCodecOptions.Default;

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);
                var encoded = JpegLosslessEncoder.EncodeFrame(frameData, info, codecOptions.SelectionValue);
                fragments.Add(encoded);
            }

            // Build offset table if requested
            var offsetTable = codecOptions.GenerateBasicOffsetTable
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

                // Check for SOI marker at start
                if (fragment.Length < 2 ||
                    fragment.Span[0] != 0xFF ||
                    fragment.Span[1] != 0xD8)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Missing SOI marker",
                        "FFD8",
                        fragment.Length >= 2
                            ? $"{fragment.Span[0]:X2}{fragment.Span[1]:X2}"
                            : "insufficient data"));
                    continue;
                }

                // Check for EOI marker at end
                if (fragment.Length < 4 ||
                    fragment.Span[fragment.Length - 2] != 0xFF ||
                    fragment.Span[fragment.Length - 1] != 0xD9)
                {
                    // EOI might be at -3 if padded
                    if (fragment.Length < 5 ||
                        fragment.Span[fragment.Length - 3] != 0xFF ||
                        fragment.Span[fragment.Length - 2] != 0xD9)
                    {
                        issues.Add(new CodecDiagnostic(
                            i, fragment.Length - 2,
                            "Missing EOI marker",
                            "FFD9",
                            "not found"));
                    }
                }

                // Look for SOF3 marker (lossless)
                bool hasSOF3 = false;
                for (int pos = 2; pos < fragment.Length - 3; pos++)
                {
                    if (fragment.Span[pos] == 0xFF && fragment.Span[pos + 1] == 0xC3)
                    {
                        hasSOF3 = true;
                        break;
                    }
                }

                if (!hasSOF3)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Missing SOF3 marker (not JPEG Lossless)",
                        "FFC3",
                        "not found"));
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

    /// <summary>
    /// Options for JPEG Lossless encoding.
    /// </summary>
    /// <param name="SelectionValue">
    /// The DPCM predictor selection value (1-7). Default is 1 (horizontal prediction)
    /// as required by DICOM Transfer Syntax 1.2.840.10008.1.2.4.70.
    /// </param>
    /// <param name="GenerateBasicOffsetTable">
    /// Whether to generate a Basic Offset Table for multi-frame images.
    /// </param>
    public readonly record struct JpegLosslessCodecOptions(
        int SelectionValue = 1,
        bool GenerateBasicOffsetTable = true)
    {
        /// <summary>
        /// Default options with Selection Value 1 and Basic Offset Table generation enabled.
        /// </summary>
        public static readonly JpegLosslessCodecOptions Default = new(1, true);
    }
}
