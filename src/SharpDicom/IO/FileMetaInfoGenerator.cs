using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.IO
{
    /// <summary>
    /// Generates File Meta Information (FMI) for DICOM Part 10 files.
    /// </summary>
    /// <remarks>
    /// FMI is always encoded as Explicit VR Little Endian regardless of the dataset transfer syntax.
    /// This generator creates all required FMI elements and calculates the FileMetaInformationGroupLength.
    /// </remarks>
    public static class FileMetaInfoGenerator
    {
        /// <summary>
        /// File Meta Information Version: 00\01 (two bytes).
        /// </summary>
        private static readonly byte[] FileMetaInfoVersion = { 0x00, 0x01 };

        /// <summary>
        /// Generates File Meta Information for a dataset.
        /// </summary>
        /// <param name="dataset">The dataset to extract SOP UIDs from.</param>
        /// <param name="transferSyntax">The transfer syntax for the dataset.</param>
        /// <param name="options">Writer options (for implementation UID/version and validation).</param>
        /// <returns>A new DicomDataset containing all FMI elements.</returns>
        /// <exception cref="DicomDataException">Thrown when required UIDs are missing and validation is enabled.</exception>
        public static DicomDataset Generate(
            DicomDataset dataset,
            TransferSyntax transferSyntax,
            DicomWriterOptions? options = null)
        {
            options ??= DicomWriterOptions.Default;

            // Extract required UIDs from dataset
            var sopClassUid = dataset.GetString(DicomTag.SOPClassUID);
            var sopInstanceUid = dataset.GetString(DicomTag.SOPInstanceUID);

            if (options.ValidateFmiUids)
            {
                if (string.IsNullOrEmpty(sopClassUid))
                    throw new DicomDataException("Dataset is missing required SOPClassUID (0008,0016)");
                if (string.IsNullOrEmpty(sopInstanceUid))
                    throw new DicomDataException("Dataset is missing required SOPInstanceUID (0008,0018)");
            }

            // Use empty strings if missing and validation disabled
            sopClassUid ??= string.Empty;
            sopInstanceUid ??= string.Empty;

            // Get implementation identifiers
            var implClassUid = options.ImplementationClassUID ?? SharpDicomInfo.ImplementationClassUID;
            var implVersionName = options.ImplementationVersionName ?? SharpDicomInfo.ImplementationVersionName;

            // Create FMI dataset (without group length first)
            var fmi = new DicomDataset();

            // (0002,0001) FileMetaInformationVersion - OB
            fmi.Add(new DicomBinaryElement(
                DicomTag.FileMetaInformationVersion,
                DicomVR.OB,
                FileMetaInfoVersion));

            // (0002,0002) MediaStorageSOPClassUID - UI
            fmi.Add(CreateUidElement(DicomTag.MediaStorageSOPClassUID, sopClassUid));

            // (0002,0003) MediaStorageSOPInstanceUID - UI
            fmi.Add(CreateUidElement(DicomTag.MediaStorageSOPInstanceUID, sopInstanceUid));

            // (0002,0010) TransferSyntaxUID - UI
            fmi.Add(CreateUidElement(DicomTag.TransferSyntaxUID, transferSyntax.UID.ToString()));

            // (0002,0012) ImplementationClassUID - UI
            fmi.Add(CreateUidElement(DicomTag.ImplementationClassUID, implClassUid.ToString()));

            // (0002,0013) ImplementationVersionName - SH (optional but we always include it)
            if (!string.IsNullOrEmpty(implVersionName))
            {
                fmi.Add(CreateStringElement(DicomTag.ImplementationVersionName, DicomVR.SH, implVersionName));
            }

            // Calculate group length and add (0002,0000) at the front
            uint groupLength = CalculateGroupLength(fmi);
            var groupLengthValue = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(groupLengthValue, groupLength);

            // Create a new dataset with group length first
            var fmiWithGroupLength = new DicomDataset();
            fmiWithGroupLength.Add(new DicomNumericElement(
                DicomTag.FileMetaInformationGroupLength,
                DicomVR.UL,
                groupLengthValue));

            // Add all other elements in order
            foreach (var element in fmi)
            {
                fmiWithGroupLength.Add(element);
            }

            return fmiWithGroupLength;
        }

        /// <summary>
        /// Creates a UI (UID) element with proper null padding for odd lengths.
        /// </summary>
        private static DicomStringElement CreateUidElement(DicomTag tag, string uid)
        {
            // Trim any existing padding
            uid = uid.TrimEnd('\0', ' ');

            // Encode as ASCII
            var bytes = Encoding.ASCII.GetBytes(uid);

            // UI VR uses null (0x00) padding for odd length
            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = 0x00;
                bytes = padded;
            }

            return new DicomStringElement(tag, DicomVR.UI, bytes);
        }

        /// <summary>
        /// Creates a string element with proper space padding for odd lengths.
        /// </summary>
        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);

            // Most string VRs use space (0x20) padding
            var vrInfo = DicomVRInfo.GetInfo(vr);
            byte paddingByte = vrInfo.PaddingByte;

            if ((bytes.Length & 1) == 1)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = paddingByte;
                bytes = padded;
            }

            return new DicomStringElement(tag, vr, bytes);
        }

        /// <summary>
        /// Calculates the group length (total bytes) for all elements in the FMI dataset.
        /// </summary>
        /// <remarks>
        /// Each element is encoded as Explicit VR Little Endian:
        /// - 16-bit length VRs: Tag(4) + VR(2) + Length(2) + Value = 8 + Length
        /// - 32-bit length VRs: Tag(4) + VR(2) + Reserved(2) + Length(4) + Value = 12 + Length
        /// </remarks>
        private static uint CalculateGroupLength(DicomDataset fmi)
        {
            uint total = 0;

            foreach (var element in fmi)
            {
                var vrInfo = DicomVRInfo.GetInfo(element.VR);

                // Value length (already padded in CreateUidElement/CreateStringElement)
                int valueLength = element.RawValue.Length;

                if (vrInfo.Is16BitLength)
                {
                    // 16-bit length format: Tag(4) + VR(2) + Length(2) + Value
                    total += 8 + (uint)valueLength;
                }
                else
                {
                    // 32-bit length format: Tag(4) + VR(2) + Reserved(2) + Length(4) + Value
                    total += 12 + (uint)valueLength;
                }
            }

            return total;
        }

        /// <summary>
        /// Calculates the encoded length of a single element in Explicit VR Little Endian format.
        /// </summary>
        /// <param name="element">The element to calculate the length for.</param>
        /// <returns>The total encoded length in bytes.</returns>
        public static uint GetEncodedLength(IDicomElement element)
        {
            var vrInfo = DicomVRInfo.GetInfo(element.VR);
            int valueLength = element.RawValue.Length;

            // Ensure even length
            if ((valueLength & 1) == 1)
                valueLength++;

            if (vrInfo.Is16BitLength)
            {
                // 16-bit length format: Tag(4) + VR(2) + Length(2) + Value
                return 8 + (uint)valueLength;
            }
            else
            {
                // 32-bit length format: Tag(4) + VR(2) + Reserved(2) + Length(4) + Value
                return 12 + (uint)valueLength;
            }
        }
    }
}
