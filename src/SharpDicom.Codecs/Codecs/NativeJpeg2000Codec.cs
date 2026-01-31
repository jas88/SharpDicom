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
    /// Native JPEG 2000 codec using OpenJPEG (and optionally nvJPEG2000) via P/Invoke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec wraps the native sharpdicom_codecs library for high-performance
    /// JPEG 2000 encoding and decoding. It supports both lossless and lossy
    /// JPEG 2000 transfer syntaxes.
    /// </para>
    /// <para>
    /// When GPU acceleration is available (nvJPEG2000 with NVIDIA GPU), the codec
    /// will automatically use GPU decoding unless <see cref="NativeCodecs.PreferCpu"/>
    /// is set to true.
    /// </para>
    /// </remarks>
    public sealed class NativeJpeg2000Codec : IPixelDataCodec
    {
        // Static arrays to avoid CA1861
        private static readonly int[] SupportedBitDepthsArray = new[] { 8, 12, 16 };
        private static readonly int[] SupportedSamplesArray = new[] { 1, 3 };

        private readonly TransferSyntax _transferSyntax;
        private readonly bool _isLossy;

        /// <summary>
        /// Initializes a new instance for JPEG 2000 Lossless encoding/decoding.
        /// </summary>
        public NativeJpeg2000Codec()
            : this(TransferSyntax.JPEG2000Lossless, isLossy: false)
        {
        }

        /// <summary>
        /// Initializes a new instance for a specific JPEG 2000 transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">The transfer syntax to handle.</param>
        /// <param name="isLossy">Whether this is a lossy codec.</param>
        internal NativeJpeg2000Codec(TransferSyntax transferSyntax, bool isLossy)
        {
            _transferSyntax = transferSyntax;
            _isLossy = isLossy;
        }

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => _transferSyntax;

        /// <inheritdoc />
        public string Name => _isLossy
            ? "Native JPEG 2000 Lossy (OpenJPEG/nvJPEG2000)"
            : "Native JPEG 2000 Lossless (OpenJPEG/nvJPEG2000)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: _isLossy,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: SupportedBitDepthsArray,
            SupportedSamplesPerPixel: SupportedSamplesArray
        );

        /// <summary>
        /// Gets a value indicating whether GPU acceleration is available and enabled.
        /// </summary>
        public static bool GpuEnabled =>
            NativeCodecs.GpuAvailable &&
            NativeCodecs.EnableGpu &&
            !NativeCodecs.PreferCpu &&
            NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.GpuJpeg2000);

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

            var fragment = fragments.Fragments[frameIndex];
            if (fragment.IsEmpty)
            {
                return DecodeResult.Fail(frameIndex, 0, "Empty fragment for frame");
            }

            using var inputHandle = fragment.Pin();
            using var outputHandle = destination.Pin();

            byte* inputPtr = (byte*)inputHandle.Pointer;
            byte* outputPtr = (byte*)outputHandle.Pointer;
            int inputLen = fragment.Length;
            int outputLen = destination.Length;

            int result;
            int width, height, components, bitsPerSample;

            // Try GPU decode first if available
            if (GpuEnabled)
            {
                result = NativeMethods.GpuJ2kDecode(
                    inputPtr, inputLen,
                    outputPtr, outputLen,
                    out width, out height, out components, out bitsPerSample);

                // If GPU decode succeeds, use the result
                if (result >= 0)
                {
                    return ValidateAndReturn(result, info, frameIndex, width, height);
                }

                // GPU decode failed - fall back to CPU
                // Error code -8 (GpuUnavailable) is expected, others are logged
            }

            // CPU decode via OpenJPEG
            result = NativeMethods.J2kDecode(
                inputPtr, inputLen,
                outputPtr, outputLen,
                out width, out height, out components, out bitsPerSample,
                resolutionLevel: 0); // 0 = full resolution

            if (result < 0)
            {
                string errorMsg = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    $"JPEG 2000 decode failed: {errorMsg}",
                    expected: $"{info.Columns}x{info.Rows}",
                    actual: $"error code {result}");
            }

            return ValidateAndReturn(result, info, frameIndex, width, height);
        }

        private static DecodeResult ValidateAndReturn(int bytesWritten, PixelDataInfo info, int frameIndex, int width, int height)
        {
            if (width != info.Columns || height != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    "Decoded dimensions do not match pixel data info",
                    expected: $"{info.Columns}x{info.Rows}",
                    actual: $"{width}x{height}");
            }

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
            cancellationToken.ThrowIfCancellationRequested();

            // J2K decode is CPU/GPU-bound - async wrapper for consistency
            // Future: could use Task.Run for CPU decode to avoid blocking
            var result = Decode(fragments, info, frameIndex, destination);
            return new ValueTask<DecodeResult>(result);
        }

        /// <inheritdoc />
        public unsafe DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            object? options = null)
        {
            var codecOptions = options as NativeJpeg2000CodecOptions ?? GetDefaultOptions();

            int frameSize = info.FrameSize;
            var fragments = new List<ReadOnlyMemory<byte>>();

            for (int frame = 0; frame < info.NumberOfFrames; frame++)
            {
                var frameData = pixelData.Slice(frame * frameSize, frameSize);
                var encoded = EncodeFrame(frameData, info, codecOptions);
                fragments.Add(encoded);
            }

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

                // Check for JPEG 2000 signature (JP2/J2C codestream)
                if (fragment.Length < 4)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short for J2K signature"));
                    continue;
                }

                var span = fragment.Span;

                // J2K codestream starts with SOC marker (0xFF4F)
                // JP2 box format starts with signature box (0x0000000C 6A502020)
                bool isJ2kCodestream = span[0] == 0xFF && span[1] == 0x4F;
                bool isJp2Box = span.Length >= 12 &&
                    span[0] == 0x00 && span[1] == 0x00 && span[2] == 0x00 && span[3] == 0x0C &&
                    span[4] == 0x6A && span[5] == 0x50 && span[6] == 0x20 && span[7] == 0x20;

                if (!isJ2kCodestream && !isJp2Box)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Invalid JPEG 2000 signature",
                        expected: "0xFF4F (J2K) or JP2 box",
                        actual: $"0x{span[0]:X2}{span[1]:X2}"));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        private unsafe byte[] EncodeFrame(ReadOnlySpan<byte> frameData, PixelDataInfo info, NativeJpeg2000CodecOptions options)
        {
            fixed (byte* inputPtr = frameData)
            {
                byte* outputPtr;
                int outputLen;

                int result = NativeMethods.J2kEncode(
                    inputPtr,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    info.BitsStored,
                    out outputPtr,
                    out outputLen,
                    options.Lossless ? 1 : 0,
                    options.CompressionRatio,
                    options.TileSize);

                if (result < 0)
                {
                    string errorMsg = NativeCodecs.GetLastError();
                    throw new NativeCodecException($"JPEG 2000 encode failed: {errorMsg}", result);
                }

                try
                {
                    var encoded = new byte[outputLen];
                    Marshal.Copy((IntPtr)outputPtr, encoded, 0, outputLen);
                    return encoded;
                }
                finally
                {
                    NativeMethods.J2kFree(outputPtr);
                }
            }
        }

        private NativeJpeg2000CodecOptions GetDefaultOptions() =>
            _isLossy ? NativeJpeg2000CodecOptions.DefaultLossy : NativeJpeg2000CodecOptions.DefaultLossless;

        /// <summary>
        /// Creates a codec instance for JPEG 2000 Lossless transfer syntax.
        /// </summary>
        public static NativeJpeg2000Codec CreateLossless() =>
            new(TransferSyntax.JPEG2000Lossless, isLossy: false);

        /// <summary>
        /// Creates a codec instance for JPEG 2000 Lossy transfer syntax.
        /// </summary>
        public static NativeJpeg2000Codec CreateLossy() =>
            new(TransferSyntax.JPEG2000Lossy, isLossy: true);
    }

    /// <summary>
    /// Options for native JPEG 2000 encoding.
    /// </summary>
    public sealed class NativeJpeg2000CodecOptions
    {
        /// <summary>
        /// Gets or sets whether to use lossless compression. Default is true.
        /// </summary>
        public bool Lossless { get; set; } = true;

        /// <summary>
        /// Gets or sets the compression ratio for lossy compression.
        /// Higher values mean more compression (lower quality).
        /// Only used when Lossless is false. Default is 20.0.
        /// </summary>
        public float CompressionRatio { get; set; } = 20.0f;

        /// <summary>
        /// Gets or sets the tile size. 0 means no tiling (single tile).
        /// Default is 0 (no tiling) for maximum compatibility.
        /// </summary>
        public int TileSize { get; set; }

        /// <summary>
        /// Gets or sets the number of resolution levels. Default is 6.
        /// </summary>
        public int ResolutionLevels { get; set; } = 6;

        /// <summary>
        /// Default options for lossless JPEG 2000 encoding.
        /// </summary>
        public static readonly NativeJpeg2000CodecOptions DefaultLossless = new()
        {
            Lossless = true,
            TileSize = 0
        };

        /// <summary>
        /// Default options for lossy JPEG 2000 encoding.
        /// </summary>
        public static readonly NativeJpeg2000CodecOptions DefaultLossy = new()
        {
            Lossless = false,
            CompressionRatio = 20.0f,
            TileSize = 0
        };
    }
}
