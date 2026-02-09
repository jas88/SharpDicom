using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    [TestFixture]
    public class HtCleanupDebugTest
    {
        [Test]
        public void Debug_SingleTopLeft_Value5()
        {
            // 4x4 block, coeff[0]=5 (row 0, col 0 = TL of quad 0)
            int width = 4, height = 4;
            int[] coefficients = new int[16];
            coefficients[0] = 5;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            Console.WriteLine($"Segment length: {segment.Length}");
            Console.Write("Segment hex: ");
            for (int i = 0; i < segment.Length; i++)
                Console.Write($"{segment[i]:X2} ");
            Console.WriteLine();

            int scup = (segment[segment.Length - 1] << 4) + (segment[segment.Length - 2] & 0x0F);
            Console.WriteLine($"ILW scup={scup}, MagSgn={segment.Length - scup}, MEL+VLC={scup}");

            int[] decoded = new int[16];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Console.Write("Decoded: ");
            for (int i = 0; i < 16; i++) Console.Write($"{decoded[i]} ");
            Console.WriteLine();

            Console.Write("Expected: ");
            for (int i = 0; i < 16; i++) Console.Write($"{coefficients[i]} ");
            Console.WriteLine();

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void Debug_2x2_Value5()
        {
            // Simplest possible: 2x2 with a single non-zero
            int[] coefficients = { 5, 0, 0, 0 };

            byte[] segment = HtCleanup.Encode(coefficients, 2, 2, 0);

            Console.WriteLine($"Segment length: {segment.Length}");
            Console.Write("Segment hex: ");
            for (int i = 0; i < segment.Length; i++)
                Console.Write($"{segment[i]:X2} ");
            Console.WriteLine();

            int scup = (segment[segment.Length - 1] << 4) + (segment[segment.Length - 2] & 0x0F);
            Console.WriteLine($"ILW scup={scup}");

            int[] decoded = new int[4];
            HtCleanup.Decode(segment, decoded, 2, 2, 0);

            Console.Write("Decoded: ");
            for (int i = 0; i < 4; i++) Console.Write($"{decoded[i]} ");
            Console.WriteLine();

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void Debug_2x2_Value1()
        {
            int[] coefficients = { 1, 0, 0, 0 };

            byte[] segment = HtCleanup.Encode(coefficients, 2, 2, 0);

            Console.WriteLine($"Segment length: {segment.Length}");
            Console.Write("Segment hex: ");
            for (int i = 0; i < segment.Length; i++)
                Console.Write($"{segment[i]:X2} ");
            Console.WriteLine();

            int scup = (segment[segment.Length - 1] << 4) + (segment[segment.Length - 2] & 0x0F);
            Console.WriteLine($"ILW scup={scup}");

            int[] decoded = new int[4];
            HtCleanup.Decode(segment, decoded, 2, 2, 0);

            Console.Write("Decoded: ");
            for (int i = 0; i < 4; i++) Console.Write($"{decoded[i]} ");
            Console.WriteLine();

            Assert.That(decoded, Is.EqualTo(coefficients));
        }
    }
}
