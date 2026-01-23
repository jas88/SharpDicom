using System.Collections.Generic;
using System.Linq;

namespace SharpDicom.Validation;

/// <summary>
/// Collection of validation issues with filtering and convenience properties.
/// </summary>
/// <remarks>
/// ValidationResult aggregates all issues found during validation or parsing.
/// Use <see cref="IsValid"/> to check if the dataset passed validation (no errors),
/// and the various filtering properties to access specific severity levels.
/// </remarks>
public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues = new();

    /// <summary>
    /// Gets a value indicating whether validation passed (no Error-level issues).
    /// </summary>
    /// <remarks>
    /// Warning and Info level issues do not affect validity.
    /// An empty result (no issues) is considered valid.
    /// </remarks>
    public bool IsValid => !_issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets a value indicating whether any Warning-level issues were found.
    /// </summary>
    public bool HasWarnings => _issues.Any(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Gets a value indicating whether any Info-level issues were found.
    /// </summary>
    public bool HasInfos => _issues.Any(i => i.Severity == ValidationSeverity.Info);

    /// <summary>
    /// Gets a value indicating whether any issues were found at any severity level.
    /// </summary>
    public bool HasIssues => _issues.Count > 0;

    /// <summary>
    /// Gets all collected issues.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>
    /// Gets Error-level issues only.
    /// </summary>
    public IEnumerable<ValidationIssue> Errors =>
        _issues.Where(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets Warning-level issues only.
    /// </summary>
    public IEnumerable<ValidationIssue> Warnings =>
        _issues.Where(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Gets Info-level issues only.
    /// </summary>
    public IEnumerable<ValidationIssue> Infos =>
        _issues.Where(i => i.Severity == ValidationSeverity.Info);

    /// <summary>
    /// Gets the total count of issues at all severity levels.
    /// </summary>
    public int Count => _issues.Count;

    /// <summary>
    /// Adds an issue to the result.
    /// </summary>
    /// <param name="issue">The issue to add.</param>
    public void Add(ValidationIssue issue) => _issues.Add(issue);

    /// <summary>
    /// Adds multiple issues to the result.
    /// </summary>
    /// <param name="issues">The issues to add.</param>
    public void AddRange(IEnumerable<ValidationIssue> issues) => _issues.AddRange(issues);

    /// <summary>
    /// Clears all issues from the result.
    /// </summary>
    public void Clear() => _issues.Clear();

    /// <summary>
    /// Gets issues filtered by a specific validation code.
    /// </summary>
    /// <param name="code">The validation code to filter by (e.g., "DICOM-003").</param>
    /// <returns>All issues with the specified code.</returns>
    public IEnumerable<ValidationIssue> GetByCode(string code) =>
        _issues.Where(i => i.Code == code);

    /// <summary>
    /// Returns a summary string of the validation result.
    /// </summary>
    /// <returns>A summary showing counts of each severity level.</returns>
    public override string ToString()
    {
        var errorCount = _issues.Count(i => i.Severity == ValidationSeverity.Error);
        var warningCount = _issues.Count(i => i.Severity == ValidationSeverity.Warning);
        var infoCount = _issues.Count(i => i.Severity == ValidationSeverity.Info);

        if (_issues.Count == 0)
        {
            return "Validation passed (no issues)";
        }

        return $"Validation {(IsValid ? "passed" : "failed")}: {errorCount} errors, {warningCount} warnings, {infoCount} infos";
    }
}
