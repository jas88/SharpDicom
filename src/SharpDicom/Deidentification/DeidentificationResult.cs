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
        /// Number of additional UID references remapped by <see cref="UidReferenceWalker"/>.
        /// </summary>
        /// <remarks>
        /// This count is separate from <see cref="UidsRemapped"/> which tracks UIDs remapped
        /// by the primary PS3.15 profile. This tracks UIDs found by the comprehensive VR=UI
        /// traversal that catches references in nested sequences not covered by the profile.
        /// </remarks>
        public int UidReferencesRemapped { get; set; }

        /// <summary>Number of pixel data frames scanned by OCR.</summary>
        public int OcrFramesScanned { get; set; }

        /// <summary>Total number of OCR text detections found (before filtering).</summary>
        public int OcrDetectionsFound { get; set; }

        /// <summary>Number of OCR detections classified as PHI candidates (after allow/deny filtering).</summary>
        public int OcrPhiCandidates { get; set; }

        /// <summary>Number of pixel regions redacted based on OCR detections.</summary>
        public int OcrRegionsRedacted { get; set; }

        /// <summary>
        /// Gets the total number of modifications made.
        /// </summary>
        public int TotalModifications =>
            AttributesRemoved + AttributesReplaced + AttributesEmptied +
            UidsRemapped + UidReferencesRemapped + DatesShifted + OcrRegionsRedacted;
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
