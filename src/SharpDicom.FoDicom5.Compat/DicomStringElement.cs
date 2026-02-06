using System;

namespace FellowOakDicom
{
    /// <summary>
    /// Concrete wrapper for string-VR DICOM elements.
    /// Matches fo-dicom 5.x DicomStringElement with Get&lt;string&gt;(index) support.
    /// </summary>
    public sealed class DicomStringElement : DicomElement
    {
        private readonly SharpDicom.Data.DicomStringElement _stringElement;
        private string[]? _cachedValues;

        internal DicomStringElement(SharpDicom.Data.DicomStringElement inner)
            : base(inner)
        {
            _stringElement = inner;
        }

        /// <inheritdoc />
        public override DicomVR ValueRepresentation => DicomVR.FromSharpDicom(_stringElement.VR);

        /// <summary>
        /// Gets the number of backslash-separated values.
        /// </summary>
        public override int Count
        {
            get
            {
                var values = GetValues();
                return values?.Length ?? 0;
            }
        }

        /// <inheritdoc />
        protected override string? GetStringValue(int index)
        {
            var values = GetValues();
            if (values == null || index < 0 || index >= values.Length)
                return null;
            return values[index];
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var str = _stringElement.GetString();
            var name = Tag.DictionaryEntry?.Name ?? "Unknown Tag";
            return $"{Tag} {ValueRepresentation} [{str}] {name}";
        }

        private string[]? GetValues()
        {
            if (_cachedValues != null)
                return _cachedValues;

            _cachedValues = _stringElement.GetStrings();
            return _cachedValues;
        }
    }
}
