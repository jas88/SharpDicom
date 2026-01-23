namespace SharpDicom.Validation;

/// <summary>
/// Unique validation error codes for programmatic handling.
/// </summary>
/// <remarks>
/// Each code follows the format "DICOM-NNN" where NNN is a zero-padded number.
/// Codes are grouped by category:
/// - 001-010: Structural issues
/// - 011-015: Length and multiplicity issues
/// - 016-025: Format and IOD issues
/// </remarks>
public static class ValidationCodes
{
    #region Structural Issues (001-010)

    /// <summary>
    /// Invalid tag format - tag cannot be parsed or is malformed.
    /// </summary>
    public const string InvalidTagFormat = "DICOM-001";

    /// <summary>
    /// Invalid VR format - unknown or unrecognized value representation.
    /// </summary>
    public const string InvalidVRFormat = "DICOM-002";

    /// <summary>
    /// VR mismatch - declared VR differs from dictionary-defined VR.
    /// </summary>
    public const string VRMismatch = "DICOM-003";

    /// <summary>
    /// Incorrect padding byte - wrong padding character for VR type.
    /// </summary>
    public const string IncorrectPaddingByte = "DICOM-006";

    /// <summary>
    /// Truncated element - declared length exceeds available data.
    /// </summary>
    public const string TruncatedElement = "DICOM-011";

    /// <summary>
    /// Invalid sequence structure - malformed sequence delimiters or nesting.
    /// </summary>
    public const string InvalidSequenceStructure = "DICOM-012";

    #endregion

    #region Length and Multiplicity Issues (004-005, 014)

    /// <summary>
    /// Value too long - value length exceeds VR maximum.
    /// </summary>
    public const string ValueTooLong = "DICOM-004";

    /// <summary>
    /// Invalid value multiplicity - number of values doesn't match VM constraint.
    /// </summary>
    public const string InvalidValueMultiplicity = "DICOM-005";

    /// <summary>
    /// Odd value length - value length is not even (padding required).
    /// </summary>
    public const string OddValueLength = "DICOM-014";

    #endregion

    #region Format Issues (007-010, 013, 019-022)

    /// <summary>
    /// Invalid date format - DA value doesn't match YYYYMMDD format.
    /// </summary>
    public const string InvalidDateFormat = "DICOM-007";

    /// <summary>
    /// Invalid time format - TM value doesn't match HHMMSS.FFFFFF format.
    /// </summary>
    public const string InvalidTimeFormat = "DICOM-008";

    /// <summary>
    /// Invalid UID format - UI value violates UID syntax rules.
    /// </summary>
    public const string InvalidUidFormat = "DICOM-009";

    /// <summary>
    /// Invalid person name format - PN value doesn't match component structure.
    /// </summary>
    public const string InvalidPersonNameFormat = "DICOM-010";

    /// <summary>
    /// Invalid character - character not allowed for the VR type.
    /// </summary>
    public const string InvalidCharacter = "DICOM-013";

    /// <summary>
    /// Invalid decimal string - DS value is not a valid decimal number.
    /// </summary>
    public const string InvalidDecimalString = "DICOM-019";

    /// <summary>
    /// Invalid integer string - IS value is not a valid integer.
    /// </summary>
    public const string InvalidIntegerString = "DICOM-020";

    /// <summary>
    /// Invalid age string - AS value doesn't match nnnW/M/Y/D format.
    /// </summary>
    public const string InvalidAgeString = "DICOM-021";

    /// <summary>
    /// Invalid code string - CS value violates character restrictions.
    /// </summary>
    public const string InvalidCodeString = "DICOM-022";

    #endregion

    #region IOD Issues (015-018)

    /// <summary>
    /// Missing required tag - Type 1 attribute is absent.
    /// </summary>
    public const string MissingRequiredTag = "DICOM-015";

    /// <summary>
    /// Missing conditional tag - Type 1C attribute absent when condition met.
    /// </summary>
    public const string MissingConditionalTag = "DICOM-016";

    /// <summary>
    /// Empty required tag - Type 2 attribute is present but has no value.
    /// </summary>
    public const string EmptyRequiredTag = "DICOM-017";

    /// <summary>
    /// Present when not allowed - Type 1C/2C attribute present when condition not met.
    /// </summary>
    public const string PresentWhenNotAllowed = "DICOM-018";

    #endregion

    #region Value Issues (023-025)

    /// <summary>
    /// Value out of range - value exceeds valid range for the attribute.
    /// </summary>
    public const string ValueOutOfRange = "DICOM-023";

    /// <summary>
    /// Non-standard defined term - value is not in the enumerated list.
    /// </summary>
    public const string NonStandardDefinedTerm = "DICOM-024";

    /// <summary>
    /// Deprecated attribute - retired attribute is present.
    /// </summary>
    public const string DeprecatedAttribute = "DICOM-025";

    #endregion

    #region Date/Value Issues (additional)

    /// <summary>
    /// Invalid date value - date components are out of valid range.
    /// </summary>
    public const string InvalidDateValue = "DICOM-026";

    /// <summary>
    /// Invalid time value - time components are out of valid range.
    /// </summary>
    public const string InvalidTimeValue = "DICOM-027";

    #endregion
}
