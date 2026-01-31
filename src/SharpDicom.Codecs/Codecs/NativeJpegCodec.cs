using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native.Interop;
using SharpDicom.Data;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Native JPEG codec using libjpeg-turbo via P/Invoke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec wraps the native sharpdicom_codecs library for high-performance
    /// JPEG encoding and decoding. It supports both JPEG Baseline (lossy) and
    /// JPEG Lossless transfer syntaxes.
    /// </para>
    /// <para>
    /// libjpeg-turbo provides SIMD-accelerated JPEG processing on x86/x64 (SSE2/AVX2)
    /// and ARM (NEON) platforms.
    /// </para>
    /// </remarks>
    public sealed class NativeJpegCodec : IPixelDataCodec
    {
        // Static arrays to avoid CA1861
        private static readonly int[] LossyBitDepths = new[] { 8 };
        private static readonly int[] LosslessBitDepths = new[] { 8, 12, 16 };
        private static readonly int[] SupportedSamples = new[] { 1, 3 };

        private readonly TransferSyntax _transferSyntax;
        private readonly bool _isLossy;

        /// <summary>
        /// Initializes a new instance for JPEG Baseline (lossy) encoding/decoding.
        /// </summary>
        public NativeJpegCodec()
            : this(TransferSyntax.JPEGBaseline, isLossy: true)
        {
        }

        /// <summary>
        /// Initializes a new instance for a specific JPEG transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">The transfer syntax to handle.</param>
        /// <param name="isLossy">Whether this is a lossy codec.</param>
        internal NativeJpegCodec(TransferSyntax transferSyntax, bool isLossy)
        {
            _transferSyntax = transferSyntax;
            _isLossy = isLossy;
        }

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => _transferSyntax;

        /// <inheritdoc />
        public string Name => _isLossy ? "Native JPEG Baseline (libjpeg-turbo)" : "Native JPEG Lossless (libjpeg-turbo)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: _isLossy,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: _isLossy ? LossyBitDepths : LosslessBitDepths,
            SupportedSamplesPerPixel: SupportedSamples
        );

        /// <inheritdoc />
        public unsafe DecodeResult Decode(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(fragments);
#else
            if (fragments == null)
                throw new ArgumentNullException(nameof(fragments));
#endif
            if (frameIndex < 0 || frameIndex >= fragments.FragmentCount)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));

            // Get the fragment for this frame
            var fragment = fragments.Fragments[frameIndex];
            if (fragment.IsEmpty)
            {
                return DecodeResult.Fail(frameIndex, 0, "Empty fragment for frame");
            }

            // Pin the input and output buffers
            using var inputHandle = fragment.Pin();
            using var outputHandle = destination.Pin();

            byte* inputPtr = (byte*)inputHandle.Pointer;
            byte* outputPtr = (byte*)outputHandle.Pointer;
            int inputLen = fragment.Length;
            int outputLen = destination.Length;

            int result = NativeMethods.JpegDecode(
                inputPtr, inputLen,
                outputPtr, outputLen,
                out int width, out int height, out _,
                colorspace: 0); // 0 = use default colorspace

            if (result < 0)
            {
                string errorMsg = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    $"JPEG decode failed: {errorMsg}",
                    expected: $"{info.Columns}x{info.Rows}",
                    actual: $"error code {result}");
            }

            // Validate decoded dimensions
            if (width != info.Columns || height != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    "Decoded dimensions do not match pixel data info",
                    expected: $"{info.Columns}x{info.Rows}",
                    actual: $"{width}x{height}");
            }

            return DecodeResult.Ok(result);
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

            // JPEG decode is CPU-bound and fast - no benefit from async
            var result = Decode(fragments, info, frameIndex, destination);
            return new ValueTask<DecodeResult>(result);
        }

        /// <inheritdoc />
        public unsafe DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            object? options = null)
        {
            var codecOptions = options as NativeJpegCodecOptions ?? NativeJpegCodecOptions.Default;

            int frameSize = info.FrameSize;
            var fragments = new List<ReadOnlyMemory<byte>>();

            for (int frame = 0; frame < info.NumberOfFrames; frame++)
            {
                var frameData = pixelData.Slice(frame * frameSize, frameSize);
                var encoded = EncodeFrame(frameData, info, codecOptions);
                fragments.Add(encoded);
            }

            // Create fragment sequence with empty offset table
            return new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
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

            // JPEG encode is CPU-bound - no benefit from async for single call
            var result = Encode(pixelData.Span, info, options);
            return new ValueTask<DicomFragmentSequence>(result);
        }

        /// <inheritdoc />
        public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
        {
            if (fragments == null)
                return ValidationResult.Invalid(0, 0, "Fragments is null");

            if (fragments.FragmentCount == 0)
                return ValidationResult.Invalid(0, 0, "No fragments in sequence");

            var issues = new List<CodecDiagnostic>();

            for (int i = 0; i < fragments.FragmentCount; i++)
            {
                var fragment = fragments.Fragments[i];
                if (fragment.IsEmpty)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Empty fragment"));
                    continue;
                }

                // Check for JPEG SOI marker (0xFFD8)
                if (fragment.Length < 2)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short for JPEG marker"));
                    continue;
                }

                var span = fragment.Span;
                if (span[0] != 0xFF || span[1] != 0xD8)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Invalid JPEG SOI marker",
                        expected: "0xFFD8",
                        actual: $"0x{span[0]:X2}{span[1]:X2}"));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        private unsafe byte[] EncodeFrame(ReadOnlySpan<byte> frameData, PixelDataInfo info, NativeJpegCodecOptions options)
        {
            fixed (byte* inputPtr = frameData)
            {
                byte* outputPtr;
                int outputLen;

                int result = NativeMethods.JpegEncode(
                    inputPtr,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    out outputPtr,
                    out outputLen,
                    options.Quality,
                    (int)options.Subsampling);

                if (result < 0)
                {
                    string errorMsg = NativeCodecs.GetLastError();
                    throw new NativeCodecException($"JPEG encode failed: {errorMsg}", result);
                }

                try
                {
                    // Copy from native buffer to managed array
                    var encoded = new byte[outputLen];
                    Marshal.Copy((IntPtr)outputPtr, encoded, 0, outputLen);
                    return encoded;
                }
                finally
                {
                    // Free the native buffer
                    NativeMethods.JpegFree(outputPtr);
                }
            }
        }

        /// <summary>
        /// Creates a codec instance for JPEG Baseline transfer syntax.
        /// </summary>
        public static NativeJpegCodec CreateBaseline() =>
            new(TransferSyntax.JPEGBaseline, isLossy: true);
    }

    /// <summary>
    /// Options for native JPEG encoding.
    /// </summary>
    public sealed class NativeJpegCodecOptions
    {
        /// <summary>
        /// Gets or sets the JPEG quality (1-100). Default is 90.
        /// </summary>
        public int Quality { get; set; } = 90;

        /// <summary>
        /// Gets or sets the chroma subsampling mode. Default is 4:2:0.
        /// </summary>
        public JpegSubsampling Subsampling { get; set; } = JpegSubsampling.Subsample420;

        /// <summary>
        /// Default JPEG encoding options.
        /// </summary>
        public static readonly NativeJpegCodecOptions Default = new();
    }

    /// <summary>
    /// JPEG chroma subsampling modes.
    /// </summary>
    public enum JpegSubsampling
    {
        /// <summary>
        /// 4:4:4 - No subsampling (highest quality).
        /// </summary>
        Subsample444 = 0,

        /// <summary>
        /// 4:2:2 - Horizontal subsampling.
        /// </summary>
        Subsample422 = 1,

        /// <summary>
        /// 4:2:0 - Both horizontal and vertical subsampling (default).
        /// </summary>
        Subsample420 = 2,

        /// <summary>
        /// Grayscale - No color components.
        /// </summary>
        Grayscale = 3,

        /// <summary>
        /// 4:4:0 - Vertical subsampling only.
        /// </summary>
        Subsample440 = 4
    }
}
