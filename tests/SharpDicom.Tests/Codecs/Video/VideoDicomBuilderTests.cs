using System;
using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using SharpDicom.Codecs.Video;
using SharpDicom.Data;

namespace SharpDicom.Tests.Codecs.Video
{
    /// <summary>
    /// Tests for <see cref="VideoDicomBuilder"/> covering SOP class mapping,
    /// UID generation, template copying, validation, and pixel data packaging.
    /// </summary>
    [TestFixture]
    public class VideoDicomBuilderTests
    {
        /// <summary>Synthetic video data for builder tests (not real H.264).</summary>
        private static readonly byte[] SyntheticVideoData = new byte[1024];

        [Test]
        public void Test_Build_Endoscopic_H264()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithTransferSyntax(TransferSyntax.H264HighProfile41)
                .WithDimensions(1920, 1080)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;

            Assert.That(ds.GetString(DicomTag.SOPClassUID),
                Is.EqualTo(DicomUID.VideoEndoscopicImageStorage.ToString()));
            Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.H264HighProfile41));
            Assert.That(ds.GetString(DicomTag.Modality), Is.EqualTo("ES"));

            // Image Pixel Module
            Assert.That(GetUInt16(ds, DicomTag.Rows), Is.EqualTo(1080));
            Assert.That(GetUInt16(ds, DicomTag.Columns), Is.EqualTo(1920));
            Assert.That(GetUInt16(ds, DicomTag.SamplesPerPixel), Is.EqualTo(3));
            Assert.That(GetUInt16(ds, DicomTag.BitsAllocated), Is.EqualTo(8));

            // Cine Module - FrameTime should be ~33.3333 ms
            var frameTimeTag = new DicomTag(0x0018, 0x1063);
            var frameTime = ds.GetFloat64(frameTimeTag);
            Assert.That(frameTime, Is.Not.Null);
            Assert.That(frameTime!.Value, Is.EqualTo(33.3333).Within(0.01));
        }

        /// <summary>Expected SOP class UID strings for each VideoSopClass value.</summary>
        private static readonly (VideoSopClass SopClass, string ExpectedUid, string ExpectedModality)[] AllSopClasses =
        {
            (VideoSopClass.Endoscopic, "1.2.840.10008.5.1.4.1.1.77.1.1.1", "ES"),
            (VideoSopClass.Microscopic, "1.2.840.10008.5.1.4.1.1.77.1.2.1", "SM"),
            (VideoSopClass.Photographic, "1.2.840.10008.5.1.4.1.1.77.1.4.1", "XC"),
            (VideoSopClass.EnhancedXA, "1.2.840.10008.5.1.4.1.1.12.2.1", "XA"),
            (VideoSopClass.EnhancedXRF, "1.2.840.10008.5.1.4.1.1.12.1.1", "RF"),
            (VideoSopClass.USMultiFrame, "1.2.840.10008.5.1.4.1.1.6.2", "US"),
            (VideoSopClass.SCMultiFrameTrueColor, "1.2.840.10008.5.1.4.1.1.7.4", "SC"),
        };

        [Test]
        public void Test_Build_AllSopClasses()
        {
            foreach (var (sopClass, expectedUid, expectedModality) in AllSopClasses)
            {
                var file = new VideoDicomBuilder()
                    .WithSopClass(sopClass)
                    .WithTransferSyntax(TransferSyntax.H264HighProfile41)
                    .WithDimensions(640, 480)
                    .WithFrameRate(25.0)
                    .WithPixelData(SyntheticVideoData)
                    .Build();

                Assert.That(file.Dataset.GetString(DicomTag.SOPClassUID),
                    Is.EqualTo(expectedUid),
                    $"SOP Class UID mismatch for {sopClass}");
                Assert.That(file.Dataset.GetString(DicomTag.Modality),
                    Is.EqualTo(expectedModality),
                    $"Modality mismatch for {sopClass}");
            }
        }

        [Test]
        public void Test_Build_AutoGeneratesUIDs()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(512, 512)
                .WithFrameRate(15.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;
            var sopInstanceUid = ds.GetString(DicomTag.SOPInstanceUID);
            var seriesInstanceUid = ds.GetString(DicomTag.SeriesInstanceUID);
            var studyInstanceUid = ds.GetString(DicomTag.StudyInstanceUID);

            Assert.That(sopInstanceUid, Does.StartWith("2.25."));
            Assert.That(seriesInstanceUid, Does.StartWith("2.25."));
            Assert.That(studyInstanceUid, Does.StartWith("2.25."));

            // Each UID should be unique
            Assert.That(sopInstanceUid, Is.Not.EqualTo(seriesInstanceUid));
            Assert.That(sopInstanceUid, Is.Not.EqualTo(studyInstanceUid));
            Assert.That(seriesInstanceUid, Is.Not.EqualTo(studyInstanceUid));
        }

        [Test]
        public void Test_Build_CustomUIDs()
        {
            const string customSopUid = "1.2.840.99999.1.1.1";
            const string customSeriesUid = "1.2.840.99999.2.2.2";
            const string customStudyUid = "1.2.840.99999.3.3.3";

            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Photographic)
                .WithDimensions(320, 240)
                .WithFrameRate(10.0)
                .WithPixelData(SyntheticVideoData)
                .WithSopInstanceUid(customSopUid)
                .WithSeriesInstanceUid(customSeriesUid)
                .WithStudyInstanceUid(customStudyUid)
                .Build();

            var ds = file.Dataset;
            Assert.That(ds.GetString(DicomTag.SOPInstanceUID), Is.EqualTo(customSopUid));
            Assert.That(ds.GetString(DicomTag.SeriesInstanceUID), Is.EqualTo(customSeriesUid));
            Assert.That(ds.GetString(DicomTag.StudyInstanceUID), Is.EqualTo(customStudyUid));
        }

        [Test]
        public void Test_Build_WithTemplate()
        {
            var template = new DicomDataset();
            AddStringElement(template, DicomTag.PatientName, DicomVR.PN, "DOE^JOHN");
            AddStringElement(template, DicomTag.PatientID, DicomVR.LO, "PAT12345");
            AddStringElement(template, DicomTag.PatientBirthDate, DicomVR.DA, "19800101");
            AddStringElement(template, DicomTag.PatientSex, DicomVR.CS, "M");

            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.USMultiFrame)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData)
                .WithPatientFromTemplate(template)
                .Build();

            var ds = file.Dataset;
            Assert.That(ds.GetString(DicomTag.PatientName), Is.EqualTo("DOE^JOHN"));
            Assert.That(ds.GetString(DicomTag.PatientID), Is.EqualTo("PAT12345"));
            Assert.That(ds.GetString(DicomTag.PatientBirthDate), Is.EqualTo("19800101"));
            Assert.That(ds.GetString(DicomTag.PatientSex), Does.Contain("M"));
        }

        [Test]
        public void Test_Build_WithPatient_OverridesTemplate()
        {
            var template = new DicomDataset();
            AddStringElement(template, DicomTag.PatientID, DicomVR.LO, "TEMPLATE_ID");

            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(320, 240)
                .WithFrameRate(15.0)
                .WithPixelData(SyntheticVideoData)
                .WithPatientFromTemplate(template)
                .WithPatient("OVERRIDE_ID", "OVERRIDE^NAME")
                .Build();

            var ds = file.Dataset;
            Assert.That(ds.GetString(DicomTag.PatientID), Is.EqualTo("OVERRIDE_ID"));
            Assert.That(ds.GetString(DicomTag.PatientName), Is.EqualTo("OVERRIDE^NAME"));
        }

        [Test]
        public void Test_Build_MissingSopClass_Throws()
        {
            var builder = new VideoDicomBuilder()
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Test_Build_MissingPixelData_Throws()
        {
            var builder = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Test_Build_MissingDimensions_Throws()
        {
            var builder = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Test_Build_MissingFrameRate_Throws()
        {
            var builder = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithPixelData(SyntheticVideoData);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Test_Build_EmptyPixelData_Throws()
        {
            var builder = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(Array.Empty<byte>());

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Test_Build_PixelDataAsSingleFragment()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(25.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var pixelData = file.Dataset.GetPixelData();
            Assert.That(pixelData, Is.Not.Null);
            Assert.That(pixelData!.Fragments, Is.Not.Null);
            Assert.That(pixelData.Fragments!.Fragments.Count, Is.EqualTo(1),
                "Video pixel data should be packaged as a single fragment");
            Assert.That(pixelData.Fragments.Fragments[0].Length, Is.EqualTo(SyntheticVideoData.Length));
        }

        [Test]
        public void Test_Build_MPEG2_TransferSyntax()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithTransferSyntax(TransferSyntax.MPEG2MainML)
                .WithDimensions(720, 480)
                .WithFrameRate(29.97)
                .WithPixelData(SyntheticVideoData)
                .Build();

            Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.MPEG2MainML));
        }

        [Test]
        public void Test_Build_HEVC_TransferSyntax()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithTransferSyntax(TransferSyntax.HEVCMainProfile51)
                .WithDimensions(3840, 2160)
                .WithFrameRate(60.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.HEVCMainProfile51));
        }

        [Test]
        public void Test_Build_DefaultTransferSyntax_IsH264()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.H264HighProfile41));
        }

        [Test]
        public void Test_Build_WithNumberOfFrames()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithNumberOfFrames(150)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;
            var numberOfFrames = ds.GetInt32(DicomTag.NumberOfFrames);
            Assert.That(numberOfFrames, Is.EqualTo(150));
        }

        [Test]
        public void Test_Build_DefaultNumberOfFrames_Is1()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;
            var numberOfFrames = ds.GetInt32(DicomTag.NumberOfFrames);
            Assert.That(numberOfFrames, Is.EqualTo(1));
        }

        [Test]
        public void Test_Build_PhotometricInterpretation_YBR()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithTransferSyntax(TransferSyntax.H264HighProfile41)
                .WithDimensions(640, 480)
                .WithFrameRate(30.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;
            var photometric = ds.GetString(new DicomTag(0x0028, 0x0004)); // PhotometricInterpretation
            Assert.That(photometric, Is.EqualTo("YBR_PARTIAL_420"));
        }

        [Test]
        public void Test_Build_ImagePixelModule_Complete()
        {
            var file = new VideoDicomBuilder()
                .WithSopClass(VideoSopClass.Endoscopic)
                .WithDimensions(1280, 720)
                .WithFrameRate(24.0)
                .WithPixelData(SyntheticVideoData)
                .Build();

            var ds = file.Dataset;
            Assert.That(GetUInt16(ds, DicomTag.BitsAllocated), Is.EqualTo(8));
            Assert.That(GetUInt16(ds, new DicomTag(0x0028, 0x0101)), Is.EqualTo(8)); // BitsStored
            Assert.That(GetUInt16(ds, new DicomTag(0x0028, 0x0102)), Is.EqualTo(7)); // HighBit
            Assert.That(GetUInt16(ds, new DicomTag(0x0028, 0x0103)), Is.EqualTo(0)); // PixelRepresentation (unsigned)
            Assert.That(GetUInt16(ds, new DicomTag(0x0028, 0x0006)), Is.EqualTo(0)); // PlanarConfiguration (color-by-pixel)
        }

        // --- Helper methods ---

        /// <summary>
        /// Reads a US (unsigned short) value from a dataset element.
        /// </summary>
        private static ushort? GetUInt16(DicomDataset ds, DicomTag tag)
        {
            var element = ds[tag];
            if (element is DicomNumericElement ne)
                return ne.GetUInt16();
            return null;
        }

        /// <summary>
        /// Adds a string element with proper even-length padding.
        /// </summary>
        private static void AddStringElement(DicomDataset dataset, DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var vrInfo = DicomVRInfo.GetInfo(vr);

            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = vrInfo.PaddingByte;
                bytes = padded;
            }

            dataset.AddOrUpdate(new DicomStringElement(tag, vr, bytes));
        }
    }
}
