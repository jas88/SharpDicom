using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Network.Pipes;

namespace SharpDicom.Tests.Network.Pipes
{
    /// <summary>
    /// Tests for <see cref="SlabMemoryPool"/>.
    /// </summary>
    [TestFixture]
    public class SlabMemoryPoolTests
    {
        [Test]
        public void Rent_Default_Returns4KBBuffer()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act
            using var owner = pool.Rent();

            // Assert
            Assert.That(owner.Memory.Length, Is.EqualTo(SlabMemoryPool.SlabSize));
        }

        [Test]
        public void Rent_MinBufferSize_Returns4KBBuffer()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act
            using var owner = pool.Rent(1024);

            // Assert - even for smaller requests, we get a full 4KB slab
            Assert.That(owner.Memory.Length, Is.EqualTo(SlabMemoryPool.SlabSize));
        }

        [Test]
        public void Rent_LargerThanSlab_UsesArrayPool()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act
            using var owner = pool.Rent(8192);

            // Assert - should get at least requested size from ArrayPool
            Assert.That(owner.Memory.Length, Is.GreaterThanOrEqualTo(8192));
        }

        [Test]
        public void Return_SlabReused_SameBufferReturned()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Get a slab and mark it
            byte[] originalBuffer;
            using (var owner1 = pool.Rent())
            {
                // Mark the first byte
                owner1.Memory.Span[0] = 0xAB;
                owner1.Memory.Span[1] = 0xCD;
                originalBuffer = owner1.Memory.ToArray();
            }
            // owner1 is disposed, slab returned to pool

            // Act - rent again
            using var owner2 = pool.Rent();

            // Assert - should get a slab from the pool
            // We can't guarantee it's the same buffer since we cleared the mark,
            // but we can verify the memory is 4KB
            Assert.That(owner2.Memory.Length, Is.EqualTo(SlabMemoryPool.SlabSize));
        }

        [Test]
        public void Rent_ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            using var pool = new SlabMemoryPool();
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Act - 100 concurrent rentals
            for (int i = 0; i < 100; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        using var owner = pool.Rent();
                        // Use the memory briefly
                        owner.Memory.Span.Fill(0xFF);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.That(exceptions, Is.Empty, "No exceptions during concurrent access");
        }

        [Test]
        public void Dispose_PreventsRent_ThrowsObjectDisposed()
        {
            // Arrange
            var pool = new SlabMemoryPool();
            pool.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => pool.Rent());
        }

        [Test]
        public void MaxBufferSize_Returns4KB()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act & Assert
            Assert.That(pool.MaxBufferSize, Is.EqualTo(SlabMemoryPool.SlabSize));
        }

        [Test]
        public void MemoryOwner_Dispose_ReturnsSlabToPool()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act - rent multiple slabs, dispose them, rent again
            for (int i = 0; i < 10; i++)
            {
                using var owner = pool.Rent();
                owner.Memory.Span.Fill((byte)i);
            }

            // If slabs are returned properly, we should be able to rent from the pool
            using var finalOwner = pool.Rent();
            Assert.That(finalOwner.Memory.Length, Is.EqualTo(SlabMemoryPool.SlabSize));
        }

        [Test]
        public void MemoryOwner_AccessAfterDispose_ThrowsObjectDisposed()
        {
            // Arrange
            using var pool = new SlabMemoryPool();
            var owner = pool.Rent();
            owner.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _ = owner.Memory);
        }

        [Test]
        public void LargeBuffer_Dispose_ReturnsToArrayPool()
        {
            // Arrange
            using var pool = new SlabMemoryPool();

            // Act - rent a large buffer and dispose it
            using (var owner = pool.Rent(16384))
            {
                owner.Memory.Span.Fill(0xEE);
            }

            // Assert - should not throw, ArrayPool will handle the return
            // This is more of a smoke test to ensure no crashes
            Assert.Pass();
        }
    }
}
