using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class DicomEncodingTests
{
    #region Single-Valued Character Set Parsing

    [Test]
    public void FromSpecificCharacterSet_EmptyString_ReturnsDefault()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("");
        Assert.That(encoding, Is.SameAs(DicomEncoding.Default));
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void FromSpecificCharacterSet_Null_ReturnsDefault()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet((string?)null);
        Assert.That(encoding, Is.SameAs(DicomEncoding.Default));
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void FromSpecificCharacterSet_Whitespace_ReturnsDefault()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("   ");
        Assert.That(encoding, Is.SameAs(DicomEncoding.Default));
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void FromSpecificCharacterSet_Latin1_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 100");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    [Test]
    public void FromSpecificCharacterSet_Utf8_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 192");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(65001)); // UTF-8
    }

    [Test]
    public void FromSpecificCharacterSet_Ascii_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 6");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void FromSpecificCharacterSet_GB18030_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("GB18030");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(54936)); // GB18030
    }

    [Test]
    public void FromSpecificCharacterSet_Latin2_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 101");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28592)); // Latin-2
    }

    [Test]
    public void FromSpecificCharacterSet_Cyrillic_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 144");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28595)); // Cyrillic
    }

    [Test]
    public void FromSpecificCharacterSet_Greek_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 126");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28597)); // Greek
    }

    [Test]
    public void FromSpecificCharacterSet_Thai_ReturnsCorrectEncoding()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 166");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(874)); // Thai
    }

    #endregion

    #region Multi-Valued Character Set Parsing

    private static readonly string[] SingleValueLatin1 = new[] { "ISO_IR 100" };
    private static readonly string[] SingleValueJapaneseISO2022 = new[] { "ISO 2022 IR 87" };
    private static readonly string[] MultiValueJapanese = new[] { "ISO 2022 IR 87", "ISO 2022 IR 13" };
    private static readonly string[] Utf8WithExtensions = new[] { "ISO_IR 192", "ISO 2022 IR 87" };
    private static readonly string[] GB18030WithExtensions = new[] { "GB18030", "ISO 2022 IR 87" };
    private static readonly string[] GBKWithExtensions = new[] { "GBK", "ISO 2022 IR 87" };
    private static readonly string[] JapaneseWithEmptyExtensions = new[] { "ISO 2022 IR 87", "", "  " };

    [Test]
    public void FromSpecificCharacterSet_Array_Empty_ReturnsDefault()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(Array.Empty<string>());
        Assert.That(encoding, Is.SameAs(DicomEncoding.Default));
    }

    [Test]
    public void FromSpecificCharacterSet_Array_Null_ReturnsDefault()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet((string[]?)null);
        Assert.That(encoding, Is.SameAs(DicomEncoding.Default));
    }

    [Test]
    public void FromSpecificCharacterSet_Array_SingleValue_ReturnsPrimaryOnly()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(SingleValueLatin1);
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
        Assert.That(encoding.Extensions, Is.Null);
    }

    [Test]
    public void FromSpecificCharacterSet_Array_JapaneseISO2022_ReturnsPrimaryAndExtensions()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(SingleValueJapaneseISO2022);
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(50220)); // JIS X 0208
        // Single value, no extensions
        Assert.That(encoding.Extensions, Is.Null);
    }

    [Test]
    public void FromSpecificCharacterSet_Array_MultiValuedJapanese_HasExtensions()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(MultiValueJapanese);
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(50220)); // JIS X 0208
        Assert.That(encoding.Extensions, Is.Not.Null);
        Assert.That(encoding.Extensions!.Count, Is.EqualTo(1));
        Assert.That(encoding.Extensions[0].CodePage, Is.EqualTo(50222)); // JIS X 0201
    }

    [Test]
    public void FromSpecificCharacterSet_Array_Utf8WithExtensions_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DicomEncoding.FromSpecificCharacterSet(Utf8WithExtensions));
        Assert.That(ex!.Message, Does.Contain("ISO_IR 192"));
        Assert.That(ex.Message, Does.Contain("code extensions"));
    }

    [Test]
    public void FromSpecificCharacterSet_Array_GB18030WithExtensions_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DicomEncoding.FromSpecificCharacterSet(GB18030WithExtensions));
        Assert.That(ex!.Message, Does.Contain("GB18030"));
        Assert.That(ex.Message, Does.Contain("code extensions"));
    }

    [Test]
    public void FromSpecificCharacterSet_Array_GBKWithExtensions_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DicomEncoding.FromSpecificCharacterSet(GBKWithExtensions));
        Assert.That(ex!.Message, Does.Contain("GBK"));
        Assert.That(ex.Message, Does.Contain("code extensions"));
    }

    [Test]
    public void FromSpecificCharacterSet_Array_SkipsEmptyExtensions()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(JapaneseWithEmptyExtensions);
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(50220));
        // Empty and whitespace extensions should be skipped
        Assert.That(encoding.Extensions, Is.Null);
    }

    #endregion

    #region Normalization Tests

    [Test]
    public void FromSpecificCharacterSet_SpaceVariant_Normalizes()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO IR 100");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    [Test]
    public void FromSpecificCharacterSet_HyphenVariant_Normalizes()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO-IR 100");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    [Test]
    public void FromSpecificCharacterSet_LeadingTrailingWhitespace_Works()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("  ISO_IR 100  ");
        Assert.That(encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    #endregion

    #region UTF-8 Compatibility Detection

    [Test]
    public void Default_IsUtf8Compatible_True()
    {
        Assert.That(DicomEncoding.Default.IsUtf8Compatible, Is.True);
    }

    [Test]
    public void Utf8_IsUtf8Compatible_True()
    {
        Assert.That(DicomEncoding.Utf8.IsUtf8Compatible, Is.True);
    }

    [Test]
    public void Latin1_IsUtf8Compatible_False()
    {
        Assert.That(DicomEncoding.Latin1.IsUtf8Compatible, Is.False);
    }

    [Test]
    public void FromSpecificCharacterSet_Utf8_IsUtf8Compatible()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 192");
        Assert.That(encoding.IsUtf8Compatible, Is.True);
    }

    [Test]
    public void FromSpecificCharacterSet_Ascii_IsUtf8Compatible()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 6");
        Assert.That(encoding.IsUtf8Compatible, Is.True);
    }

    [Test]
    public void FromSpecificCharacterSet_Latin1_IsNotUtf8Compatible()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 100");
        Assert.That(encoding.IsUtf8Compatible, Is.False);
    }

    #endregion

    #region TryGetUtf8 Tests

    [Test]
    public void TryGetUtf8_Utf8Encoding_ReturnsTrue()
    {
        var encoding = DicomEncoding.Utf8;
        var bytes = Encoding.UTF8.GetBytes("Hello, World!");

        var result = encoding.TryGetUtf8(bytes, out var utf8);

        Assert.That(result, Is.True);
        Assert.That(utf8.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void TryGetUtf8_AsciiEncoding_ReturnsTrue()
    {
        var encoding = DicomEncoding.Default;
        var bytes = Encoding.ASCII.GetBytes("Hello, World!");

        var result = encoding.TryGetUtf8(bytes, out var utf8);

        Assert.That(result, Is.True);
        Assert.That(utf8.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void TryGetUtf8_Latin1Encoding_ReturnsFalse()
    {
        var encoding = DicomEncoding.Latin1;
        var bytes = Encoding.GetEncoding(28591).GetBytes("Hëllö");

        var result = encoding.TryGetUtf8(bytes, out var utf8);

        Assert.That(result, Is.False);
        Assert.That(utf8.Length, Is.EqualTo(0));
    }

    [Test]
    public void TryGetUtf8_EmptyBytes_ReturnsTrue()
    {
        var encoding = DicomEncoding.Utf8;

        var result = encoding.TryGetUtf8(ReadOnlySpan<byte>.Empty, out var utf8);

        Assert.That(result, Is.True);
        Assert.That(utf8.Length, Is.EqualTo(0));
    }

    #endregion

    #region GetString Tests

    [Test]
    public void GetString_Ascii_DecodesCorrectly()
    {
        var encoding = DicomEncoding.Default;
        var bytes = Encoding.ASCII.GetBytes("Hello, World!");

        var result = encoding.GetString(bytes);

        Assert.That(result, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void GetString_Utf8_DecodesCorrectly()
    {
        var encoding = DicomEncoding.Utf8;
        var bytes = Encoding.UTF8.GetBytes("Hëllö Wørld! 你好");

        var result = encoding.GetString(bytes);

        Assert.That(result, Is.EqualTo("Hëllö Wørld! 你好"));
    }

    [Test]
    public void GetString_Latin1_DecodesCorrectly()
    {
        var encoding = DicomEncoding.Latin1;
        var bytes = Encoding.GetEncoding(28591).GetBytes("Café");

        var result = encoding.GetString(bytes);

        Assert.That(result, Is.EqualTo("Café"));
    }

    [Test]
    public void GetString_EmptyBytes_ReturnsEmptyString()
    {
        var encoding = DicomEncoding.Default;

        var result = encoding.GetString(ReadOnlySpan<byte>.Empty);

        Assert.That(result, Is.EqualTo(""));
    }

    #endregion

    #region Extensions Property Tests

    [Test]
    public void SingleValue_HasExtensions_False()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet("ISO_IR 100");
        Assert.That(encoding.HasExtensions, Is.False);
        Assert.That(encoding.Extensions, Is.Null);
    }

    private static readonly string[] JapaneseCharacterSets = new[] { "ISO 2022 IR 87", "ISO 2022 IR 13" };

    [Test]
    public void MultiValue_HasExtensions_True()
    {
        var encoding = DicomEncoding.FromSpecificCharacterSet(JapaneseCharacterSets);
        Assert.That(encoding.HasExtensions, Is.True);
        Assert.That(encoding.Extensions, Is.Not.Null);
        Assert.That(encoding.Extensions!.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Default_HasExtensions_False()
    {
        Assert.That(DicomEncoding.Default.HasExtensions, Is.False);
    }

    #endregion

    #region DicomCharacterSets Registry Tests

    [Test]
    public void DicomCharacterSets_GetEncoding_AllStandardTerms_ReturnsValidEncodings()
    {
        var terms = new[]
        {
            "", "ISO_IR 6", "ISO_IR 100", "ISO_IR 101", "ISO_IR 109", "ISO_IR 110",
            "ISO_IR 144", "ISO_IR 127", "ISO_IR 126", "ISO_IR 138", "ISO_IR 148",
            "ISO_IR 166", "ISO_IR 192", "GB18030", "GBK",
            "ISO 2022 IR 6", "ISO 2022 IR 87", "ISO 2022 IR 159", "ISO 2022 IR 13",
            "ISO 2022 IR 149", "ISO 2022 IR 58"
        };

        foreach (var term in terms)
        {
            Assert.DoesNotThrow(() =>
            {
                var encoding = DicomCharacterSets.GetEncoding(term);
                Assert.That(encoding, Is.Not.Null, $"Failed for term: {term}");
            }, $"Failed for term: {term}");
        }
    }

    [Test]
    public void DicomCharacterSets_GetEncoding_UnknownTerm_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DicomCharacterSets.GetEncoding("INVALID_CHARSET"));
        Assert.That(ex!.Message, Does.Contain("Unknown"));
    }

    [Test]
    public void DicomCharacterSets_GetEncoding_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DicomCharacterSets.GetEncoding(null!));
    }

    [Test]
    public void DicomCharacterSets_Register_CustomTerm_Works()
    {
        const string customTerm = "CUSTOM_ENCODING";
        const int customCodePage = 28591; // Reuse Latin-1 for testing

        DicomCharacterSets.Register(customTerm, customCodePage);

        var encoding = DicomCharacterSets.GetEncoding(customTerm);
        Assert.That(encoding.CodePage, Is.EqualTo(customCodePage));
    }

    [Test]
    public void DicomCharacterSets_GetDicomTerm_KnownEncoding_ReturnsCorrectTerm()
    {
        var encoding = Encoding.GetEncoding(28591); // Latin-1
        var term = DicomCharacterSets.GetDicomTerm(encoding);
        Assert.That(term, Is.EqualTo("ISO_IR 100"));
    }

    [Test]
    public void DicomCharacterSets_GetDicomTerm_Utf8_ReturnsCorrectTerm()
    {
        var encoding = Encoding.UTF8;
        var term = DicomCharacterSets.GetDicomTerm(encoding);
        Assert.That(term, Is.EqualTo("ISO_IR 192"));
    }

    [Test]
    public void DicomCharacterSets_GetDicomTerm_UnknownEncoding_ReturnsNull()
    {
        var encoding = Encoding.GetEncoding(1252); // Windows-1252 (not in DICOM standard)
        var term = DicomCharacterSets.GetDicomTerm(encoding);
        Assert.That(term, Is.Null);
    }

    [Test]
    public void DicomCharacterSets_GetDicomTerm_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DicomCharacterSets.GetDicomTerm(null!));
    }

    #endregion

    #region Static Instances Tests

    [Test]
    public void StaticInstances_AreNotNull()
    {
        Assert.That(DicomEncoding.Default, Is.Not.Null);
        Assert.That(DicomEncoding.Utf8, Is.Not.Null);
        Assert.That(DicomEncoding.Latin1, Is.Not.Null);
    }

    [Test]
    public void Default_IsAscii()
    {
        Assert.That(DicomEncoding.Default.Primary.CodePage, Is.EqualTo(20127));
    }

    [Test]
    public void Utf8_IsUtf8()
    {
        Assert.That(DicomEncoding.Utf8.Primary.CodePage, Is.EqualTo(65001));
    }

    [Test]
    public void Latin1_IsLatin1()
    {
        Assert.That(DicomEncoding.Latin1.Primary.CodePage, Is.EqualTo(28591));
    }

    #endregion

    #region FromEncoding Tests

    [Test]
    public void FromEncoding_CreatesWrapper()
    {
        var systemEncoding = Encoding.UTF8;
        var dicomEncoding = DicomEncoding.FromEncoding(systemEncoding);

        Assert.That(dicomEncoding.Primary, Is.SameAs(systemEncoding));
    }

    #endregion
}
