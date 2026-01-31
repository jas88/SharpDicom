using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native.Interop;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Native JPEG-LS codec using CharLS for high-performance lossless/near-lossless compression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec wraps the CharLS library for JPEG-LS (ISO 14495-1) encoding and decoding.
    /// JPEG-LS provides excellent compression ratios for medical images, particularly
    /// those with smooth gradients.
    /// </para>
    /// <para>
    /// Supported features:
    /// <list type="bullet">
    /// <item>8 and 16-bit grayscale images</item>
    /// <item>8-bit color images (RGB/YBR)</item>
    /// <item>Lossless compression (NEAR=0)</item>
    /// <item>Near-lossless compression (NEAR>0)</item>
    /// <item>Multi-frame image support</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class NativeJpegLsCodec : IPixelDataCodec
    {
        private static readonly int[] SupportedBitDepths = new[] { 8, 12, 16 };
        private static readonly int[] SupportedSamplesPerPixel = new[] { 1, 3 };

        private readonly TransferSyntax _transferSyntax;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeJpegLsCodec"/> class
        /// for lossless JPEG-LS.
        /// </summary>
        public NativeJpegLsCodec()
            : this(TransferSyntax.JPEGLSLossless)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeJpegLsCodec"/> class
        /// for the specified transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">
        /// The transfer syntax to handle (JPEGLSLossless or JPEGLSNearLossless).
        /// </param>
        public NativeJpegLsCodec(TransferSyntax transferSyntax)
        {
            if (transferSyntax.Compression != CompressionType.JPEGLSLossless &&
                transferSyntax.Compression != CompressionType.JPEGLSNearLossless)
            {
                throw new ArgumentException(
                    $"Transfer syntax {transferSyntax.UID} is not JPEG-LS",
                    nameof(transferSyntax));
            }

            _transferSyntax = transferSyntax;
        }

        /// <summary>
        /// Creates a codec for JPEG-LS lossless compression.
        /// </summary>
        public static NativeJpegLsCodec Lossless => new(TransferSyntax.JPEGLSLossless);

        /// <summary>
        /// Creates a codec for JPEG-LS near-lossless compression.
        /// </summary>
        public static NativeJpegLsCodec NearLossless => new(TransferSyntax.JPEGLSNearLossless);

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => _transferSyntax;

        /// <inheritdoc />
        public string Name => _transferSyntax.IsLossy
            ? "Native JPEG-LS Near-Lossless (CharLS)"
            : "Native JPEG-LS Lossless (CharLS)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => CodecCapabilities.Full(
            isLossy: _transferSyntax.IsLossy,
            supportedBitDepths: SupportedBitDepths,
            supportedSamplesPerPixel: SupportedSamplesPerPixel);

        /// <inheritdoc />
        public unsafe DecodeResult Decode(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination)
        {
            ThrowHelpers.ThrowIfNull(fragments, nameof(fragments));

            if (frameIndex < 0 || frameIndex >= fragments.Fragments.Count)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));

            var fragment = fragments.Fragments[frameIndex];
            if (fragment.IsEmpty)
            {
                return DecodeResult.Fail(frameIndex, 0, "Empty fragment");
            }

            using var fragmentPin = fragment.Pin();
            using var destPin = destination.Pin();

            int result = NativeMethods.jls_decode(
                (byte*)fragmentPin.Pointer, fragment.Length,
                (byte*)destPin.Pointer, destination.Length,
                out int width, out int height, out int components, out int bitsPerSample);

            if (result < 0)
            {
                var errorMessage = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    string.IsNullOrEmpty(errorMessage) ? "JPEG-LS decode failed" : errorMessage);
            }

            // Calculate bytes written
            int bytesPerSample = (bitsPerSample + 7) / 8;
            int bytesWritten = width * height * components * bytesPerSample;
            return DecodeResult.Ok(bytesWritten);
        }

        /// <inheritdoc />
        public ValueTask<DecodeResult> DecodeAsync(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<DecodeResult>(
                Task.Run(() => Decode(fragments, info, frameIndex, destination), cancellationToken));
        }

        /// <inheritdoc />
        public unsafe DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            object? options = null)
        {
            var opts = options as JpegLsEncodeOptions ?? JpegLsEncodeOptions.Default;

            // Use NEAR parameter for near-lossless encoding
            int nearLossless = _transferSyntax.IsLossy ? opts.NearLossless : 0;
            int bitsPerSample = info.BitsStored;

            fixed (byte* input = pixelData)
            {
                int result = NativeMethods.jls_encode(
                    input,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    bitsPerSample,
                    out byte* output,
                    out int outputLen,
                    nearLossless);

                if (result < 0)
                {
                    throw NativeCodecException.EncodeError(
                        Name,
                        result,
                        NativeCodecs.GetLastError(),
                        TransferSyntax);
                }

                // Validate output length from native code
                if (outputLen < 0)
                {
                    throw NativeCodecException.EncodeError(
                        Name,
                        -1,
                        "Native encoder returned negative output length",
                        TransferSyntax);
                }

                // Sanity check: output shouldn't be larger than reasonable maximum
                // For JPEG-LS, use 4x uncompressed size as upper bound
                int bytesPerSampleCheck = (info.BitsStored + 7) / 8;
                long maxReasonableSize = (long)info.Columns * info.Rows * info.SamplesPerPixel * bytesPerSampleCheck * 4;
                if (outputLen > maxReasonableSize)
                {
                    throw NativeCodecException.EncodeError(
                        Name,
                        -1,
                        $"Native encoder returned unreasonable output length: {outputLen} bytes (max expected: {maxReasonableSize})",
                        TransferSyntax);
                }

                try
                {
                    var data = new byte[outputLen];
                    Marshal.Copy((IntPtr)output, data, 0, outputLen);

                    var fragments = new List<ReadOnlyMemory<byte>> { data };
                    return new DicomFragmentSequence(
                        DicomTag.PixelData,
                        DicomVR.OB,
                        ReadOnlyMemory<byte>.Empty,
                        fragments);
                }
                finally
                {
                    NativeMethods.jls_free(output);
                }
            }
        }

        /// <inheritdoc />
        public ValueTask<DicomFragmentSequence> EncodeAsync(
            ReadOnlyMemory<byte> pixelData,
            PixelDataInfo info,
            object? options = null,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<DicomFragmentSequence>(
                Task.Run(() => Encode(pixelData.Span, info, options), cancellationToken));
        }

        /// <inheritdoc />
        public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
        {
            if (fragments == null)
            {
                return ValidationResult.Invalid(-1, 0, "Fragments is null");
            }

            var issues = new List<CodecDiagnostic>();

            for (int i = 0; i < fragments.Fragments.Count; i++)
            {
                var fragment = fragments.Fragments[i];
                if (fragment.Length < 4)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short for JPEG-LS"));
                    continue;
                }

                var span = fragment.Span;

                // Check for JPEG-LS SOI marker (0xFFD8)
                if (span[0] != 0xFF || span[1] != 0xD8)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Missing JPEG-LS SOI marker",
                        "0xFFD8",
                        $"0x{span[0]:X2}{span[1]:X2}"));
                    continue;
                }

                // Look for JPEG-LS Start of Frame marker (0xFFF7)
                bool foundJpegLsMarker = false;
                for (int j = 2; j < fragment.Length - 1; j++)
                {
                    if (span[j] == 0xFF && span[j + 1] == 0xF7)
                    {
                        foundJpegLsMarker = true;
                        break;
                    }
                }

                if (!foundJpegLsMarker)
                {
                    issues.Add(CodecDiagnostic.At(i, 0,
                        "Missing JPEG-LS SOF marker (0xFFF7)"));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }
    }

    /// <summary>
    /// Options for JPEG-LS encoding.
    /// </summary>
    public sealed class JpegLsEncodeOptions
    {
        /// <summary>
        /// Default encoding options (lossless).
        /// </summary>
        public static readonly JpegLsEncodeOptions Default = new();

        private int _nearLossless;

        /// <summary>
        /// Gets or sets the NEAR parameter for near-lossless encoding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// NEAR=0 is lossless. NEAR>0 allows each reconstructed sample to differ
        /// from the original by at most NEAR. Higher values give better compression
        /// but more quality loss.
        /// </para>
        /// <para>
        /// Typical values: 0 (lossless), 1-3 (visually lossless), 4-10 (visible artifacts).
        /// </para>
        /// </remarks>
        public int NearLossless
        {
            get => _nearLossless;
            init => _nearLossless = value;
        }

        /// <summary>
        /// Creates options for lossless encoding.
        /// </summary>
        public static JpegLsEncodeOptions Lossless => new();

        /// <summary>
        /// Creates options for visually lossless encoding (NEAR=2).
        /// </summary>
        public static JpegLsEncodeOptions VisuallyLossless => new() { NearLossless = 2 };
    }
}
