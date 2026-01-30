#if NETSTANDARD2_0
// Polyfill ArrayBufferWriter for netstandard2.0
// This is a minimal implementation sufficient for PDU building

using System;
using System.Buffers;

namespace SharpDicom.Internal
{
    /// <summary>
    /// Represents a heap-based, array-backed output sink for <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// This is a polyfill for netstandard2.0 which doesn't have ArrayBufferWriter.
    /// It provides the same API as the .NET Core version.
    /// We use SharpDicom.Internal namespace to avoid conflicts with System.Buffers.ArrayBufferWriter
    /// when the test project references both.
    /// </remarks>
    /// <typeparam name="T">The type of the elements in the buffer.</typeparam>
    internal sealed class ArrayBufferWriterPolyfill<T> : IBufferWriter<T>
    {
        private T[] _buffer;
        private int _index;

        private const int DefaultInitialBufferSize = 256;
        private const int MaxArrayLength = 0x7FFFFFC7; // Same as Array.MaxLength

        /// <summary>
        /// Initializes a new instance of the <see cref="ArrayBufferWriterPolyfill{T}"/> class.
        /// </summary>
        public ArrayBufferWriterPolyfill()
        {
            _buffer = Array.Empty<T>();
            _index = 0;
        }

        /// <summary>
        /// Initializes a new instance with the specified initial capacity.
        /// </summary>
        /// <param name="initialCapacity">The minimum capacity to allocate.</param>
        public ArrayBufferWriterPolyfill(int initialCapacity)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _buffer = new T[initialCapacity];
            _index = 0;
        }

        /// <summary>
        /// Gets the total number of elements written to the buffer.
        /// </summary>
        public int WrittenCount => _index;

        /// <summary>
        /// Gets the amount of available space in the buffer.
        /// </summary>
        public int FreeCapacity => _buffer.Length - _index;

        /// <summary>
        /// Gets the total capacity of the underlying buffer.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// Gets a <see cref="ReadOnlyMemory{T}"/> with the data written so far.
        /// </summary>
        public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> with the data written so far.
        /// </summary>
        public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);

        /// <summary>
        /// Clears the data written to the buffer.
        /// </summary>
        public void Clear()
        {
            _buffer.AsSpan(0, _index).Clear();
            _index = 0;
        }

        /// <summary>
        /// Clears the data written to the buffer without zeroing the memory.
        /// </summary>
        public void ResetWrittenCount()
        {
            _index = 0;
        }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (_index > _buffer.Length - count)
                throw new InvalidOperationException("Cannot advance past the end of the buffer");

            _index += count;
        }

        /// <inheritdoc />
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_index);
        }

        /// <inheritdoc />
        public Span<T> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_index);
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));

            if (sizeHint == 0)
            {
                sizeHint = 1;
            }

            if (sizeHint > FreeCapacity)
            {
                int currentLength = _buffer.Length;

                // Attempt to grow by the larger of the sizeHint or double the current size
                int growBy = Math.Max(sizeHint, currentLength);

                if (currentLength == 0)
                {
                    growBy = Math.Max(growBy, DefaultInitialBufferSize);
                }

                // Check for overflow before computing newSize
                int newSize;
                if (currentLength > MaxArrayLength - growBy)
                {
                    // Would overflow or exceed max - cap at MaxArrayLength if minimum requirement fits
                    if (currentLength > MaxArrayLength - sizeHint)
                    {
                        throw new InvalidOperationException($"Cannot allocate buffer larger than {MaxArrayLength}.");
                    }
                    newSize = MaxArrayLength;
                }
                else
                {
                    newSize = currentLength + growBy;
                    // Cap at MaxArrayLength
                    if (newSize > MaxArrayLength)
                    {
                        newSize = MaxArrayLength;
                    }
                }

                Array.Resize(ref _buffer, newSize);
            }
        }
    }
}
#endif
