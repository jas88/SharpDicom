namespace SharpDicom.IO
{
    /// <summary>
    /// State machine states for DICOM file parsing.
    /// </summary>
    public enum DicomReaderState
    {
        /// <summary>Initial state, not yet started.</summary>
        Initial,

        /// <summary>Reading 128-byte preamble.</summary>
        Preamble,

        /// <summary>Reading DICM prefix.</summary>
        Prefix,

        /// <summary>Reading File Meta Information (Group 0002).</summary>
        FileMetaInfo,

        /// <summary>Reading dataset elements.</summary>
        Dataset,

        /// <summary>Parsing complete.</summary>
        Complete,

        /// <summary>Error state.</summary>
        Error
    }

    /// <summary>
    /// How to handle the 128-byte preamble and DICM prefix.
    /// </summary>
    public enum FilePreambleHandling
    {
        /// <summary>Require valid preamble and DICM prefix.</summary>
        Require,

        /// <summary>Accept files with or without preamble (auto-detect).</summary>
        Optional,

        /// <summary>Skip preamble detection, assume raw dataset.</summary>
        Ignore
    }

    /// <summary>
    /// How to handle File Meta Information (Group 0002).
    /// </summary>
    public enum FileMetaInfoHandling
    {
        /// <summary>Require valid File Meta Information.</summary>
        Require,

        /// <summary>Use if present, infer if missing.</summary>
        Optional,

        /// <summary>Skip to dataset, assume Implicit VR Little Endian.</summary>
        Ignore
    }

    /// <summary>
    /// How to handle invalid or unknown VRs.
    /// </summary>
    public enum InvalidVRHandling
    {
        /// <summary>Throw DicomDataException.</summary>
        Throw,

        /// <summary>Map to UN and continue.</summary>
        MapToUN,

        /// <summary>Preserve original bytes.</summary>
        Preserve
    }
}
