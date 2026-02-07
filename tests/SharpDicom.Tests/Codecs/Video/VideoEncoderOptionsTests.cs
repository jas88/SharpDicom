using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Codecs.Video;

namespace SharpDicom.Tests.Codecs.Video
{
    /// <summary>
    /// Tests for <see cref="VideoEncoderOptions"/> covering default values,
    /// quality presets, and raw parameter escape hatch.
    /// </summary>
    [TestFixture]
    public class VideoEncoderOptionsTests
    {
        [Test]
        public void Test_Default_Options()
        {
            var options = new VideoEncoderOptions();

            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.H264));
            Assert.That(options.Preset, Is.EqualTo(VideoQualityPreset.Diagnostic));
            Assert.That(options.FrameRate, Is.EqualTo(30.0));
            Assert.That(options.AudioCodec, Is.EqualTo(AudioCodecType.None));
            Assert.That(options.AudioSampleRate, Is.EqualTo(48000));
            Assert.That(options.AudioChannels, Is.EqualTo(2));
            Assert.That(options.HwAccel, Is.EqualTo(HardwareAcceleration.Auto));
            Assert.That(options.GopSize, Is.EqualTo(0));
            Assert.That(options.RawParameters, Is.Null);
        }

        [Test]
        public void Test_Diagnostic_Preset()
        {
            var options = VideoEncoderOptions.Diagnostic;

            Assert.That(options.Preset, Is.EqualTo(VideoQualityPreset.Diagnostic));
            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.H264));
            Assert.That(options.FrameRate, Is.EqualTo(30.0));
        }

        [Test]
        public void Test_Review_Preset()
        {
            var options = VideoEncoderOptions.Review;

            Assert.That(options.Preset, Is.EqualTo(VideoQualityPreset.Review));
            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.H264));
            Assert.That(options.FrameRate, Is.EqualTo(30.0));
        }

        [Test]
        public void Test_Archive_Preset()
        {
            var options = VideoEncoderOptions.Archive;

            Assert.That(options.Preset, Is.EqualTo(VideoQualityPreset.Archive));
            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.H264));
            Assert.That(options.FrameRate, Is.EqualTo(30.0));
        }

        [Test]
        public void Test_RawParameters_EscapeHatch()
        {
            var rawParams = new Dictionary<string, string>
            {
                ["profile"] = "high",
                ["level"] = "4.1",
                ["crf"] = "18"
            };

            var options = new VideoEncoderOptions { RawParameters = rawParams };

            Assert.That(options.RawParameters, Is.Not.Null);
            Assert.That(options.RawParameters!.Count, Is.EqualTo(3));
            Assert.That(options.RawParameters["profile"], Is.EqualTo("high"));
            Assert.That(options.RawParameters["level"], Is.EqualTo("4.1"));
            Assert.That(options.RawParameters["crf"], Is.EqualTo("18"));
        }

        [Test]
        public void Test_Init_Codec_MPEG2()
        {
            var options = new VideoEncoderOptions { Codec = VideoCodecType.MPEG2 };
            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.MPEG2));
        }

        [Test]
        public void Test_Init_Codec_HEVC()
        {
            var options = new VideoEncoderOptions { Codec = VideoCodecType.HEVC };
            Assert.That(options.Codec, Is.EqualTo(VideoCodecType.HEVC));
        }

        [Test]
        public void Test_Init_FrameRate_Custom()
        {
            var options = new VideoEncoderOptions { FrameRate = 60.0 };
            Assert.That(options.FrameRate, Is.EqualTo(60.0));
        }

        [Test]
        public void Test_Init_AudioCodec_AAC()
        {
            var options = new VideoEncoderOptions { AudioCodec = AudioCodecType.AAC };
            Assert.That(options.AudioCodec, Is.EqualTo(AudioCodecType.AAC));
        }

        [Test]
        public void Test_Init_AudioCodec_PCM()
        {
            var options = new VideoEncoderOptions { AudioCodec = AudioCodecType.PCM };
            Assert.That(options.AudioCodec, Is.EqualTo(AudioCodecType.PCM));
        }

        [Test]
        public void Test_Init_HwAccel_ForceCpu()
        {
            var options = new VideoEncoderOptions { HwAccel = HardwareAcceleration.ForceCpu };
            Assert.That(options.HwAccel, Is.EqualTo(HardwareAcceleration.ForceCpu));
        }

        [Test]
        public void Test_Init_HwAccel_PreferGpu()
        {
            var options = new VideoEncoderOptions { HwAccel = HardwareAcceleration.PreferGpu };
            Assert.That(options.HwAccel, Is.EqualTo(HardwareAcceleration.PreferGpu));
        }

        [Test]
        public void Test_Init_GopSize_Custom()
        {
            var options = new VideoEncoderOptions { GopSize = 24 };
            Assert.That(options.GopSize, Is.EqualTo(24));
        }

        [Test]
        public void Test_Presets_AreDistinct()
        {
            var diagnostic = VideoEncoderOptions.Diagnostic;
            var review = VideoEncoderOptions.Review;
            var archive = VideoEncoderOptions.Archive;

            Assert.That(diagnostic.Preset, Is.Not.EqualTo(review.Preset));
            Assert.That(review.Preset, Is.Not.EqualTo(archive.Preset));
            Assert.That(diagnostic.Preset, Is.Not.EqualTo(archive.Preset));
        }
    }
}
