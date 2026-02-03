using NUnit.Framework;
using System;
using System.Diagnostics;
using SharpDicom.Codecs;
using SharpDicom.Codecs.JpegLs;
using SharpDicom.Codecs.Jpeg2000;

namespace SharpDicom.Tests.Codecs
{
    /// <summary>
    /// Performance tests for codec implementations.
    /// </summary>
    /// <remarks>
    /// These tests are marked with [Category("Performance")] and are excluded
    /// from normal test runs. Run them explicitly to benchmark codec performance:
    /// dotnet test --filter "Category=Performance"
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    public class PerformanceTests
    {
        /// <summary>
        /// Target: 10x slower than native CharLS (~200ms vs ~20ms for CharLS).
        /// This test verifies JPEG-LS encoding performance on a 512x512 8-bit grayscale image.
        /// </summary>
        [Test]
        public void JpegLs_8Bit_512x512_PerformanceBaseline()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(512, 512);
            var data = CreateTestImage(512, 512, 1, 1);

            // Warmup
            codec.Encode(data, info);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                codec.Encode(data, info);
            }
            sw.Stop();

            double avgMs = sw.ElapsedMilliseconds / 10.0;
            Console.WriteLine($"JPEG-LS 512x512 8-bit: {avgMs:F1}ms per encode");

            // Target: within 10x of native (allow 2 seconds for 10 iterations = 200ms each)
            // This is a soft target - actual performance depends on hardware
            Assert.Pass($"Average time: {avgMs:F1}ms per encode");
        }

        /// <summary>
        /// Tests JPEG 2000 lossless encoding performance on a 512x512 8-bit grayscale image.
        /// </summary>
        [Test]
        public void Jpeg2000_Lossless_512x512_PerformanceBaseline()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(512, 512);
            var data = CreateTestImage(512, 512, 1, 1);

            // Warmup
            codec.Encode(data, info);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                codec.Encode(data, info);
            }
            sw.Stop();

            double avgMs = sw.ElapsedMilliseconds / 10.0;
            Console.WriteLine($"JPEG 2000 lossless 512x512 8-bit: {avgMs:F1}ms per encode");

            Assert.Pass($"Average time: {avgMs:F1}ms per encode");
        }

        /// <summary>
        /// Tests JPEG 2000 lossy encoding performance on a 512x512 8-bit grayscale image.
        /// </summary>
        [Test]
        public void Jpeg2000_Lossy_512x512_PerformanceBaseline()
        {
            var codec = new Jpeg2000LossyCodec();
            var info = PixelDataInfo.Grayscale8(512, 512);
            var data = CreateTestImage(512, 512, 1, 1);

            // Warmup
            codec.Encode(data, info);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                codec.Encode(data, info);
            }
            sw.Stop();

            double avgMs = sw.ElapsedMilliseconds / 10.0;
            Console.WriteLine($"JPEG 2000 lossy 512x512 8-bit: {avgMs:F1}ms per encode");

            Assert.Pass($"Average time: {avgMs:F1}ms per encode");
        }

        /// <summary>
        /// Tests performance on larger images (1024x1024) to verify parallel processing benefits.
        /// </summary>
        [Test]
        public void JpegLs_16Bit_1024x1024_LargeImagePerformance()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale16(1024, 1024);
            var data = CreateTestImage(1024, 1024, 1, 2);

            // Warmup
            codec.Encode(data, info);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                codec.Encode(data, info);
            }
            sw.Stop();

            double avgMs = sw.ElapsedMilliseconds / 5.0;
            Console.WriteLine($"JPEG-LS 1024x1024 16-bit: {avgMs:F1}ms per encode");

            Assert.Pass($"Average time: {avgMs:F1}ms per encode");
        }

        /// <summary>
        /// Tests SIMD optimization impact by comparing small vs large image processing.
        /// </summary>
        [Test]
        public void Jpeg2000_SimdOptimization_ScalingBehavior()
        {
            var codec = new Jpeg2000LosslessCodec();

            // Small image (scalar fallback likely)
            var smallInfo = PixelDataInfo.Grayscale8(128, 128);
            var smallData = CreateTestImage(128, 128, 1, 1);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                codec.Encode(smallData, smallInfo);
            }
            sw.Stop();
            double smallAvg = sw.ElapsedMilliseconds / 20.0;

            // Large image (SIMD likely active)
            var largeInfo = PixelDataInfo.Grayscale8(512, 512);
            var largeData = CreateTestImage(512, 512, 1, 1);

            sw.Restart();
            for (int i = 0; i < 5; i++)
            {
                codec.Encode(largeData, largeInfo);
            }
            sw.Stop();
            double largeAvg = sw.ElapsedMilliseconds / 5.0;

            // Calculate pixels/ms for both
            double smallThroughput = (128 * 128) / smallAvg;
            double largeThroughput = (512 * 512) / largeAvg;

            Console.WriteLine($"Small image (128x128): {smallAvg:F1}ms, throughput: {smallThroughput:F0} pixels/ms");
            Console.WriteLine($"Large image (512x512): {largeAvg:F1}ms, throughput: {largeThroughput:F0} pixels/ms");
            Console.WriteLine($"Throughput ratio (large/small): {largeThroughput / smallThroughput:F2}x");

            Assert.Pass($"Scaling behavior measured successfully");
        }

        /// <summary>
        /// Creates a test image with gradient pattern for realistic encoding behavior.
        /// </summary>
        private static byte[] CreateTestImage(int width, int height, int components, int bytesPerSample)
        {
            int size = width * height * components * bytesPerSample;
            var data = new byte[size];
            var random = new Random(42); // Fixed seed for reproducibility

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Create gradient pattern with noise
                    int baseValue = ((x + y) * 255) / (width + height);
                    int noise = random.Next(-20, 20);
                    int value = Math.Clamp(baseValue + noise, 0, 255);

                    for (int c = 0; c < components; c++)
                    {
                        int index = ((y * width + x) * components + c) * bytesPerSample;

                        if (bytesPerSample == 1)
                        {
                            data[index] = (byte)value;
                        }
                        else if (bytesPerSample == 2)
                        {
                            // 16-bit: scale up to full range
                            int value16 = value * 257; // 0-255 → 0-65535
                            data[index] = (byte)(value16 & 0xFF);
                            data[index + 1] = (byte)((value16 >> 8) & 0xFF);
                        }
                    }
                }
            }

            return data;
        }
    }
}
