using System;
using NUnit.Framework;
#if !TESTING_NETSTANDARD_POLYFILLS
using SharpDicom.Codecs.Simd;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif
#endif

namespace SharpDicom.Tests.Codecs.Simd
{
#if !TESTING_NETSTANDARD_POLYFILLS
    [TestFixture]
    public class SimdHelpersExtendedTests
    {
        // =====================================================================
        // Bit manipulation tests (work on all targets)
        // =====================================================================

        [Test]
        public void LeadingZeroCount_Zero_Returns32()
        {
            Assert.That(SimdHelpers.LeadingZeroCount(0u), Is.EqualTo(32));
        }

        [Test]
        public void LeadingZeroCount_One_Returns31()
        {
            Assert.That(SimdHelpers.LeadingZeroCount(1u), Is.EqualTo(31));
        }

        [Test]
        public void LeadingZeroCount_HighBitSet_Returns0()
        {
            Assert.That(SimdHelpers.LeadingZeroCount(0x80000000u), Is.EqualTo(0));
        }

        [Test]
        public void LeadingZeroCount_AllOnes_Returns0()
        {
            Assert.That(SimdHelpers.LeadingZeroCount(0xFFFFFFFFu), Is.EqualTo(0));
        }

        [TestCase(0x00010000u, 15)]
        [TestCase(0x00000100u, 23)]
        [TestCase(0x40000000u, 1)]
        [TestCase(0x00008000u, 16)]
        public void LeadingZeroCount_KnownValues(uint value, int expected)
        {
            Assert.That(SimdHelpers.LeadingZeroCount(value), Is.EqualTo(expected));
        }

        [Test]
        public void PopCount_Zero_Returns0()
        {
            Assert.That(SimdHelpers.PopCount(0u), Is.EqualTo(0));
        }

        [Test]
        public void PopCount_One_Returns1()
        {
            Assert.That(SimdHelpers.PopCount(1u), Is.EqualTo(1));
        }

        [Test]
        public void PopCount_AllOnes_Returns32()
        {
            Assert.That(SimdHelpers.PopCount(0xFFFFFFFFu), Is.EqualTo(32));
        }

        [TestCase(0b10101010u, 4)]
        [TestCase(0b11111111u, 8)]
        [TestCase(0b10000001u, 2)]
        [TestCase(0x0F0F0F0Fu, 16)]
        public void PopCount_KnownValues(uint value, int expected)
        {
            Assert.That(SimdHelpers.PopCount(value), Is.EqualTo(expected));
        }

        [Test]
        public void ExtractBits_AllBitsSelected_ReturnsSame()
        {
            // mask = 0xFF, source = 0xA5 -> extract all 8 bits -> 0xA5
            ulong result = SimdHelpers.ExtractBits(0xA5UL, 0xFFUL);
            Assert.That(result, Is.EqualTo(0xA5UL));
        }

        [Test]
        public void ExtractBits_AlternateBits_ExtractsCorrectly()
        {
            // source = 0b10101010, mask = 0b10101010 (even bits)
            // Extract bits 1, 3, 5, 7 -> packed into low bits -> 0b1111 = 0xF
            ulong result = SimdHelpers.ExtractBits(0xAAUL, 0xAAUL);
            Assert.That(result, Is.EqualTo(0xFUL));
        }

        [Test]
        public void ExtractBits_ZeroMask_ReturnsZero()
        {
            ulong result = SimdHelpers.ExtractBits(0xFFFFFFFFUL, 0UL);
            Assert.That(result, Is.EqualTo(0UL));
        }

        [Test]
        public void DepositBits_AllBitsSelected_ReturnsSame()
        {
            // mask = 0xFF, source = 0xA5 -> deposit all 8 bits -> 0xA5
            ulong result = SimdHelpers.DepositBits(0xA5UL, 0xFFUL);
            Assert.That(result, Is.EqualTo(0xA5UL));
        }

        [Test]
        public void DepositBits_AlternateBits_DepositsCorrectly()
        {
            // source = 0b1111, mask = 0b10101010 (even bit positions)
            // Deposit 4 bits into positions 1, 3, 5, 7 -> 0b10101010 = 0xAA
            ulong result = SimdHelpers.DepositBits(0xFUL, 0xAAUL);
            Assert.That(result, Is.EqualTo(0xAAUL));
        }

        [Test]
        public void DepositBits_ZeroMask_ReturnsZero()
        {
            ulong result = SimdHelpers.DepositBits(0xFFFFFFFFUL, 0UL);
            Assert.That(result, Is.EqualTo(0UL));
        }

        [Test]
        public void ExtractBits_DepositBits_Roundtrip()
        {
            // Extract then deposit should give back the original masked value
            ulong source = 0x12345678UL;
            ulong mask = 0xFF00FF00UL;

            ulong extracted = SimdHelpers.ExtractBits(source, mask);
            ulong deposited = SimdHelpers.DepositBits(extracted, mask);

            Assert.That(deposited, Is.EqualTo(source & mask),
                "PEXT then PDEP should roundtrip to (source & mask)");
        }

