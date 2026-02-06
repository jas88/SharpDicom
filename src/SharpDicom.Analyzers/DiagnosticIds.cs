namespace SharpDicom.Analyzers;

/// <summary>
/// Diagnostic IDs for SharpDicom migration analyzers.
/// </summary>
/// <remarks>
/// SD0001-SD0003: Detect fo-dicom usage (step 1 migration: fo-dicom to compat layer).
/// SD0010-SD0011: Detect compat layer usage (step 2 migration: compat to native SharpDicom).
/// Severity is configurable via .editorconfig (e.g., dotnet_diagnostic.SD0001.severity = error).
/// </remarks>
internal static class DiagnosticIds
{
    // Step 1: fo-dicom detection (fo-dicom -> compat layer)

    /// <summary>fo-dicom using directive detected (using FellowOakDicom or using Dicom).</summary>
    public const string FoDicomUsingDirective = "SD0001";

    /// <summary>fo-dicom type instantiation detected (new DicomFile, new DicomDataset, etc.).</summary>
    public const string FoDicomTypeInstantiation = "SD0002";

    /// <summary>fo-dicom static method call detected (DicomFile.Open, etc.).</summary>
    public const string FoDicomStaticMethodCall = "SD0003";

    // Step 2: compat layer detection (compat -> native SharpDicom)

    /// <summary>Compat layer using directive detected (using SharpDicom.FoDicom5.Compat.*).</summary>
    public const string CompatUsingDirective = "SD0010";

    /// <summary>Compat layer type usage detected.</summary>
    public const string CompatTypeUsage = "SD0011";
}
