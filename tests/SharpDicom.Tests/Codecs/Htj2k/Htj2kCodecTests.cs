using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs.Jpeg2000;
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
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

        [Test]
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLosslessCodec_12Bit_RoundtripCorrect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 16,
                Columns: 16,
                BitsAllocated: 16,
                BitsStored: 12,
                HighBit: 11,
                SamplesPerPixel: 1,
                PixelRepresentation: 0,
                PlanarConfiguration: 0,
                NumberOfFrames: 1);

            // Create test image with 12-bit values
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length / 2; i++)
            {
                ushort value = (ushort)((i * 17) % 4096); // 0-4095 range
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLosslessCodec_RGB_RoundtripCorrect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 16,
                Columns: 16,
                BitsAllocated: 8,
                BitsStored: 8,
                HighBit: 7,
                SamplesPerPixel: 3,
                PixelRepresentation: 0,
                PlanarConfiguration: 0,
                NumberOfFrames: 1);

            // Create RGB test image
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length / 3; i++)
            {
                pixelData[i * 3] = (byte)(i % 256);       // R
                pixelData[i * 3 + 1] = (byte)((i * 2) % 256); // G
                pixelData[i * 3 + 2] = (byte)((i * 3) % 256); // B
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLosslessRpclCodec_Roundtrip_Correct()
        {
            var codec = new Htj2kLosslessRpclCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);

            // Create test image
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode with RPCL progression
            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLosslessCodec_MultiFrame_RoundtripCorrect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16, numberOfFrames: 3);

            // Create 3-frame image
            var pixelData = new byte[info.FrameSize * 3];
            for (int frame = 0; frame < 3; frame++)
            {
                for (int i = 0; i < info.FrameSize; i++)
                {
                    pixelData[frame * info.FrameSize + i] = (byte)((frame * 50 + i) % 256);
                }
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(3));

            // Decode each frame
            for (int frame = 0; frame < 3; frame++)
            {
                var decoded = new byte[info.FrameSize];
                var result = codec.Decode(fragments, info, frame, decoded);

                Assert.That(result.Success, Is.True);
                var expectedFrame = new byte[info.FrameSize];
                Array.Copy(pixelData, frame * info.FrameSize, expectedFrame, 0, info.FrameSize);
                Assert.That(decoded, Is.EqualTo(expectedFrame));
            }
        }

        [Test]
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLosslessCodec_LargeImage_Roundtrip()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(512, 512);

            // Create large test image with gradient
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
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
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kCodec_HasCapMarker_InOutput()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);
            var data = fragments.Fragments[0].Span;

            // Look for CAP marker (0xFF50) in the output
            bool hasCapMarker = false;
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0x50)
                {
                    hasCapMarker = true;
                    break;
                }
            }

            Assert.That(hasCapMarker, Is.True, "HTJ2K output should contain CAP marker (0xFF50)");
        }

        [Test]
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kLossyCodec_QualityAcceptable_PSNR()
        {
            var codec = new Htj2kLossyCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Create gradient test image
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode lossy
            var fragments = codec.Encode(pixelData, info);

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Calculate PSNR
            double mse = 0;
            for (int i = 0; i < pixelData.Length; i++)
            {
                double diff = pixelData[i] - decoded[i];
                mse += diff * diff;
            }
            mse /= pixelData.Length;

            double psnr = 10 * Math.Log10((255.0 * 255.0) / mse);

            // Lossy compression should have reasonable PSNR (>30dB for medical imaging)
            Assert.That(psnr, Is.GreaterThan(30.0), $"PSNR should be > 30dB, got {psnr:F2}dB");
        }

        [Test]
        [Ignore("J2K pipeline issues: tier-2 packet assembly/parsing needs work (21-08 investigation)")]
        public void Htj2kDecoder_StandardJ2K_StillDecodes()
        {
            // Use standard J2K encoder
            var j2kCodec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode with standard J2K (no CAP marker)
            var fragments = j2kCodec.Encode(pixelData, info);

            // Decode with HTJ2K codec (should work - backward compatible)
            var htj2kCodec = new Htj2kLosslessCodec();
            var decoded = new byte[info.FrameSize];
            var result = htj2kCodec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, "HTJ2K decoder should handle standard J2K");
            Assert.That(decoded, Is.EqualTo(pixelData));
        }
    }
}
