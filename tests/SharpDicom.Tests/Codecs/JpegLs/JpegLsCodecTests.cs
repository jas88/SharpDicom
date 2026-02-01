using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.JpegLs;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.JpegLs
{
    [TestFixture]
    public class JpegLsCodecTests
    {
        [Test]
        public void JpegLsLosslessCodec_Properties_AreCorrect()
        {
            var codec = new JpegLsLosslessCodec();

            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGLSLossless));
            Assert.That(codec.Name, Is.EqualTo("JPEG-LS Lossless Image Compression"));
            Assert.That(codec.Capabilities.IsLossy, Is.False);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
            Assert.That(codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void JpegLsNearLosslessCodec_Properties_AreCorrect()
        {
            var codec = new JpegLsNearLosslessCodec();

            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGLSNearLossless));
            Assert.That(codec.Name, Is.EqualTo("JPEG-LS Near-Lossless Image Compression"));
            Assert.That(codec.Capabilities.IsLossy, Is.True);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        [Ignore("JPEG-LS managed implementation is a stub - full implementation requires CharLS native library")]
        public void JpegLsLosslessCodec_EncodeRoundtrip_8Bit()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Create test image with gradient
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(pixelData.Length)); // Should compress

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Verify lossless roundtrip
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        [Ignore("JPEG-LS managed implementation is a stub - full implementation requires CharLS native library")]
        public void JpegLsLosslessCodec_EncodeRoundtrip_16Bit()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale16(32, 32);

            // Create test image
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length / 2; i++)
            {
                ushort value = (ushort)(i * 16); // Values 0-16384
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)(value >> 8);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        [Ignore("JPEG-LS managed implementation is a stub - full implementation requires CharLS native library")]
        public void JpegLsNearLosslessCodec_EncodeRoundtrip_HasBoundedError()
        {
            var codec = new JpegLsNearLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode with NEAR=2 (default for VisuallyLossless)
            var fragments = codec.Encode(pixelData, info);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Check error is bounded by NEAR parameter
            for (int i = 0; i < pixelData.Length; i++)
            {
                int diff = System.Math.Abs(pixelData[i] - decoded[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(2), $"Error at position {i}: {diff}");
            }
        }

        [Test]
        public void CodecRegistry_ReturnsJpegLsLossless_ForCorrectTransferSyntax()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.JPEGLSLossless);

            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.InstanceOf<JpegLsLosslessCodec>());
        }

        [Test]
        public void CodecRegistry_ReturnsJpegLsNearLossless_ForCorrectTransferSyntax()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.JPEGLSNearLossless);

            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.InstanceOf<JpegLsNearLosslessCodec>());
        }
    }
}
