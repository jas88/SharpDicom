namespace FellowOakDicom
{
    /// <summary>
    /// Extension methods for unwrapping compat types to native SharpDicom types.
    /// Enables gradual migration from compat layer to native SharpDicom.
    /// </summary>
    public static class Compatibility
    {
        /// <summary>
        /// Unwraps a compat DicomFile to the native SharpDicom DicomFile.
        /// </summary>
        public static SharpDicom.DicomFile Unwrap(this DicomFile file) => file.Unwrap();

        /// <summary>
        /// Unwraps a compat DicomDataset to the native SharpDicom DicomDataset.
        /// </summary>
        public static SharpDicom.Data.DicomDataset Unwrap(this DicomDataset dataset) => dataset.Unwrap();

        /// <summary>
        /// Unwraps a compat DicomTag to the native SharpDicom DicomTag.
        /// </summary>
        public static SharpDicom.Data.DicomTag Unwrap(this DicomTag tag) => tag.ToSharpDicom();

        /// <summary>
        /// Unwraps a compat DicomSequence to the native SharpDicom DicomSequence.
        /// </summary>
        public static SharpDicom.Data.DicomSequence Unwrap(this DicomSequence sequence) =>
            (SharpDicom.Data.DicomSequence)sequence.Inner;
    }
}
