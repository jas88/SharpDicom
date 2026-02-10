using System;
using System.Runtime.CompilerServices;
#if !NETSTANDARD2_0
using System.Numerics;
#endif

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT Cleanup pass encoder and decoder per ITU-T T.814,
    /// closely following the OpenJPH reference implementation (BSD-2-Clause).
    /// </summary>
    internal static class HtCleanup
    {
        // OpenJPH quad sample ordering:
        //   bit 0 = top-left     (row y,   col x)
        //   bit 1 = bottom-left  (row y+1, col x)
        //   bit 2 = top-right    (row y,   col x+1)
        //   bit 3 = bottom-right (row y+1, col x+1)

        // Nothing here — decode tables are in VlcTable.OjphDecodeTable0/1.

        /// <summary>
        /// Encodes wavelet coefficients using the HT Cleanup pass.
        /// </summary>
        public static byte[] Encode(
            ReadOnlySpan<int> coefficients, int width, int height, int subbandType)
        {
            if (coefficients.Length != width * height)
                throw new ArgumentException(
                    $"Coefficient count {coefficients.Length} does not match {width}x{height}={width * height}.",
                    nameof(coefficients));
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Width and height must be positive.");

            int estimatedSize = Math.Max(128, (width * height * 6) / 8 + 64);
            var writer = new HtCleanupWriter(estimatedSize);

            int quadW = (width + 1) >> 1;
            byte[] eVal = new byte[quadW + 2];
            byte[] cxVal = new byte[quadW + 2];

            try
            {
                EncodeFirstRow(ref writer, coefficients, width, height, quadW, eVal, cxVal);

                for (int y = 2; y < height; y += 2)
                    EncodeSubsequentRow(ref writer, coefficients, width, height, y, quadW, eVal, cxVal);

                return writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }
        }

        private static void EncodeFirstRow(
            ref HtCleanupWriter writer, ReadOnlySpan<int> coefficients,
            int width, int height, int quadW, byte[] eVal, byte[] cxVal)
        {
            int lepIdx = 0;
            int lcxIdx = 0;
            int cq0 = 0;
            ushort[] encTbl = VlcTable.EncodeTable0;
            var uvlcTbl = VlcTable.UvlcTable;

            for (int x = 0; x < width; x += 4)
            {
                // ---- Quad 0 (columns x, x+1) ----
                int rho0, Uq0, uq0, ek0;
                int eq0, eq1, eq2, eq3;
                uint s0, s1, s2, s3;
                {
                    rho0 = 0;
                    int v;

                    v = GetVal(coefficients, 0, x, width, height);
                    eq0 = ComputeE(v); s0 = ComputeS(v);
                    if (v != 0) rho0 |= 1;

                    v = (height > 1) ? GetVal(coefficients, 1, x, width, height) : 0;
                    eq1 = ComputeE(v); s1 = ComputeS(v);
                    if (v != 0) rho0 |= 2;

                    v = (x + 1 < width) ? GetVal(coefficients, 0, x + 1, width, height) : 0;
                    eq2 = ComputeE(v); s2 = ComputeS(v);
                    if (v != 0) rho0 |= 4;

                    v = (x + 1 < width && height > 1) ? GetVal(coefficients, 1, x + 1, width, height) : 0;
                    eq3 = ComputeE(v); s3 = ComputeS(v);
                    if (v != 0) rho0 |= 8;

                    int eqmax0 = Math.Max(Math.Max(eq0, eq1), Math.Max(eq2, eq3));
                    Uq0 = Math.Max(eqmax0, 1); // kappa = 1 for first row
                    uq0 = Uq0 - 1;

                    int eps0 = 0;
                    if (uq0 > 0)
                    {
                        if (eq0 == eqmax0) eps0 |= 1;
                        if (eq1 == eqmax0) eps0 |= 2;
                        if (eq2 == eqmax0) eps0 |= 4;
                        if (eq3 == eqmax0) eps0 |= 8;
                    }

                    ushort tuple0 = encTbl[(cq0 << 8) | (rho0 << 4) | eps0];
                    int cwd0 = tuple0 >> 8;
                    int cwdLen0 = (tuple0 >> 4) & 7;
                    ek0 = tuple0 & 0xF;
                    // Codewords are already in LSB-first form per OpenJPH -- write directly.
                    writer.WriteVlcBits((uint)cwd0, cwdLen0);

                    if (cq0 == 0)
                        writer.EncodeMel(rho0 != 0);

                    EncodeMagSgnSample(ref writer, rho0, 1, s0, Uq0, ek0, 0);
                    EncodeMagSgnSample(ref writer, rho0, 2, s1, Uq0, ek0, 1);
                    EncodeMagSgnSample(ref writer, rho0, 4, s2, Uq0, ek0, 2);
                    EncodeMagSgnSample(ref writer, rho0, 8, s3, Uq0, ek0, 3);

                    eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], eq1);
                    lepIdx++;
                    eVal[lepIdx] = (byte)eq3;

                    cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho0 & 2) >> 1));
                    lcxIdx++;
                    cxVal[lcxIdx] = (byte)((rho0 & 8) >> 3);
                }

                // ---- Quad 1 (columns x+2, x+3) ----
                int rho1 = 0, uq1 = 0;
                if (x + 2 < width)
                {
                    int eq4, eq5, eq6, eq7;
                    uint s4, s5, s6, s7;
                    int v;

                    v = GetVal(coefficients, 0, x + 2, width, height);
                    eq4 = ComputeE(v); s4 = ComputeS(v);
                    if (v != 0) rho1 |= 1;

                    v = (height > 1) ? GetVal(coefficients, 1, x + 2, width, height) : 0;
                    eq5 = ComputeE(v); s5 = ComputeS(v);
                    if (v != 0) rho1 |= 2;

                    v = (x + 3 < width) ? GetVal(coefficients, 0, x + 3, width, height) : 0;
                    eq6 = ComputeE(v); s6 = ComputeS(v);
                    if (v != 0) rho1 |= 4;

                    v = (x + 3 < width && height > 1) ? GetVal(coefficients, 1, x + 3, width, height) : 0;
                    eq7 = ComputeE(v); s7 = ComputeS(v);
                    if (v != 0) rho1 |= 8;

                    int eqmax1 = Math.Max(Math.Max(eq4, eq5), Math.Max(eq6, eq7));
                    int cq1 = (rho0 >> 1) | (rho0 & 1);
                    int Uq1 = Math.Max(eqmax1, 1);
                    uq1 = Uq1 - 1;

                    int eps1 = 0;
                    if (uq1 > 0)
                    {
                        if (eq4 == eqmax1) eps1 |= 1;
                        if (eq5 == eqmax1) eps1 |= 2;
                        if (eq6 == eqmax1) eps1 |= 4;
                        if (eq7 == eqmax1) eps1 |= 8;
                    }

                    ushort tuple1 = encTbl[(cq1 << 8) | (rho1 << 4) | eps1];
                    int cwd1 = tuple1 >> 8;
                    int cwdLen1 = (tuple1 >> 4) & 7;
                    int ek1 = tuple1 & 0xF;
                    writer.WriteVlcBits((uint)cwd1, cwdLen1);

                    if (cq1 == 0)
                        writer.EncodeMel(rho1 != 0);

                    EncodeMagSgnSample(ref writer, rho1, 1, s4, Uq1, ek1, 0);
                    EncodeMagSgnSample(ref writer, rho1, 2, s5, Uq1, ek1, 1);
                    EncodeMagSgnSample(ref writer, rho1, 4, s6, Uq1, ek1, 2);
                    EncodeMagSgnSample(ref writer, rho1, 8, s7, Uq1, ek1, 3);

                    eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], eq5);
                    lepIdx++;
                    eVal[lepIdx] = (byte)eq7;
                    cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho1 & 2) >> 1));
                    lcxIdx++;
                    cxVal[lcxIdx] = (byte)((rho1 & 8) >> 3);
                }

                // ---- UVLC: OpenJPH first-row interleaved encoding ----
                EncodeUvlcFirstRow(ref writer, uq0, uq1, uvlcTbl);

                // Prepare for next pair
                cq0 = (rho1 >> 1) | (rho1 & 1);
            }

            eVal[lepIdx + 1] = 0;
        }

        private static void EncodeSubsequentRow(
            ref HtCleanupWriter writer, ReadOnlySpan<int> coefficients,
            int width, int height, int y, int quadW, byte[] eVal, byte[] cxVal)
        {
            int lepIdx = 0;
            int maxE = Math.Max(eVal[0], eVal[1]) - 1;
            eVal[0] = 0;
            int lcxIdx = 0;
            int cq0 = cxVal[0] + (cxVal[1] << 2);
            cxVal[0] = 0;

            ushort[] encTbl = VlcTable.EncodeTable1;
            var uvlcTbl = VlcTable.UvlcTable;

            for (int x = 0; x < width; x += 4)
            {
                // ---- Quad 0 ----
                int rho0 = 0;
                int v;

                v = GetVal(coefficients, y, x, width, height);
                int eq0 = ComputeE(v); uint s0 = ComputeS(v);
                if (v != 0) rho0 |= 1;

                v = (y + 1 < height) ? GetVal(coefficients, y + 1, x, width, height) : 0;
                int eq1 = ComputeE(v); uint s1 = ComputeS(v);
                if (v != 0) rho0 |= 2;

                v = (x + 1 < width) ? GetVal(coefficients, y, x + 1, width, height) : 0;
                int eq2 = ComputeE(v); uint s2 = ComputeS(v);
                if (v != 0) rho0 |= 4;

                v = (x + 1 < width && y + 1 < height) ? GetVal(coefficients, y + 1, x + 1, width, height) : 0;
                int eq3 = ComputeE(v); uint s3 = ComputeS(v);
                if (v != 0) rho0 |= 8;

                int eqmax0 = Math.Max(Math.Max(eq0, eq1), Math.Max(eq2, eq3));
                int kappa0 = (rho0 & (rho0 - 1)) != 0 ? Math.Max(1, maxE) : 1;
                int Uq0 = Math.Max(eqmax0, kappa0);
                int uq0 = Uq0 - kappa0;

                int eps0 = 0;
                if (uq0 > 0)
                {
                    if (eq0 == eqmax0) eps0 |= 1;
                    if (eq1 == eqmax0) eps0 |= 2;
                    if (eq2 == eqmax0) eps0 |= 4;
                    if (eq3 == eqmax0) eps0 |= 8;
                }

                ushort tuple0 = encTbl[(cq0 << 8) | (rho0 << 4) | eps0];
                int cwd0 = tuple0 >> 8;
                int cwdLen0 = (tuple0 >> 4) & 7;
                int ek0 = tuple0 & 0xF;
                writer.WriteVlcBits((uint)cwd0, cwdLen0);

                if (cq0 == 0)
                    writer.EncodeMel(rho0 != 0);

                EncodeMagSgnSample(ref writer, rho0, 1, s0, Uq0, ek0, 0);
                EncodeMagSgnSample(ref writer, rho0, 2, s1, Uq0, ek0, 1);
                EncodeMagSgnSample(ref writer, rho0, 4, s2, Uq0, ek0, 2);
                EncodeMagSgnSample(ref writer, rho0, 8, s3, Uq0, ek0, 3);

                eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], eq1);
                lepIdx++;
                maxE = Math.Max(eVal[lepIdx], eVal[lepIdx + 1]) - 1;
                eVal[lepIdx] = (byte)eq3;
                cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho0 & 2) >> 1));
                lcxIdx++;
                int cq1 = cxVal[lcxIdx] + (cxVal[lcxIdx + 1] << 2);
                cxVal[lcxIdx] = (byte)((rho0 & 8) >> 3);

                // ---- Quad 1 ----
                int rho1 = 0, uq1 = 0;
                if (x + 2 < width)
                {
                    v = GetVal(coefficients, y, x + 2, width, height);
                    int eq4 = ComputeE(v); uint s4 = ComputeS(v);
                    if (v != 0) rho1 |= 1;

                    v = (y + 1 < height) ? GetVal(coefficients, y + 1, x + 2, width, height) : 0;
                    int eq5 = ComputeE(v); uint s5 = ComputeS(v);
                    if (v != 0) rho1 |= 2;

                    v = (x + 3 < width) ? GetVal(coefficients, y, x + 3, width, height) : 0;
                    int eq6 = ComputeE(v); uint s6 = ComputeS(v);
                    if (v != 0) rho1 |= 4;

                    v = (x + 3 < width && y + 1 < height) ? GetVal(coefficients, y + 1, x + 3, width, height) : 0;
                    int eq7 = ComputeE(v); uint s7 = ComputeS(v);
                    if (v != 0) rho1 |= 8;

                    int eqmax1 = Math.Max(Math.Max(eq4, eq5), Math.Max(eq6, eq7));
                    int kappa1 = (rho1 & (rho1 - 1)) != 0 ? Math.Max(1, maxE) : 1;
                    cq1 |= ((rho0 & 4) >> 1) | ((rho0 & 8) >> 2);
                    int Uq1 = Math.Max(eqmax1, kappa1);
                    uq1 = Uq1 - kappa1;

                    int eps1 = 0;
                    if (uq1 > 0)
                    {
                        if (eq4 == eqmax1) eps1 |= 1;
                        if (eq5 == eqmax1) eps1 |= 2;
                        if (eq6 == eqmax1) eps1 |= 4;
                        if (eq7 == eqmax1) eps1 |= 8;
                    }

                    ushort tuple1 = encTbl[(cq1 << 8) | (rho1 << 4) | eps1];
                    int cwd1 = tuple1 >> 8;
                    int cwdLen1 = (tuple1 >> 4) & 7;
                    int ek1 = tuple1 & 0xF;
                    writer.WriteVlcBits((uint)cwd1, cwdLen1);

                    if (cq1 == 0)
                        writer.EncodeMel(rho1 != 0);

                    EncodeMagSgnSample(ref writer, rho1, 1, s4, Uq1, ek1, 0);
                    EncodeMagSgnSample(ref writer, rho1, 2, s5, Uq1, ek1, 1);
                    EncodeMagSgnSample(ref writer, rho1, 4, s6, Uq1, ek1, 2);
                    EncodeMagSgnSample(ref writer, rho1, 8, s7, Uq1, ek1, 3);

                    eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], eq5);
                    lepIdx++;
                    maxE = Math.Max(eVal[lepIdx], eVal[lepIdx + 1]) - 1;
                    eVal[lepIdx] = (byte)eq7;
                    cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho1 & 2) >> 1));
                    lcxIdx++;
                    cq0 = cxVal[lcxIdx] + (cxVal[lcxIdx + 1] << 2);
                    cxVal[lcxIdx] = (byte)((rho1 & 8) >> 3);
                }

                // ---- UVLC: interleaved per OpenJPH subsequent rows ----
                EncodeUvlcInterleaved(ref writer, uq0, uq1, uvlcTbl);

                // Prepare next pair context
                cq0 |= ((rho1 & 4) >> 1) | ((rho1 & 8) >> 2);
            }
        }

        /// <summary>
        /// Decodes a cleanup codeword segment to reconstruct wavelet coefficients.
        /// </summary>
        public static void Decode(
            ReadOnlySpan<byte> segment, Span<int> output, int width, int height, int subbandType)
        {
            if (output.Length < width * height)
                throw new ArgumentException(
                    $"Output buffer length {output.Length} is less than {width}x{height}={width * height}.",
                    nameof(output));
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Width and height must be positive.");

            output.Slice(0, width * height).Clear();

            int quadW = (width + 1) >> 1;
            var reader = new HtCleanupReader(segment);

            byte[] eVal = new byte[quadW + 2];
            byte[] cxVal = new byte[quadW + 2];

            DecodeFirstRow(ref reader, output, width, height, quadW, eVal, cxVal);

            for (int y = 2; y < height; y += 2)
                DecodeSubsequentRow(ref reader, output, width, height, y, quadW, eVal, cxVal);
        }

        private static void DecodeFirstRow(
            ref HtCleanupReader reader, Span<int> output,
            int width, int height, int quadW, byte[] eVal, byte[] cxVal)
        {
            int lepIdx = 0;
            int lcxIdx = 0;
            int cq0 = 0;
            ushort[] decTbl = VlcTable.OjphDecodeTable0;

            for (int x = 0; x < width; x += 4)
            {
                int rho0, rho1 = 0;
                int ek0 = 0, ek1 = 0;
                int e10 = 0, e11 = 0;

                // ---- VLC decode quad 0 ----
                int uOff0;
                {
                    uint vlcBits = reader.PeekVlcBits(7);
                    int decIdx = (cq0 << 7) | (int)(vlcBits & 0x7F);
                    ushort inf = decTbl[decIdx];
                    int cwdLen0 = inf & 0x07;
                    uOff0 = (inf >> 3) & 1;
                    rho0 = (inf >> 4) & 0x0F;
                    e10 = (inf >> 8) & 0x0F;
                    ek0 = (inf >> 12) & 0x0F;

                    if (cq0 == 0)
                    {
                        bool melSig = reader.DecodeMelSignificance();
                        if (!melSig)
                        {
                            rho0 = 0;
                            uOff0 = 0;
                            e10 = 0;
                            ek0 = 0;
                        }
                        else
                        {
                            reader.AdvanceVlc(cwdLen0);
                        }
                    }
                    else
                    {
                        reader.AdvanceVlc(cwdLen0);
                    }
                }

                cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho0 & 2) >> 1));

                // ---- VLC decode quad 1 ----
                int cq1ctx = (rho0 >> 1) | (rho0 & 1);
                int uOff1 = 0;
                if (x + 2 < width)
                {
                    uint vlcBits = reader.PeekVlcBits(7);
                    int decIdx = (cq1ctx << 7) | (int)(vlcBits & 0x7F);
                    ushort inf = decTbl[decIdx];
                    int cwdLen1 = inf & 0x07;
                    uOff1 = (inf >> 3) & 1;
                    rho1 = (inf >> 4) & 0x0F;
                    e11 = (inf >> 8) & 0x0F;
                    ek1 = (inf >> 12) & 0x0F;

                    if (cq1ctx == 0)
                    {
                        bool melSig = reader.DecodeMelSignificance();
                        if (!melSig)
                        {
                            rho1 = 0;
                            uOff1 = 0;
                            e11 = 0;
                            ek1 = 0;
                        }
                        else
                        {
                            reader.AdvanceVlc(cwdLen1);
                        }
                    }
                    else
                    {
                        reader.AdvanceVlc(cwdLen1);
                    }
                }

                // ---- UVLC decode: first-row interleaved ----
                int uq0, uq1;
                DecodeUvlcFirstRow(ref reader, uOff0, uOff1, out uq0, out uq1);

                int Uq0 = (rho0 != 0) ? uq0 + 1 : 0; // kappa = 1
                int Uq1 = (rho1 != 0 && x + 2 < width) ? uq1 + 1 : 0;

                // ---- MagSgn decode quad 0 ----
                int deq0 = 0, deq1 = 0, deq2 = 0, deq3 = 0;
                if (rho0 != 0)
                {
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 1, Uq0, ek0, e10, 0,
                        0, x, width, height, out deq0);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 2, Uq0, ek0, e10, 1,
                        1, x, width, height, out deq1);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 4, Uq0, ek0, e10, 2,
                        0, x + 1, width, height, out deq2);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 8, Uq0, ek0, e10, 3,
                        1, x + 1, width, height, out deq3);
                }

                eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], deq1);
                lepIdx++;
                eVal[lepIdx] = (byte)deq3;
                lcxIdx++;
                cxVal[lcxIdx] = (byte)((rho0 & 8) >> 3);

                // ---- MagSgn decode quad 1 ----
                int deq4 = 0, deq5 = 0, deq6 = 0, deq7 = 0;
                if (rho1 != 0 && x + 2 < width)
                {
                    DecodeMagSgnSampleInline(ref reader, output, rho1, 1, Uq1, ek1, e11, 0,
                        0, x + 2, width, height, out deq4);
                    DecodeMagSgnSampleInline(ref reader, output, rho1, 2, Uq1, ek1, e11, 1,
                        1, x + 2, width, height, out deq5);
                    DecodeMagSgnSampleInline(ref reader, output, rho1, 4, Uq1, ek1, e11, 2,
                        0, x + 3, width, height, out deq6);
                    DecodeMagSgnSampleInline(ref reader, output, rho1, 8, Uq1, ek1, e11, 3,
                        1, x + 3, width, height, out deq7);
                }

                if (x + 2 < width)
                {
                    eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], deq5);
                    lepIdx++;
                    eVal[lepIdx] = (byte)deq7;
                    cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho1 & 2) >> 1));
                    lcxIdx++;
                    cxVal[lcxIdx] = (byte)((rho1 & 8) >> 3);
                }

                cq0 = (rho1 >> 1) | (rho1 & 1);
            }

            eVal[lepIdx + 1] = 0;
        }

        private static void DecodeSubsequentRow(
            ref HtCleanupReader reader, Span<int> output,
            int width, int height, int y, int quadW, byte[] eVal, byte[] cxVal)
        {
            int lepIdx = 0;
            int maxE = Math.Max(eVal[0], eVal[1]) - 1;
            eVal[0] = 0;
            int lcxIdx = 0;
            int cq0 = cxVal[0] + (cxVal[1] << 2);
            cxVal[0] = 0;

            ushort[] decTbl = VlcTable.OjphDecodeTable1;

            for (int x = 0; x < width; x += 4)
            {
                int rho0, rho1 = 0;
                int ek0 = 0, ek1 = 0;
                int e10 = 0, e11 = 0;

                // ---- VLC decode quad 0 ----
                int uOff0;
                {
                    uint vlcBits = reader.PeekVlcBits(7);
                    int decIdx = (cq0 << 7) | (int)(vlcBits & 0x7F);
                    ushort inf = decTbl[decIdx];
                    int cwdLen0 = inf & 0x07;
                    uOff0 = (inf >> 3) & 1;
                    rho0 = (inf >> 4) & 0x0F;
                    e10 = (inf >> 8) & 0x0F;
                    ek0 = (inf >> 12) & 0x0F;

                    if (cq0 == 0)
                    {
                        bool melSig = reader.DecodeMelSignificance();
                        if (!melSig)
                        {
                            rho0 = 0;
                            uOff0 = 0;
                            e10 = 0;
                            ek0 = 0;
                        }
                        else
                        {
                            reader.AdvanceVlc(cwdLen0);
                        }
                    }
                    else
                    {
                        reader.AdvanceVlc(cwdLen0);
                    }
                }

                int kappa0 = (rho0 & (rho0 - 1)) != 0 ? Math.Max(1, maxE) : 1;

                cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho0 & 2) >> 1));

                // ---- VLC decode quad 1 ----
                int cq1temp = cxVal[lcxIdx + 1] + (cxVal[lcxIdx + 2] << 2);
                int uOff1 = 0;
                if (x + 2 < width)
                {
                    cq1temp |= ((rho0 & 4) >> 1) | ((rho0 & 8) >> 2);

                    uint vlcBits = reader.PeekVlcBits(7);
                    int decIdx = (cq1temp << 7) | (int)(vlcBits & 0x7F);
                    ushort inf = decTbl[decIdx];
                    int cwdLen1 = inf & 0x07;
                    uOff1 = (inf >> 3) & 1;
                    rho1 = (inf >> 4) & 0x0F;
                    e11 = (inf >> 8) & 0x0F;
                    ek1 = (inf >> 12) & 0x0F;

                    if (cq1temp == 0)
                    {
                        bool melSig = reader.DecodeMelSignificance();
                        if (!melSig)
                        {
                            rho1 = 0;
                            uOff1 = 0;
                            e11 = 0;
                            ek1 = 0;
                        }
                        else
                        {
                            reader.AdvanceVlc(cwdLen1);
                        }
                    }
                    else
                    {
                        reader.AdvanceVlc(cwdLen1);
                    }
                }

                // ---- UVLC decode: interleaved ----
                int uq0, uq1;
                DecodeUvlcInterleaved(ref reader, uOff0, uOff1, out uq0, out uq1);

                int Uq0 = (rho0 != 0) ? uq0 + kappa0 : 0;

                // ---- MagSgn quad 0 ----
                int deq0 = 0, deq1 = 0, deq2 = 0, deq3 = 0;
                if (rho0 != 0)
                {
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 1, Uq0, ek0, e10, 0,
                        y, x, width, height, out deq0);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 2, Uq0, ek0, e10, 1,
                        y + 1, x, width, height, out deq1);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 4, Uq0, ek0, e10, 2,
                        y, x + 1, width, height, out deq2);
                    DecodeMagSgnSampleInline(ref reader, output, rho0, 8, Uq0, ek0, e10, 3,
                        y + 1, x + 1, width, height, out deq3);
                }

                eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], deq1);
                lepIdx++;
                maxE = Math.Max(eVal[lepIdx], eVal[lepIdx + 1]) - 1;
                eVal[lepIdx] = (byte)deq3;
                lcxIdx++;
                cxVal[lcxIdx] = (byte)((rho0 & 8) >> 3);

                // ---- MagSgn quad 1 ----
                int Uq1 = 0;
                int deq4 = 0, deq5 = 0, deq6 = 0, deq7 = 0;
                if (x + 2 < width)
                {
                    int kappa1 = (rho1 & (rho1 - 1)) != 0 ? Math.Max(1, maxE) : 1;
                    Uq1 = (rho1 != 0) ? uq1 + kappa1 : 0;

                    if (rho1 != 0)
                    {
                        DecodeMagSgnSampleInline(ref reader, output, rho1, 1, Uq1, ek1, e11, 0,
                            y, x + 2, width, height, out deq4);
                        DecodeMagSgnSampleInline(ref reader, output, rho1, 2, Uq1, ek1, e11, 1,
                            y + 1, x + 2, width, height, out deq5);
                        DecodeMagSgnSampleInline(ref reader, output, rho1, 4, Uq1, ek1, e11, 2,
                            y, x + 3, width, height, out deq6);
                        DecodeMagSgnSampleInline(ref reader, output, rho1, 8, Uq1, ek1, e11, 3,
                            y + 1, x + 3, width, height, out deq7);
                    }

                    eVal[lepIdx] = (byte)Math.Max(eVal[lepIdx], deq5);
                    lepIdx++;
                    maxE = Math.Max(eVal[lepIdx], eVal[lepIdx + 1]) - 1;
                    eVal[lepIdx] = (byte)deq7;
                    cxVal[lcxIdx] = (byte)(cxVal[lcxIdx] | (byte)((rho1 & 2) >> 1));
                    lcxIdx++;
                    cq0 = cxVal[lcxIdx] + (cxVal[lcxIdx + 1] << 2);
                    cxVal[lcxIdx] = (byte)((rho1 & 8) >> 3);
                }

                cq0 |= ((rho1 & 4) >> 1) | ((rho1 & 8) >> 2);
            }
        }

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetVal(ReadOnlySpan<int> c, int row, int col, int w, int h)
        {
            return (row < h && col < w) ? c[row * w + col] : 0;
        }

        /// <summary>
        /// Computes E (exponent) for a coefficient magnitude, matching OpenJPH.
        /// E = floor_log2(2*|v| - 1) + 1 = 32 - clz(2*|v| - 1).
        /// Uses unsigned arithmetic to handle shifted coefficients up to 31 bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeE(int value)
        {
            if (value == 0) return 0;
            uint abs = (uint)(value >= 0 ? value : -value);
            uint twoAbsMinusOne = (abs << 1) - 1;
            return FloorLog2U(twoAbsMinusOne) + 1;
        }

        /// <summary>
        /// Computes the MagSgn value: s = 2*(|v|-1) + sign_bit.
        /// The decoder reconstructs: |v| = (s >> 1) + 1, sign = s &amp; 1.
        /// Uses unsigned arithmetic to handle shifted coefficients up to 31 bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeS(int value)
        {
            if (value == 0) return 0;
            uint abs = (uint)(value >= 0 ? value : -value);
            uint sign = value < 0 ? 1u : 0u;
            return ((abs - 1) << 1) + sign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeMagSgnSample(ref HtCleanupWriter writer,
            int rho, int rhoBit, uint sVal, int Uq, int ek, int bitIdx)
        {
            if ((rho & rhoBit) == 0) return;
            int m = Uq - ((ek >> bitIdx) & 1);
            if (m > 0)
                writer.WriteMagSgnBits(sVal & ((1u << m) - 1), m);
        }

        /// <summary>
        /// Decodes a single MagSgn sample and writes to output, returning its E value.
        /// Uses separate e_k (for bit count) and e_1 (for embedded MSB) per OpenJPH.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecodeMagSgnSampleInline(
            ref HtCleanupReader reader, Span<int> output,
            int rho, int rhoBit, int Uq, int ek, int e1, int bitIdx,
            int r, int c, int width, int height, out int eqOut)
        {
            eqOut = 0;
            if ((rho & rhoBit) == 0) return;

            int ekBit = (ek >> bitIdx) & 1;
            int m = Uq - ekBit;
            uint msVal = (m > 0) ? reader.ReadMagSgnBits(m) : 0;

            int e1bit = (e1 >> bitIdx) & 1;
            uint sn = msVal | ((uint)e1bit << m);
            uint sign = sn & 1;
            int absVal = (int)(sn >> 1) + 1;

            uint twoAbsMinusOne = ((uint)absVal << 1) - 1;
            eqOut = FloorLog2U(twoAbsMinusOne) + 1;

            if (r < height && c < width)
                output[r * width + c] = sign != 0 ? -absVal : absVal;
        }

        #endregion

        #region UVLC

        /// <summary>
        /// Encodes UVLC for the first row per OpenJPH: 3-mode interleaved
        /// with a MEL event when both u_q values are positive.
        /// </summary>
        private static void EncodeUvlcFirstRow(
            ref HtCleanupWriter writer, int uq0, int uq1, VlcTable.UvlcEntry[] uvlcTbl)
        {
            // MEL event when both u_q are positive
            if (uq0 > 0 && uq1 > 0)
                writer.EncodeMel(Math.Min(uq0, uq1) > 2);

            if (uq0 > 2 && uq1 > 2)
            {
                // Mode A: both > 2, subtract 2 from indices, interleave pre/suf
                var e0 = uvlcTbl[uq0 - 2];
                var e1 = uvlcTbl[uq1 - 2];
                if (e0.PreLen > 0) writer.WriteVlcBits((uint)e0.Pre, e0.PreLen);
                if (e1.PreLen > 0) writer.WriteVlcBits((uint)e1.Pre, e1.PreLen);
                if (e0.SufLen > 0) writer.WriteVlcBits((uint)e0.Suf, e0.SufLen);
                if (e1.SufLen > 0) writer.WriteVlcBits((uint)e1.Suf, e1.SufLen);
            }
            else if (uq0 > 2 && uq1 > 0)
            {
                // Mode B: uq0 > 2, uq1 in {1,2}
                var e0 = uvlcTbl[uq0];
                writer.WriteVlcBits((uint)e0.Pre, e0.PreLen);
                writer.WriteVlcBits((uint)(uq1 - 1), 1);
                if (e0.SufLen > 0) writer.WriteVlcBits((uint)e0.Suf, e0.SufLen);
            }
            else
            {
                // Mode C: standard interleaved pre0,pre1,suf0,suf1
                var e0 = uvlcTbl[uq0];
                var e1 = uvlcTbl[uq1];
                if (e0.PreLen > 0) writer.WriteVlcBits((uint)e0.Pre, e0.PreLen);
                if (e1.PreLen > 0) writer.WriteVlcBits((uint)e1.Pre, e1.PreLen);
                if (e0.SufLen > 0) writer.WriteVlcBits((uint)e0.Suf, e0.SufLen);
                if (e1.SufLen > 0) writer.WriteVlcBits((uint)e1.Suf, e1.SufLen);
            }
        }

        /// <summary>
        /// Encodes UVLC for subsequent rows per OpenJPH: interleaved pre0,pre1,suf0,suf1,ext0,ext1.
        /// </summary>
        private static void EncodeUvlcInterleaved(
            ref HtCleanupWriter writer, int uq0, int uq1, VlcTable.UvlcEntry[] uvlcTbl)
        {
            var e0 = uvlcTbl[uq0];
            var e1 = uvlcTbl[uq1];
            if (e0.PreLen > 0) writer.WriteVlcBits((uint)e0.Pre, e0.PreLen);
            if (e1.PreLen > 0) writer.WriteVlcBits((uint)e1.Pre, e1.PreLen);
            if (e0.SufLen > 0) writer.WriteVlcBits((uint)e0.Suf, e0.SufLen);
            if (e1.SufLen > 0) writer.WriteVlcBits((uint)e1.Suf, e1.SufLen);
            if (e0.ExtLen > 0) writer.WriteVlcBits((uint)e0.Ext, e0.ExtLen);
            if (e1.ExtLen > 0) writer.WriteVlcBits((uint)e1.Ext, e1.ExtLen);
        }

        /// <summary>
        /// Decodes UVLC for the first row per OpenJPH's 3-mode interleaved format.
        /// </summary>
        private static void DecodeUvlcFirstRow(
            ref HtCleanupReader reader, int uOff0, int uOff1,
            out int uq0, out int uq1)
        {
            uq0 = 0;
            uq1 = 0;

            if (uOff0 == 0 && uOff1 == 0)
                return;

            // MEL event when both u_off are set
            bool melMinGt2 = false;
            if (uOff0 != 0 && uOff1 != 0)
                melMinGt2 = reader.DecodeMelSignificance();

            if (melMinGt2)
            {
                // Mode A: both uq > 2
                int preVal0, preSufLen0;
                DecodeUvlcPrefixPart(ref reader, out preVal0, out preSufLen0);
                int preVal1, preSufLen1;
                DecodeUvlcPrefixPart(ref reader, out preVal1, out preSufLen1);

                uq0 = preVal0;
                if (preSufLen0 > 0)
                {
                    uint s = reader.ReadVlcBits(preSufLen0);
                    uq0 = ReconstructUvlcFromPrefixAndSuffix(preVal0, (int)s, preSufLen0);
                }
                uq1 = preVal1;
                if (preSufLen1 > 0)
                {
                    uint s = reader.ReadVlcBits(preSufLen1);
                    uq1 = ReconstructUvlcFromPrefixAndSuffix(preVal1, (int)s, preSufLen1);
                }
                uq0 += 2;
                uq1 += 2;
            }
            else if (uOff0 != 0 && uOff1 != 0)
            {
                // MEL says min(uq0,uq1) <= 2. Could be mode B or C.
                // Decode first prefix to determine which mode.
                int preVal0, preSufLen0;
                DecodeUvlcPrefixPart(ref reader, out preVal0, out preSufLen0);

                if (preVal0 > 2)
                {
                    // Mode B: uq0 > 2, uq1 encoded as 1 bit
                    uint uq1bit = reader.ReadVlcBits(1);
                    uq1 = (int)uq1bit + 1;
                    uq0 = preVal0;
                    if (preSufLen0 > 0)
                    {
                        uint s = reader.ReadVlcBits(preSufLen0);
                        uq0 = ReconstructUvlcFromPrefixAndSuffix(preVal0, (int)s, preSufLen0);
                    }
                }
                else
                {
                    // Mode C
                    int preVal1, preSufLen1;
                    DecodeUvlcPrefixPart(ref reader, out preVal1, out preSufLen1);

                    uq0 = preVal0;
                    if (preSufLen0 > 0)
                    {
                        uint s = reader.ReadVlcBits(preSufLen0);
                        uq0 = ReconstructUvlcFromPrefixAndSuffix(preVal0, (int)s, preSufLen0);
                    }
                    uq1 = preVal1;
                    if (preSufLen1 > 0)
                    {
                        uint s = reader.ReadVlcBits(preSufLen1);
                        uq1 = ReconstructUvlcFromPrefixAndSuffix(preVal1, (int)s, preSufLen1);
                    }
                }
            }
            else if (uOff0 != 0)
            {
                uq0 = DecodeUvlcValue(ref reader);
            }
            else if (uOff1 != 0)
            {
                uq1 = DecodeUvlcValue(ref reader);
            }
        }

        /// <summary>
        /// Decodes the UVLC prefix and returns the prefix value and suffix length.
        /// </summary>
        private static void DecodeUvlcPrefixPart(
            ref HtCleanupReader reader, out int preVal, out int sufLen)
        {
            uint bits3 = reader.PeekVlcBits(3);

            if ((bits3 & 1) == 1)
            {
                reader.AdvanceVlc(1);
                preVal = 1;
                sufLen = 0;
                return;
            }

            if ((bits3 & 2) != 0)
            {
                reader.AdvanceVlc(2);
                preVal = 2;
                sufLen = 0;
                return;
            }

            if ((bits3 & 4) != 0)
            {
                reader.AdvanceVlc(3);
                preVal = 3;
                sufLen = 1;
                return;
            }

            reader.AdvanceVlc(3);
            preVal = 5;
            sufLen = 5;
        }

        /// <summary>
        /// Reconstructs the full UVLC value from a prefix value and suffix bits.
        /// </summary>
        private static int ReconstructUvlcFromPrefixAndSuffix(int preVal, int sufBits, int sufLen)
        {
            if (sufLen == 0)
                return preVal;
            if (sufLen == 1)
                return preVal + sufBits; // 3 + 0 or 3 + 1 = 3 or 4
            // sufLen == 5
            return 5 + sufBits;
        }

        /// <summary>
        /// Decodes UVLC for subsequent rows: interleaved pre0,pre1,suf0,suf1,ext0,ext1.
        /// </summary>
        private static void DecodeUvlcInterleaved(
            ref HtCleanupReader reader, int uOff0, int uOff1,
            out int uq0, out int uq1)
        {
            uq0 = 0;
            uq1 = 0;

            // Decode prefix for quad 0
            int preVal0 = 0, preSufLen0 = 0;
            if (uOff0 != 0)
                DecodeUvlcPrefixPart(ref reader, out preVal0, out preSufLen0);

            // Decode prefix for quad 1
            int preVal1 = 0, preSufLen1 = 0;
            if (uOff1 != 0)
                DecodeUvlcPrefixPart(ref reader, out preVal1, out preSufLen1);

            // Decode suffix for quad 0
            int suf0 = 0;
            int needExt0 = 0;
            if (uOff0 != 0 && preSufLen0 > 0)
            {
                suf0 = (int)reader.ReadVlcBits(preSufLen0);
                if (preSufLen0 == 5 && suf0 >= 28)
                    needExt0 = 1;
            }

            // Decode suffix for quad 1
            int suf1 = 0;
            int needExt1 = 0;
            if (uOff1 != 0 && preSufLen1 > 0)
            {
                suf1 = (int)reader.ReadVlcBits(preSufLen1);
                if (preSufLen1 == 5 && suf1 >= 28)
                    needExt1 = 1;
            }

            // Decode ext for quad 0
            int ext0 = 0;
            if (needExt0 != 0)
                ext0 = (int)reader.ReadVlcBits(4);

            // Decode ext for quad 1
            int ext1 = 0;
            if (needExt1 != 0)
                ext1 = (int)reader.ReadVlcBits(4);

            // Reconstruct values
            if (uOff0 != 0)
            {
                if (preSufLen0 == 0)
                    uq0 = preVal0;
                else if (preSufLen0 == 1)
                    uq0 = preVal0 + suf0;
                else
                {
                    int idx = 5 + suf0;
                    if (idx >= 33)
                        uq0 = 33 + ext0 * 4 + (suf0 - 28);
                    else
                        uq0 = idx;
                }
            }

            if (uOff1 != 0)
            {
                if (preSufLen1 == 0)
                    uq1 = preVal1;
                else if (preSufLen1 == 1)
                    uq1 = preVal1 + suf1;
                else
                {
                    int idx = 5 + suf1;
                    if (idx >= 33)
                        uq1 = 33 + ext1 * 4 + (suf1 - 28);
                    else
                        uq1 = idx;
                }
            }
        }

        /// <summary>
        /// Decodes a single UVLC value from the VLC stream (non-interleaved).
        /// </summary>
        private static int DecodeUvlcValue(ref HtCleanupReader reader)
        {
            uint bits3 = reader.PeekVlcBits(3);

            if ((bits3 & 1) == 1)
            {
                reader.AdvanceVlc(1);
                return 1;
            }

            if ((bits3 & 2) != 0)
            {
                reader.AdvanceVlc(2);
                return 2;
            }

            if ((bits3 & 4) != 0)
            {
                reader.AdvanceVlc(3);
                uint suf = reader.ReadVlcBits(1);
                return 3 + (int)suf;
            }

            reader.AdvanceVlc(3);
            uint suffix5 = reader.ReadVlcBits(5);
            int idx = 5 + (int)suffix5;

            if (idx < 33) return idx;

            uint ext4 = reader.ReadVlcBits(4);
            return 33 + (int)ext4 * 4 + ((int)suffix5 - 28);
        }

        #endregion

        #region FloorLog2

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorLog2(int n)
        {
#if !NETSTANDARD2_0
            return 31 - BitOperations.LeadingZeroCount((uint)n);
#else
            int result = 0;
            uint v = (uint)n;
            if (v >= 0x10000) { result += 16; v >>= 16; }
            if (v >= 0x100) { result += 8; v >>= 8; }
            if (v >= 0x10) { result += 4; v >>= 4; }
            if (v >= 0x4) { result += 2; v >>= 2; }
            if (v >= 0x2) { result += 1; }
            return result;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorLog2U(uint n)
        {
#if !NETSTANDARD2_0
            return 31 - BitOperations.LeadingZeroCount(n);
#else
            int result = 0;
            uint v = n;
            if (v >= 0x10000) { result += 16; v >>= 16; }
            if (v >= 0x100) { result += 8; v >>= 8; }
            if (v >= 0x10) { result += 4; v >>= 4; }
            if (v >= 0x4) { result += 2; v >>= 2; }
            if (v >= 0x2) { result += 1; }
            return result;
#endif
        }

        #endregion
    }
}
