using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace SharpDicom.Network.Pipes
{
    /// <summary>
    /// Custom memory pool using fixed 4KB slabs to avoid LOH pressure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Based on Kestrel's SlabMemoryPool pattern. Uses fixed-size 4KB slabs
    /// that stay below the LOH threshold (85KB). Slabs are pooled and reused
    /// to minimize GC pressure during high-throughput network I/O.
    /// </para>
    /// <para>
    /// Thread-safe for concurrent Rent/Return operations.
    /// </para>
    /// </remarks>
    internal sealed class SlabMemoryPool : MemoryPool<byte>
    {
        /// <summary>
        /// Size of each slab in bytes. 4KB stays well below the LOH threshold.
        /// </summary>
        internal const int SlabSize = 4096;

        /// <summary>
        /// Maximum number of slabs to keep pooled (4MB total).
        /// </summary>
        internal const int MaxSlabs = 1024;

        private readonly ConcurrentQueue<byte[]> _slabs = new ConcurrentQueue<byte[]>();
        private int _slabCount;
        private bool _disposed;

        /// <inheritdoc/>
        public override int MaxBufferSize => SlabSize;

        /// <inheritdoc/>
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
                throw new ObjectDisposedException(nameof(SlabMemoryPool));
#endif

            // For buffers larger than slab size, fall back to ArrayPool
            if (minBufferSize > SlabSize)
            {
                var largeBuffer = ArrayPool<byte>.Shared.Rent(minBufferSize);
                return new ArrayPoolMemoryOwner(largeBuffer, minBufferSize);
            }

            // Try to get a slab from the pool
            if (_slabs.TryDequeue(out var slab))
            {
                return new SlabMemoryOwner(this, slab);
            }

            // Allocate new slab (with limit)
            var count = Interlocked.Increment(ref _slabCount);
            if (count <= MaxSlabs)
            {
                return new SlabMemoryOwner(this, new byte[SlabSize]);
            }

            // Over limit - decrement and use ArrayPool fallback
            Interlocked.Decrement(ref _slabCount);
            var fallback = ArrayPool<byte>.Shared.Rent(SlabSize);
            return new ArrayPoolMemoryOwner(fallback, SlabSize);
        }

        /// <summary>
        /// Returns a slab to the pool for reuse.
        /// </summary>
        /// <param name="slab">The slab to return.</param>
        private void Return(byte[] slab)
        {
            if (!_disposed && _slabs.Count < MaxSlabs)
            {
                _slabs.Enqueue(slab);
            }
            // If disposed or over limit, let the slab be collected by GC
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            // Clear the pool - slabs will be garbage collected
            while (_slabs.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Memory owner for slabs that returns them to the pool on dispose.
        /// </summary>
        private sealed class SlabMemoryOwner : IMemoryOwner<byte>
        {
            private readonly SlabMemoryPool _pool;
            private byte[]? _slab;

            public SlabMemoryOwner(SlabMemoryPool pool, byte[] slab)
            {
                _pool = pool;
                _slab = slab;
            }

            public Memory<byte> Memory => _slab ?? throw new ObjectDisposedException(nameof(SlabMemoryOwner));

            public void Dispose()
            {
                var slab = Interlocked.Exchange(ref _slab, null);
                if (slab != null)
                {
                    _pool.Return(slab);
                }
            }
        }

        /// <summary>
        /// Memory owner for oversized buffers that returns them to ArrayPool.Shared.
        /// </summary>
        private sealed class ArrayPoolMemoryOwner : IMemoryOwner<byte>
        {
            private byte[]? _buffer;
            private readonly int _length;

            public ArrayPoolMemoryOwner(byte[] buffer, int length)
            {
                _buffer = buffer;
                _length = length;
            }

            public Memory<byte> Memory
            {
                get
                {
                    var buffer = _buffer ?? throw new ObjectDisposedException(nameof(ArrayPoolMemoryOwner));
                    return buffer.AsMemory(0, _length);
                }
            }

            public void Dispose()
            {
                var buffer = Interlocked.Exchange(ref _buffer, null);
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}
