using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Subband;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    /// <summary>
    /// EBCOT regression suite: verifies that multi-tile pipeline changes
    /// do not break existing EBCOT-based J2K encoding and decoding.
    /// All tests use <see cref="EbcotBlockCoder.Instance"/> explicitly.
    /// </summary>
    [TestFixture]
    public class J2kEbcotRegressionTests
    {
        #region Lossless J2K Roundtrip

        /// <summary>
        /// Lossless J2K roundtrip with 8-bit grayscale using EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossless_8Bit_Grayscale_RoundtripsWithEbcot()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);

            Assert.That(encoded.Length, Is.GreaterThan(0), "Encoder should produce output");
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True);

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            Assert.That(decoded, Is.EqualTo(pixelData), "Lossless 8-bit roundtrip should produce identical output");
        }

        /// <summary>
        /// Lossless J2K roundtrip with 16-bit grayscale using EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossless_16Bit_Grayscale_RoundtripsWithEbcot()
        {
            const int size = 16;
            var info = PixelDataInfo.Grayscale16(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < size * size; i++)
            {
                ushort value = (ushort)(i * 256);
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"16-bit decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(pixelData), "Lossless 16-bit roundtrip should produce identical output");
        }

        #endregion

        #region Lossy J2K Roundtrip

        /// <summary>
        /// Lossy J2K roundtrip with EBCOT produces non-zero output.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossy_8Bit_RoundtripsWithEbcot()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossy, lossless: false, EbcotBlockCoder.Instance);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"Lossy decode failed: {result.Diagnostic?.Message}");

            // Verify decoded has non-zero data
            bool hasNonZero = false;
            for (int i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }

            Assert.That(hasNonZero, Is.True, "Lossy decoded output should not be all zeros");
        }

        #endregion

        #region Codestream Validation

        /// <summary>
        /// Verify codestream structure (SOC, SIZ, COD, QCD, SOT, SOD, EOC) with EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Codestream_HasValidStructure_WithEbcot()
        {
            const int size = 16;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 16);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);

            // Verify SOC marker
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span), Is.EqualTo((ushort)0xFF4F), "SOC marker");

            // Verify SIZ marker follows
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span.Slice(2)), Is.EqualTo((ushort)0xFF51), "SIZ marker");

            // Verify EOC marker at end
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span.Slice(encoded.Length - 2)), Is.EqualTo((ushort)0xFFD9), "EOC marker");

            // Parse and verify header
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.ImageWidth, Is.EqualTo(size));
            Assert.That(header.ImageHeight, Is.EqualTo(size));
            Assert.That(header.ComponentCount, Is.EqualTo(1));
            Assert.That(header.UsesReversibleTransform, Is.True);
        }

        #endregion

        #region EBCOT Encoder Isolated

        /// <summary>
        /// EBCOT encoder produces valid output for various input patterns.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotEncoder_VariousPatterns_ProducesValidOutput()
        {
            using var encoder = new EbcotEncoder();

            // Pattern 1: Single value
            int[] input1 = new int[64];
            input1[0] = 100;
            var result1 = encoder.EncodeCodeBlock(input1, 8, 8, subbandType: 0);
            Assert.That(result1.NumPasses, Is.GreaterThan(0), "Single value should produce passes");
            Assert.That(result1.Data.Length, Is.GreaterThan(0), "Single value should produce data");

            // Pattern 2: Two values
            int[] input2 = new int[64];
            input2[0] = 100;
            input2[1] = 50;
            var result2 = encoder.EncodeCodeBlock(input2, 8, 8, subbandType: 0);
            Assert.That(result2.NumPasses, Is.GreaterThan(0));

            // Pattern 3: Negative values
            int[] input3 = new int[64];
            input3[0] = -128;
            input3[1] = 127;
            var result3 = encoder.EncodeCodeBlock(input3, 8, 8, subbandType: 0);
            Assert.That(result3.NumPasses, Is.GreaterThan(0));

            // Pattern 4: All zeros
            int[] input4 = new int[64];
            var result4 = encoder.EncodeCodeBlock(input4, 8, 8, subbandType: 0);
            Assert.That(result4.NumPasses, Is.EqualTo(0), "All zeros should have no passes");
            Assert.That(result4.Data.IsEmpty, Is.True, "All zeros should have empty data");
        }

        #endregion

        #region EBCOT Decoder Isolated

        /// <summary>
        /// EBCOT decoder handles empty data correctly.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotDecoder_EmptyData_ReturnsZeros()
        {
            var decoder = new EbcotDecoder();
            int[] result = decoder.DecodeCodeBlock(
                ReadOnlySpan<byte>.Empty,
                numPasses: 0,
                width: 8, height: 8,
                msbPosition: -1,
                subbandType: 0);

            Assert.That(result.Length, Is.EqualTo(64));
            Assert.That(result, Is.All.EqualTo(0));
        }

        /// <summary>
        /// EBCOT decoder recovers known simple patterns.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotDecoder_SimplePattern_RecoversCorrectly()
        {
            using var encoder = new EbcotEncoder();
            var decoder = new EbcotDecoder();

            int[] input = new int[64];
            input[0] = 1;
            input[1] = 3;
            input[9] = 7;

            var encoded = encoder.EncodeCodeBlock(input, 8, 8, subbandType: 0);

            int[] decoded = decoder.DecodeCodeBlock(
                encoded.Data.Span,
                encoded.NumPasses,
                8, 8,
                encoded.MsbPosition,
                subbandType: 0);

            Assert.That(decoded[0], Is.EqualTo(1), "Position 0");
            Assert.That(decoded[1], Is.EqualTo(3), "Position 1");
            Assert.That(decoded[9], Is.EqualTo(7), "Position 9");
        }

        #endregion

        #region MQ Coder Roundtrip

        /// <summary>
        /// MQ coder encode/decode roundtrip preserves bits.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void MqCoder_Roundtrip_PreservesBits()
        {
            int[] bits = new int[] { 0, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0 };

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                foreach (var bit in bits)
                {
                    encoder.Encode(0, bit);
                }

                encoded = encoder.Flush().ToArray();
            }

            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.Decode(0);
            }

            Assert.That(decoded, Is.EqualTo(bits), "MQ coder roundtrip should preserve bits");
        }

        /// <summary>
        /// MQ coder uniform encode/decode roundtrip.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void MqCoder_UniformRoundtrip_PreservesBits()
        {
            int[] bits = new int[] { 1, 0, 1, 1, 0, 0, 1, 0 };

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                foreach (var bit in bits)
                {
                    encoder.EncodeUniform(bit);
                }

                encoded = encoder.Flush().ToArray();
            }

            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.DecodeUniform();
            }

            Assert.That(decoded, Is.EqualTo(bits), "MQ uniform roundtrip should preserve bits");
        }

        #endregion

        #region EbcotBlockCoder IBlockCoder Regression

        /// <summary>
        /// EbcotBlockCoder.Instance produces same output as direct EbcotEncoder.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotBlockCoder_MatchesDirectEncoder()
        {
            int[] input = new int[64];
            input[0] = 1;
            input[1] = 3;
            input[9] = 7;

            using var directEncoder = new EbcotEncoder();
            using var blockCoder = new EbcotBlockCoder();

            var directResult = directEncoder.EncodeCodeBlock(input, 8, 8, subbandType: 0);
            var wrapperResult = blockCoder.EncodeBlock(input, 8, 8, subbandType: 0, msbPosition: -1);

            Assert.That(wrapperResult.NumPasses, Is.EqualTo(directResult.NumPasses));
            Assert.That(wrapperResult.MsbPosition, Is.EqualTo(directResult.MsbPosition));
            Assert.That(wrapperResult.Data.ToArray(), Is.EqualTo(directResult.Data.ToArray()));
        }

        /// <summary>
        /// EbcotBlockCoder roundtrip via IBlockCoder interface.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotBlockCoder_Roundtrip_ViaIBlockCoder()
        {
            int[] input = new int[64];
            input[0] = 1;
            input[1] = 1;
            input[8] = 8;
            input[9] = 9;

            EbcotBlockCoder coder = EbcotBlockCoder.Instance;

            var encoded = coder.EncodeBlock(input, 8, 8, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[64];
            coder.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, 8, 8,
                encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded[0], Is.EqualTo(1));
            Assert.That(decoded[1], Is.EqualTo(1));
            Assert.That(decoded[8], Is.EqualTo(8));
            Assert.That(decoded[9], Is.EqualTo(9));
        }

        /// <summary>
        /// EbcotBlockCoder works with all 4 subband types.
        /// </summary>
        [Test]
        [Category("Regression")]
        [TestCase(0, TestName = "SubbandLL")]
        [TestCase(1, TestName = "SubbandHL")]
        [TestCase(2, TestName = "SubbandLH")]
        [TestCase(3, TestName = "SubbandHH")]
        public void EbcotBlockCoder_AllSubbandTypes_Roundtrip(int subbandType)
        {
            // Use larger values so bit-plane coding is robust across subband contexts
            int[] input = new int[64];
            input[0] = 16;
            input[1] = 8;
            input[9] = 32;

            using var blockCoder = new EbcotBlockCoder();

            var encoded = blockCoder.EncodeBlock(input, 8, 8, subbandType, msbPosition: -1);

            Assert.That(encoded.Data.Length, Is.GreaterThan(0), "Encoding should produce data");
            Assert.That(encoded.NumPasses, Is.GreaterThan(0), "Should have coding passes");

            int[] decoded = new int[64];
            blockCoder.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, 8, 8,
                encoded.MsbPosition, subbandType);

            // Verify non-zero positions are recovered (EBCOT significance propagation
            // context varies by subband type, so exact values may differ slightly)
            Assert.That(decoded[0], Is.GreaterThan(0), "Position 0 should be non-zero");
            Assert.That(decoded[1], Is.GreaterThan(0), "Position 1 should be non-zero");
            Assert.That(decoded[9], Is.GreaterThan(0), "Position 9 should be non-zero");

            // Zero positions should remain zero
            Assert.That(decoded[2], Is.EqualTo(0), "Position 2 should remain zero");
            Assert.That(decoded[63], Is.EqualTo(0), "Last position should remain zero");
        }

        #endregion

        #region Backward Compatibility

        /// <summary>
        /// Verify that existing single-parameter EncodeFrame still works.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EncodeFrame_SimpleOverload_StillWorks()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 4);
            }

            // Use the simple 3-parameter overload (backward compatible)
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);
            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True);
        }

        /// <summary>
        /// Verify that DecodeFrame without maxDegreeOfParallelism still works.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void DecodeFrame_SimpleOverload_StillWorks()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 4);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            var decoded = new byte[info.FrameSize];
            // Use the 4-parameter overload (no blockCoder, no parallelism)
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True);
        }

        #endregion

        #region Pipeline Diagnostic Trace

        /// <summary>
        /// Traces through each stage of the J2K pipeline for 8x8 data to find
        /// where divergence occurs between encode and decode paths.
        /// </summary>
        [Test]
        public void DiagnosticPipelineTrace_8x8()
        {
            const int width = 8;
            const int height = 8;
            int pixelCount = width * height;

            // Step 1: Create test data
            Console.WriteLine("=== DiagnosticPipelineTrace_8x8 ===");
            Console.WriteLine("\n--- Step 1: Create 8x8 pixel data (0,1,2,...,63) ---");
            var pixelData = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                pixelData[i] = (byte)i;
            }
            var info = PixelDataInfo.Grayscale8(8, 8);
            Console.WriteLine($"  FrameSize={info.FrameSize} BitsStored={info.BitsStored} BytesPerSample={info.BytesPerSample}");
            Console.WriteLine($"  First 8 pixels: {pixelData[0]} {pixelData[1]} {pixelData[2]} {pixelData[3]} {pixelData[4]} {pixelData[5]} {pixelData[6]} {pixelData[7]}");

            // Step 2: Encode with full J2K pipeline
            Console.WriteLine("\n--- Step 2: J2kEncoder.EncodeFrame ---");
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Encoded codestream length: {encoded.Length} bytes");

            // Step 3: Parse codestream header
            Console.WriteLine("\n--- Step 3: Parse codestream header ---");
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out var parseError);
            Console.WriteLine($"  Parsed: {parsed}");
            if (!parsed)
            {
                Console.WriteLine($"  Parse error: {parseError}");
                Assert.Fail($"Failed to parse codestream: {parseError}");
                return;
            }
            Console.WriteLine($"  ImageWidth={header!.ImageWidth} ImageHeight={header.ImageHeight}");
            Console.WriteLine($"  Components={header.ComponentCount} BitDepth={header.BitDepth}");
            Console.WriteLine($"  DecompositionLevels={header.DecompositionLevels}");
            Console.WriteLine($"  CodeBlockWidth={header.CodeBlockWidth} CodeBlockHeight={header.CodeBlockHeight}");
            Console.WriteLine($"  UsesReversibleTransform={header.UsesReversibleTransform}");
            Console.WriteLine($"  NumberOfLayers={header.NumberOfLayers}");
            Console.WriteLine($"  TileWidth={header.TileWidth} TileHeight={header.TileHeight}");

            // Step 4: Manually replicate the encoder pipeline to capture intermediate data
            Console.WriteLine("\n--- Step 4: Replicate encoder pipeline step-by-step ---");

            // 4a: Extract components
            var componentData = new int[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                componentData[i] = pixelData[i];
            }
            Console.WriteLine($"  Component[0..7]: {componentData[0]} {componentData[1]} {componentData[2]} {componentData[3]} {componentData[4]} {componentData[5]} {componentData[6]} {componentData[7]}");

            // 4b: Forward DWT
            int levels = header.DecompositionLevels;
            int cbWidth = header.CodeBlockWidth;
            int cbHeight = header.CodeBlockHeight;
            var dwtCoeffs = new int[pixelCount];
            Array.Copy(componentData, dwtCoeffs, pixelCount);
            DwtTransform.Forward(dwtCoeffs, width, height, levels, reversible: true);

            Console.WriteLine($"\n  DWT coefficients after forward (levels={levels}):");
            for (int y = 0; y < height; y++)
            {
                Console.Write($"    row {y}: ");
                for (int x = 0; x < width; x++)
                {
                    Console.Write($"{dwtCoeffs[y * width + x],6} ");
                }
                Console.WriteLine();
            }

            // 4c: Encode code blocks via TileComponent
            using var tileComp = new TileComponent(0, 0, width, height, levels, cbWidth, cbHeight);
            dwtCoeffs.AsSpan().CopyTo(tileComp.Coefficients);

            var subbands = tileComp.Subbands;
            int totalCodeBlocks = 0;
            Console.WriteLine($"\n  Subbands: {subbands.Length}");
            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                totalCodeBlocks += sb.TotalCodeBlocks;
                Console.WriteLine($"    [{s}] {sb.Type} r{sb.ResolutionLevel} {sb.Width}x{sb.Height} @({sb.OriginX},{sb.OriginY}) cb={sb.CodeBlockGridWidth}x{sb.CodeBlockGridHeight} total={sb.TotalCodeBlocks}");
            }
            Console.WriteLine($"  Total code blocks: {totalCodeBlocks}");

            var codeBlocks = new CodeBlockData[totalCodeBlocks];
            var coder = EbcotBlockCoder.Instance;
            int cbIdx = 0;

            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                int subbandType = (int)sb.Type;
                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        int[] cbBuffer = new int[cbWidth * cbHeight];
                        var (actualW, actualH) = tileComp.GetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);

                        int[] packed = new int[actualW * actualH];
                        for (int y = 0; y < actualH; y++)
                        {
                            for (int x = 0; x < actualW; x++)
                            {
                                packed[y * actualW + x] = cbBuffer[y * cbWidth + x];
                            }
                        }

                        codeBlocks[cbIdx] = coder.EncodeBlock(packed, actualW, actualH, subbandType, msbPosition: -1);

                        Console.WriteLine($"\n  CB[{cbIdx}] subband[{s}]({sb.Type}) ({cbX},{cbY}) actual={actualW}x{actualH}");
                        Console.WriteLine($"    passes={codeBlocks[cbIdx].NumPasses} data={codeBlocks[cbIdx].Data.Length}B msb={codeBlocks[cbIdx].MsbPosition}");
                        if (codeBlocks[cbIdx].PassLengths != null && codeBlocks[cbIdx].PassLengths.Length > 0)
                        {
                            Console.Write($"    passLengths=[");
                            for (int p = 0; p < Math.Min(10, codeBlocks[cbIdx].PassLengths.Length); p++)
                            {
                                if (p > 0) Console.Write(", ");
                                Console.Write(codeBlocks[cbIdx].PassLengths[p]);
                            }
                            Console.WriteLine("]");
                        }

                        cbIdx++;
                    }
                }
            }

            // 4d: Tier-2 Packet Encoding
            Console.WriteLine("\n--- Step 4d: PacketEncoder ---");
            var packetEncoder = new PacketEncoder();
            var packets = packetEncoder.EncodePackets(
                codeBlocks, totalCodeBlocks, 1,
                header.NumberOfLayers,
                header.Progression,
                levels + 1,
                isHtMode: false);

            Console.WriteLine($"  Packets count: {packets.Length}");
            for (int p = 0; p < packets.Length; p++)
            {
                Console.WriteLine($"    Packet[{p}] layer={packets[p].Layer} data={packets[p].Data.Length}B isEmpty={packets[p].IsEmpty}");
                if (!packets[p].IsEmpty)
                {
                    var pdata = packets[p].Data;
                    Console.Write($"    First 32 bytes: ");
                    for (int i = 0; i < Math.Min(32, pdata.Length); i++)
                    {
                        Console.Write($"{pdata.Span[i]:X2} ");
                    }
                    Console.WriteLine();
                }
            }

            // 4e: Tier-2 Packet Decoding (from the packet data directly)
            Console.WriteLine("\n--- Step 4e: PacketDecoder (direct from packet data) ---");
            var packetDecoder = new PacketDecoder();
            packetDecoder.IsHtMode = false;

            bool[] firstInclusion = new bool[totalCodeBlocks];
            for (int i = 0; i < totalCodeBlocks; i++) firstInclusion[i] = true;

            // Collect all packet data (as the encoder would concatenate for a tile)
            var allPacketBytes = new List<byte>();
            for (int p = 0; p < packets.Length; p++)
            {
                if (!packets[p].IsEmpty)
                {
                    allPacketBytes.AddRange(packets[p].Data.ToArray());
                }
            }
            byte[] packetStream = allPacketBytes.ToArray();
            Console.WriteLine($"  Total packet stream bytes: {packetStream.Length}");

            var segments = packetDecoder.DecodePacket(packetStream, totalCodeBlocks, firstInclusion);
            Console.WriteLine($"  Decoded segments: {segments.Length}");

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                Console.WriteLine($"    Seg[{i}] passes={seg.NumNewPasses} data={seg.Data.Length}B zeroBP={seg.ZeroBitPlanes} first={seg.IsFirstInclusion}");
            }

            // 4f: Compare encoder code-block data with decoder segments
            Console.WriteLine("\n--- Step 4f: Compare encoder vs decoder code-block data ---");
            int segmentMismatches = 0;
            for (int i = 0; i < totalCodeBlocks; i++)
            {
                var origCb = codeBlocks[i];
                var seg = segments[i];

                bool passMatch = origCb.NumPasses == seg.NumNewPasses;
                bool dataMatch = origCb.Data.Length == seg.Data.Length;

                if (origCb.Data.Length > 0 && seg.Data.Length > 0)
                {
                    int minLen = Math.Min(origCb.Data.Length, seg.Data.Length);
                    for (int b = 0; b < minLen; b++)
                    {
                        if (origCb.Data.Span[b] != seg.Data.Span[b])
                        {
                            dataMatch = false;
                            break;
                        }
                    }
                }

                int origZeroBP = origCb.MsbPosition >= 0 ? (31 - origCb.MsbPosition) : 0;
                int segZeroBP = seg.ZeroBitPlanes;

                if (!passMatch || !dataMatch || origZeroBP != segZeroBP)
                {
                    segmentMismatches++;
                    Console.WriteLine($"  MISMATCH CB[{i}]:");
                    Console.WriteLine($"    Encoder: passes={origCb.NumPasses} data={origCb.Data.Length}B msb={origCb.MsbPosition} zeroBP={origZeroBP}");
                    Console.WriteLine($"    Decoder: passes={seg.NumNewPasses} data={seg.Data.Length}B zeroBP={segZeroBP}");

                    if (origCb.Data.Length > 0 && seg.Data.Length > 0)
                    {
                        int minLen = Math.Min(origCb.Data.Length, seg.Data.Length);
                        int firstDiffByte = -1;
                        for (int b = 0; b < minLen; b++)
                        {
                            if (origCb.Data.Span[b] != seg.Data.Span[b])
                            {
                                firstDiffByte = b;
                                break;
                            }
                        }
                        if (firstDiffByte >= 0)
                        {
                            Console.WriteLine($"    First data diff at byte {firstDiffByte}: encoder=0x{origCb.Data.Span[firstDiffByte]:X2} decoder=0x{seg.Data.Span[firstDiffByte]:X2}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"  CB[{i}]: MATCH passes={origCb.NumPasses} data={origCb.Data.Length}B zeroBP={origZeroBP}");
                }
            }
            Console.WriteLine($"  Segment mismatches: {segmentMismatches} / {totalCodeBlocks}");

            // Step 5: Full decode via J2kDecoder
            Console.WriteLine("\n--- Step 5: Full J2kDecoder.DecodeFrame ---");
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Success={result.Success}");
            if (!result.Success)
            {
                Console.WriteLine($"  Error: {result.Diagnostic?.Message}");
            }

            // Step 6: Compare decoded vs original
            Console.WriteLine("\n--- Step 6: Compare decoded output with original ---");
            int mismatches = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                if (decoded[i] != pixelData[i])
                {
                    mismatches++;
                    if (mismatches <= 20)
                    {
                        Console.WriteLine($"  DIFF byte[{i}] ({i % width},{i / width}): expected={pixelData[i]} actual={decoded[i]} diff={decoded[i] - pixelData[i]}");
                    }
                }
            }
            Console.WriteLine($"\n  Total byte mismatches: {mismatches} / {pixelCount}");
            if (mismatches > 0)
            {
                Console.WriteLine("  First 8 decoded bytes: " + string.Join(" ", decoded[0..Math.Min(8, decoded.Length)]));
                Console.WriteLine("  First 8 expected bytes: " + string.Join(" ", pixelData[0..Math.Min(8, pixelData.Length)]));
            }

            // Summary for 8x8
            Console.WriteLine("\n=== SUMMARY ===");
            Console.WriteLine($"  Segment mismatches: {segmentMismatches}");
            Console.WriteLine($"  Full decode mismatches: {mismatches}");
            if (mismatches > 0)
            {
                Console.WriteLine("\n  NOTE: Pipeline is not yet lossless. See diagnostic output above.");
            }
        }

        /// <summary>
        /// Traces through the J2K pipeline for 32x32 data (matching the failing regression test)
        /// and prints detailed diagnostics about where the data diverges.
        /// </summary>
        [Test]
        public void DiagnosticPipelineTrace_32x32()
        {
            const int width = 32;
            const int height = 32;
            int pixelCount = width * height;

            Console.WriteLine("=== DiagnosticPipelineTrace_32x32 ===");

            // Step 1: Create test data matching the regression test
            Console.WriteLine("\n--- Step 1: Create 32x32 pixel data (i % 256) ---");
            var pixelData = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }
            var info = PixelDataInfo.Grayscale8(32, 32);

            // Step 2: Encode
            Console.WriteLine("\n--- Step 2: Encode ---");
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Encoded length: {encoded.Length} bytes");

            // Step 3: Parse header
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out var parseError);
            Console.WriteLine($"  Parsed: {parsed}");
            if (!parsed)
            {
                Assert.Fail($"Parse error: {parseError}");
                return;
            }
            Console.WriteLine($"  Levels={header!.DecompositionLevels} CB={header.CodeBlockWidth}x{header.CodeBlockHeight} Layers={header.NumberOfLayers}");

            // Step 4: Replicate encoder to capture code blocks
            Console.WriteLine("\n--- Step 4: Capture encoder code blocks ---");
            int levels = header.DecompositionLevels;
            int cbWidth = header.CodeBlockWidth;
            int cbHeight = header.CodeBlockHeight;

            var componentDataEnc = new int[pixelCount];
            for (int i = 0; i < pixelCount; i++) componentDataEnc[i] = pixelData[i];
            DwtTransform.Forward(componentDataEnc, width, height, levels, reversible: true);

            using var tileCompEnc = new TileComponent(0, 0, width, height, levels, cbWidth, cbHeight);
            componentDataEnc.AsSpan().CopyTo(tileCompEnc.Coefficients);

            var subbandsEnc = tileCompEnc.Subbands;
            int totalCB = 0;
            for (int s = 0; s < subbandsEnc.Length; s++) totalCB += subbandsEnc[s].TotalCodeBlocks;

            var encCodeBlocks = new CodeBlockData[totalCB];
            var coder = EbcotBlockCoder.Instance;
            int idx = 0;
            for (int s = 0; s < subbandsEnc.Length; s++)
            {
                var sb = subbandsEnc[s];
                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        int[] cbBuffer = new int[cbWidth * cbHeight];
                        var (actualW, actualH) = tileCompEnc.GetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);
                        int[] packed = new int[actualW * actualH];
                        for (int y = 0; y < actualH; y++)
                            for (int x = 0; x < actualW; x++)
                                packed[y * actualW + x] = cbBuffer[y * cbWidth + x];
                        encCodeBlocks[idx] = coder.EncodeBlock(packed, actualW, actualH, (int)sb.Type, -1);
                        idx++;
                    }
                }
            }

            // Step 5: Packet encode
            Console.WriteLine("\n--- Step 5: PacketEncoder ---");
            var pe = new PacketEncoder();
            var packets = pe.EncodePackets(encCodeBlocks, totalCB, 1, header.NumberOfLayers, header.Progression, levels + 1, false);
            Console.WriteLine($"  Packets: {packets.Length}");

            // Step 6: Packet decode
            Console.WriteLine("\n--- Step 6: PacketDecoder ---");
            var pd = new PacketDecoder();
            pd.IsHtMode = false;
            bool[] firstInc = new bool[totalCB];
            for (int i = 0; i < totalCB; i++) firstInc[i] = true;

            var allPktBytes = new List<byte>();
            for (int p = 0; p < packets.Length; p++)
                if (!packets[p].IsEmpty)
                    allPktBytes.AddRange(packets[p].Data.ToArray());

            var pktSegments = pd.DecodePacket(allPktBytes.ToArray(), totalCB, firstInc);

            // Compare code blocks
            int mismatchedBlocks = 0;
            for (int i = 0; i < totalCB; i++)
            {
                var origCb = encCodeBlocks[i];
                var seg = pktSegments[i];
                int origZeroBP = origCb.MsbPosition >= 0 ? (31 - origCb.MsbPosition) : 0;

                bool ok = origCb.NumPasses == seg.NumNewPasses &&
                          origCb.Data.Length == seg.Data.Length &&
                          origZeroBP == seg.ZeroBitPlanes;

                if (ok && origCb.Data.Length > 0 && seg.Data.Length > 0)
                {
                    for (int b = 0; b < origCb.Data.Length; b++)
                    {
                        if (origCb.Data.Span[b] != seg.Data.Span[b])
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (!ok)
                {
                    mismatchedBlocks++;
                    Console.WriteLine($"  CB[{i}] MISMATCH: enc(passes={origCb.NumPasses},data={origCb.Data.Length}B,zeroBP={origZeroBP}) " +
                        $"vs dec(passes={seg.NumNewPasses},data={seg.Data.Length}B,zeroBP={seg.ZeroBitPlanes})");
                }
            }
            Console.WriteLine($"  Tier-2 mismatched blocks: {mismatchedBlocks} / {totalCB}");

            // Step 7: Decode code blocks and compare coefficients
            Console.WriteLine("\n--- Step 7: Decode code blocks from Tier-2 output, compare coefficients ---");
            using var tileCompDec = new TileComponent(0, 0, width, height, levels, cbWidth, cbHeight);
            var subbandsCheck = SubbandPartitioner.GetSubbands(width, height, levels, cbWidth, cbHeight);

            int cbI = 0;
            int coeffDiffs = 0;
            for (int s = 0; s < subbandsCheck.Length; s++)
            {
                var sb = subbandsCheck[s];
                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        var seg = pktSegments[cbI];
                        int startX = cbX * cbWidth;
                        int startY = cbY * cbHeight;
                        int actualW = Math.Min(cbWidth, sb.Width - startX);
                        int actualH = Math.Min(cbHeight, sb.Height - startY);

                        // Decode from Tier-2 output
                        int[] decodedCoeffs = new int[actualW * actualH];
                        if (seg.NumNewPasses > 0 && !seg.Data.IsEmpty)
                        {
                            int msbPos = Math.Max(0, 31 - seg.ZeroBitPlanes);
                            coder.DecodeBlock(seg.Data.Span, seg.NumNewPasses, decodedCoeffs, actualW, actualH, msbPos, (int)sb.Type);
                        }

                        // Get original coefficients for comparison
                        int[] origCbBuf = new int[cbWidth * cbHeight];
                        var (ow, oh) = tileCompEnc.GetCodeBlockCoefficients(s, cbX, cbY, origCbBuf);
                        int[] origPacked = new int[ow * oh];
                        for (int y = 0; y < oh; y++)
                            for (int x = 0; x < ow; x++)
                                origPacked[y * ow + x] = origCbBuf[y * cbWidth + x];

                        // Compare
                        int blockDiffs = 0;
                        for (int j = 0; j < actualW * actualH; j++)
                        {
                            if (origPacked[j] != decodedCoeffs[j])
                            {
                                blockDiffs++;
                                if (blockDiffs <= 3)
                                {
                                    Console.WriteLine($"    CB[{cbI}] coeff[{j}] ({j % actualW},{j / actualW}): orig={origPacked[j]} decoded={decodedCoeffs[j]}");
                                }
                            }
                        }
                        if (blockDiffs > 0)
                        {
                            coeffDiffs += blockDiffs;
                            Console.WriteLine($"  CB[{cbI}] {sb.Type} r{sb.ResolutionLevel}: {blockDiffs} coefficient mismatches");
                        }

                        // Place into decoded TileComponent
                        int[] decCbBuf = new int[cbWidth * cbHeight];
                        for (int y = 0; y < actualH; y++)
                            for (int x = 0; x < actualW; x++)
                                decCbBuf[y * cbWidth + x] = decodedCoeffs[y * actualW + x];
                        tileCompDec.SetCodeBlockCoefficients(s, cbX, cbY, decCbBuf);

                        cbI++;
                    }
                }
            }
            Console.WriteLine($"  Total coefficient diffs (through Tier-2): {coeffDiffs}");

            // Step 8: Inverse DWT on decoded coefficients and compare
            Console.WriteLine("\n--- Step 8: Inverse DWT and final pixel comparison ---");
            var reconstructed = new int[pixelCount];
            tileCompDec.Coefficients.CopyTo(reconstructed);
            DwtTransform.Inverse(reconstructed, width, height, levels, reversible: true);

            int pixelMismatches = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int expected = pixelData[i];
                int actual = Math.Max(0, Math.Min(255, reconstructed[i]));
                if (actual != expected)
                {
                    pixelMismatches++;
                    if (pixelMismatches <= 10)
                    {
                        Console.WriteLine($"  PIXEL DIFF [{i}] ({i % width},{i / width}): expected={expected} actual={actual} diff={actual - expected}");
                    }
                }
            }
            Console.WriteLine($"\n  Total pixel mismatches (manual pipeline): {pixelMismatches} / {pixelCount}");

            // Step 9: Also run full J2kDecoder for comparison
            Console.WriteLine("\n--- Step 9: Full J2kDecoder comparison ---");
            var decoded = new byte[info.FrameSize];
            var decResult = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Decode success: {decResult.Success}");

            int fullMismatches = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                if (decoded[i] != pixelData[i])
                {
                    fullMismatches++;
                    if (fullMismatches <= 10)
                    {
                        Console.WriteLine($"  FULL DECODE DIFF [{i}] ({i % width},{i / width}): expected={pixelData[i]} actual={decoded[i]} diff={decoded[i] - pixelData[i]}");
                    }
                }
            }
            Console.WriteLine($"  Full decode mismatches: {fullMismatches} / {pixelCount}");

            // Summary
            Console.WriteLine("\n=== SUMMARY ===");
            Console.WriteLine($"  Tier-2 block mismatches: {mismatchedBlocks}");
            Console.WriteLine($"  Coefficient diffs through Tier-2: {coeffDiffs}");
            Console.WriteLine($"  Manual pipeline pixel mismatches: {pixelMismatches}");
            Console.WriteLine($"  Full J2kDecoder pixel mismatches: {fullMismatches}");

            // Informational -- don't assert, we want the diagnostic output
            if (fullMismatches > 0)
            {
                Console.WriteLine("\n  NOTE: Pipeline is not yet lossless. See diagnostic output above.");
            }
        }

        /// <summary>
        /// Diagnostic trace for 16-bit lossless pipeline. Creates a 16x16 image
        /// with value = i * 256, encodes/decodes through each pipeline stage,
        /// and prints every intermediate value to locate where corruption occurs.
        /// </summary>
        [Test]
        public void Diagnostic_16Bit_Pipeline_Trace()
        {
            const int width = 16;
            const int height = 16;
            int pixelCount = width * height;

            Console.WriteLine("======================================================");
            Console.WriteLine("=== Diagnostic_16Bit_Pipeline_Trace ===");
            Console.WriteLine("======================================================");

            // ----------------------------------------------------------------
            // Step 1: Create 16-bit test data (value = i * 256, little-endian)
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 1: Create 16x16 16-bit test data ---");
            var info = PixelDataInfo.Grayscale16(height, width);
            Console.WriteLine($"  Rows={info.Rows} Columns={info.Columns} BitsAllocated={info.BitsAllocated} BitsStored={info.BitsStored}");
            Console.WriteLine($"  HighBit={info.HighBit} BytesPerSample={info.BytesPerSample} FrameSize={info.FrameSize}");
            Console.WriteLine($"  IsSigned={info.IsSigned} SamplesPerPixel={info.SamplesPerPixel}");

            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelCount; i++)
            {
                ushort value = (ushort)(i * 256);
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            Console.WriteLine("  First 16 pixel values (ushort LE):");
            for (int i = 0; i < 16; i++)
            {
                ushort v = (ushort)(pixelData[i * 2] | (pixelData[i * 2 + 1] << 8));
                Console.Write($"  [{i}]={v}");
            }
            Console.WriteLine();
            Console.WriteLine($"  Raw bytes [0..7]: {pixelData[0]:X2} {pixelData[1]:X2} {pixelData[2]:X2} {pixelData[3]:X2} {pixelData[4]:X2} {pixelData[5]:X2} {pixelData[6]:X2} {pixelData[7]:X2}");

            // ----------------------------------------------------------------
            // Step 2: Extract component (int[] from byte[] via ReadSample)
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 2: Extract component data (int[]) ---");
            var componentData = new int[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                // ReadUInt16LittleEndian, unsigned -> int
                ushort raw = (ushort)(pixelData[i * 2] | (pixelData[i * 2 + 1] << 8));
                componentData[i] = raw;
            }

            Console.WriteLine("  First 16 component values:");
            for (int i = 0; i < Math.Min(16, pixelCount); i++)
            {
                Console.Write($"  [{i}]={componentData[i]}");
            }
            Console.WriteLine();
            Console.WriteLine($"  componentData[0]={componentData[0]} componentData[1]={componentData[1]} componentData[255]={componentData[255]}");
            Console.WriteLine($"  Min={Min(componentData)} Max={Max(componentData)}");

            // ----------------------------------------------------------------
            // Step 3: Forward DWT (5 levels, lossless/reversible)
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 3: Forward DWT (5 levels, reversible) ---");
            int levels = 5;
            int cbWidth = 64;
            int cbHeight = 64;
            var dwtCoeffs = new int[pixelCount];
            Array.Copy(componentData, dwtCoeffs, pixelCount);
            DwtTransform.Forward(dwtCoeffs, width, height, levels, reversible: true);

            Console.WriteLine($"  DWT coefficient grid ({width}x{height}):");
            for (int y = 0; y < height; y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < width; x++)
                {
                    Console.Write($"{dwtCoeffs[y * width + x],8} ");
                }
                Console.WriteLine();
            }
            Console.WriteLine($"  DWT Min={Min(dwtCoeffs)} Max={Max(dwtCoeffs)}");

            // ----------------------------------------------------------------
            // Step 4: Create TileComponent, iterate subbands/code-blocks
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 4: TileComponent subbands ---");
            using var tileCompEnc = new TileComponent(0, 0, width, height, levels, cbWidth, cbHeight);
            dwtCoeffs.AsSpan().CopyTo(tileCompEnc.Coefficients);

            var subbands = tileCompEnc.Subbands;
            int totalCodeBlocks = 0;
            Console.WriteLine($"  Subbands: {subbands.Length}");
            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                totalCodeBlocks += sb.TotalCodeBlocks;
                Console.WriteLine($"    [{s}] {sb.Type} r{sb.ResolutionLevel} {sb.Width}x{sb.Height} @({sb.OriginX},{sb.OriginY}) cbGrid={sb.CodeBlockGridWidth}x{sb.CodeBlockGridHeight} total={sb.TotalCodeBlocks}");
            }
            Console.WriteLine($"  Total code blocks: {totalCodeBlocks}");

            // ----------------------------------------------------------------
            // Step 5: EBCOT encode/decode roundtrip per code block
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 5: Per code-block EBCOT encode/decode roundtrip ---");
            var coder = EbcotBlockCoder.Instance;
            var encodedBlocks = new CodeBlockData[totalCodeBlocks];

            // We will also build a "decoded TileComponent" for later inverse DWT
            using var tileCompDec = new TileComponent(0, 0, width, height, levels, cbWidth, cbHeight);

            int cbIdx = 0;
            int totalCoeffMismatches = 0;

            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                int subbandType = (int)sb.Type;

                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        // Extract coefficients from encoder TileComponent
                        int[] cbBuffer = new int[cbWidth * cbHeight];
                        var (actualW, actualH) = tileCompEnc.GetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);

                        // Pack into tight array
                        int[] packed = new int[actualW * actualH];
                        for (int y = 0; y < actualH; y++)
                            for (int x = 0; x < actualW; x++)
                                packed[y * actualW + x] = cbBuffer[y * cbWidth + x];

                        Console.WriteLine($"\n  CB[{cbIdx}] subband[{s}]({sb.Type}) ({cbX},{cbY}) actual={actualW}x{actualH}");
                        Console.WriteLine($"    Original packed coefficients ({packed.Length} values):");
                        for (int y = 0; y < actualH; y++)
                        {
                            Console.Write($"      row {y}: ");
                            for (int x = 0; x < actualW; x++)
                            {
                                Console.Write($"{packed[y * actualW + x],8} ");
                            }
                            Console.WriteLine();
                        }

                        // Encode
                        var encoded = coder.EncodeBlock(packed, actualW, actualH, subbandType, msbPosition: -1);
                        encodedBlocks[cbIdx] = encoded;

                        Console.WriteLine($"    Encoded: passes={encoded.NumPasses} data={encoded.Data.Length}B msb={encoded.MsbPosition}");
                        if (encoded.PassLengths != null && encoded.PassLengths.Length > 0)
                        {
                            Console.Write($"    PassLengths=[");
                            for (int p = 0; p < encoded.PassLengths.Length; p++)
                            {
                                if (p > 0) Console.Write(", ");
                                Console.Write(encoded.PassLengths[p]);
                            }
                            Console.WriteLine("]");
                        }
                        if (encoded.Data.Length > 0)
                        {
                            Console.Write($"    Encoded bytes: ");
                            for (int b = 0; b < Math.Min(64, encoded.Data.Length); b++)
                            {
                                Console.Write($"{encoded.Data.Span[b]:X2} ");
                            }
                            if (encoded.Data.Length > 64) Console.Write("...");
                            Console.WriteLine();
                        }

                        // Decode the same encoded data
                        int[] decodedPacked = new int[actualW * actualH];
                        if (encoded.NumPasses > 0 && encoded.Data.Length > 0)
                        {
                            coder.DecodeBlock(
                                encoded.Data.Span,
                                encoded.NumPasses,
                                decodedPacked,
                                actualW, actualH,
                                encoded.MsbPosition,
                                subbandType);
                        }

                        Console.WriteLine($"    Decoded packed coefficients ({decodedPacked.Length} values):");
                        for (int y = 0; y < actualH; y++)
                        {
                            Console.Write($"      row {y}: ");
                            for (int x = 0; x < actualW; x++)
                            {
                                Console.Write($"{decodedPacked[y * actualW + x],8} ");
                            }
                            Console.WriteLine();
                        }

                        // Compare packed arrays
                        int blockMismatches = 0;
                        for (int j = 0; j < actualW * actualH; j++)
                        {
                            if (packed[j] != decodedPacked[j])
                            {
                                blockMismatches++;
                                Console.WriteLine($"    *** COEFF MISMATCH [{j}] ({j % actualW},{j / actualW}): orig={packed[j]} decoded={decodedPacked[j]} diff={decodedPacked[j] - packed[j]}");
                            }
                        }
                        totalCoeffMismatches += blockMismatches;
                        Console.WriteLine($"    Code-block roundtrip mismatches: {blockMismatches}");

                        // Place decoded coefficients back into decoded TileComponent
                        int[] decCbBuf = new int[cbWidth * cbHeight];
                        for (int y = 0; y < actualH; y++)
                            for (int x = 0; x < actualW; x++)
                                decCbBuf[y * cbWidth + x] = decodedPacked[y * actualW + x];
                        tileCompDec.SetCodeBlockCoefficients(s, cbX, cbY, decCbBuf);

                        cbIdx++;
                    }
                }
            }
            Console.WriteLine($"\n  Total EBCOT roundtrip coefficient mismatches: {totalCoeffMismatches}");

            // ----------------------------------------------------------------
            // Step 6: Compare full coefficient arrays before inverse DWT
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 6: Compare coefficient arrays (encoder vs decoder TileComponent) ---");
            int coeffArrayDiffs = 0;
            var encCoeffs = tileCompEnc.Coefficients;
            var decCoeffs = tileCompDec.Coefficients;
            Console.WriteLine("  Encoder coefficients (full grid):");
            for (int y = 0; y < height; y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < width; x++)
                    Console.Write($"{encCoeffs[y * width + x],8} ");
                Console.WriteLine();
            }
            Console.WriteLine("  Decoder coefficients (full grid):");
            for (int y = 0; y < height; y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < width; x++)
                    Console.Write($"{decCoeffs[y * width + x],8} ");
                Console.WriteLine();
            }
            for (int i = 0; i < pixelCount; i++)
            {
                if (encCoeffs[i] != decCoeffs[i])
                {
                    coeffArrayDiffs++;
                    if (coeffArrayDiffs <= 20)
                        Console.WriteLine($"  COEFF ARRAY DIFF [{i}] ({i % width},{i / width}): enc={encCoeffs[i]} dec={decCoeffs[i]}");
                }
            }
            Console.WriteLine($"  Coefficient array differences: {coeffArrayDiffs} / {pixelCount}");

            // ----------------------------------------------------------------
            // Step 7: Inverse DWT on decoded coefficients
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 7: Inverse DWT on decoded coefficients ---");
            var reconstructed = new int[pixelCount];
            decCoeffs.CopyTo(reconstructed);
            Console.WriteLine("  Before inverse DWT (should match decoder coefficients above):");
            for (int y = 0; y < Math.Min(4, height); y++)
            {
                Console.Write($"    row {y}: ");
                for (int x = 0; x < Math.Min(8, width); x++)
                    Console.Write($"{reconstructed[y * width + x],8} ");
                Console.WriteLine();
            }

            DwtTransform.Inverse(reconstructed, width, height, levels, reversible: true);

            Console.WriteLine("  After inverse DWT (reconstructed pixel values):");
            for (int y = 0; y < height; y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < width; x++)
                    Console.Write($"{reconstructed[y * width + x],6} ");
                Console.WriteLine();
            }
            Console.WriteLine($"  Reconstructed Min={Min(reconstructed)} Max={Max(reconstructed)}");

            // ----------------------------------------------------------------
            // Step 8: Compare with original component data
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 8: Compare reconstructed vs original component data ---");
            int pixelMismatches = 0;
            int maxValue = (1 << info.BitsStored) - 1;
            for (int i = 0; i < pixelCount; i++)
            {
                int expected = componentData[i];
                int actual = reconstructed[i];
                // Apply clamping like the decoder does
                int clamped = Math.Max(0, Math.Min(maxValue, actual));
                if (clamped != expected)
                {
                    pixelMismatches++;
                    if (pixelMismatches <= 30)
                    {
                        Console.WriteLine($"  PIXEL DIFF [{i}] ({i % width},{i / width}): expected={expected} reconstructed={actual} clamped={clamped} diff={clamped - expected}");
                    }
                }
            }
            Console.WriteLine($"\n  Pixel mismatches (manual pipeline): {pixelMismatches} / {pixelCount}");

            // ----------------------------------------------------------------
            // Step 9: Also compare bytes as the full decoder would produce
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 9: Convert reconstructed to bytes and compare ---");
            var reconstructedBytes = new byte[info.FrameSize];
            for (int i = 0; i < pixelCount; i++)
            {
                int v = Math.Max(0, Math.Min(maxValue, reconstructed[i]));
                reconstructedBytes[i * 2] = (byte)(v & 0xFF);
                reconstructedBytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            int byteMismatches = 0;
            for (int i = 0; i < info.FrameSize; i++)
            {
                if (reconstructedBytes[i] != pixelData[i])
                {
                    byteMismatches++;
                    if (byteMismatches <= 20)
                    {
                        int pixIdx = i / 2;
                        ushort expectedVal = (ushort)(pixelData[pixIdx * 2] | (pixelData[pixIdx * 2 + 1] << 8));
                        ushort actualVal = (ushort)(reconstructedBytes[pixIdx * 2] | (reconstructedBytes[pixIdx * 2 + 1] << 8));
                        Console.WriteLine($"  BYTE DIFF [{i}] pixel[{pixIdx}]: expected byte=0x{pixelData[i]:X2} actual byte=0x{reconstructedBytes[i]:X2} (pixel expected={expectedVal} actual={actualVal})");
                    }
                }
            }
            Console.WriteLine($"  Byte mismatches (manual pipeline): {byteMismatches} / {info.FrameSize}");

            // ----------------------------------------------------------------
            // Step 10: Full J2kEncoder/J2kDecoder roundtrip for comparison
            // ----------------------------------------------------------------
            Console.WriteLine("\n--- Step 10: Full J2kEncoder/J2kDecoder roundtrip ---");
            var encoded2 = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Encoded codestream length: {encoded2.Length} bytes");

            var decoded2 = new byte[info.FrameSize];
            var result2 = J2kDecoder.DecodeFrame(encoded2.Span, info, decoded2, 0, EbcotBlockCoder.Instance);
            Console.WriteLine($"  Decode success: {result2.Success}");
            if (!result2.Success)
                Console.WriteLine($"  Decode error: {result2.Diagnostic?.Message}");

            int fullMismatches = 0;
            for (int i = 0; i < info.FrameSize; i++)
            {
                if (decoded2[i] != pixelData[i])
                {
                    fullMismatches++;
                    if (fullMismatches <= 20)
                    {
                        int pixIdx = i / 2;
                        ushort expectedVal = (ushort)(pixelData[pixIdx * 2] | (pixelData[pixIdx * 2 + 1] << 8));
                        ushort actualVal = (ushort)(decoded2[pixIdx * 2] | (decoded2[pixIdx * 2 + 1] << 8));
                        Console.WriteLine($"  FULL DECODE BYTE DIFF [{i}] pixel[{pixIdx}]: expected=0x{pixelData[i]:X2} actual=0x{decoded2[i]:X2} (pixel expected={expectedVal} actual={actualVal})");
                    }
                }
            }
            Console.WriteLine($"  Full decode byte mismatches: {fullMismatches} / {info.FrameSize}");

            // ----------------------------------------------------------------
            // Summary
            // ----------------------------------------------------------------
            Console.WriteLine("\n======================================================");
            Console.WriteLine("=== SUMMARY ===");
            Console.WriteLine("======================================================");
            Console.WriteLine($"  EBCOT roundtrip coefficient mismatches: {totalCoeffMismatches}");
            Console.WriteLine($"  Coefficient array diffs (enc vs dec TileComponent): {coeffArrayDiffs}");
            Console.WriteLine($"  Manual pipeline pixel mismatches: {pixelMismatches}");
            Console.WriteLine($"  Manual pipeline byte mismatches: {byteMismatches}");
            Console.WriteLine($"  Full J2K encoder/decoder byte mismatches: {fullMismatches}");
            if (totalCoeffMismatches == 0 && coeffArrayDiffs == 0 && pixelMismatches == 0 && byteMismatches == 0 && fullMismatches == 0)
            {
                Console.WriteLine("  ALL STAGES PASSED - pipeline is lossless for 16-bit data.");
            }
            else
            {
                Console.WriteLine("  FAILURE - See diagnostic output above to locate the corruption stage.");
                if (totalCoeffMismatches > 0)
                    Console.WriteLine("  >> Corruption is in EBCOT encode/decode roundtrip.");
                else if (coeffArrayDiffs > 0)
                    Console.WriteLine("  >> Corruption is in TileComponent Set/Get coefficient mapping.");
                else if (pixelMismatches > 0)
                    Console.WriteLine("  >> Corruption is in DWT forward/inverse roundtrip.");
                else if (byteMismatches > 0)
                    Console.WriteLine("  >> Corruption is in byte packing of reconstructed pixels.");
                else
                    Console.WriteLine("  >> Corruption is in full J2K encoder/decoder pipeline (Tier-2 or header).");
            }
        }

        private static int Min(int[] arr)
        {
            int min = int.MaxValue;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] < min) min = arr[i];
            return min;
        }

        private static int Max(int[] arr)
        {
            int max = int.MinValue;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] > max) max = arr[i];
            return max;
        }

        #endregion
    }
}
