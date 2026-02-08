using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for VLC (Variable Length Code) lookup tables used in HT block coding.
    /// </summary>
    [TestFixture]
    public class VlcTableTests
    {
        #region Table Structure Tests

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
                int sigPattern = table[i] & 0x0F;
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
                int sigPattern = table[i] & 0x0F;
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
                int length = (table[i] >> 8) & 0x0F;
                Assert.That(length, Is.InRange(1, 7),
                    $"Table0[{i}] has invalid codeword length {length}");
            }
        }

        [Test]
        public void Table1_AllEntriesHaveValidCodewordLength()
        {
            var table = VlcTable.Table1;
            for (int i = 0; i < table.Length; i++)
            {
                int length = (table[i] >> 8) & 0x0F;
                Assert.That(length, Is.InRange(1, 7),
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
            // Even all-zero significance pattern has a valid entry (length > 0)
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
            var table = VlcTable.Table1;
            int baseIndex = context << 7;

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
                $"Table1 context {context} has no populated entries");
        }

        #endregion

        #region Decode Method Tests

        [Test]
        public void DecodeTable0_Context0_ZeroCw_ReturnsAllInsignificant()
        {
            // Context 0, codeword starting with '0' (1-bit codeword for all-insignificant)
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable0(0b0000000, 0);

            Assert.That(sigPattern, Is.EqualTo(0), "All-zero pattern expected");
            Assert.That(embBits, Is.EqualTo(0), "No EMB bits expected");
            Assert.That(codewordLen, Is.EqualTo(1), "1-bit codeword expected");
        }

        [Test]
        public void DecodeTable0_Context0_ShortCw_ReplicatesAcrossSuffix()
        {
            // All 7-bit values starting with 0 should decode the same (1-bit codeword)
            for (int suffix = 0; suffix < 64; suffix++)
            {
                int vlcBits = suffix << 1; // bit 0 = 0, rest = suffix
                var (sigPattern, _, codewordLen) = VlcTable.DecodeTable0(vlcBits, 0);

                Assert.That(sigPattern, Is.EqualTo(0),
                    $"vlcBits={vlcBits:B7} should decode to sig=0");
                Assert.That(codewordLen, Is.EqualTo(1),
                    $"vlcBits={vlcBits:B7} should have length 1");
            }
        }

        [Test]
        public void DecodeTable0_Context0_Codeword10_ReturnsSig1()
        {
            // Codeword '10' (MSB-first) reversed to LSB-first = '01' = 1
            // In the VLC stream, bits are indexed LSB-first
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable0(0b01, 0);

            Assert.That(sigPattern, Is.EqualTo(0x1), "sig=0001 expected");
            Assert.That(embBits, Is.EqualTo(0x1), "emb=0001 expected");
            Assert.That(codewordLen, Is.EqualTo(2), "2-bit codeword expected");
        }

        [Test]
        public void DecodeTable1_Context0_ZeroCw_ReturnsAllInsignificant()
        {
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable1(0b0000000, 0);

            Assert.That(sigPattern, Is.EqualTo(0), "All-zero pattern expected");
            Assert.That(embBits, Is.EqualTo(0), "No EMB bits expected");
            Assert.That(codewordLen, Is.EqualTo(1), "1-bit codeword expected");
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
        public void DecodeTable0_AllContexts_7BitCw_ReturnMaxSig(int context)
        {
            // The 7-bit codeword 0b1111111 should map to sig=0xF (all significant)
            // for all contexts in Table0
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable0(0b1111111, context);

            Assert.That(sigPattern, Is.EqualTo(0xF),
                $"Context {context}: 7-bit max codeword should decode to all-significant");
            Assert.That(codewordLen, Is.EqualTo(7),
                $"Context {context}: should consume all 7 bits");
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
        public void DecodeTable1_AllContexts_7BitCw_ReturnMaxSig(int context)
        {
            var (sigPattern, embBits, codewordLen) = VlcTable.DecodeTable1(0b1111111, context);

            Assert.That(sigPattern, Is.EqualTo(0xF),
                $"Context {context}: 7-bit max codeword should decode to all-significant");
            Assert.That(codewordLen, Is.EqualTo(7),
                $"Context {context}: should consume all 7 bits");
        }

        #endregion

        #region Context-Specific Value Tests

        [Test]
        public void DecodeTable0_Context1_MostLikely_IsSig1()
        {
            // Context 1 (bottom-right neighbour significant): most likely pattern is sig=0001
            var (sigPattern, _, codewordLen) = VlcTable.DecodeTable0(0b0000000, 1);

            Assert.That(sigPattern, Is.EqualTo(0x1), "Context 1 most likely should be sig=0001");
            Assert.That(codewordLen, Is.EqualTo(1), "Most likely pattern uses 1-bit codeword");
        }

        [Test]
        public void DecodeTable1_Context3_MostLikely_IsSigC()
        {
            // Context 3 (both top samples significant): most likely is sig=1100
            var (sigPattern, _, codewordLen) = VlcTable.DecodeTable1(0b0000000, 3);

            Assert.That(sigPattern, Is.EqualTo(0xC), "Context 3 most likely should be sig=1100");
            Assert.That(codewordLen, Is.EqualTo(1), "Most likely pattern uses 1-bit codeword");
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
            // Every 7-bit input for every context should produce a valid decode
            var table = VlcTable.Table0;
            for (int ctx = 0; ctx < 8; ctx++)
            {
                for (int cw = 0; cw < 128; cw++)
                {
                    int index = (ctx << 7) | cw;
                    int length = (table[index] >> 8) & 0x0F;
                    Assert.That(length, Is.GreaterThan(0),
                        $"Table0[ctx={ctx}, cw={cw:B7}] has zero length (unpopulated entry)");
                }
            }
        }

        [Test]
        public void Table1_EveryEntryHasNonZeroLength()
        {
            var table = VlcTable.Table1;
            for (int ctx = 0; ctx < 8; ctx++)
            {
                for (int cw = 0; cw < 128; cw++)
                {
                    int index = (ctx << 7) | cw;
                    int length = (table[index] >> 8) & 0x0F;
                    Assert.That(length, Is.GreaterThan(0),
                        $"Table1[ctx={ctx}, cw={cw:B7}] has zero length (unpopulated entry)");
                }
            }
        }

        #endregion
    }
}
