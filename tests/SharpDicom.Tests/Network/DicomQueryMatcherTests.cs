using System;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// Unit tests for <see cref="DicomQueryMatcher"/> and <see cref="DicomDateRange"/>.
    /// </summary>
    [TestFixture]
    public class DicomQueryMatcherTests
    {
        #region DicomWildcardToSqlLike Tests

        [Test]
        public void WildcardToSql_AsteriskBecomesPercent()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("Smith*");

            Assert.That(pattern, Is.EqualTo("Smith%"));
            Assert.That(hasWildcard, Is.True);
        }

        [Test]
        public void WildcardToSql_QuestionMarkBecomesUnderscore()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("Sm?th");

            Assert.That(pattern, Is.EqualTo("Sm_th"));
            Assert.That(hasWildcard, Is.True);
        }

        [Test]
        public void WildcardToSql_LiteralPercentEscaped()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("100%");

            Assert.That(pattern, Is.EqualTo(@"100\%"));
            Assert.That(hasWildcard, Is.False);
        }

        [Test]
        public void WildcardToSql_LiteralUnderscoreEscaped()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("test_value");

            Assert.That(pattern, Is.EqualTo(@"test\_value"));
            Assert.That(hasWildcard, Is.False);
        }

        [Test]
        public void WildcardToSql_BackslashEscaped()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike(@"a\b");

            Assert.That(pattern, Is.EqualTo(@"a\\b"));
            Assert.That(hasWildcard, Is.False);
        }

        [Test]
        public void WildcardToSql_ComplexPattern()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("Sm*th?");

            Assert.That(pattern, Is.EqualTo("Sm%th_"));
            Assert.That(hasWildcard, Is.True);
        }

        [Test]
        public void WildcardToSql_NoWildcards()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("Smith");

            Assert.That(pattern, Is.EqualTo("Smith"));
            Assert.That(hasWildcard, Is.False);
        }

        [Test]
        public void WildcardToSql_AllWildcard()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("*");

            Assert.That(pattern, Is.EqualTo("%"));
            Assert.That(hasWildcard, Is.True);
        }

        [Test]
        public void WildcardToSql_EmptyString_ReturnsEmpty()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike("");

            Assert.That(pattern, Is.EqualTo(""));
            Assert.That(hasWildcard, Is.False);
        }

        [Test]
        public void WildcardToSql_NullString_ReturnsEmpty()
        {
            var (pattern, hasWildcard) = DicomQueryMatcher.DicomWildcardToSqlLike(null!);

            Assert.That(pattern, Is.EqualTo(""));
            Assert.That(hasWildcard, Is.False);
        }

        #endregion

        #region HasDicomWildcard Tests

        [Test]
        public void HasDicomWildcard_WithAsterisk_ReturnsTrue()
        {
            Assert.That(DicomQueryMatcher.HasDicomWildcard("Smith*"), Is.True);
        }

        [Test]
        public void HasDicomWildcard_WithQuestionMark_ReturnsTrue()
        {
            Assert.That(DicomQueryMatcher.HasDicomWildcard("Sm?th"), Is.True);
        }

        [Test]
        public void HasDicomWildcard_WithoutWildcard_ReturnsFalse()
        {
            Assert.That(DicomQueryMatcher.HasDicomWildcard("Smith"), Is.False);
        }

        [Test]
        public void HasDicomWildcard_Null_ReturnsFalse()
        {
            Assert.That(DicomQueryMatcher.HasDicomWildcard(null), Is.False);
        }

        [Test]
        public void HasDicomWildcard_Empty_ReturnsFalse()
        {
            Assert.That(DicomQueryMatcher.HasDicomWildcard(""), Is.False);
        }

        #endregion

        #region MatchesWildcard Tests

        [Test]
        public void MatchesWildcard_ExactMatch_ReturnsTrue()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("Smith", "Smith", caseInsensitive: false), Is.True);
        }

        [Test]
        public void MatchesWildcard_ExactMismatch_ReturnsFalse()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("Smith", "Jones", caseInsensitive: false), Is.False);
        }

        [Test]
        public void MatchesWildcard_AsteriskSuffix_Matches()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("Smith^John", "Smith*", caseInsensitive: false), Is.True);
        }

        [Test]
        public void MatchesWildcard_AsteriskPrefix_Matches()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("John Smith", "*Smith", caseInsensitive: false), Is.True);
        }

        [Test]
        public void MatchesWildcard_AsteriskMiddle_Matches()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("Smith^John^Dr", "Smith*Dr", caseInsensitive: false), Is.True);
        }

        [Test]
        public void MatchesWildcard_CaseInsensitive_ForPN()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("SMITH^JOHN", "smith*", caseInsensitive: true), Is.True);
        }

        [Test]
        public void MatchesWildcard_CaseSensitive_ForNonPN()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("ct", "CT", caseInsensitive: false), Is.False);
        }

        [Test]
        public void MatchesWildcard_QuestionMark_MatchesSingleChar()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("CT", "C?", caseInsensitive: false), Is.True);
        }

        [Test]
        public void MatchesWildcard_QuestionMark_DoesNotMatchMultipleChars()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("CTA", "C?", caseInsensitive: false), Is.False);
        }

        [Test]
        public void MatchesWildcard_EmptyPattern_OnlyMatchesEmptyValue()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("", "", caseInsensitive: false), Is.True);
            Assert.That(DicomQueryMatcher.MatchesWildcard("Smith", "", caseInsensitive: false), Is.False);
        }

        [Test]
        public void MatchesWildcard_AllStar_MatchesAnything()
        {
            Assert.That(DicomQueryMatcher.MatchesWildcard("anything", "*", caseInsensitive: false), Is.True);
            Assert.That(DicomQueryMatcher.MatchesWildcard("", "*", caseInsensitive: false), Is.True);
        }

        #endregion

        #region DicomDateRange Tests

        [Test]
        public void Parse_SingleDate_ReturnsSameFromTo()
        {
            var range = DicomDateRange.Parse("20240115");

            Assert.That(range.From, Is.EqualTo(new DateTime(2024, 1, 15)));
            Assert.That(range.To, Is.EqualTo(new DateTime(2024, 1, 15)));
            Assert.That(range.IsUniversal, Is.False);
        }

        [Test]
        public void Parse_FullRange_ReturnsFromTo()
        {
            var range = DicomDateRange.Parse("20240101-20240131");

            Assert.That(range.From, Is.EqualTo(new DateTime(2024, 1, 1)));
            Assert.That(range.To, Is.EqualTo(new DateTime(2024, 1, 31)));
        }

        [Test]
        public void Parse_OpenStart_ReturnsNullFrom()
        {
            var range = DicomDateRange.Parse("-20240131");

            Assert.That(range.From, Is.Null);
            Assert.That(range.To, Is.EqualTo(new DateTime(2024, 1, 31)));
        }

        [Test]
        public void Parse_OpenEnd_ReturnsNullTo()
        {
            var range = DicomDateRange.Parse("20240101-");

            Assert.That(range.From, Is.EqualTo(new DateTime(2024, 1, 1)));
            Assert.That(range.To, Is.Null);
        }

        [Test]
        public void Parse_Empty_ReturnsUniversal()
        {
            var range = DicomDateRange.Parse("");

            Assert.That(range.From, Is.Null);
            Assert.That(range.To, Is.Null);
            Assert.That(range.IsUniversal, Is.True);
        }

        [Test]
        public void Parse_Null_ReturnsUniversal()
        {
            var range = DicomDateRange.Parse(null);

            Assert.That(range.From, Is.Null);
            Assert.That(range.To, Is.Null);
            Assert.That(range.IsUniversal, Is.True);
        }

        [Test]
        public void Contains_DateInRange_ReturnsTrue()
        {
            var range = DicomDateRange.Parse("20240101-20240131");

            Assert.That(range.Contains(new DateTime(2024, 1, 15)), Is.True);
            Assert.That(range.Contains(new DateTime(2024, 1, 1)), Is.True, "From date is inclusive");
            Assert.That(range.Contains(new DateTime(2024, 1, 31)), Is.True, "To date is inclusive");
        }

        [Test]
        public void Contains_DateOutOfRange_ReturnsFalse()
        {
            var range = DicomDateRange.Parse("20240101-20240131");

            Assert.That(range.Contains(new DateTime(2023, 12, 31)), Is.False);
            Assert.That(range.Contains(new DateTime(2024, 2, 1)), Is.False);
        }

        [Test]
        public void Contains_UniversalRange_ContainsAllDates()
        {
            var range = DicomDateRange.Parse(null);

            Assert.That(range.Contains(new DateTime(1900, 1, 1)), Is.True);
            Assert.That(range.Contains(new DateTime(2099, 12, 31)), Is.True);
        }

        [Test]
        public void Contains_OpenStart_ContainsEarlyDates()
        {
            var range = DicomDateRange.Parse("-20240131");

            Assert.That(range.Contains(new DateTime(1900, 1, 1)), Is.True);
            Assert.That(range.Contains(new DateTime(2024, 2, 1)), Is.False);
        }

        [Test]
        public void Contains_OpenEnd_ContainsLateDates()
        {
            var range = DicomDateRange.Parse("20240101-");

            Assert.That(range.Contains(new DateTime(2099, 12, 31)), Is.True);
            Assert.That(range.Contains(new DateTime(2023, 12, 31)), Is.False);
        }

        #endregion

        #region FilterReturnKeys Tests

        [Test]
        public void FilterReturnKeys_OnlyIncludesRequestedTags()
        {
            // Build match with many tags
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            match.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT001"));
            match.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
            match.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
            match.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
            match.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.5"));
            match.Add(CreateStringElement(DicomTag.AccessionNumber, DicomVR.SH, "ACC001"));

            // Request only a subset (PatientName and StudyDate as matching keys, plus QR level)
            var request = new DicomDataset();
            request.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            request.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));
            request.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Array.Empty<byte>()));

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            // Should have QR level + PatientName + StudyDate = 3 tags
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[DicomTag.QueryRetrieveLevel], Is.Not.Null);
            Assert.That(result[DicomTag.PatientName], Is.Not.Null);
            Assert.That(result[DicomTag.StudyDate], Is.Not.Null);

            // Should NOT have tags not in request
            Assert.That(result[DicomTag.PatientID], Is.Null);
            Assert.That(result[DicomTag.Modality], Is.Null);
            Assert.That(result[DicomTag.StudyInstanceUID], Is.Null);
            Assert.That(result[DicomTag.AccessionNumber], Is.Null);
        }

        [Test]
        public void FilterReturnKeys_AlwaysIncludesQueryRetrieveLevel()
        {
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "PATIENT"));
            match.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT001"));

            // Request does NOT explicitly include QueryRetrieveLevel
            var request = new DicomDataset();
            request.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT001"));

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            Assert.That(result[DicomTag.QueryRetrieveLevel], Is.Not.Null);
        }

        [Test]
        public void FilterReturnKeys_PreservesMatchingKeyValues()
        {
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            match.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));

            var request = new DicomDataset();
            request.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            request.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            // The result PatientName should be from the match, not the query pattern
            var pnElement = result[DicomTag.PatientName] as DicomStringElement;
            Assert.That(pnElement, Is.Not.Null);
            Assert.That(pnElement!.GetString(), Is.EqualTo("Smith^John"));
        }

        [Test]
        public void FilterReturnKeys_IncludesReturnKeyValues()
        {
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            match.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
            match.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));

            // Request has StudyDate as return key (zero-length)
            var request = new DicomDataset();
            request.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            request.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));
            request.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Array.Empty<byte>()));

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            // StudyDate should have the value from match even though request had zero-length
            var daElement = result[DicomTag.StudyDate] as DicomStringElement;
            Assert.That(daElement, Is.Not.Null);
            Assert.That(daElement!.GetString(), Is.EqualTo("20240115"));
        }

        [Test]
        public void FilterReturnKeys_EmptyRequest_ReturnsOnlyQRLevel()
        {
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            match.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
            match.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));

            var request = new DicomDataset();
            // Empty request - no tags at all

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            // Only QueryRetrieveLevel should be present
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[DicomTag.QueryRetrieveLevel], Is.Not.Null);
        }

        [Test]
        public void FilterReturnKeys_MissingTagInMatch_ReturnsZeroLengthElement()
        {
            var match = new DicomDataset();
            match.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            match.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
            // match does NOT have AccessionNumber

            var request = new DicomDataset();
            request.Add(CreateStringElement(DicomTag.QueryRetrieveLevel, DicomVR.CS, "STUDY"));
            request.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith*"));
            request.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH, Array.Empty<byte>()));

            var result = DicomQueryMatcher.FilterReturnKeys(match, request);

            // AccessionNumber should be present but empty
            var accElement = result[DicomTag.AccessionNumber];
            Assert.That(accElement, Is.Not.Null);
            Assert.That(accElement!.IsEmpty, Is.True);
        }

        #endregion

        #region DicomDateRange Equality Tests

        [Test]
        public void DicomDateRange_Equality_SameValues()
        {
            var a = DicomDateRange.Parse("20240101-20240131");
            var b = DicomDateRange.Parse("20240101-20240131");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
        }

        [Test]
        public void DicomDateRange_Inequality_DifferentValues()
        {
            var a = DicomDateRange.Parse("20240101-20240131");
            var b = DicomDateRange.Parse("20240101-20240228");

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void DicomDateRange_ToString_SingleDate()
        {
            var range = DicomDateRange.Parse("20240115");
            Assert.That(range.ToString(), Is.EqualTo("20240115"));
        }

        [Test]
        public void DicomDateRange_ToString_Universal()
        {
            var range = DicomDateRange.Parse(null);
            Assert.That(range.ToString(), Is.EqualTo("(universal)"));
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a <see cref="DicomStringElement"/> with the specified tag, VR, and string value.
        /// </summary>
        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            // Pad to even length per DICOM spec
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                bytes.CopyTo(padded, 0);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)0 : (byte)' ';
                bytes = padded;
            }
            return new DicomStringElement(tag, vr, bytes);
        }

        #endregion
    }
}
