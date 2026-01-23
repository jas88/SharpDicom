using System.Collections.Generic;
using SharpDicom.Validation.Rules;

namespace SharpDicom.Validation;

/// <summary>
/// Provides collections of standard validation rules for DICOM elements.
/// </summary>
public static class StandardRules
{
    /// <summary>
    /// Gets all built-in validation rules.
    /// </summary>
    /// <remarks>
    /// Includes format validators for DA, TM, DT, AS, UI, PN, CS,
    /// plus length and character repertoire validators.
    /// </remarks>
    public static IReadOnlyList<IValidationRule> All { get; } = new IValidationRule[]
    {
        new DateValidator(),
        new TimeValidator(),
        new DateTimeValidator(),
        new AgeStringValidator(),
        new UidValidator(),
        new PersonNameValidator(),
        new CodeStringValidator(),
        new StringLengthValidator(),
        new CharacterRepertoireValidator()
    };

    /// <summary>
    /// Gets structural rules only for fast, minimal validation.
    /// </summary>
    /// <remarks>
    /// Only checks basic length constraints, not format or character validity.
    /// Suitable for high-performance scenarios where detailed validation is not required.
    /// </remarks>
    public static IReadOnlyList<IValidationRule> StructuralOnly { get; } = new IValidationRule[]
    {
        new StringLengthValidator()
    };

    /// <summary>
    /// Gets format validators for date/time VRs (DA, TM, DT, AS).
    /// </summary>
    public static IReadOnlyList<IValidationRule> DateTimeRules { get; } = new IValidationRule[]
    {
        new DateValidator(),
        new TimeValidator(),
        new DateTimeValidator(),
        new AgeStringValidator()
    };

    /// <summary>
    /// Gets format validators for identifier VRs (UI, CS).
    /// </summary>
    public static IReadOnlyList<IValidationRule> IdentifierRules { get; } = new IValidationRule[]
    {
        new UidValidator(),
        new CodeStringValidator()
    };

    /// <summary>
    /// Gets character repertoire validators (AE, DS, IS).
    /// </summary>
    public static IReadOnlyList<IValidationRule> CharacterRules { get; } = new IValidationRule[]
    {
        new CharacterRepertoireValidator()
    };
}
