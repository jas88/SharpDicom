using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// Thread-safe in-memory implementation of <see cref="IMppsPersistence"/>.
    /// </summary>
    /// <remarks>
    /// Stores MPPS instances in a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// Suitable for testing, development, and simple single-process deployments.
    /// </remarks>
    public sealed class InMemoryMppsPersistence : IMppsPersistence
    {
        private readonly ConcurrentDictionary<string, MppsInstance> _instances = new();

        /// <summary>
        /// Gets the number of stored MPPS instances.
        /// </summary>
        public int Count => _instances.Count;

        /// <inheritdoc />
        public ValueTask<MppsInstance?> GetAsync(DicomUID sopInstanceUid, CancellationToken ct)
        {
            var key = sopInstanceUid.ToString();
            _instances.TryGetValue(key, out var instance);
            return new ValueTask<MppsInstance?>(instance);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">Thrown if an instance with the same SOP Instance UID already exists.</exception>
        public ValueTask PutAsync(MppsInstance instance, CancellationToken ct)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(instance);
#else
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
#endif

            var key = instance.SOPInstanceUID.ToString();
            if (!_instances.TryAdd(key, instance))
            {
                throw new InvalidOperationException(
                    $"MPPS instance with SOP Instance UID '{key}' already exists.");
            }

            return default;
        }

        /// <inheritdoc />
        /// <exception cref="KeyNotFoundException">Thrown if no instance with the specified UID exists.</exception>
        public ValueTask UpdateAsync(DicomUID sopInstanceUid, DicomDataset modificationList, CancellationToken ct)
        {
            var key = sopInstanceUid.ToString();
            if (!_instances.TryGetValue(key, out var instance))
            {
                throw new KeyNotFoundException(
                    $"MPPS instance with SOP Instance UID '{key}' not found.");
            }

            lock (instance)
            {
                instance.ApplyModification(modificationList);
            }
            return default;
        }
    }
}
