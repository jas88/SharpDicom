using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Base class for JPEG-LS codecs implementing IPixelDataCodec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JPEG-LS (ITU-T T.87 / ISO/IEC 14495-1) is a lossless/near-lossless image
    /// compression standard. It uses context-based prediction and Golomb-Rice entropy coding.
    /// </para>
    /// <para>
    /// This codec provides a pure managed implementation for basic operation,
    /// with optional native library acceleration when available.
    /// </para>
    /// </remarks>
    public abstract class JpegLsCodecBase : IPixelDataCodec
    {
        /// <summary>
        /// Gets the NEAR parameter (0=lossless, &gt;0=near-lossless).
        /// </summary>
        protected abstract int Near { get; }

        /// <inheritdoc />
        public abstract TransferSyntax TransferSyntax { get; }

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract CodecCapabilities Capabilities { get; }

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
            return JpegLsDecoder.TryDecode(fragment.Span, info, destination.Span, frameIndex);
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
            var jlsOptions = options is JpegLsCodecOptions opt ? opt : GetDefaultOptions();
            var near = jlsOptions.Near;

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);
                var encoded = JpegLsEncoder.Encode(frameData, info, near);
                fragments.Add(encoded);
            }

            // Build offset table if requested
            var offsetTable = jlsOptions.GenerateBasicOffsetTable && frameCount > 1
                ? BuildOffsetTable(fragments)
                : ReadOnlyMemory<byte>.Empty;

            return new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                offsetTable,
                fragments);
        }

        /// <summary>
        /// Gets the default options for this codec.
        /// </summary>
        protected abstract JpegLsCodecOptions GetDefaultOptions();

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

                if (!JpegLsDecoder.TryParseHeader(fragment.Span, out var header, out var error))
                {
                    issues.Add(new CodecDiagnostic(i, 0, error ?? "Invalid JPEG-LS header", null, null));
                    continue;
                }

                // Validate NEAR parameter matches codec type
                if (header.Near != Near && Near == 0 && header.Near > 0)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        $"Lossless codec received near-lossless data (NEAR={header.Near})",
                        "NEAR=0", $"NEAR={header.Near}"));
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
    /// JPEG-LS Lossless codec (NEAR=0).
    /// </summary>
    /// <remarks>
    /// Implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.80.
    /// </remarks>
    public sealed class JpegLsLosslessCodec : JpegLsCodecBase
    {
        /// <inheritdoc />
        protected override int Near => 0;

        /// <inheritdoc />
        public override TransferSyntax TransferSyntax => TransferSyntax.JPEGLSLossless;

        /// <inheritdoc />
        public override string Name => "JPEG-LS Lossless Image Compression";

        /// <inheritdoc />
        public override CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: false,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8, 12, 16 },
            SupportedSamplesPerPixel: new[] { 1, 3 });

        /// <inheritdoc />
        protected override JpegLsCodecOptions GetDefaultOptions() => JpegLsCodecOptions.Default;
    }

    /// <summary>
    /// JPEG-LS Near-Lossless codec (NEAR &gt; 0).
    /// </summary>
    /// <remarks>
    /// Implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.81.
    /// </remarks>
    public sealed class JpegLsNearLosslessCodec : JpegLsCodecBase
    {
        /// <inheritdoc />
        protected override int Near => 2;

        /// <inheritdoc />
        public override TransferSyntax TransferSyntax => TransferSyntax.JPEGLSNearLossless;

        /// <inheritdoc />
        public override string Name => "JPEG-LS Near-Lossless Image Compression";

        /// <inheritdoc />
        public override CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: true,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8, 12, 16 },
            SupportedSamplesPerPixel: new[] { 1, 3 });

        /// <inheritdoc />
        protected override JpegLsCodecOptions GetDefaultOptions() => JpegLsCodecOptions.VisuallyLossless;
    }
}
