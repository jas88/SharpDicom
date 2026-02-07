namespace FellowOakDicom.Network
{
    /// <summary>
    /// DIMSE status wrapper matching fo-dicom 5.x DicomStatus.
    /// Wraps SharpDicom's DicomStatus with fo-dicom-compatible API surface.
    /// </summary>
    public sealed class DicomStatus
    {
        /// <summary>
        /// Gets the 16-bit status code.
        /// </summary>
        public ushort Code { get; }

        /// <summary>
        /// Gets the status state category.
        /// </summary>
        public DicomState State { get; }

        /// <summary>
        /// Gets a value indicating whether this status represents success.
        /// </summary>
        public bool IsSuccess => State == DicomState.Success;

        /// <summary>
        /// Gets a value indicating whether this status represents a pending response.
        /// </summary>
        public bool IsPending => State == DicomState.Pending;

        /// <summary>
        /// Gets a value indicating whether this status represents a warning.
        /// </summary>
        public bool IsWarning => State == DicomState.Warning;

        /// <summary>
        /// Gets a value indicating whether this status represents a failure.
        /// </summary>
        public bool IsFailure => State == DicomState.Failure;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomStatus"/> class.
        /// </summary>
        /// <param name="code">The 16-bit status code.</param>
        /// <param name="state">The status state.</param>
        public DicomStatus(ushort code, DicomState state)
        {
            Code = code;
            State = state;
        }

        /// <summary>
        /// Creates a compat DicomStatus from a SharpDicom DicomStatus.
        /// </summary>
        internal DicomStatus(SharpDicom.Network.DicomStatus status)
        {
            Code = status.Code;
            State = status.Category switch
            {
                SharpDicom.Network.StatusCategory.Success => DicomState.Success,
                SharpDicom.Network.StatusCategory.Pending => DicomState.Pending,
                SharpDicom.Network.StatusCategory.Warning => DicomState.Warning,
                SharpDicom.Network.StatusCategory.Cancel => DicomState.Cancel,
                _ => DicomState.Failure
            };
        }

        /// <inheritdoc />
        public override string ToString() => $"0x{Code:X4} ({State})";

        #region Well-Known Statuses

        /// <summary>Success (0x0000).</summary>
        public static readonly DicomStatus Success = new(0x0000, DicomState.Success);

        /// <summary>Pending (0xFF00).</summary>
        public static readonly DicomStatus Pending = new(0xFF00, DicomState.Pending);

        /// <summary>Cancel (0xFE00).</summary>
        public static readonly DicomStatus Cancel = new(0xFE00, DicomState.Cancel);

        #endregion
    }

    /// <summary>
    /// DIMSE status state enum matching fo-dicom 5.x DicomState.
    /// </summary>
    public enum DicomState
    {
        /// <summary>Pending - more results to follow.</summary>
        Pending,

        /// <summary>Warning - completed with warnings.</summary>
        Warning,

        /// <summary>Success - completed successfully.</summary>
        Success,

        /// <summary>Failure - operation failed.</summary>
        Failure,

        /// <summary>Cancel - operation was cancelled.</summary>
        Cancel
    }
}
