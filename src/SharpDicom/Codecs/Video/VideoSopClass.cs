namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Enumerates the DICOM SOP Classes that support video content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each SOP class corresponds to a specific medical imaging modality and defines
    /// the required metadata, transfer syntax constraints, and IOD modules.
    /// Use <see cref="VideoDicomBuilder"/> to create DICOM files with these SOP classes.
    /// </para>
    /// <para>
    /// Reference: DICOM PS3.4, Annex B - Storage Service Class.
    /// </para>
    /// </remarks>
    public enum VideoSopClass
    {
        /// <summary>
        /// Video Endoscopic Image Storage (1.2.840.10008.5.1.4.1.1.77.1.1.1).
        /// Modality: ES. Used for endoscopy video recordings.
        /// </summary>
        Endoscopic,

        /// <summary>
        /// Video Microscopic Image Storage (1.2.840.10008.5.1.4.1.1.77.1.2.1).
        /// Modality: SM. Used for microscopy video recordings.
        /// </summary>
        Microscopic,

        /// <summary>
        /// Video Photographic Image Storage (1.2.840.10008.5.1.4.1.1.77.1.4.1).
        /// Modality: XC. Used for photographic video recordings.
        /// </summary>
        Photographic,

        /// <summary>
        /// Enhanced XA Image Storage (1.2.840.10008.5.1.4.1.1.12.2.1).
        /// Modality: XA. Used for X-ray angiography video.
        /// </summary>
        EnhancedXA,

        /// <summary>
        /// Enhanced XRF Image Storage (1.2.840.10008.5.1.4.1.1.12.1.1).
        /// Modality: XRF. Used for X-ray fluoroscopy video.
        /// </summary>
        EnhancedXRF,

        /// <summary>
        /// Ultrasound Multi-frame Image Storage (1.2.840.10008.5.1.4.1.1.6.2).
        /// Modality: US. Used for ultrasound cine loops.
        /// </summary>
        USMultiFrame,

        /// <summary>
        /// Secondary Capture Multi-frame True Color Image Storage (1.2.840.10008.5.1.4.1.1.7.4).
        /// Modality: SC. Used for secondary capture video from any modality.
        /// </summary>
        SCMultiFrameTrueColor
    }
}
