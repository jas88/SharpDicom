using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// A single OCR detection with text, bounding box, confidence, and frame information.
    /// </summary>
    /// <param name="Text">The detected text string.</param>
    /// <param name="Confidence">Recognition confidence score (0.0 - 1.0).</param>
    /// <param name="BoundingBox">Bounding rectangle as a <see cref="RedactionRegion"/>.</param>
    /// <param name="FrameIndex">Zero-based frame index where this detection was found.</param>
    /// <param name="IsEdgeRegion">Whether the detection is in an edge zone of the image.</param>
    public readonly record struct OcrDetection(
        string Text,
        float Confidence,
        RedactionRegion BoundingBox,
        int FrameIndex,
        bool IsEdgeRegion);

    /// <summary>
    /// Result of an OCR scan operation on DICOM pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains all detections above the confidence threshold (<see cref="Detections"/>)
    /// as well as the filtered subset that excludes allowed non-PHI text
    /// (<see cref="FilteredDetections"/>). The filtered detections represent PHI
    /// candidates suitable for redaction.
    /// </para>
    /// <para>
    /// Use <see cref="ToRedactionRegions"/> to convert filtered detections directly
    /// to <see cref="RedactionRegion"/> instances for <see cref="PixelDataRedactor"/>.
    /// </para>
    /// </remarks>
    public sealed class OcrScanResult
    {
        /// <summary>
        /// Gets all detections above the confidence threshold before allow/deny filtering.
        /// </summary>
        public IReadOnlyList<OcrDetection> Detections { get; }

        /// <summary>
        /// Gets detections after allow/deny list filtering. These are the PHI candidates
        /// that should be considered for redaction.
        /// </summary>
        public IReadOnlyList<OcrDetection> FilteredDetections { get; }

        /// <summary>
        /// Gets the total number of frames that were scanned.
        /// </summary>
        public int TotalFramesScanned { get; }

        /// <summary>
        /// Gets the number of frames that contained at least one detection.
        /// </summary>
        public int FramesWithDetections { get; }

        /// <summary>
        /// Gets the time taken to perform the OCR scan.
        /// </summary>
        public TimeSpan ScanDuration { get; }

        /// <summary>
        /// Gets any warnings generated during the scan.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OcrScanResult"/> class.
        /// </summary>
        /// <param name="detections">All detections above threshold.</param>
        /// <param name="filteredDetections">Detections after allow/deny filtering.</param>
        /// <param name="totalFramesScanned">Number of frames scanned.</param>
        /// <param name="framesWithDetections">Number of frames with at least one detection.</param>
        /// <param name="scanDuration">Duration of the scan.</param>
        /// <param name="warnings">Any warnings generated during the scan.</param>
        public OcrScanResult(
            IReadOnlyList<OcrDetection> detections,
            IReadOnlyList<OcrDetection> filteredDetections,
            int totalFramesScanned,
            int framesWithDetections,
            TimeSpan scanDuration,
            IReadOnlyList<string>? warnings = null)
        {
            Detections = detections;
            FilteredDetections = filteredDetections;
            TotalFramesScanned = totalFramesScanned;
            FramesWithDetections = framesWithDetections;
            ScanDuration = scanDuration;
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>
        /// Gets an empty result with no detections and zero frames scanned.
        /// </summary>
        public static OcrScanResult Empty { get; } = new(
            Array.Empty<OcrDetection>(),
            Array.Empty<OcrDetection>(),
            totalFramesScanned: 0,
            framesWithDetections: 0,
            scanDuration: TimeSpan.Zero);

        /// <summary>
        /// Converts <see cref="FilteredDetections"/> to a list of <see cref="RedactionRegion"/>
        /// instances suitable for use with <see cref="PixelDataRedactor"/>.
        /// </summary>
        /// <returns>A list of redaction regions corresponding to filtered PHI detections.</returns>
        public List<RedactionRegion> ToRedactionRegions()
        {
            var regions = new List<RedactionRegion>(FilteredDetections.Count);
            foreach (var detection in FilteredDetections)
            {
                regions.Add(detection.BoundingBox);
            }
            return regions;
        }
    }
}
