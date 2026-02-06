using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Risk categories for burned-in annotation modalities when performing OCR scanning.
    /// </summary>
    [Flags]
    public enum OcrScanModality
    {
        /// <summary>No modalities selected.</summary>
        None = 0,

        /// <summary>
        /// High-risk modalities that almost always contain burned-in text:
        /// US, ES, SC, XC, GM, SM, OP, OPT, ECG, HD.
        /// </summary>
        HighRisk = 1,

        /// <summary>
        /// Moderate-risk modalities that may contain burned-in annotations:
        /// XA, RF, MG, DX, CR, PX, IO.
        /// </summary>
        ModerateRisk = 2,

        /// <summary>
        /// Low-risk modalities that rarely contain burned-in text:
        /// CT, MR, PT, etc.
        /// </summary>
        LowRisk = 4,

        /// <summary>All modality risk levels.</summary>
        All = HighRisk | ModerateRisk | LowRisk
    }

    /// <summary>
    /// Configuration options for OCR-based burned-in PHI detection in DICOM pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control the behavior of the OcrScanner including
    /// confidence thresholds, modality filtering, and allow/deny lists for
    /// distinguishing PHI from legitimate annotations.
    /// </para>
    /// <para>
    /// Edge regions (corners and borders) use a lower confidence threshold because
    /// burned-in text is most commonly placed at image edges.
    /// </para>
    /// </remarks>
    public sealed class OcrScannerOptions
    {
        /// <summary>
        /// Gets or sets the minimum confidence score (0.0 - 1.0) for detections in the center
        /// of the image. Default is 0.6.
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.6f;

        /// <summary>
        /// Gets or sets the minimum confidence score (0.0 - 1.0) for detections in edge
        /// and corner zones where burned-in text typically lives. Default is 0.4.
        /// </summary>
        public float EdgeConfidenceThreshold { get; set; } = 0.4f;

        /// <summary>
        /// Gets or sets the percentage (0.0 - 0.5) from each image edge that defines the
        /// edge zone. Default is 0.15 (15% from each edge).
        /// </summary>
        public float EdgeMarginPercent { get; set; } = 0.15f;

        /// <summary>
        /// Gets or sets which modality risk categories to scan. Default is
        /// <see cref="OcrScanModality.HighRisk"/> | <see cref="OcrScanModality.ModerateRisk"/>.
        /// </summary>
        public OcrScanModality ScanModalities { get; set; } = OcrScanModality.HighRisk | OcrScanModality.ModerateRisk;

        /// <summary>
        /// Gets or sets the Tesseract language code. Default is "eng" (English).
        /// </summary>
        public string Language { get; set; } = "eng";

        /// <summary>
        /// Gets or sets the path to the tessdata directory, or null to use bundled data.
        /// </summary>
        public string? TessdataPath { get; set; }

        /// <summary>
        /// Gets or sets the Tesseract page segmentation mode. Default is 11
        /// (PSM_SPARSE_TEXT) which is optimized for scattered text overlays.
        /// </summary>
        public int PageSegMode { get; set; } = 11;

        /// <summary>
        /// Gets or sets the maximum number of detections to return per frame.
        /// Default is 200.
        /// </summary>
        public int MaxDetectionsPerFrame { get; set; } = 200;

        /// <summary>
        /// Gets or sets whether to decompress encapsulated (compressed) pixel data
        /// using <see cref="Codecs.CodecRegistry"/> before OCR scanning. Default is true.
        /// </summary>
        /// <remarks>
        /// When false, compressed frames are skipped with a warning rather than
        /// decompressed automatically.
        /// </remarks>
        public bool DecompressForOcr { get; set; } = true;

        /// <summary>
        /// Gets or sets text patterns that are NOT PHI and should be excluded from
        /// filtered detections. Uses case-insensitive matching.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Initialised from <see cref="DefaultNonPhiAllowlist"/> which contains common
        /// orientation markers (L, R, P, A), measurement units (cm, mm, Hz),
        /// medical abbreviations (HR, BP, SpO2), and imaging labels (GAIN, DEPTH).
        /// </para>
        /// <para>
        /// Add entries to reduce false positives from legitimate imaging annotations.
        /// </para>
        /// </remarks>
        public HashSet<string> Allowlist { get; set; } = DefaultNonPhiAllowlist;

        /// <summary>
        /// Gets or sets text patterns that ARE always PHI, taking precedence over
        /// the <see cref="Allowlist"/>. Null means no deny list is applied.
        /// </summary>
        public HashSet<string>? Denylist { get; set; }

        /// <summary>
        /// Gets a new <see cref="HashSet{T}"/> containing default non-PHI text patterns.
        /// </summary>
        /// <remarks>
        /// Includes:
        /// <list type="bullet">
        /// <item><description>Single-letter orientation markers: L, R, P, A, S, I, H, F</description></item>
        /// <item><description>Measurement units: cm, mm, m, Hz, kHz, MHz, bpm, ml, mg, kg, dB, sec, min, ms</description></item>
        /// <item><description>Medical abbreviations: HR, BP, SpO2, ECG, EKG, BMI, BMR, PRF, MI, TI, TIS, TIB, TIC</description></item>
        /// <item><description>Imaging labels: GAIN, DEPTH, FREQ, PWR, DYN, MAP, DR, FR, THI</description></item>
        /// <item><description>Directional terms: SUP, INF, ANT, POST, LAT, MED, PROX, DIST</description></item>
        /// </list>
        /// </remarks>
        public static HashSet<string> DefaultNonPhiAllowlist => new(StringComparer.OrdinalIgnoreCase)
        {
            // Single-letter orientation markers
            "L", "R", "P", "A", "S", "I", "H", "F",

            // Measurement units
            "cm", "mm", "m", "Hz", "kHz", "MHz", "bpm", "ml", "mg", "kg", "dB",
            "sec", "min", "ms",

            // Common medical abbreviations
            "HR", "BP", "SpO2", "ECG", "EKG", "BMI", "BMR", "PRF",
            "MI", "TI", "TIS", "TIB", "TIC",

            // Imaging labels
            "GAIN", "DEPTH", "FREQ", "PWR", "DYN", "MAP", "DR", "FR", "THI",

            // Directional
            "SUP", "INF", "ANT", "POST", "LAT", "MED", "PROX", "DIST"
        };
    }
}
