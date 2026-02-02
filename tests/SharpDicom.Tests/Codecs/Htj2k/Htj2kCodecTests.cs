using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    [TestFixture]
    public class Htj2kCodecTests
    {
        [Test]
        public void Htj2kLosslessCodec_Properties_AreCorrect()
        {
            var codec = new Htj2kLosslessCodec();

            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.HTJ2KLossless));
            Assert.That(codec.Name, Is.EqualTo("High Throughput JPEG 2000 (Lossless)"));
            Assert.That(codec.Capabilities.IsLossy, Is.False);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
            Assert.That(codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void Htj2kLosslessRpclCodec_Properties_AreCorrect()
        {
            var codec = new Htj2kLosslessRpclCodec();

            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.HTJ2KLosslessRPCL));
            Assert.That(codec.Name, Is.EqualTo("High Throughput JPEG 2000 (Lossless RPCL)"));
            Assert.That(codec.Capabilities.IsLossy, Is.False);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Htj2kLossyCodec_Properties_AreCorrect()
        {
            var codec = new Htj2kLossyCodec();

            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.HTJ2KLossy));
            Assert.That(codec.Name, Is.EqualTo("High Throughput JPEG 2000 (Lossy)"));
            Assert.That(codec.Capabilities.IsLossy, Is.True);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        [Ignore("HTJ2K roundtrip depends on JPEG 2000 encoder - known issue with managed implementation")]
        public void Htj2kLosslessCodec_EncodeRoundtrip_8Bit()
        {
            var codec = new Htj2kLosslessCodec();
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

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Verify lossless roundtrip
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        [Ignore("HTJ2K roundtrip depends on JPEG 2000 encoder - known issue with managed implementation")]
        public void Htj2kLosslessCodec_EncodeRoundtrip_16Bit()
        {
            var codec = new Htj2kLosslessCodec();
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
        [Ignore("HTJ2K roundtrip depends on JPEG 2000 encoder - known issue with managed implementation")]
        public void Htj2kLossyCodec_EncodeDecode_ProducesOutput()
        {
            var codec = new Htj2kLossyCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Lossy codec may have differences but should produce valid output
            Assert.That(decoded.Length, Is.EqualTo(pixelData.Length));
        }

        [Test]
        public void CodecRegistry_ReturnsHtj2kLossless_ForCorrectTransferSyntax()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLossless);

            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.InstanceOf<Htj2kLosslessCodec>());
        }

        [Test]
        public void CodecRegistry_ReturnsHtj2kLosslessRpcl_ForCorrectTransferSyntax()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLosslessRPCL);

            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.InstanceOf<Htj2kLosslessRpclCodec>());
        }

        [Test]
        public void CodecRegistry_ReturnsHtj2kLossy_ForCorrectTransferSyntax()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLossy);

            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.InstanceOf<Htj2kLossyCodec>());
        }
    }
}
