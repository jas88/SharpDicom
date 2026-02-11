using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    /// <summary>
    /// Integration tests for HTJ2K codec with HT block coding.
    /// </summary>
    /// <remarks>
    /// These tests verify the HT block coder integration into the HTJ2K pipeline:
    /// encode uses HtBlockEncoder, CAP marker is generated, decode auto-detects HT mode.
    /// </remarks>
    [TestFixture]
    public class Htj2kIntegrationTests
    {
        // ---- CAP marker tests ----

        [Test]
        public void Encode_8Bit_HasCapMarker()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            var data = fragments.Fragments[0].Span;

            Assert.That(FindCapMarkerOffset(data), Is.GreaterThan(0),
                "HTJ2K output must contain CAP marker (0xFF50)");
        }

        [Test]
        public void Encode_16Bit_HasCapMarker()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale16(16, 16);
            var pixelData = CreateGradient16(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            var data = fragments.Fragments[0].Span;

            Assert.That(FindCapMarkerOffset(data), Is.GreaterThan(0),
                "HTJ2K 16-bit output must contain CAP marker");
        }

        [Test]
        public void Encode_CapMarker_ContainsHtOnlyFlag()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            var data = fragments.Fragments[0];

            // Parse the codestream and verify HT mode is detected
            bool parsed = J2kCodestream.TryParse(data.Span, out var header, out _);

            Assert.That(parsed, Is.True, "Should parse HTJ2K codestream");
            Assert.That(header!.IsHtj2k, Is.True, "Should detect HTJ2K from CAP marker");
            Assert.That(header.HtCodingMode, Is.EqualTo(HtMode.HtOnly),
                "Should detect HTONLY mode from Ccap[15]");
        }

        [Test]
        public void BuildCapMarker_HtOnly_ProducesCorrectBytes()
        {
            byte[] cap = J2kCodestream.BuildCapMarker(isHtOnly: true, isLossless: true, precision: 8);

            Assert.That(cap.Length, Is.EqualTo(10), "CAP marker should be 10 bytes");

            // Marker prefix
            Assert.That(cap[0], Is.EqualTo(0xFF));
            Assert.That(cap[1], Is.EqualTo(0x50));

            // Length = 8
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(cap.AsSpan(2)), Is.EqualTo(8));

            // Pcap: bit 17 set for Part 15
            uint pcap = BinaryPrimitives.ReadUInt32BigEndian(cap.AsSpan(4));
            Assert.That(pcap & 0x00020000u, Is.Not.EqualTo(0u), "Part 15 bit should be set");

            // Ccap[15]: bit 5 = HTIRV (cleared for lossless/reversible), bits 4-0 = Bp from MAGB
            ushort ccap = BinaryPrimitives.ReadUInt16BigEndian(cap.AsSpan(8));
            Assert.That(ccap & 0x0020, Is.EqualTo(0), "HTIRV flag should NOT be set for lossless");
            // For 8-bit with 5 decomps: MAGB=10, Bp=10-8=2
            Assert.That(ccap & 0x1F, Is.EqualTo(2), "Bp should be 2 for 8-bit lossless");
        }

        [Test]
        public void BuildCapMarker_NotHtOnly_HasDeclaredFlag()
        {
            byte[] cap = J2kCodestream.BuildCapMarker(isHtOnly: false, isLossless: true, precision: 12);

            ushort ccap = BinaryPrimitives.ReadUInt16BigEndian(cap.AsSpan(8));
            Assert.That(ccap & 0xC000, Is.EqualTo(0x4000), "Bits 15-14 should be 01 (HTDECLARED) for non-HTONLY");
            Assert.That(ccap & 0x0020, Is.EqualTo(0), "HTIRV flag should NOT be set for lossless");
            // For 12-bit with 5 decomps: MAGB=14, Bp=14-8=6
            Assert.That(ccap & 0x1F, Is.EqualTo(6), "Bp should be 6 for 12-bit lossless");
        }

        [Test]
        public void BuildCapMarker_HtOnly_HasHtOnlyMode()
        {
            byte[] cap = J2kCodestream.BuildCapMarker(isHtOnly: true, isLossless: true, precision: 8);

            ushort ccap = BinaryPrimitives.ReadUInt16BigEndian(cap.AsSpan(8));
            Assert.That(ccap & 0xC000, Is.EqualTo(0), "Bits 15-14 should be 00 (HTONLY) for HT-only");
        }

        // ---- HtEncoderOptions tests ----

        [Test]
        public void HtEncoderOptions_Lossless_IsLossless()
        {
            var opts = HtEncoderOptions.Lossless;

            Assert.That(opts.IsLossless, Is.True);
            Assert.That(opts.HtSetCount, Is.EqualTo(2));
            Assert.That(opts.IncludeSigProp, Is.True);
            Assert.That(opts.IncludeMagRef, Is.True);
            Assert.That(opts.TargetBpp, Is.Null);
            Assert.That(opts.TargetPsnr, Is.Null);
            Assert.That(opts.EffectivePassCount, Is.EqualTo(6));
        }

        [Test]
        public void HtEncoderOptions_Diagnostic_HasPsnr40()
        {
            var opts = HtEncoderOptions.Diagnostic;

            Assert.That(opts.IsLossless, Is.False);
            Assert.That(opts.TargetPsnr, Is.EqualTo(40f));
            Assert.That(opts.HtSetCount, Is.EqualTo(2));
            Assert.That(opts.EffectivePassCount, Is.EqualTo(6));
        }

        [Test]
        public void HtEncoderOptions_Archive_HasPsnr35()
        {
            var opts = HtEncoderOptions.Archive;

            Assert.That(opts.IsLossless, Is.False);
            Assert.That(opts.TargetPsnr, Is.EqualTo(35f));
        }

        [Test]
        public void HtEncoderOptions_Review_1Set_HasPsnr30()
        {
            var opts = HtEncoderOptions.Review;

            Assert.That(opts.HtSetCount, Is.EqualTo(1));
            Assert.That(opts.IncludeSigProp, Is.True);
            Assert.That(opts.IncludeMagRef, Is.True);
            Assert.That(opts.TargetPsnr, Is.EqualTo(30f));
            Assert.That(opts.EffectivePassCount, Is.EqualTo(3));
        }

        [Test]
        public void HtEncoderOptions_Fast_CleanupOnly()
        {
            var opts = HtEncoderOptions.Fast;

            Assert.That(opts.HtSetCount, Is.EqualTo(1));
            Assert.That(opts.IncludeSigProp, Is.False);
            Assert.That(opts.IncludeMagRef, Is.False);
            Assert.That(opts.TargetPsnr, Is.EqualTo(25f));
            Assert.That(opts.EffectivePassCount, Is.EqualTo(1));
        }

        [Test]
        public void Htj2kCodecOptions_Default_UsesLosslessPreset()
        {
            var opts = Htj2kCodecOptions.Default;
            var htOpts = opts.EffectiveHtOptions;

            Assert.That(htOpts.IsLossless, Is.True);
            Assert.That(htOpts.HtSetCount, Is.EqualTo(2));
        }

        [Test]
        public void Htj2kCodecOptions_Lossy_UsesDiagnosticPreset()
        {
            var opts = Htj2kCodecOptions.Lossy;
            var htOpts = opts.EffectiveHtOptions;

            Assert.That(htOpts.IsLossless, Is.False);
            Assert.That(htOpts.TargetPsnr, Is.EqualTo(40f));
        }

        // ---- Lossless roundtrip tests ----

        [Test]
        public void Lossless_8Bit_Roundtrip_PixelPerfect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(pixelData), "8-bit lossless roundtrip must be pixel-perfect");
        }

        [Test]
        public void Lossless_12Bit_Roundtrip_PixelPerfect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 16, Columns: 16,
                BitsAllocated: 16, BitsStored: 12, HighBit: 11,
                SamplesPerPixel: 1, PixelRepresentation: 0,
                PlanarConfiguration: 0, NumberOfFrames: 1);

            var pixelData = Create12BitGradient(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(pixelData), "12-bit lossless roundtrip must be pixel-perfect");
        }

        [Test]
        public void Lossless_16Bit_Roundtrip_PixelPerfect()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale16(16, 16);
            var pixelData = CreateGradient16(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(pixelData), "16-bit lossless roundtrip must be pixel-perfect");
        }

        // ---- Lossy tests ----

        [Test]
        public void Lossy_EncodeDecode_ProducesValidOutput()
        {
            var codec = new Htj2kLossyCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded.Length, Is.EqualTo(pixelData.Length));
        }

        [Test]
        public void Lossy_Diagnostic_QualityAcceptable()
        {
            var codec = new Htj2kLossyCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var opts = new Htj2kCodecOptions(false, 5, false, true, HtEncoderOptions.Diagnostic);
            var fragments = codec.Encode(pixelData, info, opts);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");

            // Measure quality: decoded should be close to original
            double mse = CalculateMse(pixelData, decoded);
            // PSNR is infinite when MSE=0 (lossless), otherwise check quality
            if (mse > 0)
            {
                double psnr = 10 * Math.Log10(255.0 * 255.0 / mse);
                // Note: the HT encoder with EBCOT-compatible pipeline may produce
                // near-lossless results since rate control is not yet applied
                Assert.That(psnr, Is.GreaterThan(20.0),
                    $"Diagnostic quality should be reasonable, got {psnr:F2}dB");
            }
        }

        // ---- Multi-frame test ----

        [Test]
        public void MultiFrame_EncodesDecode_AllFrames()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16, numberOfFrames: 3);
            int frameSize = info.FrameSize;

            var pixelData = new byte[frameSize * 3];
            for (int frame = 0; frame < 3; frame++)
            {
                for (int i = 0; i < frameSize; i++)
                {
                    pixelData[frame * frameSize + i] = (byte)((frame * 50 + i) % 256);
                }
            }

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(3), "Should have 3 encoded frames");

            for (int frame = 0; frame < 3; frame++)
            {
                var decoded = new byte[frameSize];
                var result = codec.Decode(fragments, info, frame, decoded);

                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed: {result.Diagnostic}");

                var expected = new byte[frameSize];
                Array.Copy(pixelData, frame * frameSize, expected, 0, frameSize);
                Assert.That(decoded, Is.EqualTo(expected), $"Frame {frame} mismatch");
            }
        }

        // ---- RPCL progression test ----

        [Test]
        public void Rpcl_Roundtrip_Succeeds()
        {
            var codec = new Htj2kLosslessRpclCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Verify CAP marker present
            var data = fragments.Fragments[0];
            bool parsed = J2kCodestream.TryParse(data.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.IsHtj2k, Is.True);

            // Verify RPCL progression
            Assert.That(header.Progression, Is.EqualTo(ProgressionOrder.RPCL),
                "RPCL codec should use RPCL progression order");

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        // ---- CodecRegistry test ----

        [Test]
        public void CodecRegistry_ResolvesAll3Htj2kCodecs()
        {
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var lossless = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLossless);
            var rpcl = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLosslessRPCL);
            var lossy = CodecRegistry.GetCodec(TransferSyntax.HTJ2KLossy);

            Assert.That(lossless, Is.InstanceOf<Htj2kLosslessCodec>());
            Assert.That(rpcl, Is.InstanceOf<Htj2kLosslessRpclCodec>());
            Assert.That(lossy, Is.InstanceOf<Htj2kLossyCodec>());
        }

        // ---- HT block coder is actually used ----

        [Test]
        public void Encode_UsesHtBlockCoder_NotEbcot()
        {
            // Encode with HTJ2K codec
            var htCodec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var htFragments = htCodec.Encode(pixelData, info);
            var htData = htFragments.Fragments[0];

            // Parse and verify HTJ2K identification
            bool parsed = J2kCodestream.TryParse(htData.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.IsHtj2k, Is.True, "HTJ2K encode should produce HT codestream");
            Assert.That(header.HtCodingMode, Is.EqualTo(HtMode.HtOnly));
        }

        // ---- Decode auto-detection ----

        [Test]
        public void Decode_AutoDetects_HtFromCapMarker()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            var decoded = new byte[info.FrameSize];

            // Decode should auto-detect HT mode from CAP marker and use HtBlockEncoder
            var result = codec.Decode(fragments, info, 0, decoded);
            Assert.That(result.Success, Is.True, $"Auto-detect decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(pixelData));
        }

        [Test]
        public void Decode_FallsBackToEbcot_ForStandardJ2K()
        {
            // Encode with standard J2K (no CAP marker)
            var j2kCodec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = j2kCodec.Encode(pixelData, info);

            // Verify no CAP marker in J2K output
            var j2kData = fragments.Fragments[0];
            bool parsed = J2kCodestream.TryParse(j2kData.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.IsHtj2k, Is.False, "Standard J2K should not have CAP marker");

            // HTJ2K decoder should still decode standard J2K via EBCOT fallback
            var htj2kCodec = new Htj2kLosslessCodec();
            var decoded = new byte[info.FrameSize];
            var result = htj2kCodec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, "HTJ2K decoder should handle standard J2K");
        }

        // ---- Validation test ----

        [Test]
        public void Validate_HtOutput_HasNoIssues()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(16, 16);
            var pixelData = CreateGradient8(info.FrameSize);

            var fragments = codec.Encode(pixelData, info);
            var validation = codec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.True,
                $"Validation should pass; issues: {string.Join(", ", validation.Issues ?? Array.Empty<CodecDiagnostic>())}");
        }

        // ---- Helper methods ----

        private static byte[] CreateGradient8(int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)(i % 256);
            }
            return data;
        }

        private static byte[] CreateGradient16(int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size / 2; i++)
            {
                ushort value = (ushort)(i * 16);
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }
            return data;
        }

        private static byte[] Create12BitGradient(int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size / 2; i++)
            {
                ushort value = (ushort)((i * 17) % 4096); // 0-4095 range
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }
            return data;
        }

        private static double CalculateMse(byte[] original, byte[] decoded)
        {
            if (original.Length != decoded.Length)
            {
                return double.MaxValue;
            }

            double sum = 0;
            for (int i = 0; i < original.Length; i++)
            {
                double diff = original[i] - decoded[i];
                sum += diff * diff;
            }
            return sum / original.Length;
        }

        private static int FindCapMarkerOffset(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0x50)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
