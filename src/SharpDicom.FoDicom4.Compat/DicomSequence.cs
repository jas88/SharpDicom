using System;
using System.Collections.Generic;
using System.Linq;

namespace Dicom
{
    /// <summary>
    /// Wrapper for DICOM sequence elements (SQ VR).
    /// Matches fo-dicom 4.x DicomSequence with Items property.
    /// </summary>
    public sealed class DicomSequence : DicomItem
    {
        private readonly SharpDicom.Data.DicomSequence _sequence;
        private IReadOnlyList<DicomDataset>? _cachedItems;

        internal DicomSequence(SharpDicom.Data.DicomSequence inner)
            : base(inner)
        {
            _sequence = inner;
        }

        /// <inheritdoc />
        public override DicomVR ValueRepresentation => DicomVR.SQ;

        /// <summary>
        /// Gets the nested datasets in this sequence.
        /// </summary>
        public IReadOnlyList<DicomDataset> Items
        {
            get
            {
                if (_cachedItems != null)
                    return _cachedItems;

                _cachedItems = _sequence.Items
                    .Select(ds => new DicomDataset(ds))
                    .ToList()
                    .AsReadOnly();
                return _cachedItems;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var name = Tag.DictionaryEntry?.Name ?? "Unknown Tag";
            return $"{Tag} SQ [{Items.Count} item(s)] {name}";
        }
    }
}
