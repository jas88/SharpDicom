using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Delegate that performs the actual video encoding using a native or managed backend.
    /// </summary>
    /// <param name="frames">The frames to encode.</param>
    /// <param name="options">Encoding options.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <returns>The encoded video bitstream.</returns>
    public delegate byte[] VideoEncoderBackend(
        IEnumerable<VideoFrame> frames,
        VideoEncoderOptions options,
        int width,
        int height,
        IProgress<VideoEncodeProgress>? progress);

    /// <summary>
    /// High-level static API for video encoding in DICOM workflows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// VideoEncoder provides a clean, high-level interface for creating video DICOM files.
    /// It supports both streaming (IAsyncEnumerable) and batch encoding modes with
    /// progress reporting.
    /// </para>
    /// <para>
    /// The actual encoding work is performed by a registered backend (typically
    /// <c>NativeVideoEncoder</c> from SharpDicom.Codecs). Register a backend via
    /// <see cref="RegisterBackend"/> before calling encode methods.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Backend is typically registered by NativeCodecs.Initialize()
    /// var options = VideoEncoderOptions.Diagnostic;
    /// byte[] encoded = VideoEncoder.EncodeFromFrames(frames, options, 512, 512);
    /// </code>
    /// </para>
    /// </remarks>
    public static class VideoEncoder
    {
        private static VideoEncoderBackend? _backend;

        /// <summary>
        /// Registers a video encoder backend implementation.
        /// </summary>
        /// <param name="backend">The backend delegate that performs encoding.</param>
        /// <exception cref="ArgumentNullException"><paramref name="backend"/> is null.</exception>
        public static void RegisterBackend(VideoEncoderBackend backend)
        {
            ThrowHelpers.ThrowIfNull(backend, nameof(backend));
            _backend = backend;
        }

        /// <summary>
        /// Gets a value indicating whether a video encoder backend is available.
        /// </summary>
        public static bool IsAvailable => _backend != null;

#if NET8_0_OR_GREATER
        /// <summary>
        /// Encodes video frames from an async stream with progress reporting.
        /// </summary>
        /// <param name="frames">Async stream of video frames to encode.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The encoded video bitstream.</returns>
        /// <exception cref="InvalidOperationException">No video encoder backend is registered.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="frames"/> or <paramref name="options"/> is null.</exception>
        public static async Task<byte[]> EncodeFromFramesAsync(
            IAsyncEnumerable<VideoFrame> frames,
            VideoEncoderOptions options,
            int width,
            int height,
            IProgress<VideoEncodeProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowHelpers.ThrowIfNull(frames, nameof(frames));
            ThrowHelpers.ThrowIfNull(options, nameof(options));

            var backend = _backend ?? throw new InvalidOperationException(
                "No video encoder backend is registered. " +
                "Ensure SharpDicom.Codecs is referenced and NativeCodecs.Initialize() has been called.");

            // Collect frames from async stream
            var frameList = new List<VideoFrame>();
            try
            {
                await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    frameList.Add(frame);
                }

                // Run encoding on thread pool to avoid blocking
                return await Task.Run(
                    () => backend(frameList, options, width, height, progress),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                foreach (var frame in frameList)
                {
                    frame.Dispose();
                }
            }
        }
#endif

        /// <summary>
        /// Encodes video frames from a synchronous enumerable with progress reporting.
        /// </summary>
        /// <param name="frames">The video frames to encode.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <returns>The encoded video bitstream.</returns>
        /// <exception cref="InvalidOperationException">No video encoder backend is registered.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="frames"/> or <paramref name="options"/> is null.</exception>
        public static byte[] EncodeFromFrames(
            IEnumerable<VideoFrame> frames,
            VideoEncoderOptions options,
            int width,
            int height,
            IProgress<VideoEncodeProgress>? progress = null)
        {
            ThrowHelpers.ThrowIfNull(frames, nameof(frames));
            ThrowHelpers.ThrowIfNull(options, nameof(options));

            var backend = _backend ?? throw new InvalidOperationException(
                "No video encoder backend is registered. " +
                "Ensure SharpDicom.Codecs is referenced and NativeCodecs.Initialize() has been called.");

            return backend(frames, options, width, height, progress);
        }

        /// <summary>
        /// Encodes pixel data from a multi-frame DICOM file to video format.
        /// </summary>
        /// <param name="file">The DICOM file containing multi-frame pixel data.</param>
        /// <param name="options">Encoding options. If null, uses <see cref="VideoEncoderOptions.Diagnostic"/>.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <returns>The encoded video bitstream.</returns>
        /// <exception cref="InvalidOperationException">No video encoder backend is registered.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
        /// <exception cref="ArgumentException">The DICOM file does not contain multi-frame pixel data.</exception>
        public static byte[] EncodeFromDicom(
            DicomFile file,
            VideoEncoderOptions? options = null,
            IProgress<VideoEncodeProgress>? progress = null)
        {
            ThrowHelpers.ThrowIfNull(file, nameof(file));

            var backend = _backend ?? throw new InvalidOperationException(
                "No video encoder backend is registered. " +
                "Ensure SharpDicom.Codecs is referenced and NativeCodecs.Initialize() has been called.");

            var dataset = file.Dataset;
            var pixelData = dataset.GetPixelData();

            if (pixelData == null)
                throw new ArgumentException("DICOM file does not contain pixel data.", nameof(file));

            int? numberOfFrames = dataset.GetInt32(DicomTag.NumberOfFrames);
            if (!numberOfFrames.HasValue || numberOfFrames.Value < 1)
                numberOfFrames = 1;

            if (numberOfFrames.Value < 2)
                throw new ArgumentException("DICOM file must contain multiple frames for video encoding.", nameof(file));

            var info = pixelData.Info;
            if (!info.Rows.HasValue || !info.Columns.HasValue ||
                !info.SamplesPerPixel.HasValue || !info.BitsAllocated.HasValue)
            {
                throw new ArgumentException("DICOM pixel data is missing required dimension tags.", nameof(file));
            }

            int rows = info.Rows.Value;
            int columns = info.Columns.Value;
            int samplesPerPixel = info.SamplesPerPixel.Value;
            int bitsAllocated = info.BitsAllocated.Value;

            // Determine frame rate from DICOM tags
            var resolvedOptions = options ?? VideoEncoderOptions.Diagnostic;
            double frameRate = DetectFrameRate(dataset);
            if (Math.Abs(frameRate - resolvedOptions.FrameRate) > 0.01 && frameRate > 0)
            {
                // Override with detected frame rate from DICOM
                resolvedOptions = new VideoEncoderOptions
                {
                    Codec = resolvedOptions.Codec,
                    Preset = resolvedOptions.Preset,
                    FrameRate = frameRate,
                    AudioCodec = resolvedOptions.AudioCodec,
                    AudioSampleRate = resolvedOptions.AudioSampleRate,
                    AudioChannels = resolvedOptions.AudioChannels,
                    HwAccel = resolvedOptions.HwAccel,
                    GopSize = resolvedOptions.GopSize,
                    RawParameters = resolvedOptions.RawParameters
                };
            }

            // Determine pixel format
            VideoPixelFormat pixelFormat;
            if (samplesPerPixel == 1 && bitsAllocated == 8)
                pixelFormat = VideoPixelFormat.Gray8;
            else if (samplesPerPixel == 1 && bitsAllocated == 16)
                pixelFormat = VideoPixelFormat.Gray16;
            else
                pixelFormat = VideoPixelFormat.Rgb24;

            // Extract frames
            var frames = new List<VideoFrame>();
            try
            {
                for (int i = 0; i < numberOfFrames.Value; i++)
                {
                    // GetFrameSpan returns ReadOnlySpan, copy to managed array for VideoFrame
                    var frameSpan = pixelData.GetFrameSpan(i);
                    var frameBytes = frameSpan.ToArray();
                    frames.Add(new VideoFrame(frameBytes, columns, rows, pixelFormat));
                }

                return backend(frames, resolvedOptions, columns, rows, progress);
            }
            finally
            {
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

        /// <summary>
        /// Detects the frame rate from DICOM dataset tags.
        /// </summary>
        /// <param name="dataset">The DICOM dataset to inspect.</param>
        /// <returns>
        /// The detected frame rate in frames per second, or 0.0 if not determinable.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Checks the following DICOM tags in order:
        /// </para>
        /// <list type="number">
        /// <item><description>(0018,0040) Cine Rate - frames per second as IS</description></item>
        /// <item><description>(0008,2144) Recommended Display Frame Rate - frames per second as IS</description></item>
        /// <item><description>(0018,1063) Frame Time - milliseconds per frame as DS</description></item>
        /// <item><description>(0018,1065) Frame Time Vector - variable frame times as DS</description></item>
        /// </list>
        /// </remarks>
        public static double DetectFrameRate(DicomDataset dataset)
        {
            ThrowHelpers.ThrowIfNull(dataset, nameof(dataset));

            // Tag (0018,0040) - Cine Rate (IS VR)
            var cineRate = dataset.GetInt32(new DicomTag(0x0018, 0x0040));
            if (cineRate.HasValue && cineRate.Value > 0)
                return cineRate.Value;

            // Tag (0008,2144) - Recommended Display Frame Rate (IS VR)
            var recommendedRate = dataset.GetInt32(new DicomTag(0x0008, 0x2144));
            if (recommendedRate.HasValue && recommendedRate.Value > 0)
                return recommendedRate.Value;

            // Tag (0018,1063) - Frame Time (DS VR, milliseconds per frame)
            var frameTime = dataset.GetFloat64(new DicomTag(0x0018, 0x1063));
            if (frameTime.HasValue && frameTime.Value > 0)
                return 1000.0 / frameTime.Value;

            // Tag (0018,1065) - Frame Time Vector (DS VR, array of ms values)
            // Use average frame time if available
            var frameTimeVectorStr = dataset.GetString(new DicomTag(0x0018, 0x1065));
            if (!string.IsNullOrEmpty(frameTimeVectorStr))
            {
                var parts = frameTimeVectorStr!.Split('\\');
                double totalTime = 0;
                int validCount = 0;
                foreach (var part in parts)
                {
                    if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ms) && ms > 0)
                    {
                        totalTime += ms;
                        validCount++;
                    }
                }
                if (validCount > 0)
                {
                    double avgFrameTime = totalTime / validCount;
                    if (avgFrameTime > 0)
                        return 1000.0 / avgFrameTime;
                }
            }

            return 0.0;
        }

        /// <summary>
        /// Resets the encoder backend. For testing purposes only.
        /// </summary>
        internal static void Reset()
        {
            _backend = null;
        }
    }
}
