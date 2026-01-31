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
    /// Native JPEG 2000 codec using OpenJPEG with optional GPU acceleration via nvJPEG2000.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec wraps OpenJPEG for CPU decoding and nvJPEG2000 for GPU-accelerated
    /// decoding when available. GPU acceleration provides significant performance
    /// improvements for batch processing of JPEG 2000 images.
    /// </para>
    /// <para>
    /// Supported features:
    /// <list type="bullet">
    /// <item>8, 12, and 16-bit grayscale and color images</item>
    /// <item>Both lossless and lossy compression modes</item>
    /// <item>Resolution level decode for progressive preview</item>
    /// <item>GPU acceleration (CUDA) when available</item>
    /// <item>Multi-frame image support</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class NativeJpeg2000Codec : IPixelDataCodec
    {
        private static readonly int[] SupportedBitDepths = new[] { 8, 12, 16 };
        private static readonly int[] SupportedSamplesPerPixel = new[] { 1, 3 };

        private readonly TransferSyntax _transferSyntax;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeJpeg2000Codec"/> class
        /// for the specified transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">
        /// The transfer syntax to handle (JPEG2000Lossless or JPEG2000Lossy).
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when the transfer syntax is not a JPEG 2000 variant.
        /// </exception>
        public NativeJpeg2000Codec(TransferSyntax transferSyntax)
        {
            if (transferSyntax.Compression != CompressionType.JPEG2000Lossless &&
                transferSyntax.Compression != CompressionType.JPEG2000Lossy)
            {
                throw new ArgumentException(
                    $"Transfer syntax {transferSyntax.UID} is not JPEG 2000",
                    nameof(transferSyntax));
            }

            _transferSyntax = transferSyntax;
        }

        /// <summary>
        /// Creates a codec for JPEG 2000 lossless compression.
        /// </summary>
        public static NativeJpeg2000Codec Lossless => new(TransferSyntax.JPEG2000Lossless);

        /// <summary>
        /// Creates a codec for JPEG 2000 lossy compression.
        /// </summary>
        public static NativeJpeg2000Codec Lossy => new(TransferSyntax.JPEG2000Lossy);

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => _transferSyntax;

        /// <inheritdoc />
        public string Name => _transferSyntax.IsLossy
            ? "Native JPEG 2000 Lossy (OpenJPEG)"
            : "Native JPEG 2000 Lossless (OpenJPEG)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => CodecCapabilities.Full(
            isLossy: _transferSyntax.IsLossy,
            supportedBitDepths: SupportedBitDepths,
            supportedSamplesPerPixel: SupportedSamplesPerPixel);

        /// <inheritdoc />
        public DecodeResult Decode(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination)
        {
            return DecodeInternal(fragments, info, frameIndex, destination, null);
        }

        /// <summary>
        /// Decodes a frame with optional decode options.
        /// </summary>
        /// <param name="fragments">The compressed fragment sequence.</param>
        /// <param name="info">Pixel data information.</param>
        /// <param name="frameIndex">Zero-based frame index.</param>
        /// <param name="destination">Destination buffer for decoded pixels.</param>
        /// <param name="options">Optional decode options.</param>
        /// <returns>The decode result.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Interface implementation pattern")]
        public unsafe DecodeResult DecodeInternal(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination,
            Jpeg2000DecodeOptions? options)
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

            int resolutionLevel = options?.ResolutionLevel ?? 0;
            bool useGpu = options?.UseGpu ?? ShouldUseGpu();

            // GPU decode does not support ResolutionLevel; fall back to CPU when needed
            if (resolutionLevel > 0 && useGpu)
            {
                useGpu = false;
            }

            int result;
            int width, height, components, bitsPerSample;

            if (useGpu && NativeCodecs.HasFeature(NativeCodecFeature.Gpu))
            {
                result = NativeMethods.gpu_j2k_decode(
                    (byte*)fragmentPin.Pointer, fragment.Length,
                    (byte*)destPin.Pointer, destination.Length,
                    out width, out height, out components, out bitsPerSample);
            }
            else
            {
                result = NativeMethods.j2k_decode(
                    (byte*)fragmentPin.Pointer, fragment.Length,
                    (byte*)destPin.Pointer, destination.Length,
                    out width, out height, out components, out bitsPerSample,
                    resolutionLevel);
            }

            if (result < 0)
            {
                var errorMessage = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    string.IsNullOrEmpty(errorMessage) ? "JPEG 2000 decode failed" : errorMessage);
            }

            // Calculate bytes written based on actual decoded dimensions and bit depth
            // Use long arithmetic to prevent overflow on large images
            int bytesPerSample = (bitsPerSample + 7) / 8;
            long bytesWrittenLong = (long)width * height * components * bytesPerSample;

            // Validate the result fits in an int (DecodeResult uses int)
            if (bytesWrittenLong > int.MaxValue)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Decoded image size ({bytesWrittenLong} bytes) exceeds maximum supported size");
            }

            return DecodeResult.Ok((int)bytesWrittenLong);
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
            var opts = options as Jpeg2000EncodeOptions ?? Jpeg2000EncodeOptions.Default;

            int lossless = _transferSyntax.IsLossy ? 0 : 1;
            float compressionRatio = _transferSyntax.IsLossy ? opts.CompressionRatio : 1.0f;
            int bitsPerSample = info.BitsStored;

            fixed (byte* input = pixelData)
            {
                int result = NativeMethods.j2k_encode(
                    input,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    bitsPerSample,
                    out byte* output,
                    out int outputLen,
                    lossless,
                    compressionRatio,
                    opts.TileSize);

                if (result < 0)
                {
                    throw NativeCodecException.EncodeError(
                        Name,
                        result,
                        NativeCodecs.GetLastError(),
                        TransferSyntax);
                }

                try
                {
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
                    // For small images, codec header overhead can exceed 4x raw size, so use minimum threshold
                    int bytesPerSampleCheck = (info.BitsStored + 7) / 8;
                    long rawSize = (long)info.Columns * info.Rows * info.SamplesPerPixel * bytesPerSampleCheck;
                    long maxReasonableSize = Math.Max(rawSize * 4, 4096);
                    if (outputLen > maxReasonableSize)
                    {
                        throw NativeCodecException.EncodeError(
                            Name,
                            -1,
                            $"Native encoder returned unreasonable output length: {outputLen} bytes (max expected: {maxReasonableSize})",
                            TransferSyntax);
                    }

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
                    NativeMethods.j2k_free(output);
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
                if (fragment.Length < 12)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short for JPEG 2000"));
                    continue;
                }

                var span = fragment.Span;

                // Check for JPEG 2000 codestream signature (0xFF4F) or JP2 file signature
                bool hasCodestreamSig = span[0] == 0xFF && span[1] == 0x4F;
                bool hasJp2Sig = span[0] == 0x00 && span[1] == 0x00 && span[2] == 0x00 && span[3] == 0x0C &&
                                 span[4] == 0x6A && span[5] == 0x50 && span[6] == 0x20 && span[7] == 0x20;

                if (!hasCodestreamSig && !hasJp2Sig)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Missing JPEG 2000 signature",
                        "0xFF4F (codestream) or JP2 file header",
                        $"0x{span[0]:X2}{span[1]:X2}..."));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        private static bool ShouldUseGpu()
        {
            // Use GPU when available and not explicitly disabled
            return NativeCodecs.HasFeature(NativeCodecFeature.Gpu);
        }
    }

    /// <summary>
    /// Options for JPEG 2000 decoding.
    /// </summary>
    public sealed class Jpeg2000DecodeOptions
    {
        private int _resolutionLevel;
        private bool? _useGpu;

        /// <summary>
        /// Gets or sets the resolution level to decode (0 = full resolution).
        /// </summary>
        /// <remarks>
        /// Higher values decode at lower resolutions, which is useful for
        /// generating thumbnails or progressive preview.
        /// </remarks>
        public int ResolutionLevel
        {
            get => _resolutionLevel;
            init => _resolutionLevel = value;
        }

        /// <summary>
        /// Gets or sets whether to use GPU acceleration if available.
        /// </summary>
        /// <remarks>
        /// When null, GPU is used automatically if available and not disabled.
        /// </remarks>
        public bool? UseGpu
        {
            get => _useGpu;
            init => _useGpu = value;
        }
    }

    /// <summary>
    /// Options for JPEG 2000 encoding.
    /// </summary>
    public sealed class Jpeg2000EncodeOptions
    {
        /// <summary>
        /// Default encoding options.
        /// </summary>
        public static readonly Jpeg2000EncodeOptions Default = new();

        private float _compressionRatio = 10.0f;
        private int _tileSize;
        private int _resolutionLevels = 6;

        /// <summary>
        /// Gets or sets the compression ratio for lossy encoding.
        /// </summary>
        /// <remarks>
        /// Typical values: 10-20 for lossy medical imaging.
        /// Ignored for lossless encoding.
        /// </remarks>
        public float CompressionRatio
        {
            get => _compressionRatio;
            init => _compressionRatio = value;
        }

        /// <summary>
        /// Gets or sets the tile size (0 = no tiling, use full image).
        /// </summary>
        /// <remarks>
        /// Tiling can improve decode performance for large images.
        /// Typical values: 0 (no tiling) or 512/1024.
        /// </remarks>
        public int TileSize
        {
            get => _tileSize;
            init => _tileSize = value;
        }

        /// <summary>
        /// Gets or sets the number of resolution levels.
        /// </summary>
        public int ResolutionLevels
        {
            get => _resolutionLevels;
            init => _resolutionLevels = value;
        }

        /// <summary>
        /// Creates options optimized for lossless medical imaging.
        /// </summary>
        public static Jpeg2000EncodeOptions Lossless => new()
        {
            ResolutionLevels = 6
        };

        /// <summary>
        /// Creates options optimized for lossy medical imaging.
        /// </summary>
        public static Jpeg2000EncodeOptions LossyMedical => new()
        {
            CompressionRatio = 15.0f,
            ResolutionLevels = 6
        };
    }
}
