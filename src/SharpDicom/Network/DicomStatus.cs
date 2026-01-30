using System;

namespace SharpDicom.Network
{
    /// <summary>
    /// Represents a DICOM DIMSE status code per PS3.7 Annex C.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Status codes are returned by DIMSE operations (C-STORE, C-FIND, C-MOVE, C-GET, C-ECHO)
    /// to indicate the result of the operation. The 16-bit code is divided into categories
    /// based on the high-order bits.
    /// </para>
    /// <para>
    /// Status code ranges:
    /// <list type="bullet">
    ///   <item><description>0x0000: Success</description></item>
    ///   <item><description>0xFE00: Cancel</description></item>
    ///   <item><description>0xFF00-0xFF01: Pending</description></item>
    ///   <item><description>0xB000-0xBFFF: Warning</description></item>
    ///   <item><description>0xA000-0xAFFF, 0xC000-0xCFFF: Failure</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public readonly struct DicomStatus : IEquatable<DicomStatus>
    {
        /// <summary>
        /// Gets the 16-bit status code value.
        /// </summary>
        public ushort Code { get; }

        /// <summary>
        /// Gets an optional error comment providing additional information about the status.
        /// </summary>
        /// <remarks>
        /// This corresponds to the Error Comment (0000,0902) attribute in DIMSE responses.
        /// May be null if no additional information is available.
        /// </remarks>
        public string? ErrorComment { get; init; }

        /// <summary>
        /// Gets the category of this status code.
        /// </summary>
        public StatusCategory Category => CategorizeCode(Code);

        /// <summary>
        /// Gets a value indicating whether this status represents success.
        /// </summary>
        public bool IsSuccess => Category == StatusCategory.Success;

        /// <summary>
        /// Gets a value indicating whether this status represents a pending response.
        /// </summary>
        public bool IsPending => Category == StatusCategory.Pending;

        /// <summary>
        /// Gets a value indicating whether this status represents a warning.
        /// </summary>
        public bool IsWarning => Category == StatusCategory.Warning;

        /// <summary>
        /// Gets a value indicating whether this status represents a failure.
        /// </summary>
        public bool IsFailure => Category == StatusCategory.Failure;

        /// <summary>
        /// Gets a value indicating whether this status represents a cancellation.
        /// </summary>
        public bool IsCancel => Category == StatusCategory.Cancel;

        /// <summary>
        /// Initializes a new instance of <see cref="DicomStatus"/> with the specified code.
        /// </summary>
        /// <param name="code">The 16-bit status code.</param>
        public DicomStatus(ushort code)
        {
            Code = code;
            ErrorComment = null;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="DicomStatus"/> with the specified code and error comment.
        /// </summary>
        /// <param name="code">The 16-bit status code.</param>
        /// <param name="errorComment">An optional error comment.</param>
        public DicomStatus(ushort code, string? errorComment)
        {
            Code = code;
            ErrorComment = errorComment;
        }

        /// <summary>
        /// Categorizes a status code into its corresponding category.
        /// </summary>
        /// <param name="code">The status code to categorize.</param>
        /// <returns>The <see cref="StatusCategory"/> for the code.</returns>
        public static StatusCategory CategorizeCode(ushort code)
        {
            // Success: exactly 0x0000
            if (code == 0x0000)
                return StatusCategory.Success;

            // Cancel: exactly 0xFE00
            if (code == 0xFE00)
                return StatusCategory.Cancel;

            // Pending: 0xFF00-0xFF01
            if (code == 0xFF00 || code == 0xFF01)
                return StatusCategory.Pending;

            // Warning: 0xB000-0xBFFF
            if (code >= 0xB000 && code <= 0xBFFF)
                return StatusCategory.Warning;

            // Failure: 0xA000-0xAFFF, 0xC000-0xCFFF, or any other non-success code
            // The DICOM standard also defines 0x01xx, 0xFE0x (except 0xFE00) as errors
            return StatusCategory.Failure;
        }

        /// <inheritdoc />
        public bool Equals(DicomStatus other) => Code == other.Code;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is DicomStatus other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Code;

        /// <inheritdoc />
        public override string ToString()
        {
            var result = $"0x{Code:X4} ({Category})";
            return ErrorComment != null ? $"{result}: {ErrorComment}" : result;
        }

        /// <summary>
        /// Determines whether two <see cref="DicomStatus"/> values are equal.
        /// </summary>
        public static bool operator ==(DicomStatus left, DicomStatus right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="DicomStatus"/> values are not equal.
        /// </summary>
        public static bool operator !=(DicomStatus left, DicomStatus right) => !left.Equals(right);

        #region Well-Known Status Codes

        /// <summary>
        /// Success (0x0000) - Operation completed successfully.
        /// </summary>
        public static readonly DicomStatus Success = new(0x0000);

        /// <summary>
        /// Cancel (0xFE00) - Operation was cancelled.
        /// </summary>
        public static readonly DicomStatus Cancel = new(0xFE00);

        /// <summary>
        /// Pending (0xFF00) - Operation in progress, more results to follow.
        /// </summary>
        public static readonly DicomStatus Pending = new(0xFF00);

        /// <summary>
        /// Pending with warnings (0xFF01) - Operation in progress with optional keys not supported.
        /// </summary>
        public static readonly DicomStatus PendingWarning = new(0xFF01);

        /// <summary>
        /// Refused: Out of resources (0xA700) - Association could not be established due to resource constraints.
        /// </summary>
        public static readonly DicomStatus OutOfResources = new(0xA700);

        /// <summary>
        /// Error: No such SOP Class (0xA900) - The specified SOP Class is not supported.
        /// </summary>
        public static readonly DicomStatus NoSuchSOPClass = new(0xA900);

        /// <summary>
        /// Error: Processing failure (0x0110) - General processing error.
        /// </summary>
        public static readonly DicomStatus ProcessingFailure = new(0x0110);

        /// <summary>
        /// Error: Duplicate SOP Instance (0x0111) - The SOP Instance already exists.
        /// </summary>
        public static readonly DicomStatus DuplicateSOPInstance = new(0x0111);

        /// <summary>
        /// Error: No such SOP Instance (0xC001) - The requested SOP Instance does not exist.
        /// </summary>
        public static readonly DicomStatus NoSuchObjectInstance = new(0xC001);

        /// <summary>
        /// Error: Missing attribute (0x0120) - A required attribute is missing.
        /// </summary>
        public static readonly DicomStatus MissingAttribute = new(0x0120);

        /// <summary>
        /// Warning: Coercion of data elements (0xB000) - Some attributes were modified.
        /// </summary>
        public static readonly DicomStatus CoercionOfDataElements = new(0xB000);

        /// <summary>
        /// Warning: Data set does not match SOP Class (0xB007) - The data set does not fully conform.
        /// </summary>
        public static readonly DicomStatus DataSetDoesNotMatchSOPClass = new(0xB007);

        /// <summary>
        /// Warning: Elements discarded (0xB006) - Some elements were discarded.
        /// </summary>
        public static readonly DicomStatus ElementsDiscarded = new(0xB006);

        #endregion
    }
}
