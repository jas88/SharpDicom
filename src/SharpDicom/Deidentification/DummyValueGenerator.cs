using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Generates VR-appropriate dummy values for de-identification.
    /// </summary>
    public static class DummyValueGenerator
    {
        private static readonly Dictionary<ushort, byte[]> s_dummyValues = new()
        {
            // String VRs
            { DicomVR.AE.Code, Encode("ANONYMOUS") },           // Application Entity
            { DicomVR.AS.Code, Encode("000Y") },                // Age String
            { DicomVR.CS.Code, Encode("ANON") },                // Code String
            { DicomVR.DA.Code, Encode("19000101") },            // Date
            { DicomVR.DS.Code, Encode("0") },                   // Decimal String
            { DicomVR.DT.Code, Encode("19000101000000.000000") }, // DateTime
            { DicomVR.IS.Code, Encode("0") },                   // Integer String
            { DicomVR.LO.Code, Encode("ANONYMIZED") },          // Long String
            { DicomVR.LT.Code, Encode("ANONYMIZED") },          // Long Text
            { DicomVR.PN.Code, Encode("ANONYMOUS") },           // Person Name
            { DicomVR.SH.Code, Encode("ANON") },                // Short String
            { DicomVR.ST.Code, Encode("ANONYMIZED") },          // Short Text
            { DicomVR.TM.Code, Encode("000000.000000") },       // Time
            { DicomVR.UC.Code, Encode("ANONYMIZED") },          // Unlimited Characters
            { DicomVR.UI.Code, Encode("2.25.0") },              // Unique Identifier
            { DicomVR.UR.Code, Encode("http://example.com") },  // URI
            { DicomVR.UT.Code, Encode("ANONYMIZED") },          // Unlimited Text

            // Numeric VRs (encoded as little-endian)
            { DicomVR.FL.Code, BitConverter.GetBytes(0.0f) },   // Float
            { DicomVR.FD.Code, BitConverter.GetBytes(0.0d) },   // Double
            { DicomVR.SL.Code, BitConverter.GetBytes(0) },      // Signed Long
            { DicomVR.SS.Code, BitConverter.GetBytes((short)0) }, // Signed Short
            { DicomVR.UL.Code, BitConverter.GetBytes(0u) },     // Unsigned Long
            { DicomVR.US.Code, BitConverter.GetBytes((ushort)0) }, // Unsigned Short

            // 64-bit VRs
            { DicomVR.SV.Code, BitConverter.GetBytes(0L) },     // Signed Very Long
            { DicomVR.UV.Code, BitConverter.GetBytes(0UL) },    // Unsigned Very Long

            // Attribute Tag
            { DicomVR.AT.Code, new byte[] { 0x00, 0x00, 0x00, 0x00 } },
        };

        // Binary VRs that should return empty (OB, OD, OF, OL, OV, OW, UN, SQ)
        private static readonly HashSet<ushort> s_binaryVrs = new()
        {
            DicomVR.OB.Code,
            DicomVR.OD.Code,
            DicomVR.OF.Code,
            DicomVR.OL.Code,
            DicomVR.OV.Code,
            DicomVR.OW.Code,
            DicomVR.UN.Code,
            DicomVR.SQ.Code
        };

        /// <summary>
        /// Gets a VR-appropriate dummy value.
        /// </summary>
        /// <param name="vr">The value representation.</param>
        /// <returns>Dummy value bytes suitable for the VR.</returns>
        public static byte[] GetDummy(DicomVR vr)
        {
            if (s_dummyValues.TryGetValue(vr.Code, out var value))
            {
                return value;
            }

            // Binary VRs return empty
            if (s_binaryVrs.Contains(vr.Code))
            {
                return Array.Empty<byte>();
            }

            // Default: return empty
            return Array.Empty<byte>();
        }

        /// <summary>
        /// Gets a VR-appropriate dummy value as a string (for string VRs).
        /// </summary>
        /// <param name="vr">The value representation.</param>
        /// <returns>Dummy string value, or null for non-string VRs.</returns>
        public static string? GetDummyString(DicomVR vr)
        {
            if (!vr.IsStringVR)
            {
                return null;
            }

            return vr.Code switch
            {
                var code when code == DicomVR.AE.Code => "ANONYMOUS",
                var code when code == DicomVR.AS.Code => "000Y",
                var code when code == DicomVR.CS.Code => "ANON",
                var code when code == DicomVR.DA.Code => "19000101",
                var code when code == DicomVR.DS.Code => "0",
                var code when code == DicomVR.DT.Code => "19000101000000.000000",
                var code when code == DicomVR.IS.Code => "0",
                var code when code == DicomVR.LO.Code => "ANONYMIZED",
                var code when code == DicomVR.LT.Code => "ANONYMIZED",
                var code when code == DicomVR.PN.Code => "ANONYMOUS",
                var code when code == DicomVR.SH.Code => "ANON",
                var code when code == DicomVR.ST.Code => "ANONYMIZED",
                var code when code == DicomVR.TM.Code => "000000.000000",
                var code when code == DicomVR.UC.Code => "ANONYMIZED",
                var code when code == DicomVR.UI.Code => "2.25.0",
                var code when code == DicomVR.UR.Code => "http://example.com",
                var code when code == DicomVR.UT.Code => "ANONYMIZED",
                _ => "ANONYMIZED"
            };
        }

        /// <summary>
        /// Gets a dummy date string.
        /// </summary>
        public static string GetDummyDate() => "19000101";

        /// <summary>
        /// Gets a dummy time string.
        /// </summary>
        public static string GetDummyTime() => "000000.000000";

        /// <summary>
        /// Gets a dummy datetime string.
        /// </summary>
        public static string GetDummyDateTime() => "19000101000000.000000";

        /// <summary>
        /// Gets a dummy person name string.
        /// </summary>
        public static string GetDummyPersonName() => "ANONYMOUS";

        /// <summary>
        /// Gets a dummy UID (2.25.0 - minimal valid UUID-derived UID).
        /// </summary>
        public static string GetDummyUid() => "2.25.0";

        private static byte[] Encode(string value)
        {
            // ASCII encoding for DICOM strings
            var bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                bytes[i] = (byte)value[i];
            }
            return bytes;
        }
    }
}
