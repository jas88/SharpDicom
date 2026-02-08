using System;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace SharpDicom.Codecs.Simd
{
    /// <summary>
    /// SIMD utility methods for codec hot path optimization.
    /// </summary>
    /// <remarks>
    /// Provides SIMD-accelerated operations with automatic fallback to scalar implementations
    /// when hardware acceleration is unavailable. All methods are AggressiveInlining for
    /// maximum performance in tight loops.
    /// </remarks>
    internal static class SimdHelpers
    {
        /// <summary>
        /// Gets whether Vector128 SIMD operations are hardware-accelerated.
        /// </summary>
        public static bool IsSimdSupported =>
#if NET8_0_OR_GREATER
            Vector128.IsHardwareAccelerated;
#else
            false;
#endif

        /// <summary>
        /// Gets whether Vector256 (AVX2) SIMD operations are hardware-accelerated.
        /// </summary>
        public static bool IsAvx2Supported =>
#if NET8_0_OR_GREATER
            Vector256.IsHardwareAccelerated;
#else
            false;
#endif

        /// <summary>
        /// Gets whether Vector512 (AVX-512) SIMD operations are hardware-accelerated.
        /// </summary>
        public static bool IsAvx512Supported =>
#if NET8_0_OR_GREATER
            Vector512.IsHardwareAccelerated;
#else
            false;
#endif

        /// <summary>
        /// Gets whether BMI2 bit manipulation instructions are supported.
        /// </summary>
        public static bool IsBmi2Supported =>
#if NET8_0_OR_GREATER
            Bmi2.X64.IsSupported || Bmi2.IsSupported;
#else
            false;
#endif

#if NET8_0_OR_GREATER

        // =====================================================================
        // Vector128 operations
        // =====================================================================

        /// <summary>
        /// Computes the horizontal sum of all elements in a Vector128 of integers.
        /// </summary>
        /// <param name="v">The vector to sum.</param>
        /// <returns>The sum of all elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HorizontalSum(Vector128<int> v)
        {
            // Use hardware-specific instructions if available
            if (Ssse3.IsSupported)
            {
                // Horizontal add pairs: [a, b, c, d] -> [a+b, c+d, a+b, c+d]
                var temp = Ssse3.HorizontalAdd(v, v);
                // Horizontal add again: [a+b, c+d, _, _] -> [a+b+c+d, _, _, _]
                temp = Ssse3.HorizontalAdd(temp, temp);
                return temp.GetElement(0);
            }
            else
            {
                // Portable fallback (works on ARM and other platforms)
                return v.GetElement(0) + v.GetElement(1) + v.GetElement(2) + v.GetElement(3);
            }
        }

        /// <summary>
        /// Clamps all elements in a Vector128 of integers to the specified range.
        /// </summary>
        /// <param name="v">The vector to clamp.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>A vector with all elements clamped to [min, max].</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Clamp(Vector128<int> v, int min, int max)
        {
            var minVec = Vector128.Create(min);
            var maxVec = Vector128.Create(max);
            return Vector128.Min(Vector128.Max(v, minVec), maxVec);
        }

        /// <summary>
        /// Computes the absolute value of all elements in a Vector128 of integers.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns>A vector with absolute values of all elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<int> Abs(Vector128<int> v)
        {
            if (Ssse3.IsSupported)
            {
                return Ssse3.Abs(v).AsInt32();
            }
            else
            {
                // Portable fallback: max(v, -v) using Negate
                var negated = Vector128.Negate(v);
                return Vector128.Max(v, negated);
            }
        }

        /// <summary>
        /// Computes the horizontal sum of all elements in a Vector128 of floats.
        /// </summary>
        /// <param name="v">The vector to sum.</param>
        /// <returns>The sum of all elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HorizontalSum(Vector128<float> v)
        {
            if (Sse3.IsSupported)
            {
                // Horizontal add pairs
                var temp = Sse3.HorizontalAdd(v, v);
                temp = Sse3.HorizontalAdd(temp, temp);
                return temp.GetElement(0);
            }
            else
            {
                // Portable fallback (works on ARM and other platforms)
                return v.GetElement(0) + v.GetElement(1) + v.GetElement(2) + v.GetElement(3);
            }
        }

        /// <summary>
        /// Clamps all elements in a Vector128 of floats to the specified range.
        /// </summary>
        /// <param name="v">The vector to clamp.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>A vector with all elements clamped to [min, max].</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> Clamp(Vector128<float> v, float min, float max)
        {
            var minVec = Vector128.Create(min);
            var maxVec = Vector128.Create(max);
            return Vector128.Min(Vector128.Max(v, minVec), maxVec);
        }

        /// <summary>
        /// Computes the absolute value of all elements in a Vector128 of floats.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns>A vector with absolute values of all elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> Abs(Vector128<float> v)
        {
            // Create mask with sign bit clear (0x7FFFFFFF for each element)
            var mask = Vector128.Create(0x7FFFFFFF);
            var vAsInt = v.AsInt32();
            var resultAsInt = Vector128.BitwiseAnd(vAsInt, mask);
            return resultAsInt.AsSingle();
        }

        // =====================================================================
        // Vector256 operations
        // =====================================================================

        /// <summary>
        /// Computes the horizontal sum of all elements in a Vector256 of integers.
        /// </summary>
        /// <param name="v">The vector to sum.</param>
        /// <returns>The sum of all 8 elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HorizontalSum(Vector256<int> v)
        {
            // Split into two 128-bit halves and sum them
            var lo = v.GetLower();
            var hi = v.GetUpper();
            var sum128 = lo + hi;
            return HorizontalSum(sum128);
        }

        /// <summary>
        /// Computes the horizontal sum of all elements in a Vector256 of floats.
        /// </summary>
        /// <param name="v">The vector to sum.</param>
        /// <returns>The sum of all 8 elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HorizontalSum(Vector256<float> v)
        {
            // Split into two 128-bit halves and sum them
            var lo = v.GetLower();
            var hi = v.GetUpper();
            var sum128 = lo + hi;
            return HorizontalSum(sum128);
        }

        /// <summary>
        /// Clamps all elements in a Vector256 of integers to the specified range.
        /// </summary>
        /// <param name="v">The vector to clamp.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="max">The maximum value.</param>
        /// <returns>A vector with all elements clamped to [min, max].</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Clamp(Vector256<int> v, int min, int max)
        {
            var minVec = Vector256.Create(min);
            var maxVec = Vector256.Create(max);
            return Vector256.Min(Vector256.Max(v, minVec), maxVec);
        }

        /// <summary>
        /// Computes the absolute value of all elements in a Vector256 of integers.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns>A vector with absolute values of all elements.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<int> Abs(Vector256<int> v)
        {
            // Use portable Vector256 operations
            var negated = Vector256.Negate(v);
            return Vector256.Max(v, negated);
        }
#endif

        // =====================================================================
        // Bit manipulation operations (scalar with BMI2 acceleration)
        // =====================================================================

        /// <summary>
        /// Extracts bits from a source value using a bit mask (PEXT operation).
        /// For each set bit in the mask, the corresponding bit from the source is
        /// extracted and packed into contiguous low bits of the result.
        /// </summary>
        /// <param name="source">The source value to extract bits from.</param>
        /// <param name="mask">The bit mask selecting which bits to extract.</param>
        /// <returns>The extracted bits packed into contiguous low bits.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ExtractBits(ulong source, ulong mask)
        {
#if NET8_0_OR_GREATER
            if (Bmi2.X64.IsSupported)
            {
                return Bmi2.X64.ParallelBitExtract(source, mask);
            }
#endif
            return ExtractBitsScalar(source, mask);
        }

        /// <summary>
        /// Deposits bits from a source value into positions selected by a bit mask (PDEP operation).
        /// The contiguous low bits of the source are spread to the positions of set bits in the mask.
        /// </summary>
        /// <param name="source">The source value whose low bits are deposited.</param>
        /// <param name="mask">The bit mask selecting target positions.</param>
        /// <returns>The deposited bits at the mask positions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong DepositBits(ulong source, ulong mask)
        {
#if NET8_0_OR_GREATER
            if (Bmi2.X64.IsSupported)
            {
                return Bmi2.X64.ParallelBitDeposit(source, mask);
            }
#endif
            return DepositBitsScalar(source, mask);
        }

        /// <summary>
        /// Counts the number of leading zero bits in a 32-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The count of leading zeros (0-32).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.LeadingZeroCount(value);
#else
            return LeadingZeroCountScalar(value);
#endif
        }

        /// <summary>
        /// Counts the number of set bits (population count) in a 32-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>The number of set bits (0-32).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.PopCount(value);
#else
            return PopCountScalar(value);
#endif
        }

        // =====================================================================
        // Scalar fallbacks
        // =====================================================================

        /// <summary>
        /// Scalar fallback for PEXT (Parallel Bit Extract).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ExtractBitsScalar(ulong source, ulong mask)
        {
            ulong result = 0;
            int destBit = 0;

            while (mask != 0)
            {
                // Find lowest set bit in mask
                ulong lsb = mask & (~mask + 1);

                // Get the bit position of lsb
                if ((source & lsb) != 0)
                {
                    result |= 1UL << destBit;
                }

                destBit++;
                mask &= mask - 1; // Clear lowest set bit
            }

            return result;
        }

        /// <summary>
        /// Scalar fallback for PDEP (Parallel Bit Deposit).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong DepositBitsScalar(ulong source, ulong mask)
        {
            ulong result = 0;
            int srcBit = 0;

            while (mask != 0)
            {
                // Find lowest set bit in mask
                ulong lsb = mask & (~mask + 1);

                // If source bit is set, set the corresponding mask position
                if ((source & (1UL << srcBit)) != 0)
                {
                    result |= lsb;
                }

                srcBit++;
                mask &= mask - 1; // Clear lowest set bit
            }

            return result;
        }

#if !NET8_0_OR_GREATER
        /// <summary>
        /// Scalar fallback for LeadingZeroCount on netstandard2.0.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LeadingZeroCountScalar(uint value)
        {
            if (value == 0)
            {
                return 32;
            }

            int count = 0;
            if ((value & 0xFFFF0000u) == 0) { count += 16; value <<= 16; }
            if ((value & 0xFF000000u) == 0) { count += 8; value <<= 8; }
            if ((value & 0xF0000000u) == 0) { count += 4; value <<= 4; }
            if ((value & 0xC0000000u) == 0) { count += 2; value <<= 2; }
            if ((value & 0x80000000u) == 0) { count += 1; }
            return count;
        }

        /// <summary>
        /// Scalar fallback for PopCount on netstandard2.0.
        /// Uses the standard divide-and-conquer parallel bit count algorithm.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopCountScalar(uint value)
        {
            value -= (value >> 1) & 0x55555555u;
            value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
            value = (value + (value >> 4)) & 0x0F0F0F0Fu;
            return (int)((value * 0x01010101u) >> 24);
        }
#endif
    }
}
