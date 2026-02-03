using System;
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
                int diff = Math.Abs(pixelData[i] - decoded[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(2), $"Error at position {i}: {diff}");
            }
        }

        [Test]
        public void JpegLsLosslessCodec_12Bit_RoundtripCorrect()
        {
            var codec = new JpegLsLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 32,
                Columns: 32,
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
                ushort value = (ushort)((i * 4) % 4096); // 12-bit values 0-4095
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)(value >> 8);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void JpegLsLosslessCodec_MultiFrame_RoundtripCorrect()
        {
            var codec = new JpegLsLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 32,
                Columns: 32,
                BitsAllocated: 8,
                BitsStored: 8,
                HighBit: 7,
                SamplesPerPixel: 1,
                PixelRepresentation: 0,
                PlanarConfiguration: 0,
                NumberOfFrames: 3);

            // Create test data with 3 frames
            var pixelData = new byte[info.FrameSize * 3];
            for (int frame = 0; frame < 3; frame++)
            {
                int offset = frame * info.FrameSize;
                for (int i = 0; i < info.FrameSize; i++)
                {
                    pixelData[offset + i] = (byte)((i + frame * 30) % 256);
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

                var expectedFrame = new ReadOnlySpan<byte>(pixelData, frame * info.FrameSize, info.FrameSize);
                Assert.That(decoded, Is.EqualTo(expectedFrame.ToArray()));
            }
        }

        [Test]
        public void JpegLsLosslessCodec_FlatRegion_CompressesWell()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Create flat image (all same value)
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = 128;
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // Flat region should compress extremely well
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(info.FrameSize / 10));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void JpegLsNearLosslessCodec_Near1_BoundedError()
        {
            var codec = new JpegLsNearLosslessCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode with NEAR=1
            var options = new JpegLsCodecOptions(1, JlsInterleaveMode.None, true);
            var fragments = codec.Encode(pixelData, info, options);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Verify maximum error is 1
            for (int i = 0; i < pixelData.Length; i++)
            {
                int diff = Math.Abs(pixelData[i] - decoded[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(1), $"Error at position {i}: {diff}");
            }
        }

        [Test]
        public void JpegLsNearLosslessCodec_Near5_BoundedError()
        {
            var codec = new JpegLsNearLosslessCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode with NEAR=5
            var options = new JpegLsCodecOptions(5, JlsInterleaveMode.None, true);
            var fragments = codec.Encode(pixelData, info, options);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // Verify maximum error is 5
            for (int i = 0; i < pixelData.Length; i++)
            {
                int diff = Math.Abs(pixelData[i] - decoded[i]);
                Assert.That(diff, Is.LessThanOrEqualTo(5), $"Error at position {i}: {diff}");
            }
        }

        [Test]
        public void JpegLsLosslessCodec_InterleaveMode_None_RGB()
        {
            var codec = new JpegLsLosslessCodec();
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

            // Create RGB test data
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i += 3)
            {
                pixelData[i] = (byte)((i / 3) % 256);     // R
                pixelData[i + 1] = (byte)((i / 3 + 85) % 256);  // G
                pixelData[i + 2] = (byte)((i / 3 + 170) % 256); // B
            }

            // Encode with non-interleaved mode
            var options = new JpegLsCodecOptions(0, JlsInterleaveMode.None, true);
            var fragments = codec.Encode(pixelData, info, options);

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void JpegLsLosslessCodec_GradientImage_CompressesWell()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(128, 128);

            // Create gradient image (highly predictable)
            var pixelData = new byte[info.FrameSize];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    pixelData[y * 128 + x] = (byte)((x + y) / 2);
                }
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // Gradient should compress very well
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(info.FrameSize / 5));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void JpegLsLosslessCodec_RandomNoise_NoExpansion()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Create random noise (worst case for compression)
            var random = new Random(42);
            var pixelData = new byte[info.FrameSize];
            random.NextBytes(pixelData);

            // Encode
            var fragments = codec.Encode(pixelData, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // Random data won't compress, but shouldn't expand significantly
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(info.FrameSize * 1.1));

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void JpegLsLosslessCodec_16BitMedicalRealistic_RoundtripCorrect()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale16(64, 64);

            // Create realistic 16-bit medical data (CT-like values)
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length / 2; i++)
            {
                // Values in typical CT range (-1000 to +3000 HU, offset by 1024)
                ushort value = (ushort)(1024 + (i % 4000));
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)(value >> 8);
            }

            // Encode
            var fragments = codec.Encode(pixelData, info);

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData));
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
