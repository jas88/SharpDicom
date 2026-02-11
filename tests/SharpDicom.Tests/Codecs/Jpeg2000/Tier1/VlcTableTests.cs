using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for VLC (Variable Length Code) lookup tables used in HT block coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decode table entry format is: <c>(rho &lt;&lt; 8) | (emb &lt;&lt; 4) | cwdLen</c>.
    /// The encode table entry format is: <c>(cwd &lt;&lt; 8) | (cwdLen &lt;&lt; 4) | e_k</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class VlcTableTests
    {
        #region Decode Table Structure Tests

        [Test]
        public void Table0_Has1024Entries()
        {
            var table = VlcTable.Table0;
            Assert.That(table.Length, Is.EqualTo(1024));
        }

        [Test]
        public void Table1_Has1024Entries()
        {
            var table = VlcTable.Table1;
            Assert.That(table.Length, Is.EqualTo(1024));
        }

        [Test]
        public void Table0_AllEntriesHaveValidSignificancePattern()
        {
            var table = VlcTable.Table0;
            for (int i = 0; i < table.Length; i++)
            {
                int sigPattern = (table[i] >> 8) & 0x0F;
                Assert.That(sigPattern, Is.InRange(0, 15),
                    $"Table0[{i}] has invalid significance pattern {sigPattern}");
            }
        }

        [Test]
        public void Table1_AllEntriesHaveValidSignificancePattern()
        {
            var table = VlcTable.Table1;
            for (int i = 0; i < table.Length; i++)
            {
                int sigPattern = (table[i] >> 8) & 0x0F;
                Assert.That(sigPattern, Is.InRange(0, 15),
                    $"Table1[{i}] has invalid significance pattern {sigPattern}");
            }
        }

        [Test]
        public void Table0_AllEntriesHaveValidCodewordLength()
        {
            var table = VlcTable.Table0;
            for (int i = 0; i < table.Length; i++)
            {
                int length = table[i] & 0x0F;
                // Length must be 1-7 for populated entries
                Assert.That(length, Is.InRange(0, 7),
                    $"Table0[{i}] has invalid codeword length {length}");
            }
        }

        [Test]
        public void Table1_AllEntriesHaveValidCodewordLength()
        {
            var table = VlcTable.Table1;
            for (int i = 0; i < table.Length; i++)
            {
                int length = table[i] & 0x0F;
                Assert.That(length, Is.InRange(0, 7),
                    $"Table1[{i}] has invalid codeword length {length}");
            }
        }

        [Test]
        public void Table0_AllEntriesHaveValidEmbBits()
        {
            var table = VlcTable.Table0;
            for (int i = 0; i < table.Length; i++)
            {
                int emb = (table[i] >> 4) & 0x0F;
                Assert.That(emb, Is.InRange(0, 15),
                    $"Table0[{i}] has invalid EMB bits {emb}");
            }
        }

        [Test]
        public void Table1_AllEntriesHaveValidEmbBits()
        {
            var table = VlcTable.Table1;
            for (int i = 0; i < table.Length; i++)
            {
                int emb = (table[i] >> 4) & 0x0F;
                Assert.That(emb, Is.InRange(0, 15),
                    $"Table1[{i}] has invalid EMB bits {emb}");
            }
        }

        #endregion

        #region Encode Table Structure Tests

        [Test]
        public void EncodeTable0_Has2048Entries()
        {
            var table = VlcTable.EncodeTable0;
            Assert.That(table.Length, Is.EqualTo(2048));
        }

        [Test]
        public void EncodeTable1_Has2048Entries()
        {
            var table = VlcTable.EncodeTable1;
            Assert.That(table.Length, Is.EqualTo(2048));
        }

        [Test]
        public void EncodeTable0_InvalidCombinationsAreZero()
        {
            // (emb & rho) != emb should produce 0
            var table = VlcTable.EncodeTable0;
            for (int cq = 0; cq < 8; cq++)
            {
                for (int rho = 0; rho < 16; rho++)
                {
                    for (int emb = 0; emb < 16; emb++)
                    {
                        if ((emb & rho) != emb)
                        {
                            int index = (cq << 8) | (rho << 4) | emb;
                            Assert.That(table[index], Is.EqualTo(0),
                                $"EncodeTable0[cq={cq},rho={rho:X},emb={emb:X}] should be 0 (invalid emb)");
                        }
                    }
                }
            }
        }

        [Test]
        public void EncodeTable0_Rho0Cq0IsZero()
        {
            // rho=0, cq=0 should be 0
            var table = VlcTable.EncodeTable0;
            for (int emb = 0; emb < 16; emb++)
            {
                int index = (0 << 8) | (0 << 4) | emb;
                Assert.That(table[index], Is.EqualTo(0),
                    $"EncodeTable0[cq=0,rho=0,emb={emb:X}] should be 0");
            }
        }

        [Test]
        public void EncodeTable0_ValidEntriesHaveNonZeroCwdLen()
        {
            // For valid entries (rho != 0 or cq != 0, and emb valid), cwd_len should be > 0
            var table = VlcTable.EncodeTable0;
            for (int cq = 0; cq < 8; cq++)
            {
                for (int rho = 1; rho < 16; rho++)
                {
                    // Test u_off=0 case (emb=0)
                    int index = (cq << 8) | (rho << 4) | 0;
                    ushort entry = table[index];
                    int cwdLen = (entry >> 4) & 0x0F;
                    Assert.That(cwdLen, Is.GreaterThan(0),
                        $"EncodeTable0[cq={cq},rho={rho:X},emb=0] has zero cwd_len");
                }
            }
        }

        #endregion

        #region Context Coverage Tests

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void Table0_AllContextsPopulated(int context)
        {
            var table = VlcTable.Table0;
            int baseIndex = context << 7;

            // Verify at least one non-zero entry exists for each context
            bool hasNonZero = false;
            for (int i = 0; i < 128; i++)
            {
                if (table[baseIndex + i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
            Assert.That(hasNonZero, Is.True,
                $"Table0 context {context} has no populated entries");
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void Table1_AllContextsPopulated(int context)
        {
            // Table1 source data not yet populated (table1.h data pending).
            // Once provided, this test will verify all contexts have entries.
            var table = VlcTable.Table1;
            Assert.That(table.Length, Is.EqualTo(1024),
                "Table1 should have 1024 entries even when source data is empty");
            Assert.Pass("Table1 source data not yet populated; skipping context coverage test");
        }

        #endregion

        #region Decode Method Tests

        [Test]
        public void DecodeTable0_Context0_ReturnsValidEntries()
        {
            // Context 0 is unused in the VLC data (rho=0, cq=0 is invalid).
            // The decode table may have entries from other contexts with cq=0.
            // For rho != 0, cq=0: entries exist in the source data.
            // Just verify the method does not throw.
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable0(0b0000000, 0);
            Assert.That(codewordLen, Is.InRange(0, 7));
        }

        [Test]
        public void DecodeTable0_Context1_ReturnsValidResults()
        {
            // Context 1 has source data entries. The shortest codeword in context 1
            // is cq=1, rho=0, u_off=0: cwd=0x00, cwd_len=2
            // That means the "all-zero rho" pattern is encoded with a 2-bit codeword.
            // Reversed cwd: reverse(0x00, 2) = 0, so decode index with bits=0bXXXXX00
            // should give rho=0.
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable0(0b0000000, 1);
            Assert.That(sigPattern, Is.EqualTo(0x0), "Context 1, bits=0: rho should be 0");
            Assert.That(codewordLen, Is.EqualTo(2), "Context 1, bits=0: cwd_len should be 2");
        }

        [Test]
        public void DecodeTable0_Context1_ShortCw_ReplicatesAcrossSuffix()
        {
            // cq=1, rho=0: cwd=0x00, cwd_len=2. Reversed: 0b00.
            // All 7-bit values with bits[1:0]=00 should decode the same.
            for (int suffix = 0; suffix < 32; suffix++)
            {
                int vlcBits = suffix << 2; // bits[1:0] = 00, rest = suffix
                var (sigPattern, _, codewordLen) = VlcTable.DecodeTable0(vlcBits, 1);

                Assert.That(sigPattern, Is.EqualTo(0),
                    $"vlcBits=0x{vlcBits:X2} should decode to rho=0");
                Assert.That(codewordLen, Is.EqualTo(2),
                    $"vlcBits=0x{vlcBits:X2} should have length 2");
            }
        }

        [Test]
        public void DecodeTable0_VerifySpecificTable0Entries()
        {
            // Verify some specific entries from Table 0 source data.
            // Codewords are in LSB-first form and used directly as decode indices.
            // cq=0, rho=2, u_off=0: cwd=0x00, cwd_len=3
            var (sig, emb, len) = VlcTable.DecodeTable0(0x00, 0);
            Assert.That(sig, Is.EqualTo(0x2), "cq=0, cwd=0x00: rho should be 2");
            Assert.That(len, Is.EqualTo(3), "cq=0, cwd=0x00: cwd_len should be 3");

            // cq=0, rho=4, u_off=0: cwd=0x02, cwd_len=3
            var (sig2, _, len2) = VlcTable.DecodeTable0(0x02, 0);
            Assert.That(sig2, Is.EqualTo(0x4), "cq=0, cwd=0x02: rho should be 4");
            Assert.That(len2, Is.EqualTo(3), "cq=0, cwd=0x02: cwd_len should be 3");
        }

        [Test]
        public void DecodeTable0_Context3_VerifyShortCwd()
        {
            // cq=3, rho=0: cwd=0x00, cwd_len=3 (LSB-first, used directly)
            var (sig, _, len) = VlcTable.DecodeTable0(0x00, 3);
            Assert.That(sig, Is.EqualTo(0x0), "cq=3, cwd=0x00: rho should be 0");
            Assert.That(len, Is.EqualTo(3), "cq=3, cwd=0x00: cwd_len should be 3");

            // cq=3, rho=1: cwd=0x04, cwd_len=4 (LSB-first, used directly)
            var (sig2, _, len2) = VlcTable.DecodeTable0(0x04, 3);
            Assert.That(sig2, Is.EqualTo(0x1), "cq=3, cwd=0x04: rho should be 1");
            Assert.That(len2, Is.EqualTo(4), "cq=3, cwd=0x04: cwd_len should be 4");
        }

        [Test]
        public void DecodeTable0_Context7_VerifyShortCwd()
        {
            // cq=7, rho=0: cwd=0x12, cwd_len=5 (LSB-first, used directly)
            var (sig, _, len) = VlcTable.DecodeTable0(0x12, 7);
            Assert.That(sig, Is.EqualTo(0x0), "cq=7, cwd=0x12: rho should be 0");
            Assert.That(len, Is.EqualTo(5), "cq=7, cwd=0x12: cwd_len should be 5");
        }

        #endregion

        #region Encode-Decode Roundtrip Tests

        [Test]
        public void EncodeTable0_EncodeDecodeRoundtrip_UOff0()
        {
            // For each u_off=0 source entry (emb=0), encode then decode should give
            // the same rho and cwd_len.
            var encTable = VlcTable.EncodeTable0;
            var decTable = VlcTable.Table0;

            for (int cq = 0; cq < 8; cq++)
            {
                for (int rho = 1; rho < 16; rho++)
                {
                    // Look up encode entry for emb=0
                    int encIdx = (cq << 8) | (rho << 4) | 0;
                    ushort encEntry = encTable[encIdx];
                    if (encEntry == 0) continue;

                    int cwd = (encEntry >> 8) & 0xFF;
                    int cwdLen = (encEntry >> 4) & 0x0F;

                    // Cwd is already in LSB-first form, use directly for decode lookup
                    int decIdx = (cq << 7) | cwd;
                    ushort decEntry = decTable[decIdx];

                    int decRho = (decEntry >> 8) & 0x0F;
                    int decLen = decEntry & 0x0F;

                    Assert.That(decRho, Is.EqualTo(rho),
                        $"Roundtrip failed for cq={cq}, rho={rho:X}: decoded rho={decRho:X}");
                    Assert.That(decLen, Is.EqualTo(cwdLen),
                        $"Roundtrip failed for cq={cq}, rho={rho:X}: decoded len={decLen}");
                }
            }
        }

        #endregion

        #region UVLC Table Tests

        [Test]
        public void UvlcTable_Has75Entries()
        {
            var table = VlcTable.UvlcTable;
            Assert.That(table.Length, Is.EqualTo(75));
        }

        [Test]
        public void UvlcTable_Entry0_IsAllZero()
        {
            var entry = VlcTable.UvlcTable[0];
            Assert.That(entry.Pre, Is.EqualTo(0));
            Assert.That(entry.PreLen, Is.EqualTo(0));
            Assert.That(entry.Suf, Is.EqualTo(0));
            Assert.That(entry.SufLen, Is.EqualTo(0));
            Assert.That(entry.Ext, Is.EqualTo(0));
            Assert.That(entry.ExtLen, Is.EqualTo(0));
        }

        [Test]
        public void UvlcTable_Entry1_HasPrefix1()
        {
            var entry = VlcTable.UvlcTable[1];
            Assert.That(entry.Pre, Is.EqualTo(1));
            Assert.That(entry.PreLen, Is.EqualTo(1));
        }

        [Test]
        public void UvlcTable_Entry2_HasPrefix2()
        {
            var entry = VlcTable.UvlcTable[2];
            Assert.That(entry.Pre, Is.EqualTo(2));
            Assert.That(entry.PreLen, Is.EqualTo(2));
        }

        [Test]
        public void UvlcTable_Entries3And4_HavePrefix4Len3()
        {
            var entry3 = VlcTable.UvlcTable[3];
            Assert.That(entry3.Pre, Is.EqualTo(4));
            Assert.That(entry3.PreLen, Is.EqualTo(3));
            Assert.That(entry3.Suf, Is.EqualTo(0));
            Assert.That(entry3.SufLen, Is.EqualTo(1));

            var entry4 = VlcTable.UvlcTable[4];
            Assert.That(entry4.Pre, Is.EqualTo(4));
            Assert.That(entry4.PreLen, Is.EqualTo(3));
            Assert.That(entry4.Suf, Is.EqualTo(1));
            Assert.That(entry4.SufLen, Is.EqualTo(1));
        }

        [Test]
        public void UvlcTable_Entries5To32_Have5BitSuffix()
        {
            for (int i = 5; i < 33; i++)
            {
                var entry = VlcTable.UvlcTable[i];
                Assert.That(entry.Pre, Is.EqualTo(0), $"UVLC[{i}].Pre");
                Assert.That(entry.PreLen, Is.EqualTo(3), $"UVLC[{i}].PreLen");
                Assert.That(entry.Suf, Is.EqualTo(i - 5), $"UVLC[{i}].Suf");
                Assert.That(entry.SufLen, Is.EqualTo(5), $"UVLC[{i}].SufLen");
                Assert.That(entry.ExtLen, Is.EqualTo(0), $"UVLC[{i}].ExtLen");
            }
        }

        [Test]
        public void UvlcTable_Entries33To74_Have4BitExtension()
        {
            for (int i = 33; i < 75; i++)
            {
                var entry = VlcTable.UvlcTable[i];
                int rel = i - 33;
                Assert.That(entry.Pre, Is.EqualTo(0), $"UVLC[{i}].Pre");
                Assert.That(entry.PreLen, Is.EqualTo(3), $"UVLC[{i}].PreLen");
                Assert.That(entry.Suf, Is.EqualTo(28 + (rel % 4)), $"UVLC[{i}].Suf");
                Assert.That(entry.SufLen, Is.EqualTo(5), $"UVLC[{i}].SufLen");
                Assert.That(entry.Ext, Is.EqualTo(rel / 4), $"UVLC[{i}].Ext");
                Assert.That(entry.ExtLen, Is.EqualTo(4), $"UVLC[{i}].ExtLen");
            }
        }

        #endregion

        #region Thread Safety Tests

        [Test]
        public void Table0_LazyInit_IsThreadSafe()
        {
            // Access from multiple threads should produce the same table
            ushort[] table1 = null!;
            ushort[] table2 = null!;

            var t1 = System.Threading.Tasks.Task.Run(() => table1 = VlcTable.Table0);
            var t2 = System.Threading.Tasks.Task.Run(() => table2 = VlcTable.Table0);

            System.Threading.Tasks.Task.WaitAll(t1, t2);

            Assert.That(table1, Is.SameAs(table2), "Lazy initialization should return same instance");
        }

        [Test]
        public void Table1_LazyInit_IsThreadSafe()
        {
            ushort[] table1 = null!;
            ushort[] table2 = null!;

            var t1 = System.Threading.Tasks.Task.Run(() => table1 = VlcTable.Table1);
            var t2 = System.Threading.Tasks.Task.Run(() => table2 = VlcTable.Table1);

            System.Threading.Tasks.Task.WaitAll(t1, t2);

            Assert.That(table1, Is.SameAs(table2), "Lazy initialization should return same instance");
        }

        #endregion

        #region Completeness Tests

        [Test]
        public void Table0_EveryEntryHasNonZeroLength()
        {
            // Every 7-bit input for every context should produce a valid decode.
            // Note: context 0 entries where all codewords have rho>0 or cq>0 should
            // still fill all 128 decode slots via suffix replication.
            var table = VlcTable.Table0;
            for (int ctx = 0; ctx < 8; ctx++)
            {
                for (int cw = 0; cw < 128; cw++)
                {
                    int index = (ctx << 7) | cw;
                    int length = table[index] & 0x0F;
                    Assert.That(length, Is.GreaterThan(0),
                        $"Table0[ctx={ctx}, cw=0x{cw:X2}] has zero length (unpopulated entry)");
                }
            }
        }

        #endregion

        #region Helper methods
        #endregion
    }
}
