using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.IO
{
    /// <summary>
    /// Adapts a Stream to IBufferWriter&lt;byte&gt; for efficient buffered writing.
    /// </summary>
    /// <remarks>
    /// This class provides buffered writing to any Stream by implementing IBufferWriter.
    /// Data is accumulated in an internal buffer and flushed to the stream when the buffer
    /// fills or when Flush/FlushAsync is called explicitly.
    /// </remarks>
    internal sealed class StreamBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly Stream _stream;
        private byte[] _buffer;
        private int _written;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="StreamBufferWriter"/> class.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="bufferSize">The size of the internal buffer. Default is 81920 (80KB).</param>
        public StreamBufferWriter(Stream stream, int bufferSize = 81920)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (bufferSize < 256)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be at least 256 bytes");

            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            _written = 0;
        }

        /// <summary>
        /// Gets the total number of bytes that have been written to the underlying stream.
        /// </summary>
        public long TotalBytesWritten { get; private set; }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");

            if (_written + count > _buffer.Length)
                throw new InvalidOperationException("Cannot advance past the buffer capacity");

            _written += count;
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        /// <summary>
        /// Flushes the buffer to the underlying stream.
        /// </summary>
        public void Flush()
        {
            if (_written > 0)
            {
                _stream.Write(_buffer, 0, _written);
                TotalBytesWritten += _written;
                _written = 0;
            }
        }

        /// <summary>
        /// Asynchronously flushes the buffer to the underlying stream.
        /// </summary>
        public async ValueTask FlushAsync(CancellationToken ct = default)
        {
            if (_written > 0)
            {
#if NETSTANDARD2_0
                await _stream.WriteAsync(_buffer, 0, _written, ct).ConfigureAwait(false);
#else
                await _stream.WriteAsync(_buffer.AsMemory(0, _written), ct).ConfigureAwait(false);
#endif
                TotalBytesWritten += _written;
                _written = 0;
            }
        }

        /// <summary>
        /// Ensures the buffer has capacity for at least sizeHint bytes.
        /// If the current buffer doesn't have enough space, flushes it first.
        /// </summary>
        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));

            // Default to minimum reasonable size if 0
            if (sizeHint == 0)
                sizeHint = 256;

            int available = _buffer.Length - _written;

            if (available >= sizeHint)
                return;

            // Flush current buffer to stream
            Flush();

            // If sizeHint still exceeds buffer, resize
            if (sizeHint > _buffer.Length)
            {
                var oldBuffer = _buffer;
                _buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
                ArrayPool<byte>.Shared.Return(oldBuffer);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                // Flush any remaining data
                Flush();

                // Return buffer to pool
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = Array.Empty<byte>();
                _disposed = true;
            }
        }
    }
}
