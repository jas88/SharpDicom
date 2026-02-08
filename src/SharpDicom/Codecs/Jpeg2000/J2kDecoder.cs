using System;
using System.Buffers.Binary;
using SharpDicom.Codecs.Jpeg2000.Subband;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// Decodes JPEG 2000 codestreams to raw pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This decoder handles JPEG 2000 Part 1 codestreams (ITU-T T.800) as used
    /// in DICOM files with transfer syntaxes:
    /// - JPEG 2000 Lossless (1.2.840.10008.1.2.4.90)
    /// - JPEG 2000 (1.2.840.10008.1.2.4.91)
    /// </para>
    /// <para>
    /// The decoder supports both lossless (5/3 wavelet) and lossy (9/7 wavelet) modes.
    /// </para>
    /// </remarks>
    public static class J2kDecoder
    {
        /// <summary>
        /// Decodes a JPEG 2000 codestream to raw pixel data.
        /// </summary>
        /// <param name="codestream">The JPEG 2000 codestream data.</param>
        /// <param name="info">Expected pixel data information.</param>
        /// <param name="output">Destination buffer for decoded pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <returns>Decode result indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> codestream,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex)
        {
            return DecodeFrame(codestream, info, output, frameIndex, null);
        }

        /// <summary>
        /// Decodes a JPEG 2000 codestream to raw pixel data using a specific block coder.
        /// </summary>
        /// <param name="codestream">The JPEG 2000 codestream data.</param>
        /// <param name="info">Expected pixel data information.</param>
        /// <param name="output">Destination buffer for decoded pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <param name="blockCoder">
        /// Block coder to use for decoding. If null, auto-detects from the CAP marker:
        /// uses HtBlockEncoder for HTJ2K codestreams, EbcotBlockCoder for standard J2K.
        /// </param>
        /// <returns>Decode result indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> codestream,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex,
            IBlockCoder? blockCoder)
        {
            // 1. Parse codestream header
            if (!J2kCodestream.TryParse(codestream, out var header, out var error))
            {
                return DecodeResult.Fail(frameIndex, 0, error ?? "Invalid J2K header");
            }

            // 2. Validate against PixelDataInfo
            if (header!.ImageWidth != info.Columns || header.ImageHeight != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Dimension mismatch: J2K {header.ImageWidth}x{header.ImageHeight} vs expected {info.Columns}x{info.Rows}");
            }

            if (header.ComponentCount != info.SamplesPerPixel)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Component count mismatch: J2K {header.ComponentCount} vs expected {info.SamplesPerPixel}");
            }

            // 3. Auto-detect block coder from CAP marker if not specified
            IBlockCoder effectiveCoder = blockCoder ?? (header.IsHtj2k
                ? Tier1.HtBlockEncoder.Instance
                : (IBlockCoder)EbcotBlockCoder.Instance);

            // 4. Find tile data
            int tileDataOffset = J2kCodestream.FindTileDataOffset(codestream, 0);
            if (tileDataOffset < 0)
            {
                return DecodeResult.Fail(frameIndex, 0, "Could not find tile data");
            }

            // 5. Decode tile data
            ReadOnlySpan<byte> tileData = codestream.Slice(tileDataOffset);
            return DecodeTile(tileData, header, info, output, frameIndex, effectiveCoder);
        }

        /// <summary>
        /// Checks if data starts with a valid JPEG 2000 SOC marker.
        /// </summary>
        /// <param name="data">Data to check.</param>
        /// <returns>True if data appears to be a JPEG 2000 codestream.</returns>
        public static bool IsJpeg2000(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
            {
                return false;
            }

            ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data);
            return marker == J2kMarkers.SOC;
        }

        /// <summary>
        /// Decodes a single tile's data.
        /// </summary>
        private static DecodeResult DecodeTile(
            ReadOnlySpan<byte> tileData,
            J2kCodestream header,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex,
            IBlockCoder? blockCoder = null)
        {
            int width = header.ImageWidth;
            int height = header.ImageHeight;
            int components = header.ComponentCount;
            bool lossless = header.UsesReversibleTransform;
            int levels = header.DecompositionLevels;
            int cbWidth = header.CodeBlockWidth;
            int cbHeight = header.CodeBlockHeight;

            // Calculate code-block grid
            int cbsWide = (width + cbWidth - 1) / cbWidth;
            int cbsHigh = (height + cbHeight - 1) / cbHeight;
            int numCodeBlocks = cbsWide * cbsHigh;

            // Allocate component data buffers
            int[][] componentData = new int[components][];
            for (int c = 0; c < components; c++)
            {
                componentData[c] = new int[width * height];
            }

            // Parse packets and decode code-blocks using IBlockCoder abstraction
            var packetDecoder = new PacketDecoder();
            IBlockCoder effectiveBlockCoder = blockCoder ?? EbcotBlockCoder.Instance;

            // Enable HT mode on packet decoder if using HT block coder
            packetDecoder.IsHtMode = header.IsHtj2k;

            // Build subband descriptors for subband type lookup
            var subbands = SubbandPartitioner.GetSubbands(
                width, height, levels, cbWidth, cbHeight);

            // For each component, decode its packets and code-blocks
            // This is a simplified single-tile, single-layer decoder
            int dataOffset = 0;

            for (int c = 0; c < components; c++)
            {
                // Track inclusion state for each code-block
                bool[] firstInclusion = new bool[numCodeBlocks];
                for (int i = 0; i < numCodeBlocks; i++)
                {
                    firstInclusion[i] = true;
                }

                // For now, treat entire remaining data as a single packet per component
                // A full implementation would parse packet boundaries properly
                var segments = DecodeComponentPackets(
                    tileData.Slice(dataOffset),
                    numCodeBlocks,
                    header.NumberOfLayers,
                    packetDecoder,
                    firstInclusion);

                // Decode code-blocks using block coder
                int[] cbBuffer = new int[cbWidth * cbHeight];

                for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
                {
                    var (data, totalPasses, zeroBitPlanes) = segments[cbIdx];

                    if (totalPasses > 0 && !data.IsEmpty)
                    {
                        // Calculate MSB position from zero bitplanes
                        int msbPosition = 31 - zeroBitPlanes;
                        if (msbPosition < 0)
                        {
                            msbPosition = 0;
                        }

                        // Determine code-block position for subband type lookup
                        int cbX = cbIdx % cbsWide;
                        int cbY = cbIdx / cbsWide;
                        int startX = cbX * cbWidth;
                        int startY = cbY * cbHeight;

                        // Find the correct subband type for this code-block position
                        int subbandType = FindSubbandTypeForPosition(subbands, startX, startY);

                        // Decode code-block via IBlockCoder
                        Array.Clear(cbBuffer, 0, cbBuffer.Length);
                        effectiveBlockCoder.DecodeBlock(
                            data.Span,
                            totalPasses,
                            cbBuffer,
                            cbWidth, cbHeight,
                            msbPosition,
                            subbandType);

                        // Copy to component buffer
                        int actualWidth = Math.Min(cbWidth, width - startX);
                        int actualHeight = Math.Min(cbHeight, height - startY);

                        for (int y = 0; y < actualHeight; y++)
                        {
                            for (int x = 0; x < actualWidth; x++)
                            {
                                componentData[c][(startY + y) * width + (startX + x)] =
                                    cbBuffer[y * cbWidth + x];
                            }
                        }
                    }
                }
            }

            // Apply inverse DWT
            for (int c = 0; c < components; c++)
            {
                DwtTransform.Inverse(componentData[c], width, height, levels, lossless);
            }

            // Apply inverse color transform if used
            if (components >= 3 && header.UsesMct)
            {
                ApplyInverseColorTransform(componentData, width, height, lossless);
            }

            // Write output
            WriteOutput(componentData, info, output);

            return DecodeResult.Ok(info.FrameSize);
        }

        /// <summary>
        /// Decodes packets for a component and accumulates code-block data.
        /// </summary>
        private static (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[] DecodeComponentPackets(
            ReadOnlySpan<byte> packetData,
            int numCodeBlocks,
            int numLayers,
            PacketDecoder decoder,
            bool[] firstInclusion)
        {
            var results = new (ReadOnlyMemory<byte>, int, int)[numCodeBlocks];

            // Initialize results
            for (int i = 0; i < numCodeBlocks; i++)
            {
                results[i] = (ReadOnlyMemory<byte>.Empty, 0, 0);
            }

            // Simple case: treat all data as one packet
            // Full implementation would parse layer boundaries
            if (!packetData.IsEmpty)
            {
                var segments = decoder.DecodePacket(packetData, numCodeBlocks, firstInclusion);

                for (int i = 0; i < numCodeBlocks; i++)
                {
                    var seg = segments[i];
                    if (seg.NumNewPasses > 0)
                    {
                        results[i] = (seg.Data, seg.NumNewPasses, seg.ZeroBitPlanes);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Finds the subband type for a given position in the DWT coefficient array.
        /// </summary>
        /// <param name="subbands">Subband descriptors from SubbandPartitioner.</param>
        /// <param name="x">Horizontal position in the coefficient array.</param>
        /// <param name="y">Vertical position in the coefficient array.</param>
        /// <returns>Subband type integer (0=LL, 1=HL, 2=LH, 3=HH).</returns>
        private static int FindSubbandTypeForPosition(SubbandDescriptor[] subbands, int x, int y)
        {
            for (int i = 0; i < subbands.Length; i++)
            {
                var sb = subbands[i];
                if (x >= sb.OriginX && x < sb.OriginX + sb.Width &&
                    y >= sb.OriginY && y < sb.OriginY + sb.Height)
                {
                    return (int)sb.Type;
                }
            }

            // Default to LL if no subband found (should not happen with correct partitioning)
            return 0;
        }

        /// <summary>
        /// Applies inverse color transform (inverse RCT or ICT).
        /// </summary>
        private static void ApplyInverseColorTransform(int[][] components, int width, int height, bool lossless)
        {
            if (components.Length < 3)
            {
                return;
            }

            int[] y = components[0];
            int[] cb = components[1];
            int[] cr = components[2];
            int pixelCount = width * height;

            if (lossless)
            {
                // Inverse RCT
                for (int i = 0; i < pixelCount; i++)
                {
                    int yVal = y[i];
                    int cbVal = cb[i];
                    int crVal = cr[i];

                    // G = Y - floor((Cb + Cr) / 4)
                    // R = Cr + G
                    // B = Cb + G
                    int green = yVal - ((cbVal + crVal) >> 2);
                    int red = crVal + green;
                    int blue = cbVal + green;

                    y[i] = red;
                    cb[i] = green;
                    cr[i] = blue;
                }
            }
            else
            {
                // Inverse ICT
                for (int i = 0; i < pixelCount; i++)
                {
                    double yVal = y[i];
                    double cbVal = cb[i];
                    double crVal = cr[i];

                    // R = Y + 1.402 * Cr
                    // G = Y - 0.34413 * Cb - 0.71414 * Cr
                    // B = Y + 1.772 * Cb
                    double red = yVal + 1.402 * crVal;
                    double green = yVal - 0.34413 * cbVal - 0.71414 * crVal;
                    double blue = yVal + 1.772 * cbVal;

                    y[i] = (int)Math.Round(red);
                    cb[i] = (int)Math.Round(green);
                    cr[i] = (int)Math.Round(blue);
                }
            }
        }

        /// <summary>
        /// Writes decoded component data to output buffer.
        /// </summary>
        private static void WriteOutput(int[][] componentData, PixelDataInfo info, Span<byte> output)
        {
            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;
            int bytesPerSample = info.BytesPerSample;
            int pixelCount = width * height;

            // Clamp values to valid range
            int maxValue = (1 << info.BitsStored) - 1;
            int minValue = info.IsSigned ? -(1 << (info.BitsStored - 1)) : 0;
            int maxSigned = info.IsSigned ? (1 << (info.BitsStored - 1)) - 1 : maxValue;

            if (info.IsPlanar)
            {
                // Planar output
                for (int c = 0; c < components; c++)
                {
                    int offset = c * pixelCount * bytesPerSample;
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int value = Clamp(componentData[c][i], minValue, maxSigned);
                        WriteSample(output, offset + i * bytesPerSample, value, bytesPerSample);
                    }
                }
            }
            else
            {
                // Interleaved output
                int bytesPerPixel = components * bytesPerSample;
                for (int i = 0; i < pixelCount; i++)
                {
                    int pixelOffset = i * bytesPerPixel;
                    for (int c = 0; c < components; c++)
                    {
                        int value = Clamp(componentData[c][i], minValue, maxSigned);
                        WriteSample(output, pixelOffset + c * bytesPerSample, value, bytesPerSample);
                    }
                }
            }
        }

        private static void WriteSample(Span<byte> output, int offset, int value, int bytesPerSample)
        {
            if (bytesPerSample == 1)
            {
                output[offset] = (byte)value;
            }
            else if (bytesPerSample == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(offset), (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(offset), (uint)value);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
