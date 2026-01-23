using System;
using System.Collections.Generic;

namespace SharpDicom.Data
{
    /// <summary>
    /// Contains metadata about a DICOM Value Representation.
    /// </summary>
    /// <param name="VR">The Value Representation.</param>
    /// <param name="Name">The full name of the VR (e.g., "Application Entity").</param>
    /// <param name="PaddingByte">The byte used for padding odd-length values (0x20 for strings, 0x00 for binary).</param>
    /// <param name="MaxLength">The maximum allowed length in bytes (uint.MaxValue for unlimited).</param>
    /// <param name="IsStringVR">True if this is a text-based VR.</param>
    /// <param name="Is16BitLength">True if this VR uses 16-bit length encoding in explicit VR.</param>
    /// <param name="CanHaveUndefinedLength">True if this VR can have undefined length (0xFFFFFFFF).</param>
    /// <param name="MultiValueDelimiter">The character used to delimit multiple values ('\\' for most strings, null for binary).</param>
    public readonly record struct DicomVRInfo(
        DicomVR VR,
        string Name,
        byte PaddingByte,
        uint MaxLength,
        bool IsStringVR,
        bool Is16BitLength,
        bool CanHaveUndefinedLength,
        char? MultiValueDelimiter)
    {
        private static readonly Dictionary<ushort, DicomVRInfo> _vrInfoLookup = new()
        {
            // String VRs with 16-bit length
            { DicomVR.AE.Code, new DicomVRInfo(DicomVR.AE, "Application Entity", 0x20, 16, true, true, false, '\\') },
            { DicomVR.AS.Code, new DicomVRInfo(DicomVR.AS, "Age String", 0x20, 4, true, true, false, null) },
            { DicomVR.AT.Code, new DicomVRInfo(DicomVR.AT, "Attribute Tag", 0x00, 4, false, true, false, null) },
            { DicomVR.CS.Code, new DicomVRInfo(DicomVR.CS, "Code String", 0x20, 16, true, true, false, '\\') },
            { DicomVR.DA.Code, new DicomVRInfo(DicomVR.DA, "Date", 0x20, 8, true, true, false, '\\') },
            { DicomVR.DS.Code, new DicomVRInfo(DicomVR.DS, "Decimal String", 0x20, 16, true, true, false, '\\') },
            { DicomVR.DT.Code, new DicomVRInfo(DicomVR.DT, "Date Time", 0x20, 26, true, true, false, '\\') },
            { DicomVR.FL.Code, new DicomVRInfo(DicomVR.FL, "Floating Point Single", 0x00, 4, false, true, false, null) },
            { DicomVR.FD.Code, new DicomVRInfo(DicomVR.FD, "Floating Point Double", 0x00, 8, false, true, false, null) },
            { DicomVR.IS.Code, new DicomVRInfo(DicomVR.IS, "Integer String", 0x20, 12, true, true, false, '\\') },
            { DicomVR.LO.Code, new DicomVRInfo(DicomVR.LO, "Long String", 0x20, 64, true, true, false, '\\') },
            { DicomVR.LT.Code, new DicomVRInfo(DicomVR.LT, "Long Text", 0x20, 10240, true, true, false, null) },
            { DicomVR.PN.Code, new DicomVRInfo(DicomVR.PN, "Person Name", 0x20, 64, true, true, false, '\\') },
            { DicomVR.SH.Code, new DicomVRInfo(DicomVR.SH, "Short String", 0x20, 16, true, true, false, '\\') },
            { DicomVR.SL.Code, new DicomVRInfo(DicomVR.SL, "Signed Long", 0x00, 4, false, true, false, null) },
            { DicomVR.SS.Code, new DicomVRInfo(DicomVR.SS, "Signed Short", 0x00, 2, false, true, false, null) },
            { DicomVR.ST.Code, new DicomVRInfo(DicomVR.ST, "Short Text", 0x20, 1024, true, true, false, null) },
            { DicomVR.TM.Code, new DicomVRInfo(DicomVR.TM, "Time", 0x20, 14, true, true, false, '\\') },
            { DicomVR.UI.Code, new DicomVRInfo(DicomVR.UI, "Unique Identifier", 0x00, 64, true, true, false, '\\') },
            { DicomVR.UL.Code, new DicomVRInfo(DicomVR.UL, "Unsigned Long", 0x00, 4, false, true, false, null) },
            { DicomVR.US.Code, new DicomVRInfo(DicomVR.US, "Unsigned Short", 0x00, 2, false, true, false, null) },

            // Binary VRs with 32-bit length
            { DicomVR.OB.Code, new DicomVRInfo(DicomVR.OB, "Other Byte", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.OD.Code, new DicomVRInfo(DicomVR.OD, "Other Double", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.OF.Code, new DicomVRInfo(DicomVR.OF, "Other Float", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.OL.Code, new DicomVRInfo(DicomVR.OL, "Other Long", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.OV.Code, new DicomVRInfo(DicomVR.OV, "Other 64-bit Very Long", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.OW.Code, new DicomVRInfo(DicomVR.OW, "Other Word", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.SQ.Code, new DicomVRInfo(DicomVR.SQ, "Sequence of Items", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.SV.Code, new DicomVRInfo(DicomVR.SV, "Signed 64-bit Very Long", 0x00, 8, false, false, false, null) },
            { DicomVR.UC.Code, new DicomVRInfo(DicomVR.UC, "Unlimited Characters", 0x20, uint.MaxValue, true, false, false, '\\') },
            { DicomVR.UN.Code, new DicomVRInfo(DicomVR.UN, "Unknown", 0x00, uint.MaxValue, false, false, true, null) },
            { DicomVR.UR.Code, new DicomVRInfo(DicomVR.UR, "Universal Resource Identifier", 0x20, uint.MaxValue, true, false, false, null) },
            { DicomVR.UT.Code, new DicomVRInfo(DicomVR.UT, "Unlimited Text", 0x20, uint.MaxValue, true, false, false, null) },
            { DicomVR.UV.Code, new DicomVRInfo(DicomVR.UV, "Unsigned 64-bit Very Long", 0x00, 8, false, false, false, null) },
        };

        /// <summary>
        /// Gets metadata information for the specified Value Representation.
        /// </summary>
        /// <param name="vr">The VR to get information for.</param>
        /// <returns>The VR information, or a default for unknown VRs (treated as UN).</returns>
        public static DicomVRInfo GetInfo(DicomVR vr)
        {
            if (_vrInfoLookup.TryGetValue(vr.Code, out var info))
                return info;

            // Fallback for unknown VRs - treat like UN
            return new DicomVRInfo(
                VR: vr,
                Name: "Unknown",
                PaddingByte: 0x00,
                MaxLength: uint.MaxValue,
                IsStringVR: false,
                Is16BitLength: false,
                CanHaveUndefinedLength: true,
                MultiValueDelimiter: null);
        }

        /// <summary>
        /// Determines whether the specified VR is a known standard VR.
        /// </summary>
        /// <param name="vr">The VR to check.</param>
        /// <returns>true if the VR is known; otherwise, false.</returns>
        public static bool IsKnown(DicomVR vr) => _vrInfoLookup.ContainsKey(vr.Code);
    }
}
