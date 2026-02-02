using System.Collections.Generic;

namespace SharpDicom.Deidentification;

/// <summary>
/// Strategy for date shifting during de-identification.
/// </summary>
/// <remarks>
/// Date shifting preserves temporal relationships between events while obscuring
/// the actual dates. The shift amount can be applied consistently at different
/// levels of granularity.
/// </remarks>
public enum DateShiftStrategy
{
    /// <summary>
    /// Same offset for all files with the same PatientID.
    /// This preserves temporal relationships across all studies for a patient.
    /// </summary>
    PerPatient,

    /// <summary>
    /// Same offset for all files with the same StudyInstanceUID.
    /// This preserves temporal relationships within a study but not across studies.
    /// </summary>
    PerStudy,

    /// <summary>
    /// Random offset per element (not recommended - breaks temporal relationships).
    /// Use only when temporal relationships must be destroyed.
    /// </summary>
    PerElement
}

/// <summary>
/// Pixel replacement value for burned-in PHI regions.
/// </summary>
public enum PixelReplacementValue
{
    /// <summary>Replace detected PHI regions with black pixels (0 for all samples).</summary>
    Black,

    /// <summary>Replace detected PHI regions with white pixels (max value for all samples).</summary>
    White,

    /// <summary>Replace detected PHI regions with the average value of the region.</summary>
    AverageOfRegion
}

/// <summary>
/// Options for pixel data cleaning (burned-in PHI detection and removal).
/// </summary>
/// <remarks>
/// Burned-in PHI is patient-identifying information rendered directly into image
/// pixel data, commonly found in ultrasound, secondary capture, and scanned documents.
/// </remarks>
public sealed class PixelCleaningOptions
{
    /// <summary>
    /// Gets or sets whether pixel data cleaning is enabled.
    /// Default is false due to computational cost and risk of false positives.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets the replacement value for detected PHI regions.
    /// Default is Black.
    /// </summary>
    public PixelReplacementValue ReplacementValue { get; init; } = PixelReplacementValue.Black;

    /// <summary>
    /// Gets or sets whether to process overlay planes (60xx groups) for burned-in PHI.
    /// Default is true.
    /// </summary>
    public bool ProcessOverlayPlanes { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to warn about high-risk modalities (US, SC, XC) that
    /// commonly contain burned-in PHI.
    /// Default is true.
    /// </summary>
    public bool WarnHighRiskModalities { get; init; } = true;

    /// <summary>
    /// Gets or sets known safe modalities that do not require pixel cleaning.
    /// Images with these modalities will skip pixel cleaning even if enabled.
    /// Default is null (all modalities processed when enabled).
    /// </summary>
    public IReadOnlyList<string>? SafeModalities { get; init; }
}

/// <summary>
/// Complete de-identification configuration options.
/// </summary>
/// <remarks>
/// <para>
/// This class provides comprehensive configuration for DICOM de-identification
/// per PS3.15. Default values are chosen to provide strong privacy protection.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var options = new DeidentificationOptions
/// {
///     Profile = DeidentificationProfile.Basic | DeidentificationProfile.RetainPatientCharacteristics,
///     DateShiftStrategy = DateShiftStrategy.PerPatient,
///     DateShiftRange = (-365, 365)
/// };
/// </code>
/// </para>
/// </remarks>
public sealed class DeidentificationOptions
{
    /// <summary>
    /// Gets or sets the de-identification profile(s) to apply.
    /// Default is Basic profile only.
    /// </summary>
    public DeidentificationProfile Profile { get; init; } = DeidentificationProfile.Basic;

    /// <summary>
    /// Gets or sets the date shifting strategy.
    /// Default is PerPatient (same offset for all files with the same PatientID).
    /// </summary>
    public DateShiftStrategy DateShiftStrategy { get; init; } = DateShiftStrategy.PerPatient;

    /// <summary>
    /// Gets or sets the date shift range in days as (MinDays, MaxDays).
    /// A random offset within this range will be applied.
    /// Default is (-365, 365) meaning up to one year in either direction.
    /// </summary>
    public (int MinDays, int MaxDays) DateShiftRange { get; init; } = (-365, 365);

    /// <summary>
    /// Gets or sets whether to zero time components (TM, DT) after date shifting.
    /// When true, times are set to 00:00:00 for additional privacy.
    /// Default is true.
    /// </summary>
    public bool ZeroTimeComponents { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to recalculate PatientAge (AS VR) from shifted
    /// birth date and study date.
    /// Default is true.
    /// </summary>
    public bool RecalculatePatientAge { get; init; } = true;

    /// <summary>
    /// Gets or sets the UID prefix for generated UIDs.
    /// Default is "2.25" (UUID-based, no registration required).
    /// </summary>
    /// <remarks>
    /// The 2.25 prefix allows generation of globally unique UIDs without
    /// registering with a UID assignment authority. Alternative prefixes
    /// may be used if your organization has a registered root.
    /// </remarks>
    public string UidPrefix { get; init; } = "2.25";

    /// <summary>
    /// Gets or sets whether to remove private tags not in SafePrivateCreators.
    /// Default is true (remove all private tags).
    /// </summary>
    public bool RemovePrivateTags { get; init; } = true;

    /// <summary>
    /// Gets or sets the list of private creator identification strings
    /// that are known to not contain PHI and should be retained.
    /// Only used when Profile includes RetainSafePrivate.
    /// </summary>
    public IReadOnlyList<string>? SafePrivateCreators { get; init; }

    /// <summary>
    /// Gets or sets custom de-identification rules that extend or override
    /// the standard profile behavior.
    /// </summary>
    /// <remarks>
    /// Custom rules are evaluated before the standard profile. A rule can
    /// return a specific action to override the standard, or null to fall
    /// back to the standard profile action.
    /// </remarks>
    public IReadOnlyList<IDeidentificationRule>? CustomRules { get; init; }

    /// <summary>
    /// Gets or sets options for pixel data cleaning (burned-in PHI detection).
    /// Default is a disabled instance.
    /// </summary>
    public PixelCleaningOptions PixelCleaning { get; init; } = new();
}
