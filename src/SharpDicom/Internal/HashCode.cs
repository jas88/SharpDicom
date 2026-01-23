#if NETSTANDARD2_0
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// Polyfill for HashCode struct for netstandard2.0 compatibility.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal struct HashCode
    {
        private const uint Prime1 = 2654435761U;
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        private uint _v1, _v2, _v3, _v4;
        private uint _queue1, _queue2, _queue3;
        private uint _length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(T value)
        {
            Add(value?.GetHashCode() ?? 0);
        }

        public void AddBytes(ReadOnlySpan<byte> value)
        {
            ref byte pos = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value);
            ref byte end = ref Unsafe.Add(ref pos, value.Length);

            while (Unsafe.IsAddressLessThan(ref pos, ref end))
            {
                Add(pos);
                pos = ref Unsafe.Add(ref pos, 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Add(int value)
        {
            uint val = unchecked((uint)value);
            uint previousLength = _length++;
            uint position = previousLength % 4;

            if (position == 0)
                _queue1 = val;
            else if (position == 1)
                _queue2 = val;
            else if (position == 2)
                _queue3 = val;
            else
            {
                if (previousLength == 3)
                    Initialize(out _v1, out _v2, out _v3, out _v4);

                _v1 = Round(_v1, _queue1);
                _v2 = Round(_v2, _queue2);
                _v3 = Round(_v3, _queue3);
                _v4 = Round(_v4, val);
            }
        }

        public int ToHashCode()
        {
            uint length = _length;
            uint position = length % 4;
            uint hash = length < 4 ? Prime5 : MixState(_v1, _v2, _v3, _v4);

            hash += length * 4;

            if (position > 0)
            {
                hash = QueueRound(hash, _queue1);
                if (position > 1)
                {
                    hash = QueueRound(hash, _queue2);
                    if (position > 2)
                        hash = QueueRound(hash, _queue3);
                }
            }

            hash = MixFinal(hash);
            return unchecked((int)hash);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Initialize(out uint v1, out uint v2, out uint v3, out uint v4)
        {
            unchecked
            {
                v1 = Prime1 + Prime2;
                v2 = Prime2;
                v3 = 0;
                v4 = 0u - Prime1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Round(uint hash, uint input)
        {
            unchecked
            {
                return RotateLeft(hash + input * Prime2, 13) * Prime1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint QueueRound(uint hash, uint queuedValue)
        {
            return RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixState(uint v1, uint v2, uint v3, uint v4)
        {
            return RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateLeft(uint value, int offset)
        {
            return (value << offset) | (value >> (32 - offset));
        }
    }
}
#endif
