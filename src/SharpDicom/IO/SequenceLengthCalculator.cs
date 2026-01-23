using System;
using SharpDicom.Data;

namespace SharpDicom.IO
{
    /// <summary>
    /// Calculates byte lengths for defined-length sequence encoding.
    /// </summary>
    /// <remarks>
    /// This calculator performs a two-pass calculation to determine exact byte lengths
    /// for sequences and datasets when using defined-length encoding. It handles:
    /// - Different header sizes for 16-bit vs 32-bit length VRs in explicit VR
    /// - Implicit VR format (always 8-byte header)
    /// - Recursive sequence length calculation
    /// - Value padding to even length
    /// - Overflow protection (returns uint.MaxValue if overflow detected)
    /// </remarks>
    public static class SequenceLengthCalculator
    {
        /// <summary>
        /// The undefined length constant (0xFFFFFFFF).
        /// </summary>
        public const uint UndefinedLength = 0xFFFFFFFF;

        /// <summary>
        /// Calculates the total byte length of a sequence (excluding sequence header).
        /// </summary>
        /// <param name="sequence">The sequence to calculate length for.</param>
        /// <param name="explicitVR">True if using explicit VR encoding.</param>
        /// <returns>
        /// The total byte length of all items and their contents, or <see cref="UndefinedLength"/>
        /// if the calculation would overflow.
        /// </returns>
        /// <remarks>
        /// The returned length includes:
        /// - For each item: Item tag (4 bytes) + item length (4 bytes) + item content
        /// - No ItemDelimitationItem or SequenceDelimitationItem (those are only for undefined length)
        /// </remarks>
        public static uint CalculateSequenceLength(DicomSequence sequence, bool explicitVR)
        {
            if (sequence == null)
                throw new ArgumentNullException(nameof(sequence));

            // Empty sequence has zero length
            if (sequence.Items.Count == 0)
                return 0;

            ulong total = 0;

            foreach (var item in sequence.Items)
            {
                // Item tag (FFFE,E000) = 4 bytes
                // Item length = 4 bytes
                total += 8;

                // Item content (dataset elements)
                uint itemDatasetLength = CalculateDatasetLength(item, explicitVR);
                if (itemDatasetLength == UndefinedLength)
                    return UndefinedLength;

                total += itemDatasetLength;

                // Check for overflow
                if (total > uint.MaxValue - 1) // -1 to have room for potential rounding
                    return UndefinedLength;
            }

            return total > uint.MaxValue ? UndefinedLength : (uint)total;
        }

        /// <summary>
        /// Calculates the total byte length of a dataset (sum of all element lengths).
        /// </summary>
        /// <param name="dataset">The dataset to calculate length for.</param>
        /// <param name="explicitVR">True if using explicit VR encoding.</param>
        /// <returns>
        /// The total byte length of all elements including headers and padding,
        /// or <see cref="UndefinedLength"/> if the calculation would overflow.
        /// </returns>
        public static uint CalculateDatasetLength(DicomDataset dataset, bool explicitVR)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            ulong total = 0;

            foreach (var element in dataset)
            {
                uint elementLength = CalculateElementLength(element, explicitVR);
                if (elementLength == UndefinedLength)
                    return UndefinedLength;

                total += elementLength;

                // Check for overflow
                if (total > uint.MaxValue - 1)
                    return UndefinedLength;
            }

            return total > uint.MaxValue ? UndefinedLength : (uint)total;
        }

        /// <summary>
        /// Calculates the total byte length of a single element (including header).
        /// </summary>
        /// <param name="element">The element to calculate length for.</param>
        /// <param name="explicitVR">True if using explicit VR encoding.</param>
        /// <returns>
        /// The total byte length including header, value, and padding,
        /// or <see cref="UndefinedLength"/> if the calculation would overflow.
        /// </returns>
        /// <remarks>
        /// Element header sizes:
        /// - Explicit VR, 16-bit length VRs: 8 bytes (tag 4 + VR 2 + length 2)
        /// - Explicit VR, 32-bit length VRs: 12 bytes (tag 4 + VR 2 + reserved 2 + length 4)
        /// - Implicit VR: 8 bytes (tag 4 + length 4)
        /// </remarks>
        public static uint CalculateElementLength(IDicomElement element, bool explicitVR)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            // Handle sequences specially - they contain nested content
            if (element is DicomSequence sequence)
            {
                return CalculateSequenceElementLength(sequence, explicitVR);
            }

            // Regular element
            return CalculateRegularElementLength(element, explicitVR);
        }

        /// <summary>
        /// Calculates the byte length of a sequence element including header.
        /// </summary>
        private static uint CalculateSequenceElementLength(DicomSequence sequence, bool explicitVR)
        {
            // Header size
            // In explicit VR: Tag(4) + VR(2) + Reserved(2) + Length(4) = 12 bytes
            // In implicit VR: Tag(4) + Length(4) = 8 bytes
            uint headerSize = explicitVR ? 12u : 8u;

            // Calculate sequence content length
            uint contentLength = CalculateSequenceLength(sequence, explicitVR);
            if (contentLength == UndefinedLength)
                return UndefinedLength;

            // Check for overflow
            ulong total = headerSize + contentLength;
            return total > uint.MaxValue ? UndefinedLength : (uint)total;
        }

        /// <summary>
        /// Calculates the byte length of a regular (non-sequence) element.
        /// </summary>
        private static uint CalculateRegularElementLength(IDicomElement element, bool explicitVR)
        {
            var vrInfo = DicomVRInfo.GetInfo(element.VR);

            // Calculate header size
            uint headerSize;
            if (explicitVR)
            {
                // 16-bit length VRs: Tag(4) + VR(2) + Length(2) = 8 bytes
                // 32-bit length VRs: Tag(4) + VR(2) + Reserved(2) + Length(4) = 12 bytes
                headerSize = vrInfo.Is16BitLength ? 8u : 12u;
            }
            else
            {
                // Implicit VR: Tag(4) + Length(4) = 8 bytes
                headerSize = 8u;
            }

            // Value length with even padding
            int rawLength = element.RawValue.Length;
            uint paddedLength = (rawLength & 1) == 1 ? (uint)(rawLength + 1) : (uint)rawLength;

            // Check for overflow
            ulong total = headerSize + paddedLength;
            return total > uint.MaxValue ? UndefinedLength : (uint)total;
        }

        /// <summary>
        /// Calculates the length of an item's content (the dataset inside an item).
        /// </summary>
        /// <param name="item">The item dataset.</param>
        /// <param name="explicitVR">True if using explicit VR encoding.</param>
        /// <returns>
        /// The total byte length of the item's dataset content,
        /// or <see cref="UndefinedLength"/> if the calculation would overflow.
        /// </returns>
        public static uint CalculateItemLength(DicomDataset item, bool explicitVR)
        {
            return CalculateDatasetLength(item, explicitVR);
        }
    }
}
