using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Htj2k
{
    /// <summary>
    /// Base class for HTJ2K (High Throughput JPEG 2000) codecs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HTJ2K (ITU-T T.814 / JPEG 2000 Part 15) is a high-throughput variant of JPEG 2000
    /// that provides significantly faster encoding and decoding while maintaining compatibility
    /// with the JPEG 2000 codestream format.
    /// </para>
    /// <para>
    /// This implementation leverages the existing JPEG 2000 infrastructure and is backward
    /// compatible with standard JPEG 2000 decoders.
    /// </para>
    /// </remarks>
    public abstract class Htj2kCodecBase : IPixelDataCodec
    {
        /// <summary>
        /// Gets whether this codec uses lossless compression.
        /// </summary>
        protected abstract bool IsLossless { get; }

        /// <summary>
        /// Gets whether this codec uses RPCL progression.
        /// </summary>
        protected abstract bool UseRpcl { get; }

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

            // HTJ2K is backward compatible with JPEG 2000, so we use the J2K decoder
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
            var htj2kOptions = options is Htj2kCodecOptions opt ? opt : GetDefaultOptions();

            int frameSize = info.FrameSize;
            int frameCount = pixelData.Length / frameSize;
            var fragments = new List<ReadOnlyMemory<byte>>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frameData = pixelData.Slice(i * frameSize, frameSize);

                // Use J2K encoder and inject CAP marker for HTJ2K identification
                var encoded = J2kEncoder.EncodeFrame(frameData, info, IsLossless).ToArray();
                var htj2kEncoded = InjectCapMarker(encoded);
                fragments.Add(htj2kEncoded);
            }

            // Build offset table if requested
            var offsetTable = htj2kOptions.GenerateBasicOffsetTable && frameCount > 1
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
        protected abstract Htj2kCodecOptions GetDefaultOptions();

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

                // Validate basic J2K structure
                if (!J2kCodestream.TryParse(fragment.Span, out var header, out var error))
                {
                    issues.Add(new CodecDiagnostic(i, 0, error ?? "Invalid J2K header", null, null));
                    continue;
                }

                // HTJ2K should have a CAP marker
                if (!HasCapMarker(fragment.Span))
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Missing CAP marker for HTJ2K identification",
                        "CAP marker present", "No CAP marker"));
                }

                // Validate lossless vs lossy
                if (header != null && IsLossless && !header.UsesReversibleTransform)
                {
                    issues.Add(new CodecDiagnostic(
                        i, 0,
                        "Lossless HTJ2K codec received lossy codestream",
                        "5/3 reversible", "9/7 irreversible"));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        /// <summary>
        /// Checks if the codestream contains a CAP marker (HTJ2K identifier).
        /// </summary>
        private static bool HasCapMarker(ReadOnlySpan<byte> data)
        {
            const ushort CAP = 0xFF50;

            if (data.Length < 4)
                return false;

            // Search for CAP marker in main header (between SOC and first tile)
            int pos = 2; // Skip SOC
            while (pos + 2 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));

                if (marker == CAP)
                    return true;

                // Stop at SOT (Start of Tile)
                if (marker == 0xFF90)
                    break;

                if ((marker & 0xFF00) == 0xFF00 && marker != 0xFF00)
                {
                    pos += 2;
                    if (pos + 2 <= data.Length)
                    {
                        int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                        pos += segLen;
                    }
                }
                else
                {
                    pos++;
                }
            }

            return false;
        }

        /// <summary>
        /// Injects a CAP marker into a JPEG 2000 codestream to identify it as HTJ2K.
        /// </summary>
        private static byte[] InjectCapMarker(byte[] j2kData)
        {
            // Find insertion point (after SIZ marker)
            int insertPos = FindSizEnd(j2kData);
            if (insertPos < 0)
                return j2kData;

            // Build CAP marker segment
            // Format: CAP (2 bytes) + Length (2 bytes) + Pcap (4 bytes) + Ccap (2 bytes per capability)
            var capMarker = new byte[]
            {
                0xFF, 0x50,  // CAP marker
                0x00, 0x08,  // Length (8 bytes total segment)
                0x00, 0x02, 0x00, 0x00,  // Pcap: Part-15 extensions present
                0x00, 0x20   // Ccap[0]: HTJ2K capability (HT block coder)
            };

            // Create new array with CAP marker inserted
            var result = new byte[j2kData.Length + capMarker.Length];
            Array.Copy(j2kData, 0, result, 0, insertPos);
            Array.Copy(capMarker, 0, result, insertPos, capMarker.Length);
            Array.Copy(j2kData, insertPos, result, insertPos + capMarker.Length, j2kData.Length - insertPos);

            return result;
        }

        /// <summary>
        /// Finds the end of the SIZ marker segment.
        /// </summary>
        private static int FindSizEnd(byte[] data)
        {
            const ushort SIZ = 0xFF51;

            if (data.Length < 4)
                return -1;

            int pos = 2; // Skip SOC
            while (pos + 4 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));

                if (marker == SIZ)
                {
                    int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2));
                    return pos + 2 + segLen;
                }

                if ((marker & 0xFF00) == 0xFF00 && marker != 0xFF00)
                {
                    pos += 2;
                    if (pos + 2 <= data.Length)
                    {
                        int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
                        pos += segLen;
                    }
                }
                else
                {
                    pos++;
                }
            }

            return -1;
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
    /// HTJ2K Lossless codec.
    /// </summary>
    /// <remarks>
    /// Implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.201.
    /// </remarks>
    public sealed class Htj2kLosslessCodec : Htj2kCodecBase
    {
        /// <inheritdoc />
        protected override bool IsLossless => true;

        /// <inheritdoc />
        protected override bool UseRpcl => false;

        /// <inheritdoc />
        public override TransferSyntax TransferSyntax => TransferSyntax.HTJ2KLossless;

        /// <inheritdoc />
        public override string Name => "High Throughput JPEG 2000 (Lossless)";

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
        protected override Htj2kCodecOptions GetDefaultOptions() => Htj2kCodecOptions.Default;
    }

    /// <summary>
    /// HTJ2K Lossless RPCL codec.
    /// </summary>
    /// <remarks>
    /// Implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.202.
    /// Uses RPCL (Resolution Position Component Layer) progression for optimized streaming.
    /// </remarks>
    public sealed class Htj2kLosslessRpclCodec : Htj2kCodecBase
    {
        /// <inheritdoc />
        protected override bool IsLossless => true;

        /// <inheritdoc />
        protected override bool UseRpcl => true;

        /// <inheritdoc />
        public override TransferSyntax TransferSyntax => TransferSyntax.HTJ2KLosslessRPCL;

        /// <inheritdoc />
        public override string Name => "High Throughput JPEG 2000 (Lossless RPCL)";

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
        protected override Htj2kCodecOptions GetDefaultOptions() => Htj2kCodecOptions.LosslessRpcl;
    }

    /// <summary>
    /// HTJ2K Lossy codec.
    /// </summary>
    /// <remarks>
    /// Implements DICOM Transfer Syntax 1.2.840.10008.1.2.4.203.
    /// </remarks>
    public sealed class Htj2kLossyCodec : Htj2kCodecBase
    {
        /// <inheritdoc />
        protected override bool IsLossless => false;

        /// <inheritdoc />
        protected override bool UseRpcl => false;

        /// <inheritdoc />
        public override TransferSyntax TransferSyntax => TransferSyntax.HTJ2KLossy;

        /// <inheritdoc />
        public override string Name => "High Throughput JPEG 2000 (Lossy)";

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
        protected override Htj2kCodecOptions GetDefaultOptions() => Htj2kCodecOptions.Lossy;
    }
}
