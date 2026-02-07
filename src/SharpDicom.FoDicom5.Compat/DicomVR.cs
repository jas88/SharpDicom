using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FellowOakDicom
{
    /// <summary>
    /// Represents a DICOM Value Representation.
    /// Matches fo-dicom 5.x class-instance pattern (e.g., DicomVR.LO).
    /// </summary>
    public sealed class DicomVR : IEquatable<DicomVR>
    {
        private static readonly ConcurrentDictionary<string, DicomVR> _lookup = new ConcurrentDictionary<string, DicomVR>(StringComparer.Ordinal);

        private readonly SharpDicom.Data.DicomVR _inner;

        private DicomVR(string code, SharpDicom.Data.DicomVR inner)
        {
            Code = code;
            _inner = inner;
            _lookup.TryAdd(code, this);
        }

        /// <summary>
        /// Gets the two-character VR code string.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Converts to the underlying SharpDicom VR.
        /// </summary>
        internal SharpDicom.Data.DicomVR ToSharpDicom() => _inner;

        /// <summary>
        /// Looks up a DicomVR instance by code string.
        /// </summary>
        /// <param name="code">The two-character VR code.</param>
        /// <returns>The matching DicomVR instance.</returns>
        /// <exception cref="ArgumentException">Thrown when the code is not recognized.</exception>
        public static DicomVR Lookup(string code)
        {
            if (_lookup.TryGetValue(code, out var vr))
                return vr;
            throw new ArgumentException($"Unknown VR code: {code}", nameof(code));
        }

        /// <summary>
        /// Creates a compat DicomVR from a SharpDicom DicomVR.
        /// </summary>
        internal static DicomVR FromSharpDicom(SharpDicom.Data.DicomVR vr)
        {
            var code = vr.ToString();
            return _lookup.GetOrAdd(code, _ => new DicomVR(code, vr));
        }

        /// <inheritdoc />
        public bool Equals(DicomVR? other) => other != null && Code == other.Code;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is DicomVR other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Code.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => Code;

        // Static well-known VR instances matching fo-dicom 5.x API
#pragma warning disable CA1720 // Identifier contains type name - matching fo-dicom API exactly

        /// <summary>Application Entity.</summary>
        public static readonly DicomVR AE = new DicomVR("AE", SharpDicom.Data.DicomVR.AE);
        /// <summary>Age String.</summary>
        public static readonly DicomVR AS = new DicomVR("AS", SharpDicom.Data.DicomVR.AS);
        /// <summary>Attribute Tag.</summary>
        public static readonly DicomVR AT = new DicomVR("AT", SharpDicom.Data.DicomVR.AT);
        /// <summary>Code String.</summary>
        public static readonly DicomVR CS = new DicomVR("CS", SharpDicom.Data.DicomVR.CS);
        /// <summary>Date.</summary>
        public static readonly DicomVR DA = new DicomVR("DA", SharpDicom.Data.DicomVR.DA);
        /// <summary>Decimal String.</summary>
        public static readonly DicomVR DS = new DicomVR("DS", SharpDicom.Data.DicomVR.DS);
        /// <summary>Date Time.</summary>
        public static readonly DicomVR DT = new DicomVR("DT", SharpDicom.Data.DicomVR.DT);
        /// <summary>Floating Point Single.</summary>
        public static readonly DicomVR FL = new DicomVR("FL", SharpDicom.Data.DicomVR.FL);
        /// <summary>Floating Point Double.</summary>
        public static readonly DicomVR FD = new DicomVR("FD", SharpDicom.Data.DicomVR.FD);
        /// <summary>Integer String.</summary>
        public static readonly DicomVR IS = new DicomVR("IS", SharpDicom.Data.DicomVR.IS);
        /// <summary>Long String.</summary>
        public static readonly DicomVR LO = new DicomVR("LO", SharpDicom.Data.DicomVR.LO);
        /// <summary>Long Text.</summary>
        public static readonly DicomVR LT = new DicomVR("LT", SharpDicom.Data.DicomVR.LT);
        /// <summary>Other Byte.</summary>
        public static readonly DicomVR OB = new DicomVR("OB", SharpDicom.Data.DicomVR.OB);
        /// <summary>Other Double.</summary>
        public static readonly DicomVR OD = new DicomVR("OD", SharpDicom.Data.DicomVR.OD);
        /// <summary>Other Float.</summary>
        public static readonly DicomVR OF = new DicomVR("OF", SharpDicom.Data.DicomVR.OF);
        /// <summary>Other Long.</summary>
        public static readonly DicomVR OL = new DicomVR("OL", SharpDicom.Data.DicomVR.OL);
        /// <summary>Other Word.</summary>
        public static readonly DicomVR OW = new DicomVR("OW", SharpDicom.Data.DicomVR.OW);
        /// <summary>Person Name.</summary>
        public static readonly DicomVR PN = new DicomVR("PN", SharpDicom.Data.DicomVR.PN);
        /// <summary>Short String.</summary>
        public static readonly DicomVR SH = new DicomVR("SH", SharpDicom.Data.DicomVR.SH);
        /// <summary>Signed Long.</summary>
        public static readonly DicomVR SL = new DicomVR("SL", SharpDicom.Data.DicomVR.SL);
        /// <summary>Sequence of Items.</summary>
        public static readonly DicomVR SQ = new DicomVR("SQ", SharpDicom.Data.DicomVR.SQ);
        /// <summary>Signed Short.</summary>
        public static readonly DicomVR SS = new DicomVR("SS", SharpDicom.Data.DicomVR.SS);
        /// <summary>Short Text.</summary>
        public static readonly DicomVR ST = new DicomVR("ST", SharpDicom.Data.DicomVR.ST);
        /// <summary>Time.</summary>
        public static readonly DicomVR TM = new DicomVR("TM", SharpDicom.Data.DicomVR.TM);
        /// <summary>Unlimited Characters.</summary>
        public static readonly DicomVR UC = new DicomVR("UC", SharpDicom.Data.DicomVR.UC);
        /// <summary>Unique Identifier.</summary>
        public static readonly DicomVR UI = new DicomVR("UI", SharpDicom.Data.DicomVR.UI);
        /// <summary>Unsigned Long.</summary>
        public static readonly DicomVR UL = new DicomVR("UL", SharpDicom.Data.DicomVR.UL);
        /// <summary>Unknown.</summary>
        public static readonly DicomVR UN = new DicomVR("UN", SharpDicom.Data.DicomVR.UN);
        /// <summary>Universal Resource Identifier.</summary>
        public static readonly DicomVR UR = new DicomVR("UR", SharpDicom.Data.DicomVR.UR);
        /// <summary>Unsigned Short.</summary>
        public static readonly DicomVR US = new DicomVR("US", SharpDicom.Data.DicomVR.US);
        /// <summary>Unlimited Text.</summary>
        public static readonly DicomVR UT = new DicomVR("UT", SharpDicom.Data.DicomVR.UT);

#pragma warning restore CA1720
    }
}
