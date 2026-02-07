using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDicom.Codecs.Native.Interop;
using SharpDicom.Codecs.Video;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Static helper that loads image files (PNG, JPEG, BMP, TGA) into <see cref="VideoFrame"/>
    /// objects via stb_image P/Invoke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a convenient way to load common image formats as video frames
    /// for use with <see cref="NativeVideoEncoder"/>. It uses stb_image from the native
    /// codec library for decoding.
    /// </para>
    /// <para>
    /// Supported formats: PNG, JPEG, BMP, TGA, PSD, GIF, HDR, PIC, PNM.
    /// </para>
    /// </remarks>
    public static class NativeImageLoader
    {
        /// <summary>
        /// Loads an image from file data into a <see cref="VideoFrame"/>.
        /// </summary>
        /// <param name="imageFileData">The image file data (PNG, JPEG, BMP, TGA, etc.).</param>
        /// <param name="desiredChannels">
        /// Number of output channels: 1 for grayscale, 3 for RGB (default), 4 for RGBA.
        /// Use 0 for automatic detection.
        /// </param>
        /// <returns>A <see cref="VideoFrame"/> containing the decoded pixel data.</returns>
        /// <exception cref="InvalidOperationException">Native codecs are not available or stb_image is not supported.</exception>
        /// <exception cref="NativeCodecException">The image could not be decoded.</exception>
        public static unsafe VideoFrame LoadImage(ReadOnlySpan<byte> imageFileData, int desiredChannels = 3)
        {
            if (!NativeCodecs.IsAvailable)
                throw new InvalidOperationException("Native codecs are not available.");
            if (!NativeCodecs.HasFeature(NativeCodecFeature.StbImage))
                throw new InvalidOperationException("stb_image support is not available in the native library.");
            if (imageFileData.IsEmpty)
                throw new ArgumentException("Image file data is empty.", nameof(imageFileData));

            fixed (byte* dataPtr = imageFileData)
            {
                byte* pixels = NativeMethods.stbi_load_from_memory_wrapper(
                    dataPtr,
                    imageFileData.Length,
                    desiredChannels,
                    out int width,
                    out int height,
                    out int channels);

                if (pixels == null)
                {
                    string error = NativeCodecs.GetLastError();
                    throw new NativeCodecException(
                        $"Failed to load image: {(string.IsNullOrEmpty(error) ? "unsupported format or corrupt data" : error)}");
                }

                try
                {
                    int actualChannels = desiredChannels > 0 ? desiredChannels : channels;
                    int dataSize = width * height * actualChannels;

                    // Copy native pixel data to managed array
                    var pixelData = new byte[dataSize];
                    Marshal.Copy((IntPtr)pixels, pixelData, 0, dataSize);

                    VideoPixelFormat format = actualChannels switch
                    {
                        1 => VideoPixelFormat.Gray8,
                        3 => VideoPixelFormat.Rgb24,
                        _ => VideoPixelFormat.Rgb24 // 4-channel gets treated as RGB24 for video encoding
                    };

                    return new VideoFrame(pixelData, width, height, format);
                }
                finally
                {
                    NativeMethods.stbi_free_wrapper(pixels);
                }
            }
        }

        /// <summary>
        /// Loads a sequence of image files into an enumerable of <see cref="VideoFrame"/> objects.
        /// </summary>
        /// <param name="imageFiles">Sequence of image file data buffers.</param>
        /// <param name="desiredChannels">
        /// Number of output channels: 1 for grayscale, 3 for RGB (default), 4 for RGBA.
        /// </param>
        /// <returns>An enumerable of <see cref="VideoFrame"/> objects, one per input image.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="imageFiles"/> is null.</exception>
        public static IEnumerable<VideoFrame> LoadImageSequence(
            IEnumerable<ReadOnlyMemory<byte>> imageFiles,
            int desiredChannels = 3)
        {
            ThrowHelpers.ThrowIfNull(imageFiles, nameof(imageFiles));

            foreach (var imageData in imageFiles)
            {
                yield return LoadImage(imageData.Span, desiredChannels);
            }
        }
    }
}
