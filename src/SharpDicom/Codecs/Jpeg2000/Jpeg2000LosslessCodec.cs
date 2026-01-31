using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// JPEG 2000 Lossless codec implementing IPixelDataCodec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.90
    /// (JPEG 2000 Image Compression, Lossless Only).
    /// </para>
    /// <para>
    /// It uses the reversible 5/3 wavelet transform and reversible color transform (RCT)
    /// as specified in ITU-T T.800 (JPEG 2000 Part 1). This provides bit-perfect
    /// reconstruction of the original pixel data.
    /// </para>
    /// <para>
    /// Key characteristics:
    /// <list type="bullet">
    /// <item>Lossless compression - bit-perfect reconstruction</item>
    /// <item>Supports 2-16 bit samples</item>
    /// <item>Supports grayscale and RGB images</item>
    /// <item>Typical compression ratio: 2:1 to 3:1 for medical images</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class Jpeg2000LosslessCodec : IPixelDataCodec
    {
        /// <inheritdoc />
        public TransferSyntax TransferSyntax => TransferSyntax.JPEG2000Lossless;

        /// <inheritdoc />
        public string Name => "JPEG 2000 Image Compression (Lossless Only)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: false,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8, 12, 16 },
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
            return J2kDecoder.DecodeFrame(fragment.Span, info, destination.Span, frameIndex);
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
            var j2kOptions = options is Jpeg2000CodecOptions opt ? opt : Jpeg2000CodecOptions.Default;

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            // Map codec options to encoder options
            var encoderOptions = new J2kEncoderOptions
            {
                DecompositionLevels = j2kOptions.DecompositionLevels,
                CodeBlockWidth = j2kOptions.CodeBlockSize,
                CodeBlockHeight = j2kOptions.CodeBlockSize,
                NumberOfLayers = j2kOptions.QualityLayers
            };

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);
                var encoded = J2kEncoder.EncodeFrame(frameData, info, encoderOptions, lossless: true);
                fragments.Add(encoded);
            }

            // Build offset table if requested
            var offsetTable = j2kOptions.GenerateBasicOffsetTable && frameCount > 1
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

                if (!J2kCodestream.TryParse(fragment.Span, out var header, out var error))
                {
                    issues.Add(new CodecDiagnostic(i, 0, error ?? "Invalid J2K header", null, null));
                    continue;
                }

                if (header != null && !header.UsesReversibleTransform)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Lossless codec received lossy codestream (9/7 transform)",
                        "5/3 reversible", "9/7 irreversible"));
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
