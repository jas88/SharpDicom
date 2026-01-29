using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Result of de-identifying a DICOM dataset.
    /// </summary>
    public sealed class DeidentificationResult
    {
        /// <summary>
        /// Gets a value indicating whether de-identification was successful.
        /// </summary>
        public bool Success { get; init; } = true;

        /// <summary>
        /// Gets summary statistics of the de-identification.
        /// </summary>
        public DeidentificationSummary Summary { get; } = new();

        /// <summary>
        /// Gets warnings generated during de-identification.
        /// </summary>
        public List<string> Warnings { get; } = new();

        /// <summary>
        /// Gets errors that occurred during de-identification.
        /// </summary>
        public List<string> Errors { get; } = new();

        /// <summary>
        /// Gets information about UID remappings performed.
        /// </summary>
        public List<UidRemapInfo> UidRemappings { get; } = new();
    }

    /// <summary>
    /// Summary statistics of de-identification operations.
    /// </summary>
    public sealed class DeidentificationSummary
    {
        /// <summary>Number of attributes removed.</summary>
        public int AttributesRemoved { get; set; }

        /// <summary>Number of attributes replaced with dummy values.</summary>
        public int AttributesReplaced { get; set; }

        /// <summary>Number of attributes replaced with empty values.</summary>
        public int AttributesEmptied { get; set; }

        /// <summary>Number of UIDs remapped.</summary>
        public int UidsRemapped { get; set; }

        /// <summary>Number of dates shifted.</summary>
        public int DatesShifted { get; set; }

        /// <summary>Number of private tags processed.</summary>
        public int PrivateTagsProcessed { get; set; }

        /// <summary>Number of sequence items processed.</summary>
        public int SequenceItemsProcessed { get; set; }

        /// <summary>
        /// Gets the total number of modifications made.
        /// </summary>
        public int TotalModifications =>
            AttributesRemoved + AttributesReplaced + AttributesEmptied +
            UidsRemapped + DatesShifted;
    }

    /// <summary>
    /// Information about a UID remapping.
    /// </summary>
    public readonly record struct UidRemapInfo(
        DicomTag Tag,
        string OriginalUid,
        string NewUid
    );
}
