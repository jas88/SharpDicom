namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// Performed Procedure Step status values per DICOM PS3.3 C.4.14.1.
    /// </summary>
    public enum MppsStatus
    {
        /// <summary>Procedure step is in progress.</summary>
        InProgress,

        /// <summary>Procedure step completed successfully.</summary>
        Completed,

        /// <summary>Procedure step was discontinued.</summary>
        Discontinued
    }
}
