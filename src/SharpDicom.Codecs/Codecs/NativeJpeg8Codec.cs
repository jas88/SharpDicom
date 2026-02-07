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
    /// Native 8-bit JPEG codec using libjpeg-turbo for high-performance encode/decode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This codec is an explicit 8-bit variant of the JPEG baseline codec, wrapping
    /// the native libjpeg-turbo library for JPEG baseline (Process 1) encoding and
    /// decoding. It delegates to the same underlying 8-bit P/Invoke functions as
    /// <see cref="NativeJpegCodec"/>.
    /// </para>
    /// <para>
    /// Supported features:
    /// <list type="bullet">
    /// <item>8-bit grayscale and RGB/YBR color images</item>
    /// <item>Configurable quality levels (1-100)</item>
    /// <item>Configurable chroma subsampling (4:4:4, 4:2:2, 4:2:0)</item>
    /// <item>Multi-frame image support</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class NativeJpeg8Codec : IPixelDataCodec
    {
        private static readonly int[] SupportedBitDepths = new[] { 8 };
        private static readonly int[] SupportedSamplesPerPixel = new[] { 1, 3 };

        /// <inheritdoc />
        public TransferSyntax TransferSyntax => TransferSyntax.JPEGBaseline;

        /// <inheritdoc />
        public string Name => "Native JPEG 8-bit (libjpeg-turbo)";

        /// <inheritdoc />
        public CodecCapabilities Capabilities => CodecCapabilities.Full(
            isLossy: true,
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

            int colorspace = DetermineColorspace(info.SamplesPerPixel);

            int result = NativeMethods.jpeg_decode(
                (byte*)fragmentPin.Pointer, fragment.Length,
                (byte*)destPin.Pointer, destination.Length,
                out int width, out int height, out int components,
                colorspace);

            if (result < 0)
            {
                var errorMessage = NativeCodecs.GetLastError();
                return DecodeResult.Fail(frameIndex, 0,
                    string.IsNullOrEmpty(errorMessage) ? "JPEG 8-bit decode failed" : errorMessage);
            }

            // Verify dimensions match expected
            if (width != info.Columns || height != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Dimension mismatch: expected {info.Columns}x{info.Rows}, got {width}x{height}");
            }

            int bytesWritten = width * height * components;
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
            var opts = options as JpegEncodeOptions ?? JpegEncodeOptions.Default;

            fixed (byte* input = pixelData)
            {
                int result = NativeMethods.jpeg_encode(
                    input,
                    info.Columns,
                    info.Rows,
                    info.SamplesPerPixel,
                    out byte* output,
                    out int outputLen,
                    opts.Quality,
                    (int)opts.Subsampling);

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
                    if (outputLen < 0)
                    {
                        throw NativeCodecException.EncodeError(
                            Name,
                            -1,
                            "Native encoder returned negative output length",
                            TransferSyntax);
                    }

                    long rawSize = (long)info.Columns * info.Rows * info.SamplesPerPixel;
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
                    NativeMethods.jpeg_free(output);
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
                if (fragment.Length < 2)
                {
                    issues.Add(CodecDiagnostic.At(i, 0, "Fragment too short"));
                    continue;
                }

                var span = fragment.Span;

                // Check for JPEG SOI marker (0xFFD8)
                if (span[0] != 0xFF || span[1] != 0xD8)
                {
                    issues.Add(CodecDiagnostic.Mismatch(i, 0,
                        "Missing JPEG SOI marker",
                        "0xFFD8",
                        $"0x{span[0]:X2}{span[1]:X2}"));
                }

                // Check for EOI marker at end (0xFFD9)
                if (fragment.Length >= 2)
                {
                    int endOffset = fragment.Length - 2;
                    if (span[endOffset] != 0xFF || span[endOffset + 1] != 0xD9)
                    {
                        issues.Add(CodecDiagnostic.Mismatch(i, endOffset,
                            "Missing JPEG EOI marker",
                            "0xFFD9",
                            $"0x{span[endOffset]:X2}{span[endOffset + 1]:X2}"));
                    }
                }
            }

            return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
        }

        private static int DetermineColorspace(ushort samplesPerPixel)
        {
            return samplesPerPixel switch
            {
                1 => 2, // GRAY
                3 => 1, // YBR (most common for DICOM)
                _ => 0  // RGB
            };
        }
    }
}
