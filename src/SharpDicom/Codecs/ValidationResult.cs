using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpDicom.Codecs
{
    /// <summary>
    /// Result of validating compressed pixel data.
    /// </summary>
    /// <param name="IsValid">Whether the compressed data is valid.</param>
    /// <param name="Issues">List of validation issues found, empty if valid.</param>
    public readonly record struct ValidationResult(
        bool IsValid,
        IReadOnlyList<CodecDiagnostic> Issues)
    {
        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        /// <returns>A valid ValidationResult with no issues.</returns>
        public static ValidationResult Valid() =>
            new(true, Array.Empty<CodecDiagnostic>());

        /// <summary>
        /// Creates a failed validation result from a collection of issues.
        /// </summary>
        /// <param name="issues">The validation issues found.</param>
        /// <returns>An invalid ValidationResult.</returns>
        public static ValidationResult Invalid(IEnumerable<CodecDiagnostic> issues) =>
            new(false, issues.ToList());

        /// <summary>
        /// Creates a failed validation result from a single issue.
        /// </summary>
        /// <param name="issue">The validation issue.</param>
        /// <returns>An invalid ValidationResult.</returns>
        public static ValidationResult Invalid(CodecDiagnostic issue) =>
            new(false, new[] { issue });

        /// <summary>
        /// Creates a failed validation result from a message.
        /// </summary>
        /// <param name="frameIndex">The frame index where the issue occurred.</param>
        /// <param name="position">The byte position where the issue occurred.</param>
        /// <param name="message">The validation error message.</param>
        /// <returns>An invalid ValidationResult.</returns>
        public static ValidationResult Invalid(int frameIndex, long position, string message) =>
            Invalid(CodecDiagnostic.At(frameIndex, position, message));
    }
}