        [Test]
        public void DepositBits_ExtractBits_Roundtrip()
        {
            // Deposit then extract should give back the original low bits
            ulong source = 0x00AB;
            ulong mask = 0xFFFF0000UL;

            ulong deposited = SimdHelpers.DepositBits(source, mask);
            ulong extracted = SimdHelpers.ExtractBits(deposited, mask);

            // Only the low PopCount(mask) bits of source are preserved
            int maskBits = 0;
            ulong m = mask;
            while (m != 0) { maskBits++; m &= m - 1; }
            ulong expectedMask = (1UL << maskBits) - 1;

            Assert.That(extracted, Is.EqualTo(source & expectedMask),
                "PDEP then PEXT should roundtrip within bit count of mask");
        }

        // =====================================================================
        // Hardware detection property tests
        // =====================================================================

        [Test]
        public void IsAvx512Supported_DoesNotThrow()
        {
            // Just verify the property doesn't throw
            _ = SimdHelpers.IsAvx512Supported;
        }

        [Test]
        public void IsBmi2Supported_DoesNotThrow()
        {
            // Just verify the property doesn't throw
            _ = SimdHelpers.IsBmi2Supported;
        }

#if NET8_0_OR_GREATER

        // =====================================================================
        // Vector256 tests (only on NET8+)
        // =====================================================================

        [Test]
        public void Vector256_HorizontalSum_Int_CorrectResult()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8);
            int sum = SimdHelpers.HorizontalSum(v);
            Assert.That(sum, Is.EqualTo(36)); // 1+2+3+4+5+6+7+8
        }

        [Test]
        public void Vector256_HorizontalSum_Int_WithNegatives()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(10, -5, 3, -8, 20, -10, 7, -17);
            int sum = SimdHelpers.HorizontalSum(v);
            Assert.That(sum, Is.EqualTo(0)); // 10-5+3-8+20-10+7-17 = 0
        }

        [Test]
        public void Vector256_HorizontalSum_Float_CorrectResult()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f);
            float sum = SimdHelpers.HorizontalSum(v);
            Assert.That(sum, Is.EqualTo(36.0f).Within(0.001f));
        }

        [Test]
        public void Vector256_HorizontalSum_Float_WithFractions()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1.0f);
            float sum = SimdHelpers.HorizontalSum(v);
            // Sum = 0.125+0.25+0.375+0.5+0.625+0.75+0.875+1.0 = 4.5
            Assert.That(sum, Is.EqualTo(4.5f).Within(0.001f));
        }

        [Test]
        public void Vector256_Clamp_Int_ClampsCorrectly()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(-10, 0, 50, 100, 200, 255, 300, -5);
            var clamped = SimdHelpers.Clamp(v, 0, 255);

            Assert.That(clamped.GetElement(0), Is.EqualTo(0));
            Assert.That(clamped.GetElement(1), Is.EqualTo(0));
            Assert.That(clamped.GetElement(2), Is.EqualTo(50));
            Assert.That(clamped.GetElement(3), Is.EqualTo(100));
            Assert.That(clamped.GetElement(4), Is.EqualTo(200));
            Assert.That(clamped.GetElement(5), Is.EqualTo(255));
            Assert.That(clamped.GetElement(6), Is.EqualTo(255));
            Assert.That(clamped.GetElement(7), Is.EqualTo(0));
        }

        [Test]
        public void Vector256_Clamp_Int_AlreadyInRange_Unchanged()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(10, 20, 30, 40, 50, 60, 70, 80);
            var clamped = SimdHelpers.Clamp(v, 0, 100);

            for (int i = 0; i < 8; i++)
            {
                Assert.That(clamped.GetElement(i), Is.EqualTo(v.GetElement(i)));
            }
        }

        [Test]
        public void Vector256_Abs_Int_CorrectResult()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(-1, 2, -3, 4, -5, 6, -7, 8);
            var abs = SimdHelpers.Abs(v);

            Assert.That(abs.GetElement(0), Is.EqualTo(1));
            Assert.That(abs.GetElement(1), Is.EqualTo(2));
            Assert.That(abs.GetElement(2), Is.EqualTo(3));
            Assert.That(abs.GetElement(3), Is.EqualTo(4));
            Assert.That(abs.GetElement(4), Is.EqualTo(5));
            Assert.That(abs.GetElement(5), Is.EqualTo(6));
            Assert.That(abs.GetElement(6), Is.EqualTo(7));
            Assert.That(abs.GetElement(7), Is.EqualTo(8));
        }

        [Test]
        public void Vector256_Abs_Int_ZerosUnchanged()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(0, 0, 0, 0, 0, 0, 0, 0);
            var abs = SimdHelpers.Abs(v);

            for (int i = 0; i < 8; i++)
            {
                Assert.That(abs.GetElement(i), Is.EqualTo(0));
            }
        }

        [Test]
        public void Vector256_Abs_Int_PositivesUnchanged()
        {
            if (!Vector256.IsHardwareAccelerated)
            {
                Assert.Ignore("Vector256 not hardware-accelerated on this platform");
            }

            var v = Vector256.Create(1, 100, 1000, 10000, 42, 7, 99, int.MaxValue);
            var abs = SimdHelpers.Abs(v);

            for (int i = 0; i < 8; i++)
            {
                Assert.That(abs.GetElement(i), Is.EqualTo(v.GetElement(i)));
            }
        }

#endif
    }
#endif
}
