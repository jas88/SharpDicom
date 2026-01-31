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
    /// Native JPEG-LS codec using CharLS via P/Invoke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec wraps the native sharpdicom_codecs library for high-performance
    /// JPEG-LS encoding and decoding. It supports both lossless and near-lossless
    /// JPEG-LS transfer syntaxes.
    /// </para>
    /// <para>
    /// JPEG-LS (ISO-14495-1) is particularly efficient for medical imaging due to
    /// its excellent compression ratios for grayscale images with high bit depths.
    /// </para>
    /// </remarks>
    public sealed class NativeJpegLsCodec : IPixelDataCodec
    {
        // Static arrays to avoid CA1861
        private static readonly int[] SupportedBitDepthsArray = new[] { 8, 12, 16 };
        private static readonly int[] SupportedSamplesArray = new[] { 1, 3 };

        private readonly TransferSyntax _transferSyntax;
        private readonly bool _isNearLossless;

        /// <summary>
        /// Initializes a new instance for JPEG-LS Lossless encoding/decoding.
        /// </summary>
        public NativeJpegLsCodec()
            : this(CreateJpegLsLosslessTransferSyntax(), isNearLossless: false)
        {
        }

        /// <summary>
        /// Initializes a new instance for a specific JPEG-LS transfer syntax.
        /// </summary>
        /// <param name="transferSyntax">The transfer syntax to handle.</param>
        /// <param name="isNearLossless">Whether this is near-lossless (quasi-lossless).</param>
        internal NativeJpegLsCodec(TransferSyntax transferSyntax, bool isNearLossless)
        {
            _transferSyntax = transferSyntax;
            _isNearLossless = isNearLossless;
        }

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => _transferSyntax;

        /// <inheritdoc />
        public string Name => _isNearLossless
            ? "Native JPEG-LS Near-Lossless (CharLS)"
            : "Native JPEG-LS Lossless (CharLS)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => new(
            CanEncode: true,
            CanDecode: true,
            IsLossy: _isNearLossless,
            SupportsMultiFrame: true,
            SupportsParallelEncode: true,
            SupportedBitDepths: SupportedBitDepthsArray,
            SupportedSamplesPerPixel: SupportedSamplesArray
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

            int result = NativeMethods.JlsDecode(
                inputPtr, inputLen,
                outputPtr, outputLen,
                out int width, out int height, out _, out _);

            if (result < 0)
            {
                string errorMsg = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    $"JPEG-LS decode failed: {errorMsg}",
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

            // JLS decode is CPU-bound - sync is fine
            var result = Decode(fragments, info, frameIndex, destination);
            return new ValueTask<DecodeResult>(result);
        }

        /// <inheritdoc />
        public unsafe DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            object? options = null)
        {
            var codecOptions = options as NativeJpegLsCodecOptions ?? GetDefaultOptions();

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

                // Check for JPEG-LS SOI marker (0xFFD8) followed by SOF-55 marker (0xFFF7)
                if (fragment.Length < 4)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short for JPEG-LS markers"));
                    continue;
                }

                var span = fragment.Span;

                // JPEG-LS starts with SOI (0xFFD8) like regular JPEG
                if (span[0] != 0xFF || span[1] != 0xD8)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Invalid JPEG-LS SOI marker",
                        expected: "0xFFD8",
                        actual: $"0x{span[0]:X2}{span[1]:X2}"));
                }

                // Look for SOF-55 (0xFFF7) marker which identifies JPEG-LS
                bool foundSof55 = false;
                for (int j = 2; j < fragment.Length - 1; j++)
                {
                    if (span[j] == 0xFF && span[j + 1] == 0xF7)
                    {
                        foundSof55 = true;
                        break;
                    }
                }

                if (!foundSof55)
                {
                    issues.Add(CodecDiagnostic.At(i, 0,
                        "Missing JPEG-LS SOF-55 marker (0xFFF7)"));
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        private unsafe byte[] EncodeFrame(ReadOnlySpan<byte> frameData, PixelDataInfo info, NativeJpegLsCodecOptions options)
        {
            fixed (byte* inputPtr = frameData)
            {
                byte* outputPtr;
                int outputLen;

                int result = NativeMethods.JlsEncode(
                    inputPtr,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    info.BitsStored,
                    out outputPtr,
                    out outputLen,
                    options.NearLossless);

                if (result < 0)
                {
                    string errorMsg = NativeCodecs.GetLastError();
                    throw new NativeCodecException($"JPEG-LS encode failed: {errorMsg}", result);
                }

                try
                {
                    var encoded = new byte[outputLen];
                    Marshal.Copy((IntPtr)outputPtr, encoded, 0, outputLen);
                    return encoded;
                }
                finally
                {
                    NativeMethods.JlsFree(outputPtr);
                }
            }
        }

        private NativeJpegLsCodecOptions GetDefaultOptions() =>
            _isNearLossless ? NativeJpegLsCodecOptions.DefaultNearLossless : NativeJpegLsCodecOptions.DefaultLossless;

        /// <summary>
        /// Creates a codec instance for JPEG-LS Lossless transfer syntax.
        /// </summary>
        public static NativeJpegLsCodec CreateLossless() =>
            new(CreateJpegLsLosslessTransferSyntax(), isNearLossless: false);

        /// <summary>
        /// Creates a codec instance for JPEG-LS Near-Lossless transfer syntax.
        /// </summary>
        public static NativeJpegLsCodec CreateNearLossless() =>
            new(CreateJpegLsNearLosslessTransferSyntax(), isNearLossless: true);

        // JPEG-LS transfer syntaxes not yet defined in TransferSyntax class
        // Using well-known UIDs directly

        private static TransferSyntax CreateJpegLsLosslessTransferSyntax() => new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.80"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.JPEGLSLossless,
            IsKnown = true
        };

        private static TransferSyntax CreateJpegLsNearLosslessTransferSyntax() => new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.81"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.JPEGLSNearLossless,
            IsKnown = true
        };
    }

    /// <summary>
    /// Options for native JPEG-LS encoding.
    /// </summary>
    public sealed class NativeJpegLsCodecOptions
    {
        /// <summary>
        /// Gets or sets the near-lossless parameter (NEAR).
        /// 0 = lossless, >0 = near-lossless with maximum error of NEAR.
        /// Default is 0 (lossless).
        /// </summary>
        public int NearLossless { get; set; }

        /// <summary>
        /// Gets or sets whether to use interleave mode for color images.
        /// Default is true (line interleaved).
        /// </summary>
        public bool Interleaved { get; set; } = true;

        /// <summary>
        /// Default options for lossless JPEG-LS encoding.
        /// </summary>
        public static readonly NativeJpegLsCodecOptions DefaultLossless = new()
        {
            NearLossless = 0
        };

        /// <summary>
        /// Default options for near-lossless JPEG-LS encoding.
        /// NEAR=2 provides good compression with minimal visual difference.
        /// </summary>
        public static readonly NativeJpegLsCodecOptions DefaultNearLossless = new()
        {
            NearLossless = 2
        };
    }
}
