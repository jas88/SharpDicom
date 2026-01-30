using System;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Options for C-FIND SCU operations.
    /// </summary>
    public sealed class CFindOptions
    {
        /// <summary>
        /// Default C-FIND options.
        /// </summary>
        public static CFindOptions Default { get; } = new();

        /// <summary>
        /// Gets or sets the query timeout. Default is 10 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the priority for the query operation.
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
        /// Patient Root Q/R Model (1.2.840.10008.5.1.4.1.2.1.1) supports queries at
        /// PATIENT, STUDY, SERIES, and IMAGE levels.
        /// </para>
        /// <para>
        /// Study Root Q/R Model (1.2.840.10008.5.1.4.1.2.2.1) supports queries at
        /// STUDY, SERIES, and IMAGE levels only (no PATIENT level).
        /// </para>
        /// </remarks>
        public bool UsePatientRoot { get; set; } = true;
    }
}
