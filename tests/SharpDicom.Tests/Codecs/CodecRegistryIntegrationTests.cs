using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.JpegLossless;
using SharpDicom.Codecs.Rle;
using SharpDicom.Data;

namespace SharpDicom.Tests.Codecs
{
    [TestFixture]
    public class CodecRegistryIntegrationTests
    {
        [SetUp]
        public void Setup()
        {
            CodecInitializer.Reset();
        }

        #region Registration Tests

        [Test]
        public void RegisterAll_RegistersAllBuiltInCodecs()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.Not.Null);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline), Is.Not.Null);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGLossless), Is.Not.Null);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossless), Is.Not.Null);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossy), Is.Not.Null);
        }

        [Test]
        public void RegisterAll_IsIdempotent()
        {
            CodecInitializer.RegisterAll();
            CodecInitializer.RegisterAll();
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.Not.Null);
        }

        [Test]
        public void IsInitialized_ReturnsFalseBeforeRegisterAll()
        {
            Assert.That(CodecInitializer.IsInitialized, Is.False);
        }

        [Test]
        public void IsInitialized_ReturnsTrueAfterRegisterAll()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecInitializer.IsInitialized, Is.True);
        }

        #endregion

        #region GetCodec Type Tests

        [Test]
        public void GetCodec_RLELossless_ReturnsRleCodec()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.InstanceOf<RleCodec>());
        }

        [Test]
        public void GetCodec_JPEGBaseline_ReturnsJpegBaselineCodec()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline), Is.InstanceOf<JpegBaselineCodec>());
        }

        [Test]
        public void GetCodec_JPEGLossless_ReturnsJpegLosslessCodec()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGLossless), Is.InstanceOf<JpegLosslessCodec>());
        }

        [Test]
        public void GetCodec_JPEG2000Lossless_ReturnsJpeg2000LosslessCodec()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossless), Is.InstanceOf<Jpeg2000LosslessCodec>());
        }

        [Test]
        public void GetCodec_JPEG2000Lossy_ReturnsJpeg2000LossyCodec()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossy), Is.InstanceOf<Jpeg2000LossyCodec>());
        }

        #endregion

        #region CanDecode Tests

        [Test]
        public void CanDecode_ReturnsTrueForRegisteredCodecs()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.CanDecode(TransferSyntax.RLELossless), Is.True);
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.JPEGBaseline), Is.True);
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.JPEGLossless), Is.True);
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.JPEG2000Lossless), Is.True);
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.JPEG2000Lossy), Is.True);
        }

        [Test]
        public void CanDecode_ReturnsFalseForUnregisteredCodecs()
        {
            CodecInitializer.RegisterAll();

            // Explicit VR Little Endian is not an encapsulated format
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.ExplicitVRLittleEndian), Is.False);
        }

        #endregion

        #region CanEncode Tests

        [Test]
        public void CanEncode_ReturnsTrueForRegisteredCodecs()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.CanEncode(TransferSyntax.RLELossless), Is.True);
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.JPEGBaseline), Is.True);
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.JPEGLossless), Is.True);
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.JPEG2000Lossless), Is.True);
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.JPEG2000Lossy), Is.True);
        }

        [Test]
        public void CanEncode_ReturnsFalseForUnregisteredCodecs()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.CanEncode(TransferSyntax.ExplicitVRLittleEndian), Is.False);
        }

        #endregion

        #region GetRegisteredTransferSyntaxes Tests

        [Test]
        public void GetRegisteredTransferSyntaxes_IncludesAllCodecs()
        {
            CodecInitializer.RegisterAll();

            var syntaxes = CodecRegistry.GetRegisteredTransferSyntaxes();

            Assert.That(syntaxes, Contains.Item(TransferSyntax.RLELossless));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEGBaseline));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEGLossless));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEG2000Lossless));
            Assert.That(syntaxes, Contains.Item(TransferSyntax.JPEG2000Lossy));
        }

        [Test]
        public void GetRegisteredTransferSyntaxes_HasExpectedCount()
        {
            CodecInitializer.RegisterAll();

            var syntaxes = CodecRegistry.GetRegisteredTransferSyntaxes();

            Assert.That(syntaxes.Count, Is.EqualTo(5));
        }

        #endregion

        #region Codec Capabilities Tests

        [Test]
        public void LosslessCodecs_HaveIsLossyFalse()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless)!.Capabilities.IsLossy, Is.False);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGLossless)!.Capabilities.IsLossy, Is.False);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossless)!.Capabilities.IsLossy, Is.False);
        }

        [Test]
        public void LossyCodecs_HaveIsLossyTrue()
        {
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEGBaseline)!.Capabilities.IsLossy, Is.True);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.JPEG2000Lossy)!.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void AllCodecs_SupportMultiFrame()
        {
            CodecInitializer.RegisterAll();

            var syntaxes = CodecRegistry.GetRegisteredTransferSyntaxes();
            foreach (var syntax in syntaxes)
            {
                var codec = CodecRegistry.GetCodec(syntax);
                Assert.That(codec!.Capabilities.SupportsMultiFrame, Is.True,
                    $"Codec for {syntax.UID} should support multi-frame");
            }
        }

        #endregion

        #region Reset Tests

        [Test]
        public void Reset_ClearsAllRegistrations()
        {
            CodecInitializer.RegisterAll();
            Assert.That(CodecInitializer.IsInitialized, Is.True);

            CodecInitializer.Reset();

            Assert.That(CodecInitializer.IsInitialized, Is.False);
            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.Null);
        }

        [Test]
        public void Reset_AllowsReRegistration()
        {
            CodecInitializer.RegisterAll();
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            Assert.That(CodecRegistry.GetCodec(TransferSyntax.RLELossless), Is.Not.Null);
        }

        #endregion
    }
}
