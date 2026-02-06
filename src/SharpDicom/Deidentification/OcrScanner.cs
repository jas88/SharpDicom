using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDicom.Codecs;
using SharpDicom.Data;

using DataPixelDataInfo = SharpDicom.Data.PixelDataInfo;
using CodecPixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Detects burned-in text in DICOM pixel data using Tesseract OCR.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OcrScanner uses Tesseract OCR to detect text regions in pixel data,
    /// applies allow/deny filtering to distinguish PHI from legitimate annotations
    /// (orientation markers, measurement units), and returns results that can drive
    /// <see cref="PixelDataRedactor"/> for automated redaction.
    /// </para>
    /// <para>
    /// Supported pixel formats: 8-bit grayscale, 16-bit grayscale (windowed to 8-bit),
    /// RGB (converted to grayscale via luminance formula). MONOCHROME1 images are
    /// automatically inverted before OCR.
    /// </para>
    /// <para>
    /// When pixel data is encapsulated (compressed), OcrScanner can optionally
    /// decompress it via <see cref="CodecRegistry"/> before scanning. This is controlled
    /// by <see cref="OcrScannerOptions.DecompressForOcr"/>.
    /// </para>
    /// <para>
    /// This class is NOT thread-safe. Each thread should create and use its own
    /// OcrScanner instance.
    /// </para>
    /// </remarks>
    public sealed class OcrScanner : IDisposable
    {
        private readonly OcrScannerOptions _options;
        private IntPtr _handle;
        private bool _disposed;

        // Common warning messages (CA1861: prefer static readonly over inline array allocations)
        private static readonly string[] WarningModalitySkipped = { "Modality not in configured scan modality set; skipped." };
        private static readonly string[] WarningNoDimensions = { "No pixel data dimensions found in dataset (missing Rows/Columns)." };
        private static readonly string[] WarningNoPixelData = { "No pixel data found in dataset." };
        private static readonly string[] WarningCompressedSkipped = { "Pixel data is compressed and DecompressForOcr is false. Decompress before scanning." };
        private static readonly string[] WarningEmptyPixelData = { "Pixel data is empty." };

        // Well-known modality sets matching BurnedInAnnotationDetector categories
        private static readonly HashSet<string> HighRiskModalities = new(StringComparer.OrdinalIgnoreCase)
        {
            "US", "ES", "SC", "XC", "GM", "SM", "OP", "OPT", "ECG", "HD"
        };

        private static readonly HashSet<string> ModerateRiskModalities = new(StringComparer.OrdinalIgnoreCase)
        {
            "XA", "RF", "MG", "DX", "CR", "PX", "IO"
        };

        /// <summary>
        /// Creates a new <see cref="OcrScanner"/> instance.
        /// </summary>
        /// <param name="options">
        /// Scanner configuration, or null to use default options.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the Tesseract native library is not available.
        /// </exception>
        public OcrScanner(OcrScannerOptions? options = null)
        {
            _options = options ?? new OcrScannerOptions();

            if (TessInterop.tess_available() == 0)
            {
                throw new InvalidOperationException(
                    "Tesseract OCR is not available. Install the SharpDicom.Codecs native package with Tesseract support.");
            }

            _handle = TessInterop.tess_create();
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Failed to create Tesseract instance. The native library may be corrupted.");
            }

            var tessdataPath = _options.TessdataPath ?? FindBundledTessdata();
            int initResult = TessInterop.tess_init(_handle, tessdataPath, _options.Language);
            if (initResult != 0)
            {
                TessInterop.tess_delete(_handle);
                _handle = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"Failed to initialise Tesseract with language '{_options.Language}'. " +
                    $"Ensure eng.traineddata is available in tessdata directory" +
                    (tessdataPath != null ? $" ({tessdataPath})." : "."));
            }

            TessInterop.tess_set_page_seg_mode(_handle, _options.PageSegMode);
        }

        /// <summary>
        /// Scans a DICOM dataset's pixel data for burned-in text.
        /// </summary>
        /// <param name="dataset">The dataset containing pixel data to scan.</param>
        /// <returns>An <see cref="OcrScanResult"/> containing all detections and filtered PHI candidates.</returns>
        /// <exception cref="ObjectDisposedException">The scanner has been disposed.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dataset"/> is null.</exception>
        public OcrScanResult ScanDataset(DicomDataset dataset)
        {
#if NET6_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (_disposed) throw new ObjectDisposedException(nameof(OcrScanner));
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
#endif

            var stopwatch = Stopwatch.StartNew();
            var warnings = new List<string>();

            // Check modality against ScanModalities
            if (!ShouldScanModality(dataset))
            {
                stopwatch.Stop();
                return new OcrScanResult(
                    Array.Empty<OcrDetection>(),
                    Array.Empty<OcrDetection>(),
                    totalFramesScanned: 0,
                    framesWithDetections: 0,
                    scanDuration: stopwatch.Elapsed,
                    warnings: WarningModalitySkipped);
            }

            // Get pixel data info
            var info = DataPixelDataInfo.FromDataset(dataset);
            if (!info.HasImageDimensions)
            {
                stopwatch.Stop();
                return new OcrScanResult(
                    Array.Empty<OcrDetection>(),
                    Array.Empty<OcrDetection>(),
                    totalFramesScanned: 0,
                    framesWithDetections: 0,
                    scanDuration: stopwatch.Elapsed,
                    warnings: WarningNoDimensions);
            }

            // Get pixel data element
            var pixelDataElement = dataset.GetPixelData();
            if (pixelDataElement == null)
            {
                stopwatch.Stop();
                return new OcrScanResult(
                    Array.Empty<OcrDetection>(),
                    Array.Empty<OcrDetection>(),
                    totalFramesScanned: 0,
                    framesWithDetections: 0,
                    scanDuration: stopwatch.Elapsed,
                    warnings: WarningNoPixelData);
            }

            // Handle compressed (encapsulated) data
            byte[]? decompressedData = null;
            if (pixelDataElement.IsEncapsulated)
            {
                if (!_options.DecompressForOcr)
                {
                    stopwatch.Stop();
                    return new OcrScanResult(
                        Array.Empty<OcrDetection>(),
                        Array.Empty<OcrDetection>(),
                        totalFramesScanned: 0,
                        framesWithDetections: 0,
                        scanDuration: stopwatch.Elapsed,
                        warnings: WarningCompressedSkipped);
                }

                decompressedData = DecompressPixelData(dataset, pixelDataElement, info, warnings);
                if (decompressedData == null)
                {
                    stopwatch.Stop();
                    return new OcrScanResult(
                        Array.Empty<OcrDetection>(),
                        Array.Empty<OcrDetection>(),
                        totalFramesScanned: 0,
                        framesWithDetections: 0,
                        scanDuration: stopwatch.Elapsed,
                        warnings: warnings);
                }
            }

            int width = info.Columns!.Value;
            int height = info.Rows!.Value;
            int numberOfFrames = info.NumberOfFrames ?? 1;
            var photometricInterpretation = info.PhotometricInterpretation ?? "MONOCHROME2";
            int bitsAllocated = info.BitsAllocated ?? 8;
            int samplesPerPixel = info.SamplesPerPixel ?? 1;
            int bytesPerSample = (bitsAllocated + 7) / 8;
            int bytesPerPixel = bytesPerSample * samplesPerPixel;
            long frameSize = (long)width * height * bytesPerPixel;

            var allDetections = new List<OcrDetection>();
            int framesWithDetections = 0;

            // Get pixel data bytes
            ReadOnlyMemory<byte> pixelBytes;
            if (decompressedData != null)
            {
                pixelBytes = decompressedData;
            }
            else
            {
                pixelBytes = pixelDataElement.RawValue;
            }

            if (pixelBytes.IsEmpty)
            {
                stopwatch.Stop();
                return new OcrScanResult(
                    Array.Empty<OcrDetection>(),
                    Array.Empty<OcrDetection>(),
                    totalFramesScanned: 0,
                    framesWithDetections: 0,
                    scanDuration: stopwatch.Elapsed,
                    warnings: WarningEmptyPixelData);
            }

            // Scan each frame
            for (int frameIndex = 0; frameIndex < numberOfFrames; frameIndex++)
            {
                long frameOffset = frameIndex * frameSize;
                if (frameOffset + frameSize > pixelBytes.Length)
                {
                    warnings.Add($"Frame {frameIndex}: insufficient pixel data (expected {frameOffset + frameSize} bytes, have {pixelBytes.Length}).");
                    break;
                }

                var frameData = pixelBytes.Slice((int)frameOffset, (int)frameSize);

                // Prepare 8-bit grayscale for Tesseract
                byte[] grayscale;
                try
                {
                    grayscale = PrepareFrameForOcr(
                        frameData.Span, width, height,
                        bitsAllocated, samplesPerPixel,
                        photometricInterpretation, dataset);
                }
                catch (NotSupportedException ex)
                {
                    warnings.Add($"Frame {frameIndex}: {ex.Message}");
                    continue;
                }

                // Run OCR on this frame
                var frameDetections = RecognizeFrame(
                    grayscale, width, height, frameIndex, warnings);

                if (frameDetections.Count > 0)
                {
                    allDetections.AddRange(frameDetections);
                    framesWithDetections++;
                }
            }

            // Apply allow/deny filtering
            var filteredDetections = ApplyAllowDenyFilter(allDetections);

            stopwatch.Stop();

            return new OcrScanResult(
                allDetections,
                filteredDetections,
                totalFramesScanned: numberOfFrames,
                framesWithDetections: framesWithDetections,
                scanDuration: stopwatch.Elapsed,
                warnings: warnings.Count > 0 ? warnings : null);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                TessInterop.tess_delete(_handle);
                _handle = IntPtr.Zero;
            }
        }

        #region Private Methods

        private static string? FindBundledTessdata()
        {
            // Check common locations for tessdata
            string[] candidates =
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "tessdata"),
            };

            foreach (var candidate in candidates)
            {
                var engPath = System.IO.Path.Combine(candidate, "eng.traineddata");
                if (System.IO.File.Exists(engPath))
                {
                    return candidate;
                }
            }

            // Check TESSDATA_PREFIX environment variable
            var envPrefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
            if (!string.IsNullOrEmpty(envPrefix))
            {
                var envPath = System.IO.Path.Combine(envPrefix!, "eng.traineddata");
                if (System.IO.File.Exists(envPath))
                {
                    return envPrefix;
                }
                // Even if file not found, honour the env var (tess_init will report the error)
                return envPrefix;
            }

            return null;
        }

        private bool ShouldScanModality(DicomDataset dataset)
        {
            if (_options.ScanModalities == OcrScanModality.All)
                return true;

            var modality = dataset.GetString(DicomTag.Modality)?.Trim();
            if (string.IsNullOrEmpty(modality))
            {
                // Unknown modality: scan if HighRisk is enabled (conservative)
                return (_options.ScanModalities & OcrScanModality.HighRisk) != 0;
            }

            if (HighRiskModalities.Contains(modality!))
                return (_options.ScanModalities & OcrScanModality.HighRisk) != 0;

            if (ModerateRiskModalities.Contains(modality!))
                return (_options.ScanModalities & OcrScanModality.ModerateRisk) != 0;

            // All other modalities are low risk
            return (_options.ScanModalities & OcrScanModality.LowRisk) != 0;
        }

        private static byte[]? DecompressPixelData(
            DicomDataset dataset,
            DicomPixelDataElement pixelDataElement,
            DataPixelDataInfo info,
            List<string> warnings)
        {
            // Determine transfer syntax from dataset
            var tsUidStr = dataset.GetString(DicomTag.TransferSyntaxUID)?.Trim();
            if (string.IsNullOrEmpty(tsUidStr))
            {
                warnings.Add("Cannot decompress: Transfer Syntax UID not found in dataset.");
                return null;
            }

            TransferSyntax ts;
            try
            {
                ts = TransferSyntax.FromUID(new DicomUID(tsUidStr!));
            }
            catch
            {
                warnings.Add($"Cannot decompress: unrecognised transfer syntax '{tsUidStr}'.");
                return null;
            }

            var codec = CodecRegistry.GetCodec(ts);
            if (codec == null)
            {
                warnings.Add($"Cannot decompress: no codec registered for transfer syntax '{ts.UID}'.");
                return null;
            }

            if (!codec.Capabilities.CanDecode)
            {
                warnings.Add($"Cannot decompress: codec '{codec.Name}' does not support decoding.");
                return null;
            }

            var fragments = pixelDataElement.Fragments;
            if (fragments == null)
            {
                warnings.Add("Cannot decompress: encapsulated pixel data has no fragments.");
                return null;
            }

            // Build codec PixelDataInfo from dataset info
            var codecInfo = new CodecPixelDataInfo(
                info.Rows ?? 0,
                info.Columns ?? 0,
                info.BitsAllocated ?? 16,
                info.BitsStored ?? (info.BitsAllocated ?? 16),
                info.HighBit ?? (ushort)((info.BitsStored ?? (info.BitsAllocated ?? 16)) - 1),
                info.SamplesPerPixel ?? 1,
                info.PixelRepresentation ?? 0,
                info.PlanarConfiguration ?? 0,
                info.NumberOfFrames ?? 1);

            int numberOfFrames = info.NumberOfFrames ?? 1;
            int frameSize = codecInfo.FrameSize;
            var decompressed = new byte[numberOfFrames * frameSize];

            for (int frame = 0; frame < numberOfFrames; frame++)
            {
                var destination = new Memory<byte>(decompressed, frame * frameSize, frameSize);
                var result = codec.Decode(fragments, codecInfo, frame, destination);
                if (!result.Success)
                {
                    warnings.Add($"Frame {frame}: decompression failed" +
                        (result.Diagnostic != null ? $": {result.Diagnostic.Value.Message}" : "."));
                    return null;
                }
            }

            return decompressed;
        }

        private static byte[] PrepareFrameForOcr(
            ReadOnlySpan<byte> frameData,
            int width, int height,
            int bitsAllocated, int samplesPerPixel,
            string photometricInterpretation,
            DicomDataset dataset)
        {
            int pixelCount = width * height;

            // 8-bit grayscale
            if (bitsAllocated == 8 && samplesPerPixel == 1)
            {
                var result = new byte[pixelCount];
                var src = frameData.Slice(0, Math.Min(pixelCount, frameData.Length));
                src.CopyTo(result);

                // Invert for MONOCHROME1 (white = low pixel value)
                if (IsMonochrome1(photometricInterpretation))
                {
                    InvertGrayscale8(result);
                }

                return result;
            }

            // 16-bit grayscale
            if (bitsAllocated == 16 && samplesPerPixel == 1)
            {
                return Window16BitTo8Bit(
                    frameData, pixelCount,
                    photometricInterpretation, dataset);
            }

            // RGB (3 samples per pixel, 8 bits each)
            if (samplesPerPixel == 3 && bitsAllocated == 8)
            {
                return ConvertRgbToGrayscale(frameData, pixelCount);
            }

            throw new NotSupportedException(
                $"Unsupported pixel format for OCR: BitsAllocated={bitsAllocated}, " +
                $"SamplesPerPixel={samplesPerPixel}, " +
                $"PhotometricInterpretation={photometricInterpretation}.");
        }

        private static bool IsMonochrome1(string photometricInterpretation)
        {
#if NET6_0_OR_GREATER
            return photometricInterpretation.Contains("MONOCHROME1", StringComparison.OrdinalIgnoreCase);
#else
            return photometricInterpretation.IndexOf("MONOCHROME1", StringComparison.OrdinalIgnoreCase) >= 0;
#endif
        }

        private static void InvertGrayscale8(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(255 - data[i]);
            }
        }

        private static byte[] Window16BitTo8Bit(
            ReadOnlySpan<byte> frameData,
            int pixelCount,
            string photometricInterpretation,
            DicomDataset dataset)
        {
            // Window Center/Width tags (0028,1050) and (0028,1051)
            var windowCenterTag = new DicomTag(0x0028, 0x1050);
            var windowWidthTag = new DicomTag(0x0028, 0x1051);

            var centerStr = dataset.GetString(windowCenterTag)?.Trim();
            var widthStr = dataset.GetString(windowWidthTag)?.Trim();

            // Parse first value if multi-valued (backslash-separated)
            if (centerStr != null)
            {
                int sep = centerStr.IndexOf('\\');
                if (sep >= 0)
                    centerStr = centerStr.Substring(0, sep);
            }
            if (widthStr != null)
            {
                int sep = widthStr.IndexOf('\\');
                if (sep >= 0)
                    widthStr = widthStr.Substring(0, sep);
            }

            double windowCenter = 0;
            double windowWidth = 0;
            bool hasWindow = double.TryParse(centerStr, out windowCenter) &&
                             double.TryParse(widthStr, out windowWidth) &&
                             windowWidth > 0;

            var result = new byte[pixelCount];
            int byteCount = Math.Min(pixelCount * 2, frameData.Length);

            if (hasWindow)
            {
                // Apply window/level
                double lower = windowCenter - windowWidth / 2.0;
                double upper = windowCenter + windowWidth / 2.0;
                double range = upper - lower;

                for (int i = 0; i < pixelCount && (i * 2 + 1) < byteCount; i++)
                {
                    ushort val = (ushort)(frameData[i * 2] | (frameData[i * 2 + 1] << 8));
                    double normalised;
                    if (val <= lower) normalised = 0.0;
                    else if (val >= upper) normalised = 1.0;
                    else normalised = (val - lower) / range;
                    result[i] = (byte)(normalised * 255.0);
                }
            }
            else
            {
                // Full-range normalisation: find min/max
                ushort min = ushort.MaxValue;
                ushort max = ushort.MinValue;

                for (int i = 0; i < pixelCount && (i * 2 + 1) < byteCount; i++)
                {
                    ushort val = (ushort)(frameData[i * 2] | (frameData[i * 2 + 1] << 8));
                    if (val < min) min = val;
                    if (val > max) max = val;
                }

                double range = max - min;
                if (range < 1.0) range = 1.0;

                for (int i = 0; i < pixelCount && (i * 2 + 1) < byteCount; i++)
                {
                    ushort val = (ushort)(frameData[i * 2] | (frameData[i * 2 + 1] << 8));
                    result[i] = (byte)(((val - min) / range) * 255.0);
                }
            }

            // Invert for MONOCHROME1
            if (IsMonochrome1(photometricInterpretation))
            {
                InvertGrayscale8(result);
            }

            return result;
        }

        private static byte[] ConvertRgbToGrayscale(ReadOnlySpan<byte> frameData, int pixelCount)
        {
            var result = new byte[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 3;
                if (offset + 2 >= frameData.Length) break;

                byte r = frameData[offset];
                byte g = frameData[offset + 1];
                byte b = frameData[offset + 2];

                // ITU-R BT.601 luminance formula
                result[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            return result;
        }

        private unsafe List<OcrDetection> RecognizeFrame(
            byte[] grayscaleData, int width, int height,
            int frameIndex, List<string> warnings)
        {
            var detections = new List<OcrDetection>();
            int maxDetections = _options.MaxDetectionsPerFrame;

            fixed (byte* imagePtr = grayscaleData)
            {
                TessInterop.tess_set_image(_handle, imagePtr, width, height, 1, width);

                int recognizeResult = TessInterop.tess_recognize(_handle);
                if (recognizeResult != 0)
                {
                    warnings.Add($"Frame {frameIndex}: Tesseract recognition failed (error code {recognizeResult}).");
                    TessInterop.tess_clear(_handle);
                    return detections;
                }

                var nativeResults = new TessDetectionResult[maxDetections];
                fixed (TessDetectionResult* resultsPtr = nativeResults)
                {
                    int getResult = TessInterop.tess_get_detections(
                        _handle, resultsPtr, maxDetections, out int actualCount);

                    if (getResult != 0)
                    {
                        warnings.Add($"Frame {frameIndex}: failed to retrieve detection results.");
                        TessInterop.tess_clear(_handle);
                        return detections;
                    }

                    for (int i = 0; i < actualCount; i++)
                    {
                        ref var native = ref nativeResults[i];
                        string? text = null;

                        try
                        {
                            if (native.Text != IntPtr.Zero)
                            {
#if NET6_0_OR_GREATER
                                text = Marshal.PtrToStringUTF8(native.Text);
#else
                                text = PtrToStringUtf8(native.Text);
#endif
                            }
                        }
                        finally
                        {
                            if (native.Text != IntPtr.Zero)
                            {
                                TessInterop.tess_free_text(native.Text);
                            }
                        }

                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        // Convert confidence from 0-100 to 0.0-1.0
                        float confidence = native.Confidence / 100.0f;

                        // Build bounding box
                        int bboxX = native.Left;
                        int bboxY = native.Top;
                        int bboxW = native.Right - native.Left;
                        int bboxH = native.Bottom - native.Top;

                        if (bboxW <= 0 || bboxH <= 0)
                            continue;

                        // Determine if detection is in edge region
                        bool isEdge = IsInEdgeRegion(bboxX, bboxY, bboxW, bboxH, width, height);

                        // Apply appropriate confidence threshold
                        float threshold = isEdge
                            ? _options.EdgeConfidenceThreshold
                            : _options.ConfidenceThreshold;

                        if (confidence < threshold)
                            continue;

                        var region = new RedactionRegion(bboxX, bboxY, bboxW, bboxH, frameIndex);

                        detections.Add(new OcrDetection(
                            text!,
                            confidence,
                            region,
                            frameIndex,
                            isEdge));
                    }
                }

                TessInterop.tess_clear(_handle);
            }

            return detections;
        }

        private bool IsInEdgeRegion(int x, int y, int w, int h, int imageWidth, int imageHeight)
        {
            float marginX = imageWidth * _options.EdgeMarginPercent;
            float marginY = imageHeight * _options.EdgeMarginPercent;

            // Check if the center of the detection is in an edge zone
            float centerX = x + w / 2.0f;
            float centerY = y + h / 2.0f;

            return centerX < marginX ||
                   centerX > imageWidth - marginX ||
                   centerY < marginY ||
                   centerY > imageHeight - marginY;
        }

        private List<OcrDetection> ApplyAllowDenyFilter(List<OcrDetection> detections)
        {
            var filtered = new List<OcrDetection>();
            var allowlist = _options.Allowlist;
            var denylist = _options.Denylist;

            foreach (var detection in detections)
            {
                var text = detection.Text.Trim();

                // Skip purely numeric strings (measurements like "3.2" are not PHI)
                if (IsNumericOrMeasurement(text))
                    continue;

                // Check denylist first (takes precedence)
                if (denylist != null && denylist.Contains(text))
                {
                    filtered.Add(detection);
                    continue;
                }

                // Check allowlist
                if (allowlist.Contains(text))
                    continue;

                // Not in allowlist and not filtered out: this is a PHI candidate
                filtered.Add(detection);
            }

            return filtered;
        }

        private static bool IsNumericOrMeasurement(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            // Check if the text is purely numeric (with optional decimal point, sign, percent)
            foreach (char c in text)
            {
                if (char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+' || c == '%' || c == ' ')
                    continue;
                return false;
            }

            return true;
        }

#if !NET6_0_OR_GREATER
        private static unsafe string? PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;

            // Find null terminator
            int len = 0;
            byte* p = (byte*)ptr;
            while (p[len] != 0) len++;

            if (len == 0) return string.Empty;

            return System.Text.Encoding.UTF8.GetString(p, len);
        }
#endif

        #endregion

        #region Native Interop

        /// <summary>
        /// Internal P/Invoke declarations for Tesseract OCR native functions.
        /// These mirror the declarations in SharpDicom.Codecs.Native.Interop.TesseractNativeMethods
        /// but are defined here to avoid circular project references.
        /// Uses DllImport uniformly for simplicity in a private nested class.
        /// </summary>
        private static unsafe class TessInterop
        {
            private const string LibraryName = "sharpdicom_codecs";

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_create")]
            internal static extern IntPtr tess_create();

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_delete")]
            internal static extern void tess_delete(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_init",
                BestFitMapping = false, ThrowOnUnmappableChar = true)]
            internal static extern int tess_init(IntPtr handle,
                [MarshalAs(UnmanagedType.LPStr)] string? datapath,
                [MarshalAs(UnmanagedType.LPStr)] string? language);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_set_image")]
            internal static extern void tess_set_image(
                IntPtr handle, byte* imagedata,
                int width, int height,
                int bytes_per_pixel, int bytes_per_line);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_set_page_seg_mode")]
            internal static extern void tess_set_page_seg_mode(IntPtr handle, int mode);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_recognize")]
            internal static extern int tess_recognize(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_get_detections")]
            internal static extern int tess_get_detections(
                IntPtr handle,
                TessDetectionResult* results, int maxResults,
                out int actualCount);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_free_text")]
            internal static extern void tess_free_text(IntPtr text);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_clear")]
            internal static extern void tess_clear(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_available")]
            internal static extern int tess_available();
        }

        /// <summary>
        /// Native detection result structure matching the C API's TessDetectionResult.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct TessDetectionResult
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public float Confidence;
            public IntPtr Text;
        }

        #endregion
    }
}
