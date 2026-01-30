using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network.Items;
using System;

namespace SharpDicom.Tests.Network.Items
{
    [TestFixture]
    public class PresentationContextTests
    {
        // A sample abstract syntax (CT Image Storage)
        private static readonly DicomUID TestAbstractSyntax = new("1.2.840.10008.5.1.4.1.1.2");

        #region ID Validation Tests

        [TestCase((byte)1)]
        [TestCase((byte)3)]
        [TestCase((byte)127)]
        [TestCase((byte)255)]
        public void Constructor_ValidOddId_Succeeds(byte id)
        {
            var pc = new PresentationContext(id, TestAbstractSyntax, TransferSyntax.ExplicitVRLittleEndian);

            Assert.That(pc.Id, Is.EqualTo(id));
            Assert.That(pc.AbstractSyntax, Is.EqualTo(TestAbstractSyntax));
            Assert.That(pc.TransferSyntaxes, Has.Count.EqualTo(1));
            Assert.That(pc.Result, Is.Null);
            Assert.That(pc.AcceptedTransferSyntax, Is.Null);
        }

        [TestCase((byte)0)]
        [TestCase((byte)2)]
        [TestCase((byte)4)]
        [TestCase((byte)128)]
        [TestCase((byte)254)]
        public void Constructor_InvalidEvenOrZeroId_ThrowsArgumentOutOfRangeException(byte id)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PresentationContext(id, TestAbstractSyntax, TransferSyntax.ExplicitVRLittleEndian));
        }

        [Test]
        public void IsValidId_OddNumbersInRange_ReturnsTrue()
        {
            Assert.That(PresentationContext.IsValidId(1), Is.True);
            Assert.That(PresentationContext.IsValidId(3), Is.True);
            Assert.That(PresentationContext.IsValidId(127), Is.True);
            Assert.That(PresentationContext.IsValidId(255), Is.True);
        }

        [Test]
        public void IsValidId_EvenOrZero_ReturnsFalse()
        {
            Assert.That(PresentationContext.IsValidId(0), Is.False);
            Assert.That(PresentationContext.IsValidId(2), Is.False);
            Assert.That(PresentationContext.IsValidId(4), Is.False);
            Assert.That(PresentationContext.IsValidId(128), Is.False);
            Assert.That(PresentationContext.IsValidId(254), Is.False);
        }

        #endregion

        #region Abstract Syntax Validation Tests

        [Test]
        public void Constructor_EmptyAbstractSyntax_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationContext(1, new DicomUID(""), TransferSyntax.ExplicitVRLittleEndian));
        }

        #endregion

        #region Transfer Syntax Validation Tests

        [Test]
        public void Constructor_EmptyTransferSyntaxes_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationContext(1, TestAbstractSyntax, Array.Empty<TransferSyntax>()));
        }

        [Test]
        public void Constructor_NullTransferSyntaxes_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationContext(1, TestAbstractSyntax, null!));
        }

        [Test]
        public void Constructor_MultipleTransferSyntaxes_StoresAll()
        {
            var pc = new PresentationContext(
                1,
                TestAbstractSyntax,
                TransferSyntax.ExplicitVRLittleEndian,
                TransferSyntax.ImplicitVRLittleEndian,
                TransferSyntax.ExplicitVRBigEndian);

            Assert.That(pc.TransferSyntaxes, Has.Count.EqualTo(3));
        }

        #endregion

        #region CreateAccepted Tests

        [Test]
        public void CreateAccepted_ValidParameters_SetsResultAndAcceptedTransferSyntax()
        {
            var pc = PresentationContext.CreateAccepted(
                1,
                TestAbstractSyntax,
                TransferSyntax.ExplicitVRLittleEndian);

            Assert.That(pc.Id, Is.EqualTo(1));
            Assert.That(pc.AbstractSyntax, Is.EqualTo(TestAbstractSyntax));
            Assert.That(pc.Result, Is.EqualTo(PresentationContextResult.Acceptance));
            Assert.That(pc.AcceptedTransferSyntax, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
            Assert.That(pc.TransferSyntaxes, Has.Count.EqualTo(1));
            Assert.That(pc.TransferSyntaxes[0], Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
        }

        [Test]
        public void CreateAccepted_InvalidId_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PresentationContext.CreateAccepted(0, TestAbstractSyntax, TransferSyntax.ExplicitVRLittleEndian));
        }

        #endregion

        #region CreateRejected Tests

        [TestCase(PresentationContextResult.UserRejection)]
        [TestCase(PresentationContextResult.NoReason)]
        [TestCase(PresentationContextResult.AbstractSyntaxNotSupported)]
        [TestCase(PresentationContextResult.TransferSyntaxesNotSupported)]
        public void CreateRejected_ValidReason_SetsResultAndNullAcceptedTransferSyntax(PresentationContextResult reason)
        {
            var pc = PresentationContext.CreateRejected(1, TestAbstractSyntax, reason);

            Assert.That(pc.Id, Is.EqualTo(1));
            Assert.That(pc.Result, Is.EqualTo(reason));
            Assert.That(pc.AcceptedTransferSyntax, Is.Null);
            Assert.That(pc.TransferSyntaxes, Is.Empty);
        }

        [Test]
        public void CreateRejected_AcceptanceReason_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                PresentationContext.CreateRejected(1, TestAbstractSyntax, PresentationContextResult.Acceptance));
        }

        [Test]
        public void CreateRejected_InvalidId_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PresentationContext.CreateRejected(2, TestAbstractSyntax, PresentationContextResult.UserRejection));
        }

        #endregion
    }
}
