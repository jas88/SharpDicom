using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Subband;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    /// <summary>
    /// Diagnostic test for the lossless J2K subband pipeline: DWT, TileComponent
    /// iteration, EBCOT code-block roundtrip, inverse DWT, and final comparison.
    /// Produces extensive console output at every step to aid debugging.
    /// </summary>
    [TestFixture]
    public class SubbandPipelineDiagnosticTests
    {
        /// <summary>
        /// Full diagnostic pipeline: creates a 32x32 8-bit grayscale ramp,
        /// applies forward DWT (5 levels, lossless), iterates every code block
        /// in every subband via TileComponent, roundtrips each through EBCOT,
        /// places decoded coefficients back, applies inverse DWT, and compares.
        /// </summary>
        [Test]
        public void DiagnosticLossless32x32_SubbandEbcotRoundtrip()
        {
            const int width = 32;
            const int height = 32;
            const int levels = 5;
            const int cbWidth = 64;
            const int cbHeight = 64;
            int pixelCount = width * height;

            // ----------------------------------------------------------
            // Step 1: Create test data (same as the failing regression test)
            // ----------------------------------------------------------
            Console.WriteLine("=== Step 1: Create 32x32 8-bit test data (pixels = i % 256) ===");
            var original = new int[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                original[i] = i % 256;
            }

            Console.WriteLine($"  original[0]={original[0]}, original[1]={original[1]}, original[31]={original[31]}, original[{pixelCount - 1}]={original[pixelCount - 1]}");

            // ----------------------------------------------------------
            // Step 2: Extract components (grayscale = single int[] copy)
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 2: Extract component (grayscale copy) ===");
            var componentData = new int[pixelCount];
            Array.Copy(original, componentData, pixelCount);
            Console.WriteLine($"  componentData[0]={componentData[0]}, componentData[1]={componentData[1]}");

            // ----------------------------------------------------------
            // Step 3: Forward DWT (5 levels, reversible / lossless)
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 3: Forward DWT (5 levels, reversible=true) ===");
            DwtTransform.Forward(componentData, width, height, levels, reversible: true);

            Console.WriteLine("  First 64 DWT coefficients (row-major, stride=32):");
            for (int y = 0; y < Math.Min(8, height); y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < Math.Min(8, width); x++)
                {
                    Console.Write($"{componentData[y * width + x],6} ");
                }
                Console.WriteLine();
            }

            // ----------------------------------------------------------
            // Step 4: Create TileComponent and enumerate subbands
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 4: Create TileComponent and enumerate subbands ===");
            using var tileComp = new TileComponent(
                tileIndex: 0,
                componentIndex: 0,
                tileWidth: width,
                tileHeight: height,
                decompositionLevels: levels,
                codeBlockWidth: cbWidth,
                codeBlockHeight: cbHeight);

            // Copy DWT coefficients into TileComponent
            componentData.AsSpan().CopyTo(tileComp.Coefficients);

            var subbands = tileComp.Subbands;
            int totalCodeBlocks = 0;
            Console.WriteLine($"  Total subbands: {subbands.Length}");
            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                Console.WriteLine($"  [{s}] Type={sb.Type,-3} ResLevel={sb.ResolutionLevel} " +
                    $"Dims={sb.Width}x{sb.Height} Origin=({sb.OriginX},{sb.OriginY}) " +
                    $"CBGrid={sb.CodeBlockGridWidth}x{sb.CodeBlockGridHeight} " +
                    $"TotalCBs={sb.TotalCodeBlocks}");
                totalCodeBlocks += sb.TotalCodeBlocks;
            }
            Console.WriteLine($"  Grand total code blocks: {totalCodeBlocks}");

            // ----------------------------------------------------------
            // Step 5: EBCOT roundtrip for every code block
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 5: EBCOT encode/decode per code block ===");
            var coder = EbcotBlockCoder.Instance;
            int cbBufferSize = cbWidth * cbHeight;
            var encodeBuffer = new int[cbBufferSize];
            var decodeBuffer = new int[cbBufferSize];

            int blocksProcessed = 0;
            int blockMismatches = 0;
            int totalCoefficientMismatches = 0;

            // We will build a second TileComponent for the decoded side
            using var decodedTileComp = new TileComponent(
                tileIndex: 0,
                componentIndex: 0,
                tileWidth: width,
                tileHeight: height,
                decompositionLevels: levels,
                codeBlockWidth: cbWidth,
                codeBlockHeight: cbHeight);

            for (int s = 0; s < subbands.Length; s++)
            {
                var sb = subbands[s];
                if (sb.TotalCodeBlocks == 0)
                {
                    Console.WriteLine($"  Subband [{s}] {sb.Type} r{sb.ResolutionLevel}: EMPTY (0 code blocks)");
                    continue;
                }

                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        blocksProcessed++;

                        // Extract coefficients from the original DWT output
                        Array.Clear(encodeBuffer, 0, cbBufferSize);
                        var (actualW, actualH) = tileComp.GetCodeBlockCoefficients(s, cbX, cbY, encodeBuffer);

                        // Show a summary of the code block coefficients
                        int nonZeroCount = 0;
                        int minVal = int.MaxValue;
                        int maxVal = int.MinValue;
                        for (int i = 0; i < actualW * actualH; i++)
                        {
                            // Coefficients are stored with cbWidth stride in the buffer
                            int row = i / actualW;
                            int col = i % actualW;
                            int val = encodeBuffer[row * cbWidth + col];
                            if (val != 0) nonZeroCount++;
                            if (val < minVal) minVal = val;
                            if (val > maxVal) maxVal = val;
                        }

                        Console.WriteLine($"  Subband[{s}] {sb.Type} r{sb.ResolutionLevel} CB({cbX},{cbY}) " +
                            $"actual={actualW}x{actualH} nonZero={nonZeroCount} " +
                            $"range=[{minVal},{maxVal}]");

                        // Show first few coefficients
                        int showCount = Math.Min(8, actualW);
                        Console.Write("    first coeffs: ");
                        for (int i = 0; i < showCount; i++)
                        {
                            Console.Write($"{encodeBuffer[i]} ");
                        }
                        Console.WriteLine();

                        // Repack from cbWidth-stride to actualW-stride for EBCOT
                        int subbandType = (int)sb.Type;
                        int[] packed = new int[actualW * actualH];
                        for (int y = 0; y < actualH; y++)
                        {
                            for (int x = 0; x < actualW; x++)
                            {
                                packed[y * actualW + x] = encodeBuffer[y * cbWidth + x];
                            }
                        }

                        // EBCOT encode with actual dimensions
                        var encoded = coder.EncodeBlock(
                            packed, actualW, actualH,
                            subbandType, msbPosition: -1);

                        Console.WriteLine($"    EBCOT encoded: passes={encoded.NumPasses} " +
                            $"bytes={encoded.Data.Length} msb={encoded.MsbPosition}");

                        // EBCOT decode with actual dimensions
                        int[] decodedPacked = new int[actualW * actualH];
                        if (encoded.NumPasses > 0 && encoded.Data.Length > 0)
                        {
                            coder.DecodeBlock(
                                encoded.Data.Span, encoded.NumPasses,
                                decodedPacked, actualW, actualH,
                                encoded.MsbPosition, subbandType);
                        }

                        // Compare encode input vs decode output
                        int cbMismatches = 0;
                        for (int y = 0; y < actualH; y++)
                        {
                            for (int x = 0; x < actualW; x++)
                            {
                                int pIdx = y * actualW + x;
                                if (packed[pIdx] != decodedPacked[pIdx])
                                {
                                    cbMismatches++;
                                    if (cbMismatches <= 5)
                                    {
                                        Console.WriteLine($"    MISMATCH at ({x},{y}): " +
                                            $"original={packed[pIdx]} decoded={decodedPacked[pIdx]}");
                                    }
                                }
                            }
                        }

                        if (cbMismatches > 0)
                        {
                            Console.WriteLine($"    !!! {cbMismatches} coefficient mismatches in this CB");
                            blockMismatches++;
                            totalCoefficientMismatches += cbMismatches;
                        }
                        else
                        {
                            Console.WriteLine("    EBCOT roundtrip: EXACT match");
                        }

                        // Unpack back to cbWidth-stride for SetCodeBlockCoefficients
                        Array.Clear(decodeBuffer, 0, cbBufferSize);
                        for (int y = 0; y < actualH; y++)
                        {
                            for (int x = 0; x < actualW; x++)
                            {
                                decodeBuffer[y * cbWidth + x] = decodedPacked[y * actualW + x];
                            }
                        }

                        // Place decoded coefficients into the decoded TileComponent
                        decodedTileComp.SetCodeBlockCoefficients(s, cbX, cbY, decodeBuffer);
                    }
                }
            }

            Console.WriteLine($"\n  Blocks processed: {blocksProcessed}");
            Console.WriteLine($"  Blocks with mismatches: {blockMismatches}");
            Console.WriteLine($"  Total coefficient mismatches: {totalCoefficientMismatches}");

            // ----------------------------------------------------------
            // Step 6: Copy decoded coefficients out and apply inverse DWT
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 6: Copy coefficients and apply inverse DWT ===");
            var reconstructed = new int[pixelCount];
            decodedTileComp.Coefficients.CopyTo(reconstructed);

            Console.WriteLine("  Decoded DWT coefficients before inverse (first 8x8):");
            for (int y = 0; y < Math.Min(8, height); y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < Math.Min(8, width); x++)
                {
                    Console.Write($"{reconstructed[y * width + x],6} ");
                }
                Console.WriteLine();
            }

            // Compare DWT coefficients before inverse
            Console.WriteLine("\n  DWT coefficient comparison (original vs decoded-before-inverse):");
            int dwtMismatchCount = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                if (componentData[i] != reconstructed[i])
                {
                    dwtMismatchCount++;
                    if (dwtMismatchCount <= 20)
                    {
                        int y = i / width;
                        int x = i % width;
                        Console.WriteLine($"    DWT coeff mismatch at [{i}] ({x},{y}): " +
                            $"original={componentData[i]} decoded={reconstructed[i]}");
                    }
                }
            }
            Console.WriteLine($"  Total DWT coefficient mismatches: {dwtMismatchCount}");

            DwtTransform.Inverse(reconstructed, width, height, levels, reversible: true);

            Console.WriteLine("\n  Reconstructed pixel values (first 8x8):");
            for (int y = 0; y < Math.Min(8, height); y++)
            {
                Console.Write($"    row {y,2}: ");
                for (int x = 0; x < Math.Min(8, width); x++)
                {
                    Console.Write($"{reconstructed[y * width + x],4} ");
                }
                Console.WriteLine();
            }

            // ----------------------------------------------------------
            // Step 7: Compare with original and report
            // ----------------------------------------------------------
            Console.WriteLine("\n=== Step 7: Final comparison (original vs reconstructed) ===");
            int pixelMismatches = 0;
            int maxError = 0;
            long sumAbsError = 0;
            int firstMismatchIndex = -1;

            for (int i = 0; i < pixelCount; i++)
            {
                int diff = Math.Abs(original[i] - reconstructed[i]);
                if (diff > 0)
                {
                    pixelMismatches++;
                    sumAbsError += diff;
                    if (diff > maxError) maxError = diff;
                    if (firstMismatchIndex < 0) firstMismatchIndex = i;

                    if (pixelMismatches <= 20)
                    {
                        int y = i / width;
                        int x = i % width;
                        Console.WriteLine($"  DIFF at [{i}] ({x},{y}): " +
                            $"original={original[i]} reconstructed={reconstructed[i]} " +
                            $"diff={diff}");
                    }
                }
            }

            Console.WriteLine($"\n  Total pixel mismatches: {pixelMismatches} / {pixelCount}");
            Console.WriteLine($"  Max absolute error: {maxError}");
            if (pixelMismatches > 0)
            {
                Console.WriteLine($"  Mean absolute error: {(double)sumAbsError / pixelMismatches:F4}");
                Console.WriteLine($"  First mismatch index: {firstMismatchIndex} " +
                    $"(pixel ({firstMismatchIndex % width},{firstMismatchIndex / width}))");
            }

            // Assert lossless roundtrip
            Assert.That(blockMismatches, Is.EqualTo(0),
                $"EBCOT code-block roundtrip had {blockMismatches} blocks with mismatches " +
                $"({totalCoefficientMismatches} total coefficient errors)");
            Assert.That(dwtMismatchCount, Is.EqualTo(0),
                $"DWT coefficients diverged: {dwtMismatchCount} mismatches after " +
                "TileComponent Get/Set roundtrip");
            Assert.That(pixelMismatches, Is.EqualTo(0),
                $"Lossless roundtrip failed: {pixelMismatches} pixel mismatches, " +
                $"max error={maxError}");
        }
    }
}
