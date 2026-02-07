using System;
using System.Runtime.InteropServices;
using SharpDicom.Codecs.Native.Interop;
using SharpDicom.Codecs.Video;
using SharpDicom.Internal;

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Managed wrapper around the native video encoder, handling the handle lifecycle
    /// and marshalling between managed types and native P/Invoke calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class wraps the native FFmpeg-based video encoder. It manages the native
    /// encoder handle and provides a safe managed API for frame-by-frame encoding.
    /// </para>
    /// <para>
    /// Usage pattern:
    /// <code>
    /// using var encoder = new NativeVideoEncoder(options, width, height);
    /// foreach (var frame in frames)
    /// {
    ///     encoder.EncodeFrame(frame);
    /// }
    /// encoder.Flush();
    /// byte[] output = encoder.GetOutput();
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class NativeVideoEncoder : IDisposable
    {
        private readonly VideoEncoderHandle _handle;
        private bool _flushed;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeVideoEncoder"/> class.
        /// </summary>
        /// <param name="options">Encoding options (codec, quality, frame rate, etc.).</param>
        /// <param name="width">Frame width in pixels.</param>
        /// <param name="height">Frame height in pixels.</param>
        /// <exception cref="NativeCodecException">Thrown when the native encoder cannot be created.</exception>
        /// <exception cref="InvalidOperationException">Thrown when native codecs are not available.</exception>
        public unsafe NativeVideoEncoder(VideoEncoderOptions options, int width, int height)
        {
            if (!NativeCodecs.IsAvailable)
                throw new InvalidOperationException("Native codecs are not available.");
            if (!NativeCodecs.HasFeature(NativeCodecFeature.VideoEncoder))
                throw new InvalidOperationException("Native video encoder is not available.");
            ThrowHelpers.ThrowIfNull(options, nameof(options));
            ThrowHelpers.ThrowIfNegative(width, nameof(width));
            ThrowHelpers.ThrowIfNegative(height, nameof(height));
            if (width == 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height == 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            // Convert frame rate to rational number
            FrameRateToRational(options.FrameRate, out int num, out int den);

            var config = new VideoEncoderConfig
            {
                CodecId = (int)options.Codec,
                Width = width,
                Height = height,
                FrameRateNum = num,
                FrameRateDen = den,
                QualityPreset = (int)options.Preset,
                GopSize = options.GopSize,
                HwAccel = (int)options.HwAccel,
                AudioCodec = (int)options.AudioCodec,
                AudioSampleRate = options.AudioSampleRate,
                AudioChannels = options.AudioChannels
            };

            IntPtr handle = NativeMethods.video_encoder_create(&config);
            if (handle == IntPtr.Zero)
            {
                string error = NativeCodecs.GetLastError();
                throw new NativeCodecException(
                    $"Failed to create video encoder: {(string.IsNullOrEmpty(error) ? "unknown error" : error)}");
            }

            _handle = new VideoEncoderHandle(handle, ownsHandle: true);
        }

        /// <summary>
        /// Encodes a single video frame.
        /// </summary>
        /// <param name="frame">The frame to encode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The encoder has already been flushed.</exception>
        /// <exception cref="NativeCodecException">The native encoder returned an error.</exception>
        public unsafe void EncodeFrame(VideoFrame frame)
        {
            ThrowIfDisposed();
            if (_flushed)
                throw new InvalidOperationException("Cannot encode frames after flush.");
            ThrowHelpers.ThrowIfNull(frame, nameof(frame));

            using var pin = frame.PixelData.Pin();
            int result = NativeMethods.video_encode_frame(
                _handle.DangerousGetHandle(),
                (byte*)pin.Pointer,
                frame.PixelData.Length,
                (int)frame.Format);

            if (result < 0)
            {
                NativeCodecs.ThrowForError(result, "video_encode_frame");
            }
        }

        /// <summary>
        /// Encodes audio samples to be muxed with the video stream.
        /// </summary>
        /// <param name="audioSamples">The audio sample data.</param>
        /// <param name="format">The audio sample format.</param>
        /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The encoder has already been flushed.</exception>
        /// <exception cref="NativeCodecException">The native encoder returned an error.</exception>
        public unsafe void EncodeAudio(ReadOnlyMemory<byte> audioSamples, AudioSampleFormat format)
        {
            ThrowIfDisposed();
            if (_flushed)
                throw new InvalidOperationException("Cannot encode audio after flush.");
            if (format == AudioSampleFormat.None)
                return;

            using var pin = audioSamples.Pin();
            // Map AudioSampleFormat to native: Pcm16=0, IeeeFloat=1
            int nativeFormat = format == AudioSampleFormat.Pcm16 ? 0 : 1;

            int result = NativeMethods.video_encode_audio(
                _handle.DangerousGetHandle(),
                (byte*)pin.Pointer,
                audioSamples.Length,
                nativeFormat);

            if (result < 0)
            {
                NativeCodecs.ThrowForError(result, "video_encode_audio");
            }
        }

        /// <summary>
        /// Flushes the encoder, finalizing the output bitstream.
        /// </summary>
        /// <remarks>
        /// After flushing, no more frames can be encoded. Call <see cref="GetOutput"/>
        /// to retrieve the encoded data.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
        /// <exception cref="NativeCodecException">The native encoder returned an error.</exception>
        public void Flush()
        {
            ThrowIfDisposed();
            if (_flushed)
                return;

            int result = NativeMethods.video_encoder_flush(_handle.DangerousGetHandle());
            if (result < 0)
            {
                NativeCodecs.ThrowForError(result, "video_encoder_flush");
            }

            _flushed = true;
        }

        /// <summary>
        /// Gets the encoded output data after flushing.
        /// </summary>
        /// <returns>The encoded video bitstream.</returns>
        /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The encoder has not been flushed yet.</exception>
        /// <exception cref="NativeCodecException">The native encoder returned an error.</exception>
        public unsafe byte[] GetOutput()
        {
            ThrowIfDisposed();
            if (!_flushed)
                throw new InvalidOperationException("Must call Flush() before GetOutput().");

            int result = NativeMethods.video_encoder_get_output(
                _handle.DangerousGetHandle(),
                out byte* output,
                out int outputLen);

            if (result < 0)
            {
                NativeCodecs.ThrowForError(result, "video_encoder_get_output");
            }

            if (outputLen <= 0 || output == null)
            {
                return Array.Empty<byte>();
            }

            var data = new byte[outputLen];
            Marshal.Copy((IntPtr)output, data, 0, outputLen);
            return data;
        }

        /// <summary>
        /// Releases all resources used by this encoder.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _handle.Dispose();
                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            ThrowHelpers.ThrowIfDisposed(_disposed, this);
        }

        /// <summary>
        /// Converts a floating-point frame rate to a rational number (numerator/denominator).
        /// </summary>
        private static void FrameRateToRational(double frameRate, out int num, out int den)
        {
            // Handle common NTSC rates exactly
            if (Math.Abs(frameRate - 29.97) < 0.01)
            {
                num = 30000;
                den = 1001;
                return;
            }
            if (Math.Abs(frameRate - 23.976) < 0.01)
            {
                num = 24000;
                den = 1001;
                return;
            }
            if (Math.Abs(frameRate - 59.94) < 0.01)
            {
                num = 60000;
                den = 1001;
                return;
            }

            // Check if it's a clean integer rate
            int intRate = (int)Math.Round(frameRate);
            if (Math.Abs(frameRate - intRate) < 0.001)
            {
                num = intRate;
                den = 1;
                return;
            }

            // General case: scale to get reasonable precision
            num = (int)Math.Round(frameRate * 1000);
            den = 1000;

            // Simplify by GCD
            int gcd = Gcd(num, den);
            num /= gcd;
            den /= gcd;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }
    }
}
