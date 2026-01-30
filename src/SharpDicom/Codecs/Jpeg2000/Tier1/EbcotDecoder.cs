using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// EBCOT decoder for reconstructing wavelet coefficients from tier-1 coded data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decodes MQ-coded bitstreams produced by the EBCOT encoder, reconstructing
    /// wavelet coefficients by processing bitplanes from MSB to LSB.
    /// </para>
    /// <para>
    /// The decoder mirrors the encoder's three-pass structure:
    /// significance propagation, magnitude refinement, and cleanup.
    /// </para>
    /// <para>
    /// Reference: ITU-T T.800 Annex D (Coefficient bit modeling).
    /// </para>
    /// </remarks>
    public sealed class EbcotDecoder
    {
        // Context indices (same as encoder)
        private const int CtxSigLL = 0;
        private const int CtxSign = 9;
        private const int CtxMag = 14;
        private const int CtxRun = 17;

        // State arrays
        private byte[]? _significanceState;
        private byte[]? _signState;
        private int[]? _magnitudeState; // Accumulated magnitude bits
        private int _currentWidth;
        private int _currentHeight;

        /// <summary>
        /// Initializes a new EBCOT decoder.
        /// </summary>
        public EbcotDecoder()
        {
        }

        /// <summary>
        /// Decodes a code-block given its encoded passes.
        /// </summary>
        /// <param name="data">Encoded MQ-coder data.</param>
        /// <param name="numPasses">Number of coding passes to decode.</param>
        /// <param name="width">Code-block width.</param>
        /// <param name="height">Code-block height.</param>
        /// <param name="msbPosition">MSB bitplane position.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</param>
        /// <returns>Decoded wavelet coefficients.</returns>
        /// <exception cref="ArgumentException">If parameters are invalid.</exception>
        public int[] DecodeCodeBlock(
            ReadOnlySpan<byte> data,
            int numPasses,
            int width, int height,
            int msbPosition,
            int subbandType = 0)
        {
            if (width <= 0 || width > EbcotEncoder.MaxCodeBlockDimension)
            {
                throw new ArgumentException($"Width must be between 1 and {EbcotEncoder.MaxCodeBlockDimension}.", nameof(width));
            }

            if (height <= 0 || height > EbcotEncoder.MaxCodeBlockDimension)
            {
                throw new ArgumentException($"Height must be between 1 and {EbcotEncoder.MaxCodeBlockDimension}.", nameof(height));
            }

            int size = width * height;

            // Handle empty code-block
            if (numPasses == 0 || data.IsEmpty || msbPosition < 0)
            {
                return new int[size];
            }

            // Prepare state arrays
            EnsureStateArrays(size);
            Array.Clear(_significanceState!, 0, size);
            Array.Clear(_signState!, 0, size);
            Array.Clear(_magnitudeState!, 0, size);
            _currentWidth = width;
            _currentHeight = height;

            // Create decoder
            var mqDecoder = new MqDecoder(data.ToArray());

            // Process passes
            // Each bitplane has 3 passes: significance propagation, refinement, cleanup
            int passesPerBitplane = 3;
            int currentPass = 0;
            int bitplane = msbPosition;

            while (currentPass < numPasses && bitplane >= 0)
            {
                int passInBitplane = currentPass % passesPerBitplane;

                switch (passInBitplane)
                {
                    case 0:
                        // Significance Propagation Pass
                        DecodeSignificancePropagationPass(mqDecoder, width, height, bitplane, subbandType);
                        break;

                    case 1:
                        // Magnitude Refinement Pass
                        DecodeMagnitudeRefinementPass(mqDecoder, width, height, bitplane);
                        break;

                    case 2:
                        // Cleanup Pass
                        DecodeCleanupPass(mqDecoder, width, height, bitplane, subbandType);
                        bitplane--; // Move to next bitplane after cleanup
                        break;
                }

                currentPass++;
            }

            // Reconstruct final coefficients from accumulated bits and signs
            int[] coefficients = new int[size];
            for (int i = 0; i < size; i++)
            {
                int magnitude = _magnitudeState![i];
                if (_signState![i] != 0)
                {
                    coefficients[i] = -magnitude;
                }
                else
                {
                    coefficients[i] = magnitude;
                }
            }

            return coefficients;
        }

        private void EnsureStateArrays(int size)
        {
            if (_significanceState == null || _significanceState.Length < size)
            {
                _significanceState = new byte[size];
                _signState = new byte[size];
                _magnitudeState = new int[size];
            }
        }

        /// <summary>
        /// Significance Propagation Pass: Decode samples with significant neighbors.
        /// </summary>
        private void DecodeSignificancePropagationPass(
            MqDecoder decoder,
            int width, int height,
            int bitplane,
            int subbandType)
        {
            int bitMask = 1 << bitplane;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Skip if already significant
                    if (_significanceState![idx] != 0)
                    {
                        continue;
                    }

                    // Check if any neighbor is significant
                    if (!HasSignificantNeighbor(x, y, width, height))
                    {
                        continue;
                    }

                    // Decode significance bit
                    int context = GetSignificanceContext(x, y, width, height, subbandType);
                    int bit = decoder.Decode(context);

                    if (bit == 1)
                    {
                        // Sample became significant
                        _significanceState[idx] = 1;
                        _magnitudeState![idx] |= bitMask;

                        // Decode sign
                        int signContext = GetSignContext(x, y, width, height);
                        int signBit = decoder.Decode(signContext);
                        _signState![idx] = (byte)signBit;
                    }
                }
            }
        }

        /// <summary>
        /// Magnitude Refinement Pass: Decode refinement bits for significant samples.
        /// </summary>
        private void DecodeMagnitudeRefinementPass(
            MqDecoder decoder,
            int width, int height,
            int bitplane)
        {
            int bitMask = 1 << bitplane;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Only process already-significant samples
                    if (_significanceState![idx] == 0)
                    {
                        continue;
                    }

                    // Check if sample was made significant at a higher bitplane
                    // (has magnitude bits above current bitplane)
                    int higherBits = _magnitudeState![idx] >> (bitplane + 1);
                    if (higherBits == 0)
                    {
                        // Made significant at this bitplane, not refined yet
                        continue;
                    }

                    // Decode refinement bit
                    int context = GetMagnitudeRefinementContext(x, y, width, height);
                    int bit = decoder.Decode(context);

                    if (bit == 1)
                    {
                        _magnitudeState[idx] |= bitMask;
                    }
                }
            }
        }

        /// <summary>
        /// Cleanup Pass: Decode remaining samples not processed by previous passes.
        /// </summary>
        private void DecodeCleanupPass(
            MqDecoder decoder,
            int width, int height,
            int bitplane,
            int subbandType)
        {
            int bitMask = 1 << bitplane;

            // Process in vertical stripes of 4 rows
            for (int stripeY = 0; stripeY < height; stripeY += 4)
            {
                int stripeHeight = Math.Min(4, height - stripeY);

                for (int x = 0; x < width; x++)
                {
                    // Check if we should expect run-length coding
                    bool allInsignificant = true;
                    bool allNoSignificantNeighbors = true;

                    for (int dy = 0; dy < stripeHeight && allInsignificant; dy++)
                    {
                        int y = stripeY + dy;
                        int idx = y * width + x;

                        if (_significanceState![idx] != 0)
                        {
                            allInsignificant = false;
                        }
                        else if (HasSignificantNeighbor(x, y, width, height))
                        {
                            allNoSignificantNeighbors = false;
                        }
                    }

                    if (allInsignificant && allNoSignificantNeighbors && stripeHeight == 4)
                    {
                        // Decode run-length mode
                        DecodeRunLengthMode(decoder, width, x, stripeY, bitplane, subbandType);
                    }
                    else
                    {
                        // Process each sample in stripe
                        for (int dy = 0; dy < stripeHeight; dy++)
                        {
                            int y = stripeY + dy;
                            DecodeCleanupSample(decoder, width, height, x, y, bitplane, subbandType);
                        }
                    }
                }
            }
        }

        private void DecodeRunLengthMode(
            MqDecoder decoder,
            int width, int x, int stripeY,
            int bitplane,
            int subbandType)
        {
            int bitMask = 1 << bitplane;

            // Decode run-length flag
            int runFlag = decoder.Decode(CtxRun);

            if (runFlag == 0)
            {
                // All samples are zero at this bitplane - nothing to do
                return;
            }

            // Decode position (2 bits)
            int pos = decoder.DecodeUniform() << 1;
            pos |= decoder.DecodeUniform();

            // Process samples from the indicated position
            for (int dy = pos; dy < 4; dy++)
            {
                int y = stripeY + dy;
                int idx = y * width + x;

                if (dy == pos)
                {
                    // This sample is significant
                    _significanceState![idx] = 1;
                    _magnitudeState![idx] |= bitMask;

                    // Decode sign
                    int signContext = GetSignContext(x, y, width, _currentHeight);
                    int signBit = decoder.Decode(signContext);
                    _signState![idx] = (byte)signBit;
                }
                else
                {
                    // Decode normally
                    DecodeCleanupSample(decoder, width, _currentHeight, x, y, bitplane, subbandType);
                }
            }
        }

        private void DecodeCleanupSample(
            MqDecoder decoder,
            int width, int height,
            int x, int y,
            int bitplane,
            int subbandType)
        {
            int idx = y * width + x;
            int bitMask = 1 << bitplane;

            // Skip if already significant
            if (_significanceState![idx] != 0)
            {
                return;
            }

            // Skip if processed by significance propagation
            if (HasSignificantNeighbor(x, y, width, height))
            {
                return;
            }

            // Decode significance
            int context = GetSignificanceContext(x, y, width, height, subbandType);
            int bit = decoder.Decode(context);

            if (bit == 1)
            {
                // Sample became significant
                _significanceState[idx] = 1;
                _magnitudeState![idx] |= bitMask;

                // Decode sign
                int signContext = GetSignContext(x, y, width, height);
                int signBit = decoder.Decode(signContext);
                _signState![idx] = (byte)signBit;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasSignificantNeighbor(int x, int y, int width, int height)
        {
            // Check 8 neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    if (nx < 0 || nx >= width)
                    {
                        continue;
                    }

                    if (_significanceState![ny * width + nx] != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSignificanceContext(int x, int y, int width, int height, int subbandType)
        {
            int h = GetNeighborSignificance(x - 1, y, width, height) +
                    GetNeighborSignificance(x + 1, y, width, height);
            int v = GetNeighborSignificance(x, y - 1, width, height) +
                    GetNeighborSignificance(x, y + 1, width, height);
            int d = GetNeighborSignificance(x - 1, y - 1, width, height) +
                    GetNeighborSignificance(x + 1, y - 1, width, height) +
                    GetNeighborSignificance(x - 1, y + 1, width, height) +
                    GetNeighborSignificance(x + 1, y + 1, width, height);

            return GetSignificanceContextFromCounts(h, v, d, subbandType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetNeighborSignificance(int x, int y, int width, int height)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return 0;
            }

            return _significanceState![y * width + x];
        }

        private static int GetSignificanceContextFromCounts(int h, int v, int d, int subbandType)
        {
            if (subbandType == 3) // HH
            {
                int hv = h + v;
                if (hv >= 2)
                {
                    return 8;
                }

                if (hv == 1)
                {
                    return d >= 1 ? 7 : 6;
                }

                if (d >= 2)
                {
                    return 5;
                }

                return d >= 1 ? 4 : 0;
            }
            else if (subbandType == 1) // HL
            {
                if (h >= 1)
                {
                    return h == 2 ? 8 : (v >= 1 ? 7 : 6);
                }

                if (v >= 2)
                {
                    return 5;
                }

                if (v == 1)
                {
                    return d >= 1 ? 4 : 3;
                }

                if (d >= 2)
                {
                    return 2;
                }

                return d >= 1 ? 1 : 0;
            }
            else // LL or LH
            {
                if (v >= 1)
                {
                    return v == 2 ? 8 : (h >= 1 ? 7 : 6);
                }

                if (h >= 2)
                {
                    return 5;
                }

                if (h == 1)
                {
                    return d >= 1 ? 4 : 3;
                }

                if (d >= 2)
                {
                    return 2;
                }

                return d >= 1 ? 1 : 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSignContext(int x, int y, int width, int height)
        {
            // Suppress unused parameter warnings - full implementation would use these
            _ = x;
            _ = y;
            _ = width;
            _ = height;
            return CtxSign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMagnitudeRefinementContext(int x, int y, int width, int height)
        {
            bool hasSignificantNeighbor = HasSignificantNeighbor(x, y, width, height);
            return hasSignificantNeighbor ? CtxMag + 1 : CtxMag;
        }
    }
}
