using System;

namespace SharpDicom.Deidentification;

/// <summary>
/// De-identification profiles per DICOM PS3.15 Sections E.2-E.3.
/// </summary>
/// <remarks>
/// <para>
/// The Basic Application Level Confidentiality Profile is the mandatory base profile
/// that removes or modifies attributes containing patient-identifying information.
/// </para>
/// <para>
/// Option profiles can be combined with the Basic profile using bitwise OR to
/// selectively retain or clean specific categories of information. For example:
/// <c>DeidentificationProfile.Basic | DeidentificationProfile.RetainPatientCharacteristics</c>
/// </para>
/// </remarks>
[Flags]
public enum DeidentificationProfile : uint
{
    /// <summary>
    /// No profile - all tags kept unchanged.
    /// This is NOT recommended for de-identification; use Basic as minimum.
    /// </summary>
    None = 0,

    /// <summary>
    /// Basic Application Level Confidentiality Profile (mandatory base).
    /// Removes or modifies all attributes identified as containing or potentially
    /// containing patient-identifying information (PS3.15 Table E.1-1).
    /// </summary>
    Basic = 1 << 0,

    /// <summary>
    /// Retain Safe Private Option (PS3.15 Annex E, Option).
    /// Retains private data elements that have been reviewed and determined
    /// to not contain patient-identifying information. Requires explicit
    /// identification of safe private creators in DeidentificationOptions.
    /// </summary>
    RetainSafePrivate = 1 << 1,

    /// <summary>
    /// Retain UIDs Option (PS3.15 Annex E, Option).
    /// Keeps original SOP Instance UIDs, Study Instance UIDs, Series Instance UIDs,
    /// and other UIDs unchanged. Use only when UIDs are not considered identifying.
    /// </summary>
    RetainUIDs = 1 << 2,

    /// <summary>
    /// Retain Device Identity Option (PS3.15 Annex E, Option).
    /// Retains device-related identifying information such as Station Name,
    /// Device Serial Number, and Institutional Department Name.
    /// </summary>
    RetainDeviceIdentity = 1 << 3,

    /// <summary>
    /// Retain Institution Identity Option (PS3.15 Annex E, Option).
    /// Retains institution-related identifying information such as Institution Name,
    /// Institution Address, and Institutional Department Name.
    /// </summary>
    RetainInstitutionIdentity = 1 << 11,

    /// <summary>
    /// Retain Patient Characteristics Option (PS3.15 Annex E, Option).
    /// Retains physical characteristics (height, weight) and demographics
    /// (age, sex, ethnic group) that may be needed for research purposes.
    /// </summary>
    RetainPatientCharacteristics = 1 << 4,

    /// <summary>
    /// Retain Longitudinal Temporal Information with Modified Dates Option
    /// (PS3.15 Annex E, Option).
    /// Retains dates and times with consistent date shifting applied per patient
    /// or study to preserve temporal relationships while obscuring actual dates.
    /// </summary>
    RetainLongitudinalModifiedDates = 1 << 5,

    /// <summary>
    /// Retain Longitudinal Temporal Information with Full Dates Option
    /// (PS3.15 Annex E, Option).
    /// Keeps original dates and times unchanged. Use with extreme caution as
    /// dates can be strongly identifying when combined with other information.
    /// </summary>
    RetainLongitudinalFullDates = 1 << 6,

    /// <summary>
    /// Clean Descriptors Option (PS3.15 Annex E, Option).
    /// Processes text descriptor attributes (Study Description, Series Description,
    /// Protocol Name, etc.) to remove embedded patient-identifying information.
    /// </summary>
    CleanDescriptors = 1 << 7,

    /// <summary>
    /// Clean Structured Content Option (PS3.15 Annex E, Option).
    /// Processes structured report content (SR) sequences to remove
    /// patient-identifying information while preserving clinical content.
    /// </summary>
    CleanStructuredContent = 1 << 8,

    /// <summary>
    /// Clean Graphics Option (PS3.15 Annex E, Option).
    /// Processes graphic annotations (GSPS overlays) to remove any
    /// text annotations that may contain patient-identifying information.
    /// </summary>
    CleanGraphics = 1 << 9,

    /// <summary>
    /// Clean Pixel Data Option (PS3.15 Annex E, Option).
    /// Detects and removes burned-in patient-identifying information from
    /// pixel data and overlay planes. This includes modality worklist data,
    /// patient demographics, and other text annotations rendered into images.
    /// </summary>
    CleanPixelData = 1 << 10
}
