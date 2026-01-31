using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;
using SharpDicom.Data;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Tests for <see cref="CodecRegistry"/> priority-based registration.
    /// </summary>
    [TestFixture]
    public class CodecRegistryPriorityTests
    {
        [SetUp]
        public void Setup()
        {
            // Reset registry and NativeCodecs before each test
            CodecRegistry.Reset();
            NativeCodecs.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CodecRegistry.Reset();
            NativeCodecs.Reset();
        }

        #region Priority Constants Tests

        [Test]
        public void PriorityDefault_Is50()
        {
            Assert.That(CodecRegistry.PriorityDefault, Is.EqualTo(50));
        }

        [Test]
        public void PriorityNative_Is100()
        {
            Assert.That(CodecRegistry.PriorityNative, Is.EqualTo(100));
        }

        [Test]
        public void PriorityUserOverride_Is200()
        {
            Assert.That(CodecRegistry.PriorityUserOverride, Is.EqualTo(200));
        }

        [Test]
        public void PriorityNative_IsHigherThanDefault()
        {
            Assert.That(CodecRegistry.PriorityNative, Is.GreaterThan(CodecRegistry.PriorityDefault));
        }

        [Test]
        public void PriorityUserOverride_IsHigherThanNative()
        {
            Assert.That(CodecRegistry.PriorityUserOverride, Is.GreaterThan(CodecRegistry.PriorityNative));
        }

        #endregion

        #region Priority Registration Tests

        [Test]
        public void Register_DefaultPriority_Uses50()
        {
            var codec = new TestCodec(TransferSyntax.JPEGBaseline);
            CodecRegistry.Register(codec);

            var priority = CodecRegistry.GetPriority(TransferSyntax.JPEGBaseline);
            Assert.That(priority, Is.EqualTo(50));
        }

        [Test]
        public void Register_HigherPriority_OverridesLowerPriority()
        {
            var lowPriorityCodec = new TestCodec(TransferSyntax.JPEGBaseline, "LowPriority");
            var highPriorityCodec = new TestCodec(TransferSyntax.JPEGBaseline, "HighPriority");

            // Register low priority first
            CodecRegistry.Register(lowPriorityCodec, 50);

            // Then register higher priority
            CodecRegistry.Register(highPriorityCodec, 100);

            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("HighPriority"));
        }

        [Test]
        public void Register_LowerPriority_DoesNotOverride()
        {
            var highPriorityCodec = new TestCodec(TransferSyntax.JPEGBaseline, "HighPriority");
            var lowPriorityCodec = new TestCodec(TransferSyntax.JPEGBaseline, "LowPriority");

            // Register high priority first
            CodecRegistry.Register(highPriorityCodec, 100);

            // Then try to register lower priority
            CodecRegistry.Register(lowPriorityCodec, 50);

            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("HighPriority"));
        }

        [Test]
        public void Register_EqualPriority_DoesNotOverride()
        {
            var firstCodec = new TestCodec(TransferSyntax.JPEGBaseline, "First");
            var secondCodec = new TestCodec(TransferSyntax.JPEGBaseline, "Second");

            // Register first codec
            CodecRegistry.Register(firstCodec, 100);

            // Try to register with equal priority
            CodecRegistry.Register(secondCodec, 100);

            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("First"));
        }

        [Test]
        public void Register_UserOverride_TakesPrecedenceOverNative()
        {
            var nativeCodec = new TestCodec(TransferSyntax.JPEGBaseline, "Native");
            var userCodec = new TestCodec(TransferSyntax.JPEGBaseline, "UserOverride");

            // Native codec
            CodecRegistry.Register(nativeCodec, CodecRegistry.PriorityNative);

            // User override
            CodecRegistry.Register(userCodec, CodecRegistry.PriorityUserOverride);

            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("UserOverride"));
        }

        #endregion

        #region GetCodecInfo Tests

        [Test]
        public void GetCodecInfo_ReturnsCorrectPriority()
        {
            var codec = new TestCodec(TransferSyntax.JPEG2000Lossless, "TestJ2K");
            CodecRegistry.Register(codec, 75);

            var info = CodecRegistry.GetCodecInfo(TransferSyntax.JPEG2000Lossless);

            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Name, Is.EqualTo("TestJ2K"));
            Assert.That(info!.Value.Priority, Is.EqualTo(75));
        }

        [Test]
        public void GetCodecInfo_ReturnsAssemblyName()
        {
            var codec = new TestCodec(TransferSyntax.JPEGLSLossless, "TestJLS");
            CodecRegistry.Register(codec, 50);

            var info = CodecRegistry.GetCodecInfo(TransferSyntax.JPEGLSLossless);

            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Assembly, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetCodecInfo_UnregisteredTransferSyntax_ReturnsNull()
        {
            var info = CodecRegistry.GetCodecInfo(TransferSyntax.RLELossless);
            Assert.That(info, Is.Null);
        }

        [Test]
        public void GetPriority_UnregisteredTransferSyntax_ReturnsNull()
        {
            var priority = CodecRegistry.GetPriority(TransferSyntax.RLELossless);
            Assert.That(priority, Is.Null);
        }

        [Test]
        public void GetPriority_RegisteredTransferSyntax_ReturnsCorrectValue()
        {
            var codec = new TestCodec(TransferSyntax.JPEGBaseline, "Test");
            CodecRegistry.Register(codec, 123);

            var priority = CodecRegistry.GetPriority(TransferSyntax.JPEGBaseline);
            Assert.That(priority, Is.EqualTo(123));
        }

        #endregion

        #region Priority Update Tests

        [Test]
        public void Register_AfterFreeze_InvalidatesCache()
        {
            var firstCodec = new TestCodec(TransferSyntax.JPEGBaseline, "First");
            CodecRegistry.Register(firstCodec, 50);

            // Force freeze by doing a lookup
            _ = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(CodecRegistry.IsFrozen, Is.True);

            // Register with higher priority - should invalidate freeze
            var secondCodec = new TestCodec(TransferSyntax.JPEGBaseline, "Second");
            CodecRegistry.Register(secondCodec, 100);

            // Should get the new codec
            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("Second"));
        }

        [Test]
        public void Register_MultipleTransferSyntaxes_IndependentPriorities()
        {
            var jpegCodec = new TestCodec(TransferSyntax.JPEGBaseline, "JPEG");
            var j2kCodec = new TestCodec(TransferSyntax.JPEG2000Lossless, "J2K");
            var jlsCodec = new TestCodec(TransferSyntax.JPEGLSLossless, "JLS");

            CodecRegistry.Register(jpegCodec, 50);
            CodecRegistry.Register(j2kCodec, 100);
            CodecRegistry.Register(jlsCodec, 75);

            Assert.That(CodecRegistry.GetPriority(TransferSyntax.JPEGBaseline), Is.EqualTo(50));
            Assert.That(CodecRegistry.GetPriority(TransferSyntax.JPEG2000Lossless), Is.EqualTo(100));
            Assert.That(CodecRegistry.GetPriority(TransferSyntax.JPEGLSLossless), Is.EqualTo(75));
        }

        #endregion

        #region Native Codec Priority Tests

        [Test]
        [Category("NativeCodecs")]
        public void NativeCodecs_WhenAvailable_RegisterWithPriority100()
        {
            // Initialize NativeCodecs (suppress errors if native libs not present)
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            if (!NativeCodecs.IsAvailable)
            {
                Assert.Ignore("Native codecs not available - skipping priority test");
            }

            // If native JPEG is available, check priority
            if (NativeCodecs.HasFeature(NativeCodecFeature.Jpeg))
            {
                var priority = CodecRegistry.GetPriority(TransferSyntax.JPEGBaseline);
                Assert.That(priority, Is.EqualTo(CodecRegistry.PriorityNative));
            }
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpegCodec_OverridesPureCSharp_WhenAvailable()
        {
            // Register a pure C# codec first
            var pureCSharpCodec = new TestCodec(TransferSyntax.JPEGBaseline, "Pure C#");
            CodecRegistry.Register(pureCSharpCodec, CodecRegistry.PriorityDefault);

            // Initialize NativeCodecs
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            if (!NativeCodecs.IsAvailable || !NativeCodecs.HasFeature(NativeCodecFeature.Jpeg))
            {
                Assert.Ignore("Native JPEG codec not available - skipping override test");
            }

            // Native codec should have replaced the pure C# one
            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Does.Contain("Native").Or.Contain("libjpeg"));
        }

        [Test]
        [Category("NativeCodecs")]
        public void UserOverride_TakesPrecedence_EvenOverNative()
        {
            // Initialize NativeCodecs
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            // Register user override
            var userCodec = new TestCodec(TransferSyntax.JPEGBaseline, "UserOverride");
            CodecRegistry.Register(userCodec, CodecRegistry.PriorityUserOverride);

            // User override should win
            var registered = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(registered?.Name, Is.EqualTo("UserOverride"));
        }

        #endregion

        #region CanDecode/CanEncode Tests

        [Test]
        public void CanDecode_WhenCodecRegistered_ReturnsTrue()
        {
            var codec = new TestCodec(TransferSyntax.JPEGBaseline, "Test", canDecode: true);
            CodecRegistry.Register(codec);

            Assert.That(CodecRegistry.CanDecode(TransferSyntax.JPEGBaseline), Is.True);
        }

        [Test]
        public void CanDecode_WhenNotRegistered_ReturnsFalse()
        {
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.RLELossless), Is.False);
        }

        [Test]
        public void CanEncode_WhenCodecRegistered_ReturnsTrue()
        {
            var codec = new TestCodec(TransferSyntax.JPEGBaseline, "Test", canEncode: true);
            CodecRegistry.Register(codec);

            Assert.That(CodecRegistry.CanEncode(TransferSyntax.JPEGBaseline), Is.True);
        }

        [Test]
        public void CanEncode_WhenNotRegistered_ReturnsFalse()
        {
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.RLELossless), Is.False);
        }

        #endregion

        #region GetRegisteredTransferSyntaxes Tests

        [Test]
        public void GetRegisteredTransferSyntaxes_ReturnsAllRegistered()
        {
            var codec1 = new TestCodec(TransferSyntax.JPEGBaseline, "JPEG");
            var codec2 = new TestCodec(TransferSyntax.JPEG2000Lossless, "J2K");

            CodecRegistry.Register(codec1);
            CodecRegistry.Register(codec2);

            var registered = CodecRegistry.GetRegisteredTransferSyntaxes();

            Assert.That(registered, Contains.Item(TransferSyntax.JPEGBaseline));
            Assert.That(registered, Contains.Item(TransferSyntax.JPEG2000Lossless));
        }

        [Test]
        public void GetRegisteredTransferSyntaxes_EmptyWhenNoneRegistered()
        {
            var registered = CodecRegistry.GetRegisteredTransferSyntaxes();
            Assert.That(registered, Is.Empty);
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Simple test codec for verifying registry behavior.
        /// </summary>
        private sealed class TestCodec : IPixelDataCodec
        {
            private static readonly int[] DefaultBitDepths = new[] { 8, 16 };
            private static readonly int[] DefaultSamplesPerPixel = new[] { 1, 3 };

            private readonly TransferSyntax _transferSyntax;
            private readonly bool _canDecode;
            private readonly bool _canEncode;

            public TestCodec(TransferSyntax transferSyntax, string name = "TestCodec",
                bool canDecode = true, bool canEncode = true)
            {
                _transferSyntax = transferSyntax;
                Name = name;
                _canDecode = canDecode;
                _canEncode = canEncode;
            }

            public TransferSyntax TransferSyntax => _transferSyntax;
            public string Name { get; }
            public CodecCapabilities Capabilities => new CodecCapabilities(
                CanEncode: _canEncode,
                CanDecode: _canDecode,
                IsLossy: true,
                SupportsMultiFrame: false,
                SupportsParallelEncode: false,
                SupportedBitDepths: DefaultBitDepths,
                SupportedSamplesPerPixel: DefaultSamplesPerPixel);

            public DecodeResult Decode(DicomFragmentSequence fragments, PixelDataInfo info,
                int frameIndex, Memory<byte> destination)
            {
                throw new NotImplementedException("Test codec");
            }

            public System.Threading.Tasks.ValueTask<DecodeResult> DecodeAsync(
                DicomFragmentSequence fragments, PixelDataInfo info,
                int frameIndex, Memory<byte> destination,
                System.Threading.CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException("Test codec");
            }

            public DicomFragmentSequence Encode(ReadOnlySpan<byte> pixelData, PixelDataInfo info,
                object? options = null)
            {
                throw new NotImplementedException("Test codec");
            }

            public System.Threading.Tasks.ValueTask<DicomFragmentSequence> EncodeAsync(
                ReadOnlyMemory<byte> pixelData, PixelDataInfo info,
                object? options = null,
                System.Threading.CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException("Test codec");
            }

            public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
            {
                return ValidationResult.Valid();
            }
        }

        #endregion
    }
}
