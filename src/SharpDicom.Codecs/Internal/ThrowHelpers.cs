using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Internal
{
    /// <summary>
    /// Polyfills for ArgumentNullException.ThrowIfNull and related throw helpers
    /// that are not available in netstandard2.0.
    /// </summary>
    internal static class ThrowHelpers
    {
#if NET6_0_OR_GREATER
        /// <summary>
        /// Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNull(
            [System.Diagnostics.CodeAnalysis.NotNull] object? argument,
            [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            ArgumentNullException.ThrowIfNull(argument, paramName);
        }

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is negative.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
#else
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
            }
#endif
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if <paramref name="condition"/> is true.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfDisposed(
            [System.Diagnostics.CodeAnalysis.DoesNotReturnIf(true)] bool condition,
            object instance)
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(condition, instance);
#else
            if (condition)
            {
                throw new ObjectDisposedException(instance.GetType().FullName);
            }
#endif
        }
#else
        /// <summary>
        /// Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNull(object? argument, string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/> if <paramref name="value"/> is negative.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNegative(int value, string? paramName = null)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
            }
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if <paramref name="condition"/> is true.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfDisposed(bool condition, object instance)
        {
            if (condition)
            {
                throw new ObjectDisposedException(instance.GetType().FullName);
            }
        }
#endif
    }
}
