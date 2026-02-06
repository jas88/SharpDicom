namespace FellowOakDicom.Network
{
    /// <summary>
    /// Query/Retrieve level matching fo-dicom 5.x DicomQueryRetrieveLevel enum.
    /// </summary>
    public enum DicomQueryRetrieveLevel
    {
        /// <summary>Patient level.</summary>
        Patient,

        /// <summary>Study level.</summary>
        Study,

        /// <summary>Series level.</summary>
        Series,

        /// <summary>Image (instance) level. fo-dicom calls this "Image".</summary>
        Image
    }

    /// <summary>
    /// Extension methods for converting between compat and SharpDicom query levels.
    /// </summary>
    internal static class DicomQueryRetrieveLevelExtensions
    {
        /// <summary>
        /// Converts a compat query/retrieve level to the SharpDicom equivalent.
        /// </summary>
        internal static SharpDicom.Network.Dimse.QueryRetrieveLevel ToSharpDicom(
            this DicomQueryRetrieveLevel level)
        {
            return level switch
            {
                DicomQueryRetrieveLevel.Patient => SharpDicom.Network.Dimse.QueryRetrieveLevel.Patient,
                DicomQueryRetrieveLevel.Study => SharpDicom.Network.Dimse.QueryRetrieveLevel.Study,
                DicomQueryRetrieveLevel.Series => SharpDicom.Network.Dimse.QueryRetrieveLevel.Series,
                DicomQueryRetrieveLevel.Image => SharpDicom.Network.Dimse.QueryRetrieveLevel.Image,
                _ => throw new System.ArgumentOutOfRangeException(nameof(level), level, "Invalid DicomQueryRetrieveLevel value.")
            };
        }
    }
}
