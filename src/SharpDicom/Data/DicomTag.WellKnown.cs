namespace SharpDicom.Data
{
    /// <summary>
    /// Well-known DICOM tag constants.
    /// </summary>
    public readonly partial struct DicomTag
    {
        // Command tags (Group 0000) - DIMSE per PS3.7

        /// <summary>(0000,0000) Command Group Length</summary>
        /// <remarks>UL VR. Total length of the Command Group.</remarks>
        public static readonly DicomTag CommandGroupLength = new(0x0000, 0x0000);

        /// <summary>(0000,0002) Affected SOP Class UID</summary>
        /// <remarks>UI VR. SOP Class UID of the affected SOP Instance.</remarks>
        public static readonly DicomTag AffectedSOPClassUID = new(0x0000, 0x0002);

        /// <summary>(0000,0003) Requested SOP Class UID</summary>
        /// <remarks>UI VR. Used in N-CREATE operations.</remarks>
        public static readonly DicomTag RequestedSOPClassUID = new(0x0000, 0x0003);

        /// <summary>(0000,0100) Command Field</summary>
        /// <remarks>US VR. Identifies the DIMSE command type (C-STORE, C-FIND, etc.).</remarks>
        public static readonly DicomTag CommandField = new(0x0000, 0x0100);

        /// <summary>(0000,0110) Message ID</summary>
        /// <remarks>US VR. Unique identifier for the message.</remarks>
        public static readonly DicomTag MessageID = new(0x0000, 0x0110);

        /// <summary>(0000,0120) Message ID Being Responded To</summary>
        /// <remarks>US VR. Message ID of the request this response corresponds to.</remarks>
        public static readonly DicomTag MessageIDBeingRespondedTo = new(0x0000, 0x0120);

        /// <summary>(0000,0600) Move Destination</summary>
        /// <remarks>AE VR. Destination AE for C-MOVE operations.</remarks>
        public static readonly DicomTag MoveDestination = new(0x0000, 0x0600);

        /// <summary>(0000,0700) Priority</summary>
        /// <remarks>US VR. Priority of the operation (LOW=2, MEDIUM=0, HIGH=1).</remarks>
        public static readonly DicomTag Priority = new(0x0000, 0x0700);

        /// <summary>(0000,0800) Command Data Set Type</summary>
        /// <remarks>
        /// US VR. Indicates presence of Data Set.
        /// 0x0101 = No Data Set present, any other value = Data Set present.
        /// </remarks>
        public static readonly DicomTag CommandDataSetType = new(0x0000, 0x0800);

        /// <summary>(0000,0900) Status</summary>
        /// <remarks>US VR. Response status code.</remarks>
        public static readonly DicomTag Status = new(0x0000, 0x0900);

        /// <summary>(0000,0901) Offending Element</summary>
        /// <remarks>AT VR. Tag of the element causing an error.</remarks>
        public static readonly DicomTag OffendingElement = new(0x0000, 0x0901);

        /// <summary>(0000,0902) Error Comment</summary>
        /// <remarks>LO VR. Additional description of the error.</remarks>
        public static readonly DicomTag ErrorComment = new(0x0000, 0x0902);

        /// <summary>(0000,0903) Error ID</summary>
        /// <remarks>US VR. Implementation-specific error code.</remarks>
        public static readonly DicomTag ErrorID = new(0x0000, 0x0903);

        /// <summary>(0000,1000) Affected SOP Instance UID</summary>
        /// <remarks>UI VR. SOP Instance UID of the affected object.</remarks>
        public static readonly DicomTag AffectedSOPInstanceUID = new(0x0000, 0x1000);

        /// <summary>(0000,1001) Requested SOP Instance UID</summary>
        /// <remarks>UI VR. Used in N-CREATE operations.</remarks>
        public static readonly DicomTag RequestedSOPInstanceUID = new(0x0000, 0x1001);

        /// <summary>(0000,1002) Event Type ID</summary>
        /// <remarks>US VR. Used in N-EVENT-REPORT operations.</remarks>
        public static readonly DicomTag EventTypeID = new(0x0000, 0x1002);

        /// <summary>(0000,1005) Attribute Identifier List</summary>
        /// <remarks>AT VR. Used in N-GET operations.</remarks>
        public static readonly DicomTag AttributeIdentifierList = new(0x0000, 0x1005);

        /// <summary>(0000,1008) Action Type ID</summary>
        /// <remarks>US VR. Used in N-ACTION operations.</remarks>
        public static readonly DicomTag ActionTypeID = new(0x0000, 0x1008);

        /// <summary>(0000,1020) Number of Remaining Sub-operations</summary>
        /// <remarks>US VR. Used in C-MOVE/C-GET responses.</remarks>
        public static readonly DicomTag NumberOfRemainingSuboperations = new(0x0000, 0x1020);

        /// <summary>(0000,1021) Number of Completed Sub-operations</summary>
        /// <remarks>US VR. Used in C-MOVE/C-GET responses.</remarks>
        public static readonly DicomTag NumberOfCompletedSuboperations = new(0x0000, 0x1021);

        /// <summary>(0000,1022) Number of Failed Sub-operations</summary>
        /// <remarks>US VR. Used in C-MOVE/C-GET responses.</remarks>
        public static readonly DicomTag NumberOfFailedSuboperations = new(0x0000, 0x1022);

        /// <summary>(0000,1023) Number of Warning Sub-operations</summary>
        /// <remarks>US VR. Used in C-MOVE/C-GET responses.</remarks>
        public static readonly DicomTag NumberOfWarningSuboperations = new(0x0000, 0x1023);

        /// <summary>(0000,1030) Move Originator Application Entity Title</summary>
        /// <remarks>AE VR. AE that initiated the C-MOVE.</remarks>
        public static readonly DicomTag MoveOriginatorApplicationEntityTitle = new(0x0000, 0x1030);

        /// <summary>(0000,1031) Move Originator Message ID</summary>
        /// <remarks>US VR. Message ID from the C-MOVE request.</remarks>
        public static readonly DicomTag MoveOriginatorMessageID = new(0x0000, 0x1031);

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
