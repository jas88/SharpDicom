using System;
using System.Buffers.Binary;

namespace FellowOakDicom
{
    /// <summary>
    /// Concrete wrapper for AT-VR (Attribute Tag) DICOM elements.
    /// Matches fo-dicom 5.x DicomAttributeTag with Values property.
    /// </summary>
    public sealed class DicomAttributeTag : DicomElement
    {
        private readonly SharpDicom.Data.DicomNumericElement _numericElement;
        private DicomTag[]? _cachedValues;

        internal DicomAttributeTag(SharpDicom.Data.DicomNumericElement inner)
            : base(inner)
        {
            _numericElement = inner;
        }

        /// <inheritdoc />
        public override DicomVR ValueRepresentation => DicomVR.AT;

        /// <summary>
        /// Gets the number of tag values in this element.
        /// Each tag is 4 bytes (2 bytes group + 2 bytes element).
        /// </summary>
        public override int Count => GetTagValues().Length;

        /// <summary>
        /// Gets all tag values from this AT element.
        /// </summary>
        public DicomTag[] Values => GetTagValues();

        /// <inheritdoc />
        protected override string? GetStringValue(int index)
        {
            var tags = GetTagValues();
            if (index < 0 || index >= tags.Length)
                return null;
            return tags[index].ToString();
        }

        /// <inheritdoc />
        protected override DicomTag GetTagValue(int index)
        {
            var tags = GetTagValues();
            if (index < 0 || index >= tags.Length)
                throw new DicomDataException($"Tag value index {index} out of range (count: {tags.Length})");
            return tags[index];
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var tags = GetTagValues();
            return $"{Tag} AT [{string.Join(", ", (object[])tags)}] {Tag.DictionaryEntry.Name}";
        }

        private DicomTag[] GetTagValues()
        {
            if (_cachedValues != null)
                return _cachedValues;

            var raw = _numericElement.RawValue;
            if (raw.IsEmpty || raw.Length % 4 != 0)
            {
                _cachedValues = Array.Empty<DicomTag>();
                return _cachedValues;
            }

            var count = raw.Length / 4;
            var result = new DicomTag[count];
            for (int i = 0; i < count; i++)
            {
                var group = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4));
                var element = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4 + 2));
                result[i] = new DicomTag(group, element);
            }

            _cachedValues = result;
            return _cachedValues;
        }
    }
}
