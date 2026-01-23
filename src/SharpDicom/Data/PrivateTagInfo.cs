namespace SharpDicom.Data
{
    /// <summary>
    /// Information about a private DICOM tag from a vendor dictionary.
    /// </summary>
    /// <param name="Creator">The private creator string.</param>
    /// <param name="ElementOffset">The element offset (0x00-0xFF).</param>
    /// <param name="VR">The value representation.</param>
    /// <param name="Keyword">The keyword (PascalCase identifier).</param>
    /// <param name="Name">The human-readable name.</param>
    /// <param name="IsSafeToRetain">Whether this tag is safe to retain during de-identification.</param>
    public readonly record struct PrivateTagInfo(
        string Creator,
        byte ElementOffset,
        DicomVR VR,
        string Keyword,
        string Name,
        bool IsSafeToRetain);
}
