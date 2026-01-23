using System;
using System.Linq;
using SharpDicom.Data;

namespace SharpDicom.IO
{
    /// <summary>
    /// Resolves context-dependent Value Representations for multi-VR DICOM tags.
    /// </summary>
    /// <remarks>
    /// Some DICOM tags allow multiple VRs with the correct choice depending
    /// on other elements in the dataset:
    /// <list type="bullet">
    /// <item><description>Pixel Data (7FE0,0010): OW if BitsAllocated &gt; 8, OB if &lt;= 8, OB if encapsulated</description></item>
    /// <item><description>US/SS tags (e.g., SmallestImagePixelValue): US if PixelRepresentation = 0, SS if = 1</description></item>
    /// <item><description>LUT Data tags: OW if entries &gt; 256, US otherwise</description></item>
    /// </list>
    /// This class provides static methods for resolving the correct VR based on dataset context.
    /// </remarks>
    public static class VRResolver
    {
        /// <summary>
        /// Resolves the correct VR for a tag that may have multiple valid VRs.
        /// </summary>
        /// <param name="tag">The DICOM tag to resolve VR for.</param>
        /// <param name="entry">The dictionary entry for the tag (may be null for unknown tags).</param>
        /// <param name="context">The dataset context for VR resolution (may be null).</param>
        /// <returns>The resolved VR for the tag.</returns>
        /// <remarks>
        /// Resolution logic:
        /// <list type="bullet">
        /// <item><description>If entry is null (unknown tag): returns UN</description></item>
        /// <item><description>If entry has single VR: returns that VR</description></item>
        /// <item><description>For Pixel Data: returns OW if BitsAllocated &gt; 8, OB otherwise</description></item>
        /// <item><description>For US/SS multi-VR tags: returns US if PixelRepresentation = 0, SS if = 1</description></item>
        /// <item><description>Default: returns entry.DefaultVR</description></item>
        /// </list>
        /// </remarks>
        public static DicomVR ResolveVR(DicomTag tag, DicomDictionaryEntry? entry, DicomDataset? context)
        {
            // Unknown tag - default to UN
            if (entry == null)
            {
                return DicomVR.UN;
            }

            // Single VR - no resolution needed
            if (!entry.Value.HasMultipleVRs)
            {
                return entry.Value.DefaultVR;
            }

            // Pixel Data (7FE0,0010) - OB or OW based on BitsAllocated
            if (tag == DicomTag.PixelData)
            {
                return ResolvePixelDataVR(context);
            }

            // US/SS multi-VR tags - based on PixelRepresentation
            if (IsUsOrSsMultiVR(entry.Value))
            {
                return ResolveUsOrSsVR(context);
            }

            // Default to first VR in dictionary entry
            return entry.Value.DefaultVR;
        }

        /// <summary>
        /// Determines if a tag requires context for VR resolution.
        /// </summary>
        /// <param name="tag">The DICOM tag to check.</param>
        /// <param name="entry">The dictionary entry for the tag.</param>
        /// <returns>True if the tag requires context values to determine VR.</returns>
        public static bool NeedsContext(DicomTag tag, DicomDictionaryEntry? entry)
        {
            if (entry == null || !entry.Value.HasMultipleVRs)
            {
                return false;
            }

            // Pixel Data needs BitsAllocated context
            if (tag == DicomTag.PixelData)
            {
                return true;
            }

            // US/SS multi-VR tags need PixelRepresentation context
            if (IsUsOrSsMultiVR(entry.Value))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if a tag has multiple valid VRs in the dictionary.
        /// </summary>
        /// <param name="tag">The DICOM tag to check.</param>
        /// <returns>True if the tag has multiple VRs; false if single VR or unknown.</returns>
        public static bool IsMultiVRTag(DicomTag tag)
        {
            var entry = DicomDictionary.Default.GetEntry(tag);
            return entry?.HasMultipleVRs ?? false;
        }

        /// <summary>
        /// Resolves Pixel Data VR based on BitsAllocated context.
        /// </summary>
        /// <param name="context">The dataset context.</param>
        /// <returns>OW if BitsAllocated &gt; 8; OB otherwise.</returns>
        /// <remarks>
        /// Per DICOM PS3.5:
        /// - OW is used when BitsAllocated &gt; 8 (word-aligned pixel data)
        /// - OB is used when BitsAllocated &lt;= 8 (byte-aligned pixel data)
        /// - OB is used for encapsulated (compressed) pixel data
        /// If BitsAllocated is not available, defaults to OB as the safer choice.
        /// </remarks>
        private static DicomVR ResolvePixelDataVR(DicomDataset? context)
        {
            if (context == null)
            {
                // No context - default to OB (safe default for unknown)
                return DicomVR.OB;
            }

            var bitsAllocated = context.BitsAllocated;
            if (bitsAllocated == null)
            {
                // BitsAllocated not present - default to OB
                return DicomVR.OB;
            }

            // OW if BitsAllocated > 8, OB otherwise
            return bitsAllocated.Value > 8 ? DicomVR.OW : DicomVR.OB;
        }

        /// <summary>
        /// Resolves US/SS VR based on PixelRepresentation context.
        /// </summary>
        /// <param name="context">The dataset context.</param>
        /// <returns>US if PixelRepresentation = 0 or not present; SS if PixelRepresentation = 1.</returns>
        /// <remarks>
        /// Per DICOM PS3.3 C.7.6.3:
        /// - PixelRepresentation = 0 means unsigned pixel values (US)
        /// - PixelRepresentation = 1 means signed pixel values (SS)
        /// If PixelRepresentation is not available, defaults to US (unsigned).
        /// </remarks>
        private static DicomVR ResolveUsOrSsVR(DicomDataset? context)
        {
            if (context == null)
            {
                // No context - default to US (unsigned is more common)
                return DicomVR.US;
            }

            var pixelRepresentation = context.PixelRepresentation;
            if (pixelRepresentation == null)
            {
                // PixelRepresentation not present - default to US
                return DicomVR.US;
            }

            // US if PixelRepresentation = 0 (unsigned), SS if = 1 (signed)
            return pixelRepresentation.Value == 0 ? DicomVR.US : DicomVR.SS;
        }

        /// <summary>
        /// Determines if a dictionary entry represents a US/SS multi-VR tag.
        /// </summary>
        /// <param name="entry">The dictionary entry to check.</param>
        /// <returns>True if the entry has both US and SS as valid VRs.</returns>
        private static bool IsUsOrSsMultiVR(DicomDictionaryEntry entry)
        {
            var vrs = entry.ValueRepresentations;
            if (vrs == null || vrs.Length < 2)
            {
                return false;
            }

            bool hasUS = false;
            bool hasSS = false;

            for (int i = 0; i < vrs.Length; i++)
            {
                if (vrs[i] == DicomVR.US)
                {
                    hasUS = true;
                }
                else if (vrs[i] == DicomVR.SS)
                {
                    hasSS = true;
                }
            }

            return hasUS && hasSS;
        }
    }
}
