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

#if NET8_0_OR_GREATER
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
#endif
    }
}
