using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Codecs.Rle
{
    /// <summary>
    /// RLE Lossless codec implementing IPixelDataCodec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec implements DICOM RLE compression using the TIFF PackBits algorithm.
    /// It provides lossless compression suitable for all types of DICOM images.
    /// </para>
    /// <para>
    /// The codec splits pixel data into byte segments (MSB first), compresses each
    /// segment independently, and stores them with a 64-byte header containing offsets.
    /// </para>
    /// </remarks>
    public sealed class RleCodec : IPixelDataCodec
    {
        /// <inheritdoc />
        public TransferSyntax TransferSyntax => TransferSyntax.RLELossless;

        /// <inheritdoc />
        public string Name => "RLE Lossless";

        /// <inheritdoc />
        public CodecCapabilities Capabilities { get; } = new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: false,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: new[] { 8, 16 },
            SupportedSamplesPerPixel: new[] { 1, 3 });

        /// <inheritdoc />
        public DecodeResult Decode(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination)
        {
            if (fragments == null)
            {
                throw new ArgumentNullException(nameof(fragments));
            }

            if (frameIndex < 0 || frameIndex >= fragments.Fragments.Count)
            {
                return DecodeResult.Fail(frameIndex, 0, $"Frame index {frameIndex} out of range [0, {fragments.Fragments.Count})");
            }

            var fragment = fragments.Fragments[frameIndex];
            return RleDecoder.DecodeFrame(fragment.Span, info, destination.Span, frameIndex);
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
            var rleOptions = options as RleCodecOptions ?? RleCodecOptions.Default;

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);
                var encoded = RleEncoder.EncodeFrame(frameData, info);
                fragments.Add(encoded);
            }

            // Build offset table if requested
            var offsetTable = rleOptions.GenerateBasicOffsetTable
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
            int expectedSegments = (info.BitsAllocated / 8) * info.SamplesPerPixel;

            for (int i = 0; i < fragments.Fragments.Count; i++)
            {
                var fragment = fragments.Fragments[i];

                if (!RleSegmentHeader.TryParse(fragment.Span, out var header, out var error))
                {
                    issues.Add(new CodecDiagnostic(i, 0, error ?? "Invalid RLE header", null, null));
                    continue;
                }

                if (header.NumberOfSegments != expectedSegments)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Wrong segment count",
                        expectedSegments.ToString(),
                        header.NumberOfSegments.ToString()));
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
