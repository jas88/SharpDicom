namespace SharpDicom.Data
{
    /// <summary>
    /// Well-known DICOM tag constants.
    /// </summary>
    public readonly partial struct DicomTag
    {
        // File Meta Information tags (Group 0002)

        /// <summary>(0002,0000) File Meta Information Group Length</summary>
        public static readonly DicomTag FileMetaInformationGroupLength = new(0x0002, 0x0000);

        /// <summary>(0002,0001) File Meta Information Version</summary>
        public static readonly DicomTag FileMetaInformationVersion = new(0x0002, 0x0001);

        /// <summary>(0002,0002) Media Storage SOP Class UID</summary>
        public static readonly DicomTag MediaStorageSOPClassUID = new(0x0002, 0x0002);

        /// <summary>(0002,0003) Media Storage SOP Instance UID</summary>
        public static readonly DicomTag MediaStorageSOPInstanceUID = new(0x0002, 0x0003);

        /// <summary>(0002,0010) Transfer Syntax UID</summary>
        public static readonly DicomTag TransferSyntaxUID = new(0x0002, 0x0010);

        /// <summary>(0002,0012) Implementation Class UID</summary>
        public static readonly DicomTag ImplementationClassUID = new(0x0002, 0x0012);

        /// <summary>(0002,0013) Implementation Version Name</summary>
        public static readonly DicomTag ImplementationVersionName = new(0x0002, 0x0013);

        /// <summary>(0002,0016) Source Application Entity Title</summary>
        public static readonly DicomTag SourceApplicationEntityTitle = new(0x0002, 0x0016);

        // Common dataset tags used in file processing

        /// <summary>(0008,0005) Specific Character Set</summary>
        public static readonly DicomTag SpecificCharacterSet = new(0x0008, 0x0005);

        /// <summary>(0008,0016) SOP Class UID</summary>
        public static readonly DicomTag SOPClassUID = new(0x0008, 0x0016);

        /// <summary>(0008,0018) SOP Instance UID</summary>
        public static readonly DicomTag SOPInstanceUID = new(0x0008, 0x0018);

        /// <summary>(0010,0010) Patient Name</summary>
        public static readonly DicomTag PatientName = new(0x0010, 0x0010);

        /// <summary>(0010,0020) Patient ID</summary>
        public static readonly DicomTag PatientID = new(0x0010, 0x0020);

        /// <summary>(7FE0,0001) Extended Offset Table</summary>
        public static readonly DicomTag ExtendedOffsetTable = new(0x7FE0, 0x0001);

        /// <summary>(7FE0,0002) Extended Offset Table Lengths</summary>
        public static readonly DicomTag ExtendedOffsetTableLengths = new(0x7FE0, 0x0002);

        /// <summary>(7FE0,0010) Pixel Data</summary>
        public static readonly DicomTag PixelData = new(0x7FE0, 0x0010);

        // Image Pixel Module tags

        /// <summary>(0028,0002) Samples Per Pixel</summary>
        public static readonly DicomTag SamplesPerPixel = new(0x0028, 0x0002);

        /// <summary>(0028,0004) Photometric Interpretation</summary>
        public static readonly DicomTag PhotometricInterpretation = new(0x0028, 0x0004);

        /// <summary>(0028,0006) Planar Configuration</summary>
        public static readonly DicomTag PlanarConfiguration = new(0x0028, 0x0006);

        /// <summary>(0028,0008) Number of Frames</summary>
        public static readonly DicomTag NumberOfFrames = new(0x0028, 0x0008);

        /// <summary>(0028,0010) Rows</summary>
        public static readonly DicomTag Rows = new(0x0028, 0x0010);

        /// <summary>(0028,0011) Columns</summary>
        public static readonly DicomTag Columns = new(0x0028, 0x0011);

        /// <summary>(0028,0100) Bits Allocated</summary>
        public static readonly DicomTag BitsAllocated = new(0x0028, 0x0100);

        /// <summary>(0028,0101) Bits Stored</summary>
        public static readonly DicomTag BitsStored = new(0x0028, 0x0101);

        /// <summary>(0028,0102) High Bit</summary>
        public static readonly DicomTag HighBit = new(0x0028, 0x0102);

        /// <summary>(0028,0103) Pixel Representation</summary>
        public static readonly DicomTag PixelRepresentation = new(0x0028, 0x0103);

        /// <summary>(0028,0106) Smallest Image Pixel Value - multi-VR tag (US or SS)</summary>
        public static readonly DicomTag SmallestImagePixelValue = new(0x0028, 0x0106);

        /// <summary>(0028,0107) Largest Image Pixel Value - multi-VR tag (US or SS)</summary>
        public static readonly DicomTag LargestImagePixelValue = new(0x0028, 0x0107);

        // Item delimiters for sequence/fragment parsing

        /// <summary>(FFFE,E000) Item</summary>
        public static readonly DicomTag Item = new(0xFFFE, 0xE000);

        /// <summary>(FFFE,E00D) Item Delimitation Item</summary>
        public static readonly DicomTag ItemDelimitationItem = new(0xFFFE, 0xE00D);

        /// <summary>(FFFE,E0DD) Sequence Delimitation Item</summary>
        public static readonly DicomTag SequenceDelimitationItem = new(0xFFFE, 0xE0DD);
    }
}
