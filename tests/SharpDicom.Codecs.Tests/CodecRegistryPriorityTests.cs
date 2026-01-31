using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;
using SharpDicom.Data;

// Alias to disambiguate from SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Static arrays for mock codecs to avoid CA1861 warnings.
    /// </summary>
    internal static class NativeTestMockArrays
    {
        public static readonly int[] StandardBitDepths = new[] { 8, 16 };
        public static readonly int[] StandardSamplesPerPixel = new[] { 1, 3 };
    }

    /// <summary>
    /// Tests for CodecRegistry priority-based registration with native codecs.
    /// </summary>
    [TestFixture]
    public class CodecRegistryPriorityTests
    {
        [SetUp]
        public void Setup()
        {
            // Reset codec registry to clean state before each test
            CodecRegistry.Reset();
        }

        [Test]
        public void Register_HigherPriority_OverridesLowerPriority()
        {
            var lowPriority = new PriorityTestCodec("Low");
            var highPriority = new PriorityTestCodec("High");

            CodecRegistry.Register(lowPriority, 50);
            CodecRegistry.Register(highPriority, 100);

            var result = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(result, Is.SameAs(highPriority));
        }

        [Test]
        public void Register_LowerPriority_DoesNotOverride()
        {
            var highPriority = new PriorityTestCodec("High");
            var lowPriority = new PriorityTestCodec("Low");

            CodecRegistry.Register(highPriority, 100);
            CodecRegistry.Register(lowPriority, 50);

            var result = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(result, Is.SameAs(highPriority));
        }

        [Test]
        public void Register_DefaultPriority_Is50()
        {
            var defaultCodec = new PriorityTestCodec("Default");
            var lowPriority = new PriorityTestCodec("Low");

            CodecRegistry.Register(defaultCodec);  // Default = 50
            CodecRegistry.Register(lowPriority, 40);

            var result = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(result, Is.SameAs(defaultCodec));

            // Verify default priority via constant
            Assert.That(CodecRegistry.DefaultPriority, Is.EqualTo(50));
        }

        [Test]
        public void Register_SamePriority_LastWins()
        {
            var first = new PriorityTestCodec("First");
            var second = new PriorityTestCodec("Second");

            CodecRegistry.Register(first, 75);
            CodecRegistry.Register(second, 75);

            var result = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(result, Is.SameAs(second));
        }

        [Test]
        public void GetCodecInfo_ReturnsCorrectPriority()
        {
            var codec = new PriorityTestCodec("Test");
            CodecRegistry.Register(codec, 75);

            var info = CodecRegistry.GetCodecInfo(TransferSyntax.JPEGBaseline);

            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Priority, Is.EqualTo(75));
            Assert.That(info.Value.Codec.Name, Is.EqualTo("Test"));
        }

        [Test]
        public void GetCodecInfo_ReturnsNull_WhenNotRegistered()
        {
            var info = CodecRegistry.GetCodecInfo(TransferSyntax.JPEG2000Lossless);
            Assert.That(info, Is.Null);
        }

        [Test]
        public void GetPriority_ReturnsCorrectValue()
        {
            var codec = new PriorityTestCodec("Test");
            CodecRegistry.Register(codec, 42);

            var priority = CodecRegistry.GetPriority(TransferSyntax.JPEGBaseline);

            Assert.That(priority, Is.EqualTo(42));
        }

        [Test]
        public void GetPriority_ReturnsNull_WhenNotRegistered()
        {
            var priority = CodecRegistry.GetPriority(TransferSyntax.JPEG2000Lossy);
            Assert.That(priority, Is.Null);
        }

        [Test]
        public void NativePriority_Is100()
        {
            Assert.That(CodecRegistry.NativePriority, Is.EqualTo(100));
        }

        [Test]
        [Category("Native")]
        public void NativeCodecs_RegisterWithPriority100()
        {
            // Attempt to initialize native codecs
            try
            {
                NativeCodecs.Initialize();
            }
            catch (NativeCodecException)
            {
                Assert.Ignore("Native library not available");
            }

            if (!NativeCodecs.IsAvailable)
            {
                Assert.Ignore("Native library not available");
            }

            // If JPEG is available and enabled, check its priority
            if (NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.Jpeg) &&
                NativeCodecs.EnableJpeg)
            {
                var info = CodecRegistry.GetCodecInfo(TransferSyntax.JPEGBaseline);

                Assert.That(info, Is.Not.Null);
                Assert.That(info!.Value.Priority, Is.EqualTo(100));
                Assert.That(info.Value.Codec.Name, Does.Contain("Native"));
            }
            else
            {
                Assert.Ignore("Native JPEG codec not available");
            }
        }

        [Test]
        [Category("Native")]
        public void NativeCodecs_OverrideManagedCodecs()
        {
            // First register a managed codec at default priority
            var managedCodec = new PriorityTestCodec("Managed");
            CodecRegistry.Register(managedCodec, CodecRegistry.DefaultPriority);

            // Verify managed codec is registered
            var beforeNative = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(beforeNative, Is.SameAs(managedCodec));

            // Now initialize native codecs (which register at priority 100)
            try
            {
                NativeCodecs.Initialize();
            }
            catch (NativeCodecException)
            {
                Assert.Ignore("Native library not available");
            }

            if (!NativeCodecs.IsAvailable ||
                !NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.Jpeg) ||
                !NativeCodecs.EnableJpeg)
            {
                Assert.Ignore("Native JPEG codec not available");
            }

            // Native codec should now be returned (priority 100 > 50)
            var afterNative = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(afterNative, Is.Not.SameAs(managedCodec));
            Assert.That(afterNative!.Name, Does.Contain("Native"));
        }

        [Test]
        public void ManagedCodec_CanOverrideNative_WithHigherPriority()
        {
            // Initialize native codecs first
            try
            {
                NativeCodecs.Initialize();
            }
            catch (NativeCodecException)
            {
                // Native not available - skip test
                Assert.Ignore("Native library not available");
            }

            // Register managed codec at priority 150 (higher than native's 100)
            var managedCodec = new PriorityTestCodec("Managed High Priority");
            CodecRegistry.Register(managedCodec, 150);

            // Managed codec should be returned due to higher priority
            var result = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(result, Is.SameAs(managedCodec));
        }

        /// <summary>
        /// Mock codec implementation for priority testing.
        /// </summary>
        private sealed class PriorityTestCodec : IPixelDataCodec
        {
            public TransferSyntax TransferSyntax => TransferSyntax.JPEGBaseline;
            public string Name { get; }

            public CodecCapabilities Capabilities => new(
                CanEncode: true,
                CanDecode: true,
                IsLossy: true,
                SupportsMultiFrame: true,
                SupportsParallelEncode: true,
                SupportedBitDepths: NativeTestMockArrays.StandardBitDepths,
                SupportedSamplesPerPixel: NativeTestMockArrays.StandardSamplesPerPixel);

            public PriorityTestCodec(string name) => Name = name;

            public DecodeResult Decode(DicomFragmentSequence fragments, PixelDataInfo info,
                int frameIndex, Memory<byte> destination) => DecodeResult.Ok(0);

            public ValueTask<DecodeResult> DecodeAsync(DicomFragmentSequence fragments,
                PixelDataInfo info, int frameIndex, Memory<byte> destination,
                CancellationToken cancellationToken = default)
                => new(DecodeResult.Ok(0));

            public DicomFragmentSequence Encode(ReadOnlySpan<byte> pixelData,
                PixelDataInfo info, object? options = null)
                => throw new NotImplementedException();

            public ValueTask<DicomFragmentSequence> EncodeAsync(ReadOnlyMemory<byte> pixelData,
                PixelDataInfo info, object? options = null, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments,
                PixelDataInfo info) => ValidationResult.Valid();
        }
    }
}
