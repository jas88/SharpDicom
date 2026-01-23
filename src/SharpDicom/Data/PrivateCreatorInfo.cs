namespace SharpDicom.Data
{
    /// <summary>
    /// Information about a known private creator.
    /// </summary>
    /// <param name="Creator">The private creator string.</param>
    /// <param name="Vendor">The vendor name (e.g., "Siemens", "GE", "Philips").</param>
    /// <param name="Description">A description of the private creator.</param>
    /// <param name="TagCount">The number of tags defined for this creator.</param>
    public readonly record struct PrivateCreatorInfo(
        string Creator,
        string Vendor,
        string Description,
        int TagCount);
}
