using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Benchmarks
{
    /// <summary>
    /// BenchmarkDotNet performance suite for HTJ2K vs EBCOT block coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Run manually with:
    ///   dotnet run --project tests/SharpDicom.Tests -c Release -- --filter "*Htj2kBenchmarks*"
    /// </para>
    /// <para>
    /// NOT wired into CI gate. For automated performance regression detection,
    /// see the companion NUnit smoke test <see cref="Htj2kBenchmarkSmokeTests"/>.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
#if NET9_0_OR_GREATER
    [SimpleJob(RuntimeMoniker.Net90)]
#endif
#if NET8_0_OR_GREATER
    [SimpleJob(RuntimeMoniker.Net80)]
#endif
    public class Htj2kBenchmarks
    {
        private byte[] _pixelData8 = null!;
        private byte[] _pixelData16 = null!;
        private DicomFragmentSequence _htEncoded8 = null!;
        private DicomFragmentSequence _htEncoded16 = null!;
        private DicomFragmentSequence _ebcotEncoded8 = null!;
        private PixelDataInfo _info8;
        private PixelDataInfo _info16;

        [Params(256, 512, 2048)]
        public int Size { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // Generate gradient test image (not random, for reproducibility)
            _info8 = PixelDataInfo.Grayscale8((ushort)Size, (ushort)Size);
            _pixelData8 = CreateGradient8(Size, Size);

            _info16 = PixelDataInfo.Grayscale16((ushort)Size, (ushort)Size);
            _pixelData16 = CreateGradient16(Size, Size);

            // Pre-encode for decode benchmarks
            var htCodec = new Htj2kLosslessCodec();
            _htEncoded8 = htCodec.Encode(_pixelData8, _info8);
            _htEncoded16 = htCodec.Encode(_pixelData16, _info16);

            var j2kCodec = new Jpeg2000LosslessCodec();
            _ebcotEncoded8 = j2kCodec.Encode(_pixelData8, _info8);
        }

        // ---- HT Encode benchmarks ----

        [Benchmark(Description = "HT Encode 8-bit")]
        public DicomFragmentSequence HtEncode8Bit()
        {
            var codec = new Htj2kLosslessCodec();
            return codec.Encode(_pixelData8, _info8);
        }

        [Benchmark(Description = "HT Encode 16-bit")]
        public DicomFragmentSequence HtEncode16Bit()
        {
            var codec = new Htj2kLosslessCodec();
            return codec.Encode(_pixelData16, _info16);
        }

        // ---- HT Decode benchmarks ----

        [Benchmark(Description = "HT Decode 8-bit")]
        public byte[] HtDecode8Bit()
        {
            var codec = new Htj2kLosslessCodec();
            var decoded = new byte[_info8.FrameSize];
            codec.Decode(_htEncoded8, _info8, 0, decoded);
            return decoded;
        }

        [Benchmark(Description = "HT Decode 16-bit")]
        public byte[] HtDecode16Bit()
        {
            var codec = new Htj2kLosslessCodec();
            var decoded = new byte[_info16.FrameSize];
            codec.Decode(_htEncoded16, _info16, 0, decoded);
            return decoded;
        }

        // ---- EBCOT Encode benchmark (baseline) ----

        [Benchmark(Baseline = true, Description = "EBCOT Encode 8-bit")]
        public DicomFragmentSequence EbcotEncode8Bit()
        {
            var codec = new Jpeg2000LosslessCodec();
            return codec.Encode(_pixelData8, _info8);
        }

        // ---- EBCOT Decode benchmark (baseline) ----

        [Benchmark(Description = "EBCOT Decode 8-bit")]
        public byte[] EbcotDecode8Bit()
        {
            var codec = new Jpeg2000LosslessCodec();
            var decoded = new byte[_info8.FrameSize];
            codec.Decode(_ebcotEncoded8, _info8, 0, decoded);
            return decoded;
        }

        // ---- Lossy HT benchmarks ----

        [Benchmark(Description = "HT Lossy Fast 8-bit")]
        public DicomFragmentSequence HtLossyFast()
        {
            var codec = new Htj2kLossyCodec();
            var opts = new Htj2kCodecOptions(false, 5, false, true, HtEncoderOptions.Fast);
            return codec.Encode(_pixelData8, _info8, opts);
        }

        [Benchmark(Description = "HT Lossy Diagnostic 8-bit")]
        public DicomFragmentSequence HtLossyDiagnostic()
        {
            var codec = new Htj2kLossyCodec();
            var opts = new Htj2kCodecOptions(false, 5, false, true, HtEncoderOptions.Diagnostic);
            return codec.Encode(_pixelData8, _info8, opts);
        }

        // ---- Helpers ----

        private static byte[] CreateGradient8(int width, int height)
        {
            var data = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[y * width + x] = (byte)((x + y) % 256);
                }
            }
            return data;
        }

        private static byte[] CreateGradient16(int width, int height)
        {
            var data = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)((x + y) * 128 % 65536);
                    int offset = (y * width + x) * 2;
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)(value >> 8);
                }
            }
            return data;
        }
    }
}
