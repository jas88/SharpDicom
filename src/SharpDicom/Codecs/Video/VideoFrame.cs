using System;

namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Container for a single video frame's pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="VideoFrame"/> holds the raw pixel data for one frame of video,
    /// along with its dimensions and pixel format. Optionally, it can also carry
    /// audio samples corresponding to the frame's time window.
    /// </para>
    /// <para>
    /// The pixel data is stored as <see cref="ReadOnlyMemory{T}"/> to support
    /// zero-copy slicing from larger buffers. The frame does not own the underlying
    /// memory; callers must ensure the memory remains valid for the frame's lifetime.
    /// </para>
    /// </remarks>
    public sealed class VideoFrame : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// Gets the raw pixel data for this frame.
        /// </summary>
        /// <remarks>
        /// The data layout depends on <see cref="Format"/>:
        /// <list type="bullet">
        /// <item><description><see cref="VideoPixelFormat.Rgb24"/>: 3 bytes per pixel (R, G, B), row-major order.</description></item>
        /// <item><description><see cref="VideoPixelFormat.Gray8"/>: 1 byte per pixel, row-major order.</description></item>
        /// <item><description><see cref="VideoPixelFormat.Gray16"/>: 2 bytes per pixel (little-endian), row-major order.</description></item>
        /// <item><description><see cref="VideoPixelFormat.Yuv420P"/>: Planar Y, then U (quarter-size), then V (quarter-size).</description></item>
        /// </list>
        /// </remarks>
        public ReadOnlyMemory<byte> PixelData { get; }

        /// <summary>
        /// Gets the width of the frame in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the frame in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the pixel format of the frame data.
        /// </summary>
        public VideoPixelFormat Format { get; }

        /// <summary>
        /// Gets the optional audio samples for this frame's time window.
        /// </summary>
        /// <remarks>
        /// When present, contains PCM or float audio samples interleaved by channel.
        /// The format is described by <see cref="AudioFormat"/>.
        /// </remarks>
        public ReadOnlyMemory<byte>? AudioSamples { get; init; }

        /// <summary>
        /// Gets the format of the audio samples, if present.
        /// </summary>
        public AudioSampleFormat AudioFormat { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFrame"/> class.
        /// </summary>
        /// <param name="pixelData">The raw pixel data for the frame.</param>
        /// <param name="width">The width of the frame in pixels. Must be positive.</param>
        /// <param name="height">The height of the frame in pixels. Must be positive.</param>
        /// <param name="format">The pixel format of the data.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="width"/> or <paramref name="height"/> is not positive,
        /// or <paramref name="pixelData"/> length does not match the expected size.
        /// </exception>
        public VideoFrame(ReadOnlyMemory<byte> pixelData, int width, int height, VideoPixelFormat format)
        {
            if (width <= 0)
                throw new ArgumentException("Width must be positive.", nameof(width));
            if (height <= 0)
                throw new ArgumentException("Height must be positive.", nameof(height));

            int expectedSize = CalculateDataSize(width, height, format);
            if (pixelData.Length < expectedSize)
            {
                throw new ArgumentException(
                    $"Pixel data length {pixelData.Length} is less than expected {expectedSize} for {width}x{height} {format}.",
                    nameof(pixelData));
            }

            PixelData = pixelData;
            Width = width;
            Height = height;
            Format = format;
        }

        /// <summary>
        /// Calculates the expected data size in bytes for the given dimensions and format.
        /// </summary>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <param name="format">Pixel format.</param>
        /// <returns>The expected data size in bytes.</returns>
        public static int CalculateDataSize(int width, int height, VideoPixelFormat format)
        {
            return format switch
            {
                VideoPixelFormat.Rgb24 => width * height * 3,
                VideoPixelFormat.Gray8 => width * height,
                VideoPixelFormat.Gray16 => width * height * 2,
                // YUV 4:2:0 planar: Y plane + U plane (1/4) + V plane (1/4) = 1.5 * width * height
                VideoPixelFormat.Yuv420P => width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
            };
        }

        /// <summary>
        /// Releases resources associated with this frame.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
        }

        /// <summary>
        /// Gets a value indicating whether this frame has been disposed.
        /// </summary>
        internal bool IsDisposed => _disposed;
    }

    /// <summary>
    /// Pixel format for video frame data.
    /// </summary>
    public enum VideoPixelFormat
    {
        /// <summary>
        /// 24-bit RGB (8 bits per channel, interleaved).
        /// </summary>
        Rgb24,

        /// <summary>
        /// 8-bit grayscale.
        /// </summary>
        Gray8,

        /// <summary>
        /// 16-bit grayscale (little-endian).
        /// </summary>
        Gray16,

        /// <summary>
        /// YUV 4:2:0 planar format.
        /// </summary>
        /// <remarks>
        /// This is the most common input format for H.264/H.265 encoders.
        /// Data is stored as three separate planes: Y (full resolution),
        /// U and V (each quarter resolution).
        /// </remarks>
        Yuv420P
    }

    /// <summary>
    /// Audio sample format for video frames with embedded audio.
    /// </summary>
    public enum AudioSampleFormat
    {
        /// <summary>
        /// No audio data.
        /// </summary>
        None,

        /// <summary>
        /// 16-bit signed PCM samples (interleaved by channel).
        /// </summary>
        Pcm16,

        /// <summary>
        /// 32-bit IEEE 754 floating-point samples (interleaved by channel).
        /// </summary>
        IeeeFloat
    }
}
