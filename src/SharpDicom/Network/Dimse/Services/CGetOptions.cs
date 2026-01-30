using System;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Options for C-GET SCU operations.
    /// </summary>
    public sealed class CGetOptions
    {
        /// <summary>
        /// Default C-GET options.
        /// </summary>
        public static CGetOptions Default { get; } = new();

        /// <summary>
        /// Gets or sets the operation timeout. Default is 120 seconds.
        /// </summary>
        /// <remarks>
        /// C-GET operations typically take longer than C-FIND because they include
        /// the actual data transfer via C-STORE sub-operations.
        /// </remarks>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Gets or sets the priority for the get operation.
        /// </summary>
        /// <remarks>
        /// Priority values per DICOM PS3.7:
        /// <list type="bullet">
        ///   <item><description>0 = MEDIUM (default)</description></item>
        ///   <item><description>1 = HIGH</description></item>
        ///   <item><description>2 = LOW</description></item>
        /// </list>
        /// </remarks>
        public ushort Priority { get; set; }

        /// <summary>
        /// Gets or sets whether to use Patient Root information model.
        /// When false, Study Root is used. Default is true.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Patient Root Q/R Model (1.2.840.10008.5.1.4.1.2.1.3) supports retrieval at
        /// PATIENT, STUDY, SERIES, and IMAGE levels.
        /// </para>
        /// <para>
        /// Study Root Q/R Model (1.2.840.10008.5.1.4.1.2.2.3) supports retrieval at
        /// STUDY, SERIES, and IMAGE levels only (no PATIENT level).
        /// </para>
        /// </remarks>
        public bool UsePatientRoot { get; set; } = true;

        /// <summary>
        /// Gets or sets the cancellation behavior when CancellationToken is triggered.
        /// </summary>
        public CGetCancellationBehavior CancellationBehavior { get; set; } = CGetCancellationBehavior.RejectInFlight;
    }

    /// <summary>
    /// Specifies behavior when C-GET is cancelled.
    /// </summary>
    public enum CGetCancellationBehavior
    {
        /// <summary>
        /// Send C-CANCEL and reject incoming C-STORE sub-operations.
        /// Some data may be lost.
        /// </summary>
        RejectInFlight,

        /// <summary>
        /// Send C-CANCEL but accept already-started C-STORE sub-operations.
        /// Preserves data integrity for in-flight transfers.
        /// </summary>
        CompleteInFlight
    }
}
