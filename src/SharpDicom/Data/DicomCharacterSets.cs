using System;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif
using System.Collections.Generic;
using System.Text;

namespace SharpDicom.Data;

/// <summary>
/// Registry for mapping DICOM character set terms (Specific Character Set values)
/// to .NET Encoding instances.
/// </summary>
public static class DicomCharacterSets
{
#if NET8_0_OR_GREATER
    private static readonly FrozenDictionary<string, int> TermToCodePage;
#else
    private static readonly Dictionary<string, int> TermToCodePage;
#endif
    private static readonly Dictionary<int, string> CodePageToTerm;
    private static readonly Dictionary<string, int> CustomTerms;

    static DicomCharacterSets()
    {
        // Register code page provider for extended encodings
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Build standard character set mappings
        var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Default and ASCII
            [""] = 20127,           // Default when Specific Character Set absent
            ["ISO_IR 6"] = 20127,   // ASCII (explicit)

            // Latin family
            ["ISO_IR 100"] = 28591, // Latin-1 (Western European)
            ["ISO_IR 101"] = 28592, // Latin-2 (Central European)
            ["ISO_IR 109"] = 28593, // Latin-3 (South European)
            ["ISO_IR 110"] = 28594, // Latin-4 (Baltic)
            ["ISO_IR 148"] = 28599, // Latin-5 (Turkish)

            // Other scripts
            ["ISO_IR 144"] = 28595, // Cyrillic
            ["ISO_IR 127"] = 28596, // Arabic
            ["ISO_IR 126"] = 28597, // Greek
            ["ISO_IR 138"] = 28598, // Hebrew
            ["ISO_IR 166"] = 874,   // Thai (TIS-620)

            // UTF-8
            ["ISO_IR 192"] = 65001, // UTF-8

            // Asian multi-byte without code extensions
            ["GB18030"] = 54936,    // Chinese (full Unicode mapping)
            ["GBK"] = 936,          // Chinese (subset of GB18030)

            // ISO 2022 with code extensions
            ["ISO 2022 IR 6"] = 20127,   // ASCII with ISO 2022
            ["ISO 2022 IR 87"] = 50220,  // Japanese Kanji (JIS X 0208)
            ["ISO 2022 IR 159"] = 50220, // Japanese Supplementary (JIS X 0212)
            ["ISO 2022 IR 13"] = 50222,  // Japanese Katakana (JIS X 0201)
            ["ISO 2022 IR 149"] = 50225, // Korean (KS X 1001)
            ["ISO 2022 IR 58"] = 50227,  // Simplified Chinese (GB 2312)

            // ISO 2022 with Latin code extensions
            ["ISO 2022 IR 100"] = 28591, // Latin-1 with ISO 2022
            ["ISO 2022 IR 101"] = 28592, // Latin-2 with ISO 2022
            ["ISO 2022 IR 109"] = 28593, // Latin-3 with ISO 2022
            ["ISO 2022 IR 110"] = 28594, // Latin-4 with ISO 2022
            ["ISO 2022 IR 144"] = 28595, // Cyrillic with ISO 2022
            ["ISO 2022 IR 127"] = 28596, // Arabic with ISO 2022
            ["ISO 2022 IR 126"] = 28597, // Greek with ISO 2022
            ["ISO 2022 IR 138"] = 28598, // Hebrew with ISO 2022
            ["ISO 2022 IR 148"] = 28599, // Turkish with ISO 2022
            ["ISO 2022 IR 166"] = 874,   // Thai with ISO 2022
        };

#if NET8_0_OR_GREATER
        TermToCodePage = mappings.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
#else
        TermToCodePage = mappings;
#endif

        // Build reverse mapping (code page to term)
        CodePageToTerm = new Dictionary<int, string>
        {
            [20127] = "ISO_IR 6",
            [28591] = "ISO_IR 100",
            [28592] = "ISO_IR 101",
            [28593] = "ISO_IR 109",
            [28594] = "ISO_IR 110",
            [28595] = "ISO_IR 144",
            [28596] = "ISO_IR 127",
            [28597] = "ISO_IR 126",
            [28598] = "ISO_IR 138",
            [28599] = "ISO_IR 148",
            [874] = "ISO_IR 166",
            [65001] = "ISO_IR 192",
            [54936] = "GB18030",
            [936] = "GBK",
            [50220] = "ISO 2022 IR 87",
            [50222] = "ISO 2022 IR 13",
            [50225] = "ISO 2022 IR 149",
            [50227] = "ISO 2022 IR 58",
        };

        CustomTerms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the .NET Encoding corresponding to a DICOM character set term.
    /// </summary>
    /// <param name="dicomTerm">The DICOM character set term (e.g., "ISO_IR 100").</param>
    /// <returns>The corresponding .NET Encoding.</returns>
    /// <exception cref="ArgumentException">Thrown when the term is not recognized.</exception>
    public static Encoding GetEncoding(string dicomTerm)
    {
        if (dicomTerm == null)
            throw new ArgumentNullException(nameof(dicomTerm));

        // Normalize the term
        var normalized = NormalizeTerm(dicomTerm);

        // Check custom terms first (allow override)
        if (CustomTerms.TryGetValue(normalized, out var customCodePage))
            return Encoding.GetEncoding(customCodePage);

        // Check standard terms
        if (TermToCodePage.TryGetValue(normalized, out var codePage))
            return Encoding.GetEncoding(codePage);

        throw new ArgumentException($"Unknown DICOM character set term: {dicomTerm}", nameof(dicomTerm));
    }

    /// <summary>
    /// Get the DICOM character set term for a .NET Encoding.
    /// </summary>
    /// <param name="encoding">The .NET Encoding.</param>
    /// <returns>The DICOM character set term, or null if not found.</returns>
    public static string? GetDicomTerm(Encoding encoding)
    {
        if (encoding == null)
            throw new ArgumentNullException(nameof(encoding));

        return CodePageToTerm.TryGetValue(encoding.CodePage, out var term) ? term : null;
    }

    /// <summary>
    /// Register a custom DICOM character set term for vendor-specific encodings.
    /// </summary>
    /// <param name="dicomTerm">The DICOM character set term.</param>
    /// <param name="codePage">The .NET code page number.</param>
    public static void Register(string dicomTerm, int codePage)
    {
        if (string.IsNullOrWhiteSpace(dicomTerm))
            throw new ArgumentException("Term cannot be null or whitespace", nameof(dicomTerm));

        var normalized = NormalizeTerm(dicomTerm);
        CustomTerms[normalized] = codePage;
    }

    /// <summary>
    /// Normalize a DICOM character set term to standard format.
    /// </summary>
    private static string NormalizeTerm(string term)
    {
        // Trim whitespace
        term = term.Trim();

        // Replace common variants with standard format
        term = term.Replace("ISO IR ", "ISO_IR ");
        term = term.Replace("ISO-IR ", "ISO_IR ");

        return term;
    }
}
