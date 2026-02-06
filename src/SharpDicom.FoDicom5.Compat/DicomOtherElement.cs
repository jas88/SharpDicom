namespace FellowOakDicom
{
    /// <summary>
    /// Fallback wrapper for DICOM elements not matching string or AT specializations.
    /// Covers binary elements (OB, OW, etc.) and numeric elements.
    /// </summary>
    public sealed class DicomOtherElement : DicomElement
    {
        internal DicomOtherElement(SharpDicom.Data.IDicomElement inner)
            : base(inner)
        {
        }

        /// <inheritdoc />
        public override DicomVR ValueRepresentation => DicomVR.FromSharpDicom(Inner.VR);

        /// <summary>
        /// Gets the count of values. For binary elements, returns 1 if non-empty, 0 if empty.
        /// </summary>
        public override int Count => Inner.IsEmpty ? 0 : 1;

        /// <inheritdoc />
        protected override string? GetStringValue(int index)
        {
            if (index != 0 || Inner.IsEmpty)
                return null;

            // For numeric elements, try to get a useful string representation
            if (Inner is SharpDicom.Data.DicomNumericElement ne)
            {
                var vr = ne.VR;
                if (vr == SharpDicom.Data.DicomVR.US)
                    return ne.GetUInt16()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (vr == SharpDicom.Data.DicomVR.SS)
                    return ne.GetInt16()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (vr == SharpDicom.Data.DicomVR.UL)
                    return ne.GetUInt32()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (vr == SharpDicom.Data.DicomVR.SL)
                    return ne.GetInt32()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (vr == SharpDicom.Data.DicomVR.FL)
                    return ne.GetFloat32()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (vr == SharpDicom.Data.DicomVR.FD)
                    return ne.GetFloat64()?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return "[binary data]";
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var length = Inner.Length;
            return $"{Tag} {ValueRepresentation} [{length} bytes] {Tag.DictionaryEntry.Name}";
        }
    }
}
