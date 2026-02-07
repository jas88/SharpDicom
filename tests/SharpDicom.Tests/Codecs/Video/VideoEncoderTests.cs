using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using SharpDicom.Codecs.Video;
using SharpDicom.Data;

namespace SharpDicom.Tests.Codecs.Video
{
    /// <summary>
    /// Tests for <see cref="VideoEncoder"/> covering frame rate detection,
    /// codec-to-transfer-syntax mapping, VideoFrame, and VideoEncodeProgress types.
    /// </summary>
    [TestFixture]
    public class VideoEncoderTests
    {
        [SetUp]
        public void SetUp()
        {
            // Reset encoder backend before each test to ensure clean state
            VideoEncoder.Reset();
        }

        // --- Frame rate detection tests ---

        [Test]
        public void Test_DetectFrameRate_FromFrameTime()
        {
            // FrameTime = 33.33 ms -> ~30 fps
            var ds = new DicomDataset();
            var frameTimeTag = new DicomTag(0x0018, 0x1063);
            AddDsString(ds, frameTimeTag, DicomVR.DS, "33.33");

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(30.003).Within(0.01));
        }

        [Test]
        public void Test_DetectFrameRate_FromCineRate()
        {
            var ds = new DicomDataset();
            var cineRateTag = new DicomTag(0x0018, 0x0040);
            AddIsString(ds, cineRateTag, 25);

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(25.0));
        }

        [Test]
        public void Test_DetectFrameRate_FromRecommendedDisplayFrameRate()
        {
            var ds = new DicomDataset();
            var recommendedRateTag = new DicomTag(0x0008, 0x2144);
            AddIsString(ds, recommendedRateTag, 15);

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(15.0));
        }

        [Test]
        public void Test_DetectFrameRate_CineRate_Priority_Over_RecommendedRate()
        {
            // CineRate is checked before RecommendedDisplayFrameRate
            var ds = new DicomDataset();
            var cineRateTag = new DicomTag(0x0018, 0x0040);
            var recommendedRateTag = new DicomTag(0x0008, 0x2144);

            AddIsString(ds, cineRateTag, 30);
            AddIsString(ds, recommendedRateTag, 15);

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(30.0),
                "CineRate should take priority over RecommendedDisplayFrameRate");
        }

        [Test]
        public void Test_DetectFrameRate_CineRate_Priority_Over_FrameTime()
        {
            // Per the implementation: CineRate > RecommendedRate > FrameTime
            var ds = new DicomDataset();
            var cineRateTag = new DicomTag(0x0018, 0x0040);
            var frameTimeTag = new DicomTag(0x0018, 0x1063);

            AddIsString(ds, cineRateTag, 25);
            AddDsString(ds, frameTimeTag, DicomVR.DS, "33.33"); // ~30 fps

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(25.0),
                "CineRate should take priority over FrameTime");
        }

        [Test]
        public void Test_DetectFrameRate_NoTags_ReturnsZero()
        {
            var ds = new DicomDataset();
            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(0.0));
        }

        [Test]
        public void Test_DetectFrameRate_NullDataset_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => VideoEncoder.DetectFrameRate(null!));
        }

        [Test]
        public void Test_DetectFrameRate_FromFrameTimeVector()
        {
            // Frame Time Vector with variable frame times
            var ds = new DicomDataset();
            var frameTimeVectorTag = new DicomTag(0x0018, 0x1065);
            // Average of 33.33, 33.33, 33.34 = ~33.333 ms -> ~30 fps
            AddDsString(ds, frameTimeVectorTag, DicomVR.DS, "33.33\\33.33\\33.34");

            double fps = VideoEncoder.DetectFrameRate(ds);
            Assert.That(fps, Is.EqualTo(30.0).Within(0.1));
        }

        // --- VideoFrame tests ---

        [Test]
        public void Test_VideoFrame_Constructor_Rgb24()
        {
            int width = 64;
            int height = 48;
            int expectedSize = width * height * 3; // RGB24
            var data = new byte[expectedSize];

            var frame = new VideoFrame(data, width, height, VideoPixelFormat.Rgb24);

            Assert.That(frame.Width, Is.EqualTo(width));
            Assert.That(frame.Height, Is.EqualTo(height));
            Assert.That(frame.Format, Is.EqualTo(VideoPixelFormat.Rgb24));
            Assert.That(frame.PixelData.Length, Is.EqualTo(expectedSize));
        }

        [Test]
        public void Test_VideoFrame_Constructor_Gray8()
        {
            int width = 128;
            int height = 128;
            int expectedSize = width * height;
            var data = new byte[expectedSize];

            var frame = new VideoFrame(data, width, height, VideoPixelFormat.Gray8);

            Assert.That(frame.Width, Is.EqualTo(width));
            Assert.That(frame.Height, Is.EqualTo(height));
            Assert.That(frame.Format, Is.EqualTo(VideoPixelFormat.Gray8));
        }

        [Test]
        public void Test_VideoFrame_Constructor_Gray16()
        {
            int width = 64;
            int height = 64;
            int expectedSize = width * height * 2;
            var data = new byte[expectedSize];

            var frame = new VideoFrame(data, width, height, VideoPixelFormat.Gray16);
            Assert.That(frame.PixelData.Length, Is.EqualTo(expectedSize));
        }

        [Test]
        public void Test_VideoFrame_Constructor_TooSmallData_Throws()
        {
            int width = 64;
            int height = 48;
            var tooSmall = new byte[10]; // Way too small for 64x48 RGB24

            Assert.Throws<ArgumentException>(() =>
                new VideoFrame(tooSmall, width, height, VideoPixelFormat.Rgb24));
        }

        [Test]
        public void Test_VideoFrame_Constructor_InvalidWidth_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VideoFrame(new byte[100], 0, 10, VideoPixelFormat.Gray8));
        }

        [Test]
        public void Test_VideoFrame_Constructor_InvalidHeight_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VideoFrame(new byte[100], 10, 0, VideoPixelFormat.Gray8));
        }

        [Test]
        public void Test_VideoFrame_Dispose_NoException()
        {
            int width = 8;
            int height = 8;
            var data = new byte[width * height * 3];
            var frame = new VideoFrame(data, width, height, VideoPixelFormat.Rgb24);

            Assert.DoesNotThrow(() => frame.Dispose());
        }

        [Test]
        public void Test_VideoFrame_IsDisposed_After_Dispose()
        {
            int width = 8;
            int height = 8;
            var data = new byte[width * height * 3];
            var frame = new VideoFrame(data, width, height, VideoPixelFormat.Rgb24);

            Assert.That(frame.IsDisposed, Is.False);
            frame.Dispose();
            Assert.That(frame.IsDisposed, Is.True);
        }

        [Test]
        public void Test_VideoFrame_AudioSamples_Default_Null()
        {
            var data = new byte[8 * 8 * 3];
            var frame = new VideoFrame(data, 8, 8, VideoPixelFormat.Rgb24);

            Assert.That(frame.AudioSamples, Is.Null);
            Assert.That(frame.AudioFormat, Is.EqualTo(AudioSampleFormat.None));
        }

        [Test]
        public void Test_VideoFrame_CalculateDataSize_AllFormats()
        {
            int w = 100;
            int h = 80;

            Assert.That(VideoFrame.CalculateDataSize(w, h, VideoPixelFormat.Rgb24),
                Is.EqualTo(w * h * 3));
            Assert.That(VideoFrame.CalculateDataSize(w, h, VideoPixelFormat.Gray8),
                Is.EqualTo(w * h));
            Assert.That(VideoFrame.CalculateDataSize(w, h, VideoPixelFormat.Gray16),
                Is.EqualTo(w * h * 2));

            // YUV420P: Y(100*80) + U(50*40) + V(50*40) = 8000 + 2000 + 2000 = 12000
            int yuv420Size = VideoFrame.CalculateDataSize(w, h, VideoPixelFormat.Yuv420P);
            Assert.That(yuv420Size, Is.EqualTo(w * h + 2 * ((w + 1) / 2) * ((h + 1) / 2)));
        }

        // --- VideoEncodeProgress tests ---

        [Test]
        public void Test_VideoEncodeProgress_Percentage()
        {
            var progress = VideoEncodeProgress.Create(50, 100, TimeSpan.FromSeconds(5));

            Assert.That(progress.FramesEncoded, Is.EqualTo(50));
            Assert.That(progress.TotalFrames, Is.EqualTo(100));
            Assert.That(progress.Percentage, Is.EqualTo(50.0));
        }

        [Test]
        public void Test_VideoEncodeProgress_EstimatedRemaining()
        {
            var progress = VideoEncodeProgress.Create(50, 100, TimeSpan.FromSeconds(10));

            // 50 frames in 10s = 0.2s/frame, 50 remaining = 10s remaining
            Assert.That(progress.EstimatedRemaining, Is.Not.Null);
            Assert.That(progress.EstimatedRemaining!.Value.TotalSeconds, Is.EqualTo(10.0).Within(0.01));
        }

        [Test]
        public void Test_VideoEncodeProgress_ZeroTotal_ZeroPercentage()
        {
            var progress = VideoEncodeProgress.Create(10, 0, TimeSpan.FromSeconds(1));
            Assert.That(progress.Percentage, Is.EqualTo(0.0));
        }

        [Test]
        public void Test_VideoEncodeProgress_Equality()
        {
            var a = new VideoEncodeProgress(10, 100, 10.0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
            var b = new VideoEncodeProgress(10, 100, 10.0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        }

        [Test]
        public void Test_VideoEncodeProgress_Inequality()
        {
            var a = new VideoEncodeProgress(10, 100, 10.0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
            var b = new VideoEncodeProgress(20, 100, 20.0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void Test_VideoEncodeProgress_ToString()
        {
            var progress = VideoEncodeProgress.Create(50, 100, TimeSpan.FromSeconds(5));
            var str = progress.ToString();

            Assert.That(str, Does.Contain("50"));
            Assert.That(str, Does.Contain("100"));
            Assert.That(str, Does.Contain("%"));
        }

        [Test]
        public void Test_VideoEncodeProgress_ToString_UnknownTotal()
        {
            var progress = VideoEncodeProgress.Create(50, 0, TimeSpan.FromSeconds(5));
            var str = progress.ToString();

            Assert.That(str, Does.Contain("50 frames"));
        }

        // --- MapCodecToTransferSyntax tests ---

        [Test]
        public void Test_MapCodecToTransferSyntax_MPEG2()
        {
            var ts = VideoEncoder.MapCodecToTransferSyntax(VideoCodecType.MPEG2);
            Assert.That(ts, Is.EqualTo(TransferSyntax.MPEG2MainML));
        }

        [Test]
        public void Test_MapCodecToTransferSyntax_H264()
        {
            var ts = VideoEncoder.MapCodecToTransferSyntax(VideoCodecType.H264);
            Assert.That(ts, Is.EqualTo(TransferSyntax.H264HighProfile41));
        }

        [Test]
        public void Test_MapCodecToTransferSyntax_HEVC()
        {
            var ts = VideoEncoder.MapCodecToTransferSyntax(VideoCodecType.HEVC);
            Assert.That(ts, Is.EqualTo(TransferSyntax.HEVCMainProfile51));
        }

        [Test]
        public void Test_MapCodecToTransferSyntax_InvalidCodec_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                VideoEncoder.MapCodecToTransferSyntax((VideoCodecType)999));
        }

        // --- Encoder availability tests ---

        [Test]
        public void Test_IsAvailable_False_WithoutBackend()
        {
            Assert.That(VideoEncoder.IsAvailable, Is.False);
        }

        [Test]
        public void Test_IsAvailable_True_AfterRegisterBackend()
        {
            VideoEncoder.RegisterBackend((frames, options, w, h, progress) => Array.Empty<byte>());
            Assert.That(VideoEncoder.IsAvailable, Is.True);
        }

        [Test]
        public void Test_RegisterBackend_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => VideoEncoder.RegisterBackend(null!));
        }

        [Test]
        public void Test_EncodeFromFrames_NoBackend_Throws()
        {
            var frames = Array.Empty<VideoFrame>();
            var options = new VideoEncoderOptions();

            Assert.Throws<InvalidOperationException>(() =>
                VideoEncoder.EncodeFromFrames(frames, options, 640, 480));
        }

        // --- Explicit test: actual encoding requires native lib ---

        [Test]
        [Explicit("Requires native video encoding library (FFmpeg). Not available in CI.")]
        public void Test_EncodeFromFrames_RequiresNative()
        {
            // This test documents that actual encoding requires a native backend.
            // Without calling RegisterBackend with a real implementation,
            // EncodeFromFrames will throw InvalidOperationException.
            var width = 64;
            var height = 48;
            var frameData = new byte[width * height * 3];
            var frame = new VideoFrame(frameData, width, height, VideoPixelFormat.Rgb24);
            var options = VideoEncoderOptions.Diagnostic;

            Assert.Throws<InvalidOperationException>(() =>
                VideoEncoder.EncodeFromFrames(new[] { frame }, options, width, height));
        }

        // --- Helper methods ---

        /// <summary>
        /// Adds a DS (Decimal String) element to the dataset.
        /// </summary>
        private static void AddDsString(DicomDataset ds, DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = (byte)' '; // DS pads with space
                bytes = padded;
            }
            ds.AddOrUpdate(new DicomStringElement(tag, vr, bytes));
        }

        /// <summary>
        /// Adds an IS (Integer String) element to the dataset.
        /// </summary>
        private static void AddIsString(DicomDataset ds, DicomTag tag, int value)
        {
            var str = value.ToString(CultureInfo.InvariantCulture);
            var bytes = Encoding.ASCII.GetBytes(str);
            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = (byte)' '; // IS pads with space
                bytes = padded;
            }
            ds.AddOrUpdate(new DicomStringElement(tag, DicomVR.IS, bytes));
        }
    }
}
