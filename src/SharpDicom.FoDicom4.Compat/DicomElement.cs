using System;
using System.Globalization;

namespace Dicom
{
    /// <summary>
    /// Abstract class for value elements (not sequences).
    /// Matches fo-dicom 4.x DicomElement base type with Get&lt;T&gt;.
    /// </summary>
    public abstract class DicomElement : DicomItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomElement"/> class.
        /// </summary>
        protected DicomElement(SharpDicom.Data.IDicomElement inner)
            : base(inner)
        {
        }

        /// <summary>
        /// Gets the number of values (VM) in this element.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Gets a typed value at the specified index.
        /// </summary>
        /// <typeparam name="T">The target type (string, int, double supported).</typeparam>
        /// <param name="index">The value index (0-based).</param>
        /// <returns>The value at the specified index.</returns>
#pragma warning disable CA1716 // Must match fo-dicom API name exactly
        public virtual T Get<T>(int index = 0)
#pragma warning restore CA1716
        {
            var stringVal = GetStringValue(index);

            if (typeof(T) == typeof(string))
                return (T)(object)(stringVal ?? "");

            if (typeof(T) == typeof(int))
            {
                if (stringVal != null && int.TryParse(stringVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                    return (T)(object)intVal;
                return (T)(object)0;
            }

            if (typeof(T) == typeof(double))
            {
                if (stringVal != null && double.TryParse(stringVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
                    return (T)(object)dblVal;
                return (T)(object)0.0;
            }

            if (typeof(T) == typeof(DicomTag))
            {
                return (T)(object)GetTagValue(index);
            }

            throw new DicomDataException($"Cannot convert element value to {typeof(T).Name}");
        }

        /// <summary>
        /// Gets the string representation of the value at the specified index.
        /// Override in subclasses for specialized behavior.
        /// </summary>
        protected virtual string? GetStringValue(int index)
        {
            return null;
        }

        /// <summary>
        /// Gets a DicomTag value at the specified index.
        /// Override in subclasses that hold tag values (AT VR).
        /// </summary>
        protected virtual DicomTag GetTagValue(int index)
        {
            throw new DicomDataException("Element does not contain tag values");
        }
    }
}
