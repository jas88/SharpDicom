using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Deidentification;
using SharpDicom.Internal;
using SharpDicom.IO;

namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Fluent builder for creating valid video DICOM files with correct SOP class,
    /// transfer syntax, metadata, and encapsulated pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// VideoDicomBuilder simplifies the creation of video DICOM files by handling the
    /// complex metadata requirements: SOP class selection, transfer syntax mapping,
    /// required Image Pixel Module attributes, and encapsulated pixel data packaging.
    /// </para>
    /// <para>
    /// The builder follows the same fluent pattern as <see cref="DicomDeidentifierBuilder"/>
    /// for API consistency.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var file = new VideoDicomBuilder()
    ///     .WithSopClass(VideoSopClass.Endoscopic)
    ///     .WithTransferSyntax(TransferSyntax.H264HighProfile41)
    ///     .WithDimensions(1920, 1080)
    ///     .WithFrameRate(30.0)
    ///     .WithPixelData(encodedVideoBytes)
    ///     .WithPatient("12345", "DOE^JOHN")
    ///     .Build();
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class VideoDicomBuilder
    {
        private VideoSopClass? _sopClass;
        private TransferSyntax? _transferSyntax;
        private DicomDataset? _templateDataset;
        private double _frameRate;
        private int _width;
        private int _height;
        private byte[]? _encodedVideoData;
        private string? _sopInstanceUid;
        private string? _seriesInstanceUid;
        private string? _studyInstanceUid;
        private string? _patientId;
        private string? _patientName;
        private int _numberOfFrames;

        /// <summary>
        /// Sets the video SOP class for the DICOM file.
        /// </summary>
        /// <param name="sopClass">The video SOP class to use.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithSopClass(VideoSopClass sopClass)
        {
            _sopClass = sopClass;
            return this;
        }

        /// <summary>
        /// Sets the transfer syntax for encoding.
        /// </summary>
        /// <param name="ts">The transfer syntax (must be a video-compatible transfer syntax).</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithTransferSyntax(TransferSyntax ts)
        {
            _transferSyntax = ts;
            return this;
        }

        /// <summary>
        /// Copies patient and study-level attributes from a template dataset.
        /// </summary>
        /// <param name="template">
        /// The template dataset to copy from. Patient-level tags (0010,xxxx) and
        /// study-level tags (0008,0020 StudyDate, 0008,0030 StudyTime, 0020,000D StudyInstanceUID,
        /// 0008,0050 AccessionNumber) are copied.
        /// </param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithPatientFromTemplate(DicomDataset template)
        {
            ThrowHelpers.ThrowIfNull(template, nameof(template));
            _templateDataset = template;
            return this;
        }

        /// <summary>
        /// Sets the video frame rate in frames per second.
        /// </summary>
        /// <param name="fps">Frame rate (must be positive).</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithFrameRate(double fps)
        {
            _frameRate = fps;
            return this;
        }

        /// <summary>
        /// Sets the video dimensions in pixels.
        /// </summary>
        /// <param name="width">Frame width (Columns) in pixels.</param>
        /// <param name="height">Frame height (Rows) in pixels.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithDimensions(int width, int height)
        {
            _width = width;
            _height = height;
            return this;
        }

        /// <summary>
        /// Sets the encoded video bitstream data.
        /// </summary>
        /// <param name="encodedVideoData">
        /// The complete encoded video bitstream (e.g., H.264 NAL units, MPEG-2 elementary stream).
        /// This is packaged as a single encapsulated fragment in the DICOM pixel data.
        /// </param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithPixelData(byte[] encodedVideoData)
        {
            ThrowHelpers.ThrowIfNull(encodedVideoData, nameof(encodedVideoData));
            _encodedVideoData = encodedVideoData;
            return this;
        }

        /// <summary>
        /// Sets the SOP Instance UID. If not set, a UID is auto-generated.
        /// </summary>
        /// <param name="uid">A valid DICOM UID string.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithSopInstanceUid(string uid)
        {
            _sopInstanceUid = uid;
            return this;
        }

        /// <summary>
        /// Sets the Series Instance UID. If not set, a UID is auto-generated.
        /// </summary>
        /// <param name="uid">A valid DICOM UID string.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithSeriesInstanceUid(string uid)
        {
            _seriesInstanceUid = uid;
            return this;
        }

        /// <summary>
        /// Sets the Study Instance UID. If not set, a UID is auto-generated.
        /// </summary>
        /// <param name="uid">A valid DICOM UID string.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithStudyInstanceUid(string uid)
        {
            _studyInstanceUid = uid;
            return this;
        }

        /// <summary>
        /// Sets patient identification attributes.
        /// </summary>
        /// <param name="patientId">The Patient ID (0010,0020).</param>
        /// <param name="patientName">The Patient Name (0010,0010) in DICOM PN format (e.g., "DOE^JOHN").</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithPatient(string patientId, string patientName)
        {
            _patientId = patientId;
            _patientName = patientName;
            return this;
        }

        /// <summary>
        /// Sets the number of frames in the video.
        /// </summary>
        /// <param name="numberOfFrames">The total frame count. If not set, defaults to 1.</param>
        /// <returns>This builder for chaining.</returns>
        public VideoDicomBuilder WithNumberOfFrames(int numberOfFrames)
        {
            _numberOfFrames = numberOfFrames;
            return this;
        }

        /// <summary>
        /// Builds a complete video DICOM file from the configured parameters.
        /// </summary>
        /// <returns>A valid <see cref="DicomFile"/> ready for saving or transmission.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when required fields are not set: SOP class, pixel data, dimensions, or frame rate.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The built file includes:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Correct SOP Class UID and Modality for the video type.</description></item>
        /// <item><description>Auto-generated UIDs (2.25.{uuid}) for any unset instance UIDs.</description></item>
        /// <item><description>Image Pixel Module attributes (Rows, Columns, BitsAllocated, etc.).</description></item>
        /// <item><description>Cine Module attributes (FrameTime, CineRate).</description></item>
        /// <item><description>Encapsulated pixel data with the video bitstream as a single fragment.</description></item>
        /// <item><description>Properly generated File Meta Information.</description></item>
        /// </list>
        /// </remarks>
        public DicomFile Build()
        {
            // Validate required fields
            if (!_sopClass.HasValue)
                throw new InvalidOperationException("SOP class is required. Call WithSopClass().");
            if (_encodedVideoData == null || _encodedVideoData.Length == 0)
                throw new InvalidOperationException("Encoded video data is required. Call WithPixelData().");
            if (_width <= 0)
                throw new InvalidOperationException("Width must be positive. Call WithDimensions().");
            if (_height <= 0)
                throw new InvalidOperationException("Height must be positive. Call WithDimensions().");
            if (_frameRate <= 0)
                throw new InvalidOperationException("Frame rate must be positive. Call WithFrameRate().");

            var sopClass = _sopClass.Value;
            var sopClassUid = GetSopClassUid(sopClass);
            var modality = GetModality(sopClass);
            var transferSyntax = _transferSyntax ?? TransferSyntax.H264HighProfile41;

            // Auto-generate UIDs if not provided
            var sopInstanceUid = _sopInstanceUid ?? UidGenerator.GenerateUid();
            var seriesInstanceUid = _seriesInstanceUid ?? UidGenerator.GenerateUid();
            var studyInstanceUid = _studyInstanceUid ?? UidGenerator.GenerateUid();

            // Build dataset
            var dataset = new DicomDataset();

            // Copy template patient/study attributes first (explicit values below will override)
            if (_templateDataset != null)
            {
                CopyTemplateAttributes(dataset, _templateDataset);
            }

            // SOP Common Module
            AddStringElement(dataset, DicomTag.SOPClassUID, DicomVR.UI, sopClassUid);
            AddStringElement(dataset, DicomTag.SOPInstanceUID, DicomVR.UI, sopInstanceUid);

            // General Study Module
            AddStringElement(dataset, DicomTag.StudyInstanceUID, DicomVR.UI, studyInstanceUid);

            // General Series Module
            AddStringElement(dataset, DicomTag.SeriesInstanceUID, DicomVR.UI, seriesInstanceUid);
            AddStringElement(dataset, DicomTag.Modality, DicomVR.CS, modality);

            // Patient Module (explicit values override template)
            if (_patientId != null)
                AddStringElement(dataset, DicomTag.PatientID, DicomVR.LO, _patientId);
            if (_patientName != null)
                AddStringElement(dataset, DicomTag.PatientName, DicomVR.PN, _patientName);

            // Image Pixel Module
            AddUInt16Element(dataset, DicomTag.Rows, (ushort)_height);
            AddUInt16Element(dataset, DicomTag.Columns, (ushort)_width);
            AddUInt16Element(dataset, DicomTag.BitsAllocated, 8);
            AddUInt16Element(dataset, DicomTag.BitsStored, 8);
            AddUInt16Element(dataset, DicomTag.HighBit, 7);
            AddUInt16Element(dataset, DicomTag.PixelRepresentation, 0); // Unsigned
            AddUInt16Element(dataset, DicomTag.SamplesPerPixel, 3);
            AddUInt16Element(dataset, DicomTag.PlanarConfiguration, 0); // Color-by-pixel

            // Photometric interpretation depends on codec
            var photometric = GetPhotometricInterpretation(transferSyntax);
            AddStringElement(dataset, DicomTag.PhotometricInterpretation, DicomVR.CS, photometric);

            // Multi-frame Module
            int numberOfFrames = _numberOfFrames > 0 ? _numberOfFrames : 1;
            AddStringElement(dataset, DicomTag.NumberOfFrames, DicomVR.IS,
                numberOfFrames.ToString(CultureInfo.InvariantCulture));

            // Cine Module
            var cineRateTag = new DicomTag(0x0018, 0x0040);
            AddStringElement(dataset, cineRateTag, DicomVR.IS,
                ((int)_frameRate).ToString(CultureInfo.InvariantCulture));

            var frameTimeTag = new DicomTag(0x0018, 0x1063);
            double frameTimeMs = 1000.0 / _frameRate;
            AddStringElement(dataset, frameTimeTag, DicomVR.DS,
                frameTimeMs.ToString("F4", CultureInfo.InvariantCulture));

            // Recommended Display Frame Rate
            var recommendedFrameRateTag = new DicomTag(0x0008, 0x2144);
            AddStringElement(dataset, recommendedFrameRateTag, DicomVR.IS,
                ((int)_frameRate).ToString(CultureInfo.InvariantCulture));

            // Create encapsulated pixel data as a single fragment
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty, // Empty Basic Offset Table
                new[] { (ReadOnlyMemory<byte>)_encodedVideoData });

            var pixelDataInfo = Data.PixelDataInfo.FromDataset(dataset);
            var source = new ImmediatePixelDataSource(Array.Empty<byte>());
            var pixelDataElement = new DicomPixelDataElement(
                source,
                DicomVR.OB,
                pixelDataInfo,
                isEncapsulated: true,
                fragments: fragments);

            dataset.Add(pixelDataElement);

            // Create DicomFile with the selected transfer syntax
            return new DicomFile(dataset, transferSyntax);
        }

        /// <summary>
        /// Maps a <see cref="VideoSopClass"/> to its DICOM SOP Class UID string.
        /// </summary>
        /// <param name="sopClass">The video SOP class.</param>
        /// <returns>The SOP Class UID string.</returns>
        internal static string GetSopClassUid(VideoSopClass sopClass)
        {
            return sopClass switch
            {
                VideoSopClass.Endoscopic => DicomUID.VideoEndoscopicImageStorage.ToString(),
                VideoSopClass.Microscopic => DicomUID.VideoMicroscopicImageStorage.ToString(),
                VideoSopClass.Photographic => DicomUID.VideoPhotographicImageStorage.ToString(),
                VideoSopClass.EnhancedXA => DicomUID.EnhancedXAImageStorage.ToString(),
                VideoSopClass.EnhancedXRF => DicomUID.EnhancedXRFImageStorage.ToString(),
                VideoSopClass.USMultiFrame => DicomUID.USMultiFrameImageStorage.ToString(),
                VideoSopClass.SCMultiFrameTrueColor => DicomUID.SCMultiFrameTrueColorImageStorage.ToString(),
                _ => throw new ArgumentOutOfRangeException(nameof(sopClass), sopClass, "Unknown video SOP class.")
            };
        }

        /// <summary>
        /// Maps a <see cref="VideoSopClass"/> to its DICOM modality code.
        /// </summary>
        /// <param name="sopClass">The video SOP class.</param>
        /// <returns>The DICOM modality code string.</returns>
        internal static string GetModality(VideoSopClass sopClass)
        {
            return sopClass switch
            {
                VideoSopClass.Endoscopic => "ES",
                VideoSopClass.Microscopic => "SM",
                VideoSopClass.Photographic => "XC",
                VideoSopClass.EnhancedXA => "XA",
                VideoSopClass.EnhancedXRF => "RF",
                VideoSopClass.USMultiFrame => "US",
                VideoSopClass.SCMultiFrameTrueColor => "SC",
                _ => throw new ArgumentOutOfRangeException(nameof(sopClass), sopClass, "Unknown video SOP class.")
            };
        }

        /// <summary>
        /// Determines the appropriate photometric interpretation for a given transfer syntax.
        /// </summary>
        /// <param name="ts">The transfer syntax.</param>
        /// <returns>The photometric interpretation string.</returns>
        private static string GetPhotometricInterpretation(TransferSyntax ts)
        {
            // MPEG-2 uses YBR_PARTIAL_420 per DICOM PS3.5 C.7.6.3.1.2
            if (ts.Compression == CompressionType.MPEG2)
                return "YBR_PARTIAL_420";

            // H.264 and HEVC use YBR_PARTIAL_420 as well (4:2:0 chroma subsampling)
            if (ts.Compression == CompressionType.H264 || ts.Compression == CompressionType.HEVC)
                return "YBR_PARTIAL_420";

            // Default for unrecognised transfer syntaxes
            return "YBR_FULL_422";
        }

        /// <summary>
        /// Copies patient and study-level attributes from a template dataset.
        /// </summary>
        private static void CopyTemplateAttributes(DicomDataset target, DicomDataset template)
        {
            // Patient-level tags
            CopyElementIfPresent(target, template, DicomTag.PatientName);
            CopyElementIfPresent(target, template, DicomTag.PatientID);
            CopyElementIfPresent(target, template, DicomTag.PatientBirthDate);
            CopyElementIfPresent(target, template, DicomTag.PatientSex);

            // Study-level tags
            CopyElementIfPresent(target, template, DicomTag.StudyInstanceUID);
            CopyElementIfPresent(target, template, DicomTag.StudyDate);
            CopyElementIfPresent(target, template, DicomTag.StudyTime);
            CopyElementIfPresent(target, template, DicomTag.AccessionNumber);
            CopyElementIfPresent(target, template, DicomTag.StudyDescription);
            CopyElementIfPresent(target, template, DicomTag.ReferringPhysicianName);
        }

        /// <summary>
        /// Copies a single element from source to target if it exists.
        /// </summary>
        private static void CopyElementIfPresent(DicomDataset target, DicomDataset source, DicomTag tag)
        {
            var element = source[tag];
            if (element != null)
            {
                target.AddOrUpdate(element.ToOwned());
            }
        }

        /// <summary>
        /// Adds a string element with proper padding.
        /// </summary>
        private static void AddStringElement(DicomDataset dataset, DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var vrInfo = DicomVRInfo.GetInfo(vr);

            // Pad to even length
            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = vrInfo.PaddingByte;
                bytes = padded;
            }

            dataset.AddOrUpdate(new DicomStringElement(tag, vr, bytes));
        }

        /// <summary>
        /// Adds a US (unsigned short) numeric element.
        /// </summary>
        private static void AddUInt16Element(DicomDataset dataset, DicomTag tag, ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            dataset.AddOrUpdate(new DicomNumericElement(tag, DicomVR.US, bytes));
        }
    }
}
