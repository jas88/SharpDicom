using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FellowOakDicom
{
    /// <summary>
    /// Compatibility wrapper for DICOM dataset.
    /// Matches fo-dicom 5.x DicomDataset API surface.
    /// </summary>
    public sealed class DicomDataset : IEnumerable<DicomItem>
    {
        private readonly SharpDicom.Data.DicomDataset _inner;

        /// <summary>
        /// Creates a new empty dataset.
        /// </summary>
        public DicomDataset()
        {
            _inner = new SharpDicom.Data.DicomDataset();
        }

        /// <summary>
        /// Wraps an existing SharpDicom dataset.
        /// </summary>
        internal DicomDataset(SharpDicom.Data.DicomDataset inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// Unwraps to the underlying SharpDicom dataset.
        /// </summary>
        public SharpDicom.Data.DicomDataset Unwrap() => _inner;

        /// <summary>
        /// Enumerates all elements as compat DicomItem wrappers.
        /// </summary>
        public IEnumerator<DicomItem> GetEnumerator()
        {
            foreach (var element in _inner)
            {
                yield return DicomItem.Wrap(element);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Checks whether the dataset contains the specified tag.
        /// </summary>
        public bool Contains(DicomTag tag) => _inner.Contains(tag.ToSharpDicom());

        /// <summary>
        /// Removes the element with the specified tag.
        /// </summary>
        public bool Remove(DicomTag tag) => _inner.Remove(tag.ToSharpDicom());

        /// <summary>
        /// Gets a single typed value for the specified tag.
        /// </summary>
        /// <typeparam name="T">The target type (string, int, double, DicomUID).</typeparam>
        /// <param name="tag">The tag to retrieve.</param>
        /// <returns>The value.</returns>
        public T GetSingleValue<T>(DicomTag tag)
        {
            var sdTag = tag.ToSharpDicom();

            if (typeof(T) == typeof(string))
            {
                var str = _inner.GetString(sdTag) ?? "";
                return (T)(object)str;
            }

            if (typeof(T) == typeof(int))
            {
                var val = _inner.GetInt32(sdTag) ?? 0;
                return (T)(object)val;
            }

            if (typeof(T) == typeof(double))
            {
                var val = _inner.GetFloat64(sdTag) ?? 0.0;
                return (T)(object)val;
            }

            if (typeof(T) == typeof(DicomUID))
            {
                var str = _inner.GetString(sdTag)?.TrimEnd();
                return (T)(object)(str != null ? new DicomUID(str) : new DicomUID(""));
            }

            throw new DicomDataException($"Cannot convert tag value to {typeof(T).Name}");
        }

        /// <summary>
        /// Gets a typed value at the specified index.
        /// </summary>
        public T GetValue<T>(DicomTag tag, int index)
        {
            var sdTag = tag.ToSharpDicom();

            if (typeof(T) == typeof(string))
            {
                var values = _inner.GetStrings(sdTag);
                if (values != null && index >= 0 && index < values.Length)
                    return (T)(object)values[index];
                return (T)(object)"";
            }

            // For non-string types, parse from string values
            var strings = _inner.GetStrings(sdTag);
            if (strings != null && index >= 0 && index < strings.Length)
            {
                if (typeof(T) == typeof(int) && int.TryParse(strings[index],
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intVal))
                    return (T)(object)intVal;
                if (typeof(T) == typeof(double) && double.TryParse(strings[index],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
                    return (T)(object)dblVal;
            }

            throw new DicomDataException($"Cannot get value at index {index} for tag {tag}");
        }

        /// <summary>
        /// Gets all values for a tag as a typed array.
        /// </summary>
        public T[] GetValues<T>(DicomTag tag)
        {
            var sdTag = tag.ToSharpDicom();

            if (typeof(T) == typeof(string))
            {
                var values = _inner.GetStrings(sdTag);
                return (T[])(object)(values ?? Array.Empty<string>());
            }

            // For other types, convert from string values
            var strings = _inner.GetStrings(sdTag);
            if (strings == null)
                return Array.Empty<T>();

            if (typeof(T) == typeof(int))
            {
                var result = new int[strings.Length];
                for (int i = 0; i < strings.Length; i++)
                {
                    int.TryParse(strings[i], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out result[i]);
                }
                return (T[])(object)result;
            }

            if (typeof(T) == typeof(double))
            {
                var result = new double[strings.Length];
                for (int i = 0; i < strings.Length; i++)
                {
                    double.TryParse(strings[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out result[i]);
                }
                return (T[])(object)result;
            }

            throw new DicomDataException($"Cannot get values as {typeof(T).Name}[] for tag {tag}");
        }

        /// <summary>
        /// Shortcut to get a string value for the specified tag.
        /// </summary>
        public string? GetString(DicomTag tag)
        {
            return _inner.GetString(tag.ToSharpDicom());
        }

        /// <summary>
        /// Tries to get a single typed value. Returns false if the tag is missing.
        /// </summary>
        public bool TryGetSingleValue<T>(DicomTag tag, out T value)
        {
            var sdTag = tag.ToSharpDicom();
            if (!_inner.Contains(sdTag))
            {
                value = default!;
                return false;
            }

            try
            {
                value = GetSingleValue<T>(tag);
                return true;
            }
            catch
            {
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// Tries to get a sequence for the specified tag.
        /// </summary>
        public bool TryGetSequence(DicomTag tag, out DicomSequence sequence)
        {
            var sdTag = tag.ToSharpDicom();
            var sdSeq = _inner.GetSequence(sdTag);
            if (sdSeq != null)
            {
                sequence = new DicomSequence(sdSeq);
                return true;
            }
            sequence = null!;
            return false;
        }

        /// <summary>
        /// Gets a sequence for the specified tag. Throws if missing.
        /// </summary>
        public DicomSequence GetSequence(DicomTag tag)
        {
            if (TryGetSequence(tag, out var seq))
                return seq;
            throw new DicomDataException($"Sequence not found for tag {tag}");
        }

        /// <summary>
        /// Adds or updates a string element in the dataset.
        /// </summary>
        public DicomDataset AddOrUpdate(DicomTag tag, params string[] values)
        {
            var sdTag = tag.ToSharpDicom();
            var joined = string.Join("\\", values);
            var bytes = System.Text.Encoding.ASCII.GetBytes(joined);

            // Pad to even length per DICOM spec
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                // UI VR pads with null, others with space
                var entry = SharpDicom.Data.DicomDictionary.Default.GetEntry(sdTag);
                var vr = entry?.DefaultVR ?? SharpDicom.Data.DicomVR.LO;
                padded[padded.Length - 1] = vr == SharpDicom.Data.DicomVR.UI ? (byte)0 : (byte)' ';
                bytes = padded;
            }

            var dictEntry = SharpDicom.Data.DicomDictionary.Default.GetEntry(sdTag);
            var elementVr = dictEntry?.DefaultVR ?? SharpDicom.Data.DicomVR.LO;
            _inner.AddOrUpdate(new SharpDicom.Data.DicomStringElement(sdTag, elementVr, bytes));
            return this;
        }

        /// <summary>
        /// Adds or updates a DicomItem in the dataset.
        /// </summary>
        public DicomDataset AddOrUpdate(DicomItem item)
        {
            _inner.AddOrUpdate(item.Inner);
            return this;
        }
    }
}
