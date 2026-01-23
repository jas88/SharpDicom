using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Codecs
{
    /// <summary>
    /// Interface for pixel data codecs that encode and decode compressed pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Codecs implement this interface to provide encoding (compression) and decoding
    /// (decompression) capabilities for DICOM pixel data.
    /// </para>
    /// <para>
    /// The codec is responsible for handling the encapsulated pixel data format defined
    /// in DICOM PS3.5, where compressed data is stored as fragments within a sequence.
    /// </para>
    /// </remarks>
    public interface IPixelDataCodec
    {
        /// <summary>
        /// Gets the transfer syntax this codec handles.
        /// </summary>
        TransferSyntax TransferSyntax { get; }

        /// <summary>
        /// Gets the display name of this codec.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the capabilities of this codec.
        /// </summary>
        CodecCapabilities Capabilities { get; }

        /// <summary>
        /// Decodes a single frame from compressed pixel data.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="frameIndex">Zero-based index of the frame to decode.</param>
        /// <param name="destination">Buffer to write the decompressed pixel data.</param>
        /// <returns>The decode result indicating success or failure with diagnostics.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fragments"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is negative or exceeds available frames.</exception>
        DecodeResult Decode(
            DicomFragmentSequence fragments,
            Codecs.PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination);

        /// <summary>
        /// Asynchronously decodes a single frame from compressed pixel data.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="frameIndex">Zero-based index of the frame to decode.</param>
        /// <param name="destination">Buffer to write the decompressed pixel data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that resolves to the decode result.</returns>
        ValueTask<DecodeResult> DecodeAsync(
            DicomFragmentSequence fragments,
            Codecs.PixelDataInfo info,
            int frameIndex,
            Memory<byte> destination,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Encodes raw pixel data to the compressed format.
        /// </summary>
        /// <param name="pixelData">The uncompressed pixel data.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="options">Codec-specific encoding options, or null for defaults.</param>
        /// <returns>A DicomFragmentSequence containing the compressed data.</returns>
        /// <exception cref="InvalidOperationException">The codec does not support encoding.</exception>
        DicomFragmentSequence Encode(
            ReadOnlySpan<byte> pixelData,
            Codecs.PixelDataInfo info,
            object? options = null);

        /// <summary>
        /// Asynchronously encodes raw pixel data to the compressed format.
        /// </summary>
        /// <param name="pixelData">The uncompressed pixel data.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="options">Codec-specific encoding options, or null for defaults.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that resolves to a DicomFragmentSequence containing the compressed data.</returns>
        ValueTask<DicomFragmentSequence> EncodeAsync(
            ReadOnlyMemory<byte> pixelData,
            Codecs.PixelDataInfo info,
            object? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates compressed pixel data without fully decoding it.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments to validate.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <returns>A validation result indicating whether the data is valid.</returns>
        /// <remarks>
        /// This method performs a lightweight check of the compressed data structure
        /// without fully decompressing it. Use this for quick validation when full
        /// decoding is not needed.
        /// </remarks>
        ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, Codecs.PixelDataInfo info);
    }
}
