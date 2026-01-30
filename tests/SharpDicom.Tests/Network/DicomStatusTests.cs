using NUnit.Framework;
using SharpDicom.Network;

namespace SharpDicom.Tests.Network
{
    [TestFixture]
    public class DicomStatusTests
    {
        #region Category Tests

        [Test]
        public void Success_HasSuccessCategory()
        {
            var status = DicomStatus.Success;

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Success));
            Assert.That(status.Code, Is.EqualTo((ushort)0x0000));
        }

        [Test]
        public void Cancel_HasCancelCategory()
        {
            var status = DicomStatus.Cancel;

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Cancel));
            Assert.That(status.Code, Is.EqualTo((ushort)0xFE00));
        }

        [Test]
        public void Pending_HasPendingCategory()
        {
            var status = DicomStatus.Pending;

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Pending));
            Assert.That(status.Code, Is.EqualTo((ushort)0xFF00));
        }

        [Test]
        public void PendingWarning_HasPendingCategory()
        {
            var status = DicomStatus.PendingWarning;

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Pending));
            Assert.That(status.Code, Is.EqualTo((ushort)0xFF01));
        }

        [TestCase((ushort)0xB000)]
        [TestCase((ushort)0xB007)]
        [TestCase((ushort)0xBFFF)]
        public void WarningRange_HasWarningCategory(ushort code)
        {
            var status = new DicomStatus(code);

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Warning));
        }

        [TestCase((ushort)0xA700)]
        [TestCase((ushort)0xA900)]
        [TestCase((ushort)0xAFFF)]
        public void FailureRangeA_HasFailureCategory(ushort code)
        {
            var status = new DicomStatus(code);

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Failure));
        }

        [TestCase((ushort)0xC000)]
        [TestCase((ushort)0xC001)]
        [TestCase((ushort)0xCFFF)]
        public void FailureRangeC_HasFailureCategory(ushort code)
        {
            var status = new DicomStatus(code);

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Failure));
        }

        [TestCase((ushort)0x0110)]
        [TestCase((ushort)0x0111)]
        [TestCase((ushort)0x0120)]
        public void LowFailureCodes_HasFailureCategory(ushort code)
        {
            var status = new DicomStatus(code);

            Assert.That(status.Category, Is.EqualTo(StatusCategory.Failure));
        }

        #endregion

        #region Boolean Property Tests

        [Test]
        public void IsSuccess_TrueForSuccessStatus()
        {
            var status = DicomStatus.Success;

            Assert.That(status.IsSuccess, Is.True);
            Assert.That(status.IsWarning, Is.False);
            Assert.That(status.IsFailure, Is.False);
            Assert.That(status.IsPending, Is.False);
            Assert.That(status.IsCancel, Is.False);
        }

        [Test]
        public void IsWarning_TrueForWarningStatus()
        {
            var status = DicomStatus.CoercionOfDataElements;

            Assert.That(status.IsSuccess, Is.False);
            Assert.That(status.IsWarning, Is.True);
            Assert.That(status.IsFailure, Is.False);
            Assert.That(status.IsPending, Is.False);
            Assert.That(status.IsCancel, Is.False);
        }

        [Test]
        public void IsFailure_TrueForFailureStatus()
        {
            var status = DicomStatus.ProcessingFailure;

            Assert.That(status.IsSuccess, Is.False);
            Assert.That(status.IsWarning, Is.False);
            Assert.That(status.IsFailure, Is.True);
            Assert.That(status.IsPending, Is.False);
            Assert.That(status.IsCancel, Is.False);
        }

        [Test]
        public void IsPending_TrueForPendingStatus()
        {
            var status = DicomStatus.Pending;

            Assert.That(status.IsSuccess, Is.False);
            Assert.That(status.IsWarning, Is.False);
            Assert.That(status.IsFailure, Is.False);
            Assert.That(status.IsPending, Is.True);
            Assert.That(status.IsCancel, Is.False);
        }

        [Test]
        public void IsCancel_TrueForCancelStatus()
        {
            var status = DicomStatus.Cancel;

            Assert.That(status.IsSuccess, Is.False);
            Assert.That(status.IsWarning, Is.False);
            Assert.That(status.IsFailure, Is.False);
            Assert.That(status.IsPending, Is.False);
            Assert.That(status.IsCancel, Is.True);
        }

        #endregion

        #region Equality Tests

        [Test]
        public void Equals_SameCode_ReturnsTrue()
        {
            var status1 = new DicomStatus(0x0000);
            var status2 = new DicomStatus(0x0000);

            Assert.That(status1.Equals(status2), Is.True);
            Assert.That(status1 == status2, Is.True);
            Assert.That(status1 != status2, Is.False);
        }

        [Test]
        public void Equals_DifferentCode_ReturnsFalse()
        {
            var status1 = new DicomStatus(0x0000);
            var status2 = new DicomStatus(0x0110);

            Assert.That(status1.Equals(status2), Is.False);
            Assert.That(status1 == status2, Is.False);
            Assert.That(status1 != status2, Is.True);
        }

        [Test]
        public void Equals_SameCodeDifferentComment_ReturnsTrue()
        {
            // Equality is based only on code, not error comment
            var status1 = new DicomStatus(0x0110, "Error A");
            var status2 = new DicomStatus(0x0110, "Error B");

            Assert.That(status1.Equals(status2), Is.True);
        }

        [Test]
        public void GetHashCode_SameCode_ReturnsSameHash()
        {
            var status1 = new DicomStatus(0xB000);
            var status2 = new DicomStatus(0xB000);

            Assert.That(status1.GetHashCode(), Is.EqualTo(status2.GetHashCode()));
        }

        [Test]
        public void Equals_BoxedObject_ReturnsCorrectResult()
        {
            var status = new DicomStatus(0x0000);
            object boxed = new DicomStatus(0x0000);
            object different = new DicomStatus(0x0110);
            object notAStatus = "not a status";

            Assert.That(status.Equals(boxed), Is.True);
            Assert.That(status.Equals(different), Is.False);
            Assert.That(status.Equals(notAStatus), Is.False);
            Assert.That(status.Equals(null), Is.False);
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToString_Success_IncludesCodeAndCategory()
        {
            var status = DicomStatus.Success;

            var result = status.ToString();

            Assert.That(result, Is.EqualTo("0x0000 (Success)"));
        }

        [Test]
        public void ToString_WithErrorComment_IncludesComment()
        {
            var status = new DicomStatus(0x0110, "Test error message");

            var result = status.ToString();

            Assert.That(result, Is.EqualTo("0x0110 (Failure): Test error message"));
        }

        [Test]
        public void ToString_Warning_FormatsCorrectly()
        {
            var status = DicomStatus.CoercionOfDataElements;

            var result = status.ToString();

            Assert.That(result, Is.EqualTo("0xB000 (Warning)"));
        }

        #endregion

        #region Well-Known Status Tests

        [Test]
        public void WellKnownStatuses_HaveCorrectCodes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DicomStatus.Success.Code, Is.EqualTo((ushort)0x0000));
                Assert.That(DicomStatus.Cancel.Code, Is.EqualTo((ushort)0xFE00));
                Assert.That(DicomStatus.Pending.Code, Is.EqualTo((ushort)0xFF00));
                Assert.That(DicomStatus.PendingWarning.Code, Is.EqualTo((ushort)0xFF01));
                Assert.That(DicomStatus.OutOfResources.Code, Is.EqualTo((ushort)0xA700));
                Assert.That(DicomStatus.NoSuchSOPClass.Code, Is.EqualTo((ushort)0xA900));
                Assert.That(DicomStatus.ProcessingFailure.Code, Is.EqualTo((ushort)0x0110));
                Assert.That(DicomStatus.DuplicateSOPInstance.Code, Is.EqualTo((ushort)0x0111));
                Assert.That(DicomStatus.NoSuchObjectInstance.Code, Is.EqualTo((ushort)0xC001));
                Assert.That(DicomStatus.MissingAttribute.Code, Is.EqualTo((ushort)0x0120));
                Assert.That(DicomStatus.CoercionOfDataElements.Code, Is.EqualTo((ushort)0xB000));
                Assert.That(DicomStatus.DataSetDoesNotMatchSOPClass.Code, Is.EqualTo((ushort)0xB007));
                Assert.That(DicomStatus.ElementsDiscarded.Code, Is.EqualTo((ushort)0xB006));
            });
        }

        [Test]
        public void WellKnownStatuses_HaveCorrectCategories()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DicomStatus.Success.Category, Is.EqualTo(StatusCategory.Success));
                Assert.That(DicomStatus.Cancel.Category, Is.EqualTo(StatusCategory.Cancel));
                Assert.That(DicomStatus.Pending.Category, Is.EqualTo(StatusCategory.Pending));
                Assert.That(DicomStatus.PendingWarning.Category, Is.EqualTo(StatusCategory.Pending));
                Assert.That(DicomStatus.OutOfResources.Category, Is.EqualTo(StatusCategory.Failure));
                Assert.That(DicomStatus.NoSuchSOPClass.Category, Is.EqualTo(StatusCategory.Failure));
                Assert.That(DicomStatus.ProcessingFailure.Category, Is.EqualTo(StatusCategory.Failure));
                Assert.That(DicomStatus.CoercionOfDataElements.Category, Is.EqualTo(StatusCategory.Warning));
                Assert.That(DicomStatus.DataSetDoesNotMatchSOPClass.Category, Is.EqualTo(StatusCategory.Warning));
                Assert.That(DicomStatus.ElementsDiscarded.Category, Is.EqualTo(StatusCategory.Warning));
            });
        }

        #endregion

        #region Constructor Tests

        [Test]
        public void Constructor_WithCode_SetsCodeAndNullComment()
        {
            var status = new DicomStatus(0x1234);

            Assert.That(status.Code, Is.EqualTo((ushort)0x1234));
            Assert.That(status.ErrorComment, Is.Null);
        }

        [Test]
        public void Constructor_WithCodeAndComment_SetsBothProperties()
        {
            var status = new DicomStatus(0xC000, "Custom error");

            Assert.That(status.Code, Is.EqualTo((ushort)0xC000));
            Assert.That(status.ErrorComment, Is.EqualTo("Custom error"));
        }

        [Test]
        public void InitOnlyProperty_CanSetErrorComment()
        {
            var status = new DicomStatus(0x0110) { ErrorComment = "Init-only comment" };

            Assert.That(status.ErrorComment, Is.EqualTo("Init-only comment"));
        }

        #endregion

        #region Static CategorizeCode Tests

        [Test]
        public void CategorizeCode_BoundaryValues()
        {
            Assert.Multiple(() =>
            {
                // Success boundary
                Assert.That(DicomStatus.CategorizeCode(0x0000), Is.EqualTo(StatusCategory.Success));
                Assert.That(DicomStatus.CategorizeCode(0x0001), Is.EqualTo(StatusCategory.Failure));

                // Warning boundaries
                Assert.That(DicomStatus.CategorizeCode(0xAFFF), Is.EqualTo(StatusCategory.Failure));
                Assert.That(DicomStatus.CategorizeCode(0xB000), Is.EqualTo(StatusCategory.Warning));
                Assert.That(DicomStatus.CategorizeCode(0xBFFF), Is.EqualTo(StatusCategory.Warning));
                Assert.That(DicomStatus.CategorizeCode(0xC000), Is.EqualTo(StatusCategory.Failure));

                // Cancel
                Assert.That(DicomStatus.CategorizeCode(0xFE00), Is.EqualTo(StatusCategory.Cancel));
                Assert.That(DicomStatus.CategorizeCode(0xFE01), Is.EqualTo(StatusCategory.Failure));

                // Pending
                Assert.That(DicomStatus.CategorizeCode(0xFF00), Is.EqualTo(StatusCategory.Pending));
                Assert.That(DicomStatus.CategorizeCode(0xFF01), Is.EqualTo(StatusCategory.Pending));
                Assert.That(DicomStatus.CategorizeCode(0xFF02), Is.EqualTo(StatusCategory.Failure));
            });
        }

        #endregion
    }
}
