using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Encoded code-block data produced by EBCOT tier-1 encoding.
    /// </summary>
    public readonly struct CodeBlockData
    {
        /// <summary>Gets the encoded MQ-coder bitstream data.</summary>
        public ReadOnlyMemory<byte> Data { get; init; }

        /// <summary>Gets the total number of coding passes.</summary>
        public int NumPasses { get; init; }

        /// <summary>Gets the cumulative byte lengths at the end of each pass (for truncation).</summary>
        public int[] PassLengths { get; init; }

        /// <summary>Gets the most significant bitplane position.</summary>
        public int MsbPosition { get; init; }

        /// <summary>
        /// Creates empty code-block data (for code-blocks with no significant coefficients).
        /// </summary>
        public static CodeBlockData Empty => new()
        {
            Data = ReadOnlyMemory<byte>.Empty,
            NumPasses = 0,
            PassLengths = Array.Empty<int>(),
            MsbPosition = 0
        };
    }

    /// <summary>
    /// EBCOT (Embedded Block Coding with Optimal Truncation) encoder for JPEG 2000 tier-1 coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Encodes wavelet coefficients in code-blocks (typically 64x64) using
    /// three coding passes per bitplane: significance propagation, refinement, and cleanup.
    /// </para>
    /// <para>
    /// The encoder uses the MQ arithmetic coder for entropy coding with
    /// context-adaptive probability estimation based on neighbor patterns.
    /// </para>
    /// <para>
    /// Reference: ITU-T T.800 Annex D (Coefficient bit modeling).
    /// </para>
    /// </remarks>
    public sealed class EbcotEncoder : IDisposable
    {
        /// <summary>
        /// Default code-block size (64x64).
        /// </summary>
        public const int DefaultCodeBlockSize = 64;

        /// <summary>
        /// Maximum code-block dimension per ITU-T T.800.
        /// </summary>
        public const int MaxCodeBlockDimension = 1024;

        // Context indices per ITU-T T.800 Table D.2
        private const int CtxSigLL = 0;   // LL/LH significance context base
        private const int CtxSigHL = 9;   // HL significance context base (offset from pattern)
        private const int CtxSign = 9;    // Sign coding context
        private const int CtxMag = 14;    // Magnitude refinement context base
        private const int CtxRun = 17;    // Run-length coding context
        private const int CtxUniform = 18; // Uniform distribution

        private readonly MqEncoder _mqEncoder;
        private readonly List<int> _passLengths;

        // State arrays (lazily allocated per code-block size)
        private byte[]? _significanceState;
        private byte[]? _signState;
        private int _currentWidth;
        private int _currentHeight;
        private bool _disposed;

        /// <summary>
        /// Initializes a new EBCOT encoder.
        /// </summary>
        /// <param name="initialBufferSize">Initial MQ encoder buffer size.</param>
        public EbcotEncoder(int initialBufferSize = 8192)
        {
            _mqEncoder = new MqEncoder(initialBufferSize);
            _passLengths = new List<int>(64);
        }

        /// <summary>
        /// Encodes a single code-block of wavelet coefficients.
        /// </summary>
        /// <param name="coefficients">Wavelet coefficients for this code-block.</param>
        /// <param name="width">Code-block width.</param>
        /// <param name="height">Code-block height.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</param>
        /// <returns>Encoded data with pass information.</returns>
        /// <exception cref="ArgumentException">If dimensions are invalid.</exception>
        public CodeBlockData EncodeCodeBlock(ReadOnlySpan<int> coefficients, int width, int height, int subbandType = 0)
        {
            if (width <= 0 || width > MaxCodeBlockDimension)
            {
                throw new ArgumentException($"Width must be between 1 and {MaxCodeBlockDimension}.", nameof(width));
            }

            if (height <= 0 || height > MaxCodeBlockDimension)
            {
                throw new ArgumentException($"Height must be between 1 and {MaxCodeBlockDimension}.", nameof(height));
            }

            if (coefficients.Length < width * height)
            {
                throw new ArgumentException("Coefficient buffer is too small for the specified dimensions.");
            }

            // Find MSB position (max magnitude)
            int msbPosition = FindMsbPosition(coefficients, width * height);
            if (msbPosition < 0)
            {
                // All coefficients are zero
                return CodeBlockData.Empty;
            }

            // Prepare state arrays
            EnsureStateArrays(width, height);
            Array.Clear(_significanceState!, 0, _significanceState!.Length);
            Array.Clear(_signState!, 0, _signState!.Length);
            _currentWidth = width;
            _currentHeight = height;

            // Reset encoder and pass tracking
            _mqEncoder.Reset();
            _passLengths.Clear();

            // Process each bitplane from MSB down to 0
            int numBitplanes = msbPosition + 1;

            for (int bitplane = msbPosition; bitplane >= 0; bitplane--)
            {
                // Pass 1: Significance Propagation
                EncodeSignificancePropagationPass(coefficients, width, height, bitplane, subbandType);
                RecordPassLength();

                // Pass 2: Magnitude Refinement
                EncodeMagnitudeRefinementPass(coefficients, width, height, bitplane);
                RecordPassLength();

                // Pass 3: Cleanup
                EncodeCleanupPass(coefficients, width, height, bitplane, subbandType);
                RecordPassLength();
            }

            // Flush and get encoded data
            ReadOnlySpan<byte> encoded = _mqEncoder.Flush();

            return new CodeBlockData
            {
                Data = encoded.ToArray(),
                NumPasses = _passLengths.Count,
                PassLengths = _passLengths.ToArray(),
                MsbPosition = msbPosition
            };
        }

        /// <summary>
        /// Disposes the encoder and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _mqEncoder.Dispose();
                _disposed = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordPassLength()
        {
            // Record cumulative length after this pass
            ReadOnlySpan<byte> data = _mqEncoder.GetEncodedData();
            _passLengths.Add(data.Length);
        }

        /// <summary>
        /// Finds the most significant bitplane containing a non-zero coefficient.
        /// </summary>
        private static int FindMsbPosition(ReadOnlySpan<int> coefficients, int count)
        {
            int maxMagnitude = 0;
            for (int i = 0; i < count; i++)
            {
                int magnitude = Math.Abs(coefficients[i]);
                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                }
            }

            return FindMsbPositionSingle(maxMagnitude);
        }

        /// <summary>
        /// Finds the most significant bit position for a single value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindMsbPositionSingle(int magnitude)
        {
            if (magnitude == 0)
            {
                return -1;
            }

            // Find position of highest set bit
            int msb = 0;
            while ((magnitude >> (msb + 1)) != 0)
            {
                msb++;
            }

            return msb;
        }

        private void EnsureStateArrays(int width, int height)
        {
            int size = width * height;
            if (_significanceState == null || _significanceState.Length < size)
            {
                _significanceState = new byte[size];
                _signState = new byte[size];
            }
        }

        /// <summary>
        /// Significance Propagation Pass: Code samples that have a significant neighbor.
        /// </summary>
        private void EncodeSignificancePropagationPass(
            ReadOnlySpan<int> coefficients,
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

                    // This sample is in the significance propagation pass
                    int value = coefficients[idx];
                    int magnitude = Math.Abs(value);
                    int bit = (magnitude >> bitplane) & 1;

                    // Encode significance bit using context
                    int context = GetSignificanceContext(x, y, width, height, subbandType);
                    _mqEncoder.Encode(context, bit);

                    if (bit == 1)
                    {
                        // Sample just became significant
                        _significanceState[idx] = 1;

                        // Encode sign
                        int signBit = value < 0 ? 1 : 0;
                        int signContext = GetSignContext(x, y, width, height);
                        _mqEncoder.Encode(signContext, signBit);
                        _signState![idx] = (byte)signBit;
                    }
                }
            }
        }

        /// <summary>
        /// Magnitude Refinement Pass: Refine already-significant coefficients.
        /// </summary>
        private void EncodeMagnitudeRefinementPass(
            ReadOnlySpan<int> coefficients,
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

                    // Skip if this is the first bitplane where it became significant
                    // (handled by significance propagation or cleanup)
                    int value = coefficients[idx];
                    int magnitude = Math.Abs(value);
                    int msbOfSample = FindMsbPositionSingle(magnitude);

                    if (msbOfSample == bitplane)
                    {
                        // This was just made significant at this bitplane
                        continue;
                    }

                    // Encode refinement bit
                    int bit = (magnitude >> bitplane) & 1;
                    int context = GetMagnitudeRefinementContext(x, y, width, height);
                    _mqEncoder.Encode(context, bit);
                }
            }
        }

        /// <summary>
        /// Cleanup Pass: Process remaining samples not handled by previous passes.
        /// </summary>
        private void EncodeCleanupPass(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int bitplane,
            int subbandType)
        {
            // Process in vertical stripes of 4 rows (as per ITU-T T.800)
            for (int stripeY = 0; stripeY < height; stripeY += 4)
            {
                int stripeHeight = Math.Min(4, height - stripeY);

                for (int x = 0; x < width; x++)
                {
                    // Check if we can use run-length coding
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
                        // Try run-length coding
                        EncodeRunLengthMode(coefficients, width, x, stripeY, bitplane, subbandType);
                    }
                    else
                    {
                        // Process each sample in the stripe
                        for (int dy = 0; dy < stripeHeight; dy++)
                        {
                            int y = stripeY + dy;
                            EncodeCleanupSample(coefficients, width, height, x, y, bitplane, subbandType);
                        }
                    }
                }
            }
        }

        private void EncodeRunLengthMode(
            ReadOnlySpan<int> coefficients,
            int width, int x, int stripeY,
            int bitplane,
            int subbandType)
        {
            // Check if all 4 samples are zero at this bitplane
            bool allZero = true;
            int firstNonZeroIdx = -1;

            for (int dy = 0; dy < 4; dy++)
            {
                int y = stripeY + dy;
                int idx = y * width + x;
                int value = coefficients[idx];
                int magnitude = Math.Abs(value);
                int bit = (magnitude >> bitplane) & 1;

                if (bit != 0)
                {
                    allZero = false;
                    if (firstNonZeroIdx < 0)
                    {
                        firstNonZeroIdx = dy;
                    }
                }
            }

            if (allZero)
            {
                // All zeros - encode 0 using run-length context
                _mqEncoder.Encode(CtxRun, 0);
            }
            else
            {
                // Not all zeros - encode 1, then position using uniform context
                _mqEncoder.Encode(CtxRun, 1);

                // Encode position (2 bits for position 0-3)
                _mqEncoder.EncodeUniform((firstNonZeroIdx >> 1) & 1);
                _mqEncoder.EncodeUniform(firstNonZeroIdx & 1);

                // Process samples from the first non-zero one
                for (int dy = firstNonZeroIdx; dy < 4; dy++)
                {
                    int y = stripeY + dy;
                    int idx = y * width + x;

                    if (dy == firstNonZeroIdx)
                    {
                        // This one is significant (just indicated by position)
                        _significanceState![idx] = 1;

                        // Encode sign
                        int value = coefficients[idx];
                        int signBit = value < 0 ? 1 : 0;
                        int signContext = GetSignContext(x, y, width, _currentHeight);
                        _mqEncoder.Encode(signContext, signBit);
                        _signState![idx] = (byte)signBit;
                    }
                    else
                    {
                        // Encode normally
                        EncodeCleanupSample(coefficients, width, _currentHeight, x, y, bitplane, subbandType);
                    }
                }
            }
        }

        private void EncodeCleanupSample(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int x, int y,
            int bitplane,
            int subbandType)
        {
            int idx = y * width + x;

            // Skip if already significant
            if (_significanceState![idx] != 0)
            {
                return;
            }

            // Skip if processed by significance propagation (has significant neighbor)
            if (HasSignificantNeighbor(x, y, width, height))
            {
                return;
            }

            // Encode significance
            int value = coefficients[idx];
            int magnitude = Math.Abs(value);
            int bit = (magnitude >> bitplane) & 1;

            int context = GetSignificanceContext(x, y, width, height, subbandType);
            _mqEncoder.Encode(context, bit);

            if (bit == 1)
            {
                // Sample became significant
                _significanceState[idx] = 1;

                // Encode sign
                int signBit = value < 0 ? 1 : 0;
                int signContext = GetSignContext(x, y, width, height);
                _mqEncoder.Encode(signContext, signBit);
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

        /// <summary>
        /// Gets significance coding context based on neighbor pattern (ITU-T T.800 Table D.1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSignificanceContext(int x, int y, int width, int height, int subbandType)
        {
            // Count significant neighbors in horizontal, vertical, and diagonal directions
            int h = GetNeighborSignificance(x - 1, y, width, height) +
                    GetNeighborSignificance(x + 1, y, width, height);
            int v = GetNeighborSignificance(x, y - 1, width, height) +
                    GetNeighborSignificance(x, y + 1, width, height);
            int d = GetNeighborSignificance(x - 1, y - 1, width, height) +
                    GetNeighborSignificance(x + 1, y - 1, width, height) +
                    GetNeighborSignificance(x - 1, y + 1, width, height) +
                    GetNeighborSignificance(x + 1, y + 1, width, height);

            // Context depends on subband type and neighbor counts
            // Simplified context selection (full table in ITU-T T.800 Table D.1)
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

        /// <summary>
        /// Maps neighbor counts to significance context (ITU-T T.800 Table D.1).
        /// </summary>
        private static int GetSignificanceContextFromCounts(int h, int v, int d, int subbandType)
        {
            // Subband type: 0=LL, 1=HL, 2=LH, 3=HH
            // Context table based on ITU-T T.800 Table D.1
            // Simplified version - maps to context indices 0-8

            if (subbandType == 3) // HH subband
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
            else if (subbandType == 1) // HL subband
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
            else // LL or LH subband
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

        /// <summary>
        /// Gets sign coding context based on neighbor signs (ITU-T T.800 Table D.3).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSignContext(int x, int y, int width, int height)
        {
            // Get horizontal and vertical neighbor contributions
            int hContrib = GetSignContribution(x - 1, y, width, height) +
                          GetSignContribution(x + 1, y, width, height);
            int vContrib = GetSignContribution(x, y - 1, width, height) +
                          GetSignContribution(x, y + 1, width, height);

            // Map to context 9-13 based on contributions
            // Simplified - using base sign context
            return CtxSign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSignContribution(int x, int y, int width, int height)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return 0;
            }

            int idx = y * width + x;
            if (_significanceState![idx] == 0)
            {
                return 0;
            }

            // Return -1 for negative sign, +1 for positive
            return _signState![idx] == 0 ? 1 : -1;
        }

        /// <summary>
        /// Gets magnitude refinement context (ITU-T T.800 Table D.4).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMagnitudeRefinementContext(int x, int y, int width, int height)
        {
            // Simplified: check if any neighbor is significant
            bool hasSignificantNeighbor = HasSignificantNeighbor(x, y, width, height);

            // Context 14-16 based on neighbor status
            return hasSignificantNeighbor ? CtxMag + 1 : CtxMag;
        }
    }
}
