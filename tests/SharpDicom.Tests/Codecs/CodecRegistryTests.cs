using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Data;

// Alias to disambiguate from SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs
{
    /// <summary>
    /// Static arrays for mock codecs to avoid CA1861 warnings.
    /// </summary>
    internal static class MockCodecArrays
    {
        public static readonly int[] StandardBitDepths = new[] { 8, 16 };
        public static readonly int[] StandardSamplesPerPixel = new[] { 1, 3 };
        public static readonly int[] LimitedBitDepths = new[] { 8 };
    }

    /// <summary>
    /// Mock codec implementation for testing CodecRegistry.
    /// </summary>
    internal sealed class MockCodec : IPixelDataCodec
    {
        public TransferSyntax TransferSyntax { get; }
        public string Name { get; }
        public CodecCapabilities Capabilities { get; }

        public MockCodec(TransferSyntax transferSyntax, string name = "Mock Codec", bool canEncode = true, bool canDecode = true)
        {
            TransferSyntax = transferSyntax;
            Name = name;
            Capabilities = new CodecCapabilities(
                CanEncode: canEncode,
                CanDecode: canDecode,
                IsLossy: false,
                SupportsMultiFrame: true,
                SupportsParallelEncode: true,
                SupportedBitDepths: MockCodecArrays.StandardBitDepths,
                SupportedSamplesPerPixel: MockCodecArrays.StandardSamplesPerPixel);
        }

        /// <summary>
        /// Default constructor for generic registration.
        /// </summary>
        public MockCodec() : this(TransferSyntax.RLELossless)
        {
        }

        public DecodeResult Decode(DicomFragmentSequence fragments, PixelDataInfo info, int frameIndex, Memory<byte> destination)
            => DecodeResult.Ok(0);

        public ValueTask<DecodeResult> DecodeAsync(DicomFragmentSequence fragments, PixelDataInfo info, int frameIndex, Memory<byte> destination, CancellationToken cancellationToken = default)
            => new(DecodeResult.Ok(0));

        public DicomFragmentSequence Encode(ReadOnlySpan<byte> pixelData, PixelDataInfo info, object? options = null)
            => throw new NotImplementedException("Mock codec encode not implemented");

        public ValueTask<DicomFragmentSequence> EncodeAsync(ReadOnlyMemory<byte> pixelData, PixelDataInfo info, object? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Mock codec encode async not implemented");

        public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
            => ValidationResult.Valid();
    }

    /// <summary>
    /// Decode-only mock codec for testing capabilities.
    /// </summary>
    internal sealed class DecodeOnlyMockCodec : IPixelDataCodec
    {
        public TransferSyntax TransferSyntax => TransferSyntax.JPEGBaseline;
        public string Name => "Decode Only Mock";
        public CodecCapabilities Capabilities => new(
            CanEncode: false,
            CanDecode: true,
            IsLossy: true,
            SupportsMultiFrame: true,
            SupportsParallelEncode: false,
            SupportedBitDepths: MockCodecArrays.LimitedBitDepths,
            SupportedSamplesPerPixel: MockCodecArrays.StandardSamplesPerPixel);

        public DecodeResult Decode(DicomFragmentSequence fragments, PixelDataInfo info, int frameIndex, Memory<byte> destination)
            => DecodeResult.Ok(0);

        public ValueTask<DecodeResult> DecodeAsync(DicomFragmentSequence fragments, PixelDataInfo info, int frameIndex, Memory<byte> destination, CancellationToken cancellationToken = default)
            => new(DecodeResult.Ok(0));

        public DicomFragmentSequence Encode(ReadOnlySpan<byte> pixelData, PixelDataInfo info, object? options = null)
            => throw new InvalidOperationException("Encode not supported");

        public ValueTask<DicomFragmentSequence> EncodeAsync(ReadOnlyMemory<byte> pixelData, PixelDataInfo info, object? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("EncodeAsync not supported");

        public ValidationResult ValidateCompressedData(DicomFragmentSequence fragments, PixelDataInfo info)
            => ValidationResult.Valid();
    }

    [TestFixture]
    public class CodecRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            // Reset registry to clean state before each test
            CodecRegistry.Reset();
        }

        [Test]
        public void Register_AddsCodecToRegistry()
        {
            // Arrange
            var codec = new MockCodec(TransferSyntax.RLELossless);

            // Act
            CodecRegistry.Register(codec);
            var retrieved = CodecRegistry.GetCodec(TransferSyntax.RLELossless);

            // Assert
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved, Is.SameAs(codec));
        }

        [Test]
        public void GetCodec_ReturnsNull_WhenNotRegistered()
        {
            // Act
            var codec = CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossless);

            // Assert
            Assert.That(codec, Is.Null);
        }

        [Test]
        public void CanDecode_ReturnsTrue_WhenCodecRegistered()
        {
            // Arrange
            var codec = new MockCodec(TransferSyntax.RLELossless, canDecode: true);
            CodecRegistry.Register(codec);

            // Act
            var result = CodecRegistry.CanDecode(TransferSyntax.RLELossless);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void CanDecode_ReturnsFalse_WhenNotRegistered()
        {
            // Act
            var result = CodecRegistry.CanDecode(TransferSyntax.JPEG2000Lossless);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void CanEncode_ReturnsTrue_WhenCodecCanEncode()
        {
            // Arrange
            var codec = new MockCodec(TransferSyntax.RLELossless, canEncode: true);
            CodecRegistry.Register(codec);

            // Act
            var result = CodecRegistry.CanEncode(TransferSyntax.RLELossless);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void CanEncode_ReturnsFalse_WhenCodecCannotEncode()
        {
            // Arrange
            var codec = new DecodeOnlyMockCodec();
            CodecRegistry.Register(codec);

            // Act
            var result = CodecRegistry.CanEncode(TransferSyntax.JPEGBaseline);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Register_Generic_CreatesInstanceAndRegisters()
        {
            // Act
            CodecRegistry.Register<MockCodec>();
            var codec = CodecRegistry.GetCodec(TransferSyntax.RLELossless);

            // Assert
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.TypeOf<MockCodec>());
        }

        [Test]
        public void GetRegisteredTransferSyntaxes_ReturnsAllRegistered()
        {
            // Arrange
            CodecRegistry.Register(new MockCodec(TransferSyntax.RLELossless));
            CodecRegistry.Register(new MockCodec(TransferSyntax.JPEGBaseline));
            CodecRegistry.Register(new MockCodec(TransferSyntax.JPEG2000Lossless));

            // Act
            var syntaxes = CodecRegistry.GetRegisteredTransferSyntaxes();

            // Assert
            Assert.That(syntaxes, Has.Count.EqualTo(3));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.RLELossless));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEGBaseline));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEG2000Lossless));
        }

        [Test]
        public void Freeze_FreezesRegistry()
        {
            // Arrange
            CodecRegistry.Register(new MockCodec(TransferSyntax.RLELossless));

            // Act
            CodecRegistry.Freeze();

            // Assert
            Assert.That(CodecRegistry.IsFrozen, Is.True);
        }

        [Test]
        public void Register_AfterFreeze_InvalidatesFrozenCache()
        {
            // Arrange
            CodecRegistry.Register(new MockCodec(TransferSyntax.RLELossless));
            CodecRegistry.Freeze();
            Assert.That(CodecRegistry.IsFrozen, Is.True);

            // Act - register after freeze
            CodecRegistry.Register(new MockCodec(TransferSyntax.JPEGBaseline));

            // Assert - registry is unfrozen
            Assert.That(CodecRegistry.IsFrozen, Is.False);

            // Lookup re-freezes and finds new codec
            var codec = CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline);
            Assert.That(codec, Is.Not.Null);
            Assert.That(CodecRegistry.IsFrozen, Is.True);
        }

        [Test]
        public void Reset_ClearsAllRegistrations()
        {
            // Arrange
            CodecRegistry.Register(new MockCodec(TransferSyntax.RLELossless));
            CodecRegistry.Register(new MockCodec(TransferSyntax.JPEGBaseline));
            CodecRegistry.Freeze();

            // Act
            CodecRegistry.Reset();

            // Assert
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.Null);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline), Is.Null);
            Assert.That(CodecRegistry.GetRegisteredTransferSyntaxes(), Is.Empty);
        }

        [Test]
        public void Register_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => CodecRegistry.Register(null!));
        }

        [Test]
        public void Register_ReplacesExistingCodec_WhenHigherPriority()
        {
            // Arrange
            var codec1 = new MockCodec(TransferSyntax.RLELossless, "Codec 1");
            var codec2 = new MockCodec(TransferSyntax.RLELossless, "Codec 2");

            // Act - second registration at higher priority replaces first
            CodecRegistry.Register(codec1, CodecRegistry.PriorityDefault);
            CodecRegistry.Register(codec2, CodecRegistry.PriorityNative);
            var retrieved = CodecRegistry.GetCodec(TransferSyntax.RLELossless);

            // Assert - higher priority codec wins
            Assert.That(retrieved, Is.SameAs(codec2));
            Assert.That(retrieved!.Name, Is.EqualTo("Codec 2"));
        }

        [Test]
        public void Register_KeepsExistingCodec_WhenEqualOrLowerPriority()
        {
            // Arrange
            var codec1 = new MockCodec(TransferSyntax.RLELossless, "Codec 1");
            var codec2 = new MockCodec(TransferSyntax.RLELossless, "Codec 2");

            // Act - second registration at same priority does NOT replace
            CodecRegistry.Register(codec1, CodecRegistry.PriorityDefault);
            CodecRegistry.Register(codec2, CodecRegistry.PriorityDefault);
            var retrieved = CodecRegistry.GetCodec(TransferSyntax.RLELossless);

            // Assert - first codec retained (equal priority doesn't replace)
            Assert.That(retrieved, Is.SameAs(codec1));
            Assert.That(retrieved!.Name, Is.EqualTo("Codec 1"));
        }
    }
}
