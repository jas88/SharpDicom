using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpDicom.Data;

/// <summary>
/// Represents DICOM character encoding (Specific Character Set).
/// Provides mapping from DICOM character set terms to .NET Encoding instances,
/// with support for multi-valued character sets (ISO 2022 extensions) and
/// UTF-8 zero-copy optimization.
/// </summary>
public sealed class DicomEncoding
{
    /// <summary>
    /// The primary encoding (always present).
    /// For single-valued Specific Character Set, this is the only encoding.
    /// For multi-valued (ISO 2022), this is the first value (default encoding).
    /// </summary>
    public Encoding Primary { get; }

    /// <summary>
    /// Extension encodings for ISO 2022 code extensions.
    /// Null for non-ISO 2022 encodings.
    /// When present, contains additional character sets that can be switched to via escape sequences.
    /// </summary>
    public IReadOnlyList<Encoding>? Extensions { get; }

    /// <summary>
    /// True if this encoding is UTF-8 or ASCII compatible, enabling zero-copy string access.
    /// UTF-8 (code page 65001) and ASCII (code page 20127) are both UTF-8 compatible
    /// for the ASCII subset (0x00-0x7F).
    /// </summary>
    public bool IsUtf8Compatible => Primary.CodePage is 65001 or 20127;

    /// <summary>
    /// True if this encoding has ISO 2022 code extensions (multi-valued Specific Character Set).
    /// </summary>
    public bool HasExtensions => Extensions is { Count: > 0 };

    /// <summary>
    /// Default encoding (ASCII, code page 20127).
    /// Used when Specific Character Set (0008,0005) is absent or empty.
    /// </summary>
    public static readonly DicomEncoding Default = new(Encoding.GetEncoding(20127));

    /// <summary>
    /// UTF-8 encoding (code page 65001).
    /// Recommended for modern DICOM files.
    /// </summary>
    public static readonly DicomEncoding Utf8 = new(Encoding.UTF8);

    /// <summary>
    /// Latin-1 encoding (ISO-8859-1, code page 28591).
    /// Most common non-ASCII encoding in legacy DICOM files.
    /// </summary>
    public static readonly DicomEncoding Latin1 = new(Encoding.GetEncoding(28591));

    private DicomEncoding(Encoding encoding, IReadOnlyList<Encoding>? extensions = null)
    {
        Primary = encoding ?? throw new ArgumentNullException(nameof(encoding));
        Extensions = extensions;
    }

    /// <summary>
    /// Create a DicomEncoding from a .NET Encoding.
    /// </summary>
    /// <param name="encoding">The .NET Encoding to wrap.</param>
    /// <returns>A DicomEncoding wrapping the provided encoding.</returns>
    public static DicomEncoding FromEncoding(Encoding encoding) => new(encoding);

    /// <summary>
    /// Parse a single-valued Specific Character Set (0008,0005) into a DicomEncoding.
    /// </summary>
    /// <param name="value">The Specific Character Set value (e.g., "ISO_IR 100").</param>
    /// <returns>The corresponding DicomEncoding.</returns>
    /// <exception cref="ArgumentException">Thrown if the character set term is not recognized.</exception>
    /// <remarks>
    /// If the value is null, empty, or whitespace, returns the Default encoding (ASCII).
    /// Multi-valued character sets should use the overload accepting string[].
    /// </remarks>
    public static DicomEncoding FromSpecificCharacterSet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Default;

        var encoding = DicomCharacterSets.GetEncoding(value!);
        return new DicomEncoding(encoding);
    }

    /// <summary>
    /// Parse a multi-valued Specific Character Set (0008,0005) into a DicomEncoding.
    /// </summary>
    /// <param name="values">The Specific Character Set values. First value is the primary encoding,
    /// remaining values are ISO 2022 code extensions.</param>
    /// <returns>The corresponding DicomEncoding.</returns>
    /// <exception cref="ArgumentException">Thrown if any character set term is not recognized,
    /// or if UTF-8/GB18030/GBK appears with multiple values (prohibited by DICOM).</exception>
    /// <remarks>
    /// If the array is null or empty, returns the Default encoding (ASCII).
    /// For single-valued arrays, equivalent to calling the single-value overload.
    /// For multi-valued arrays, the first value becomes the Primary encoding,
    /// and remaining values become Extensions (for ISO 2022 escape sequences).
    /// UTF-8, GB18030, and GBK prohibit code extensions and must be single-valued.
    /// </remarks>
    public static DicomEncoding FromSpecificCharacterSet(string[]? values)
    {
        if (values == null || values.Length == 0)
            return Default;

        // Single value - simple case
        if (values.Length == 1)
            return FromSpecificCharacterSet(values[0]);

        // Multi-valued - parse primary and extensions
        var primary = DicomCharacterSets.GetEncoding(values[0]);

        // UTF-8, GB18030, and GBK prohibit code extensions
        if (primary.CodePage is 65001 or 54936 or 936)
        {
            throw new ArgumentException(
                $"Character set {values[0]} does not support code extensions (ISO 2022). " +
                "It must be the only value in Specific Character Set.",
                nameof(values));
        }

        // Parse extension encodings
        var extensions = new List<Encoding>(values.Length - 1);
        for (int i = 1; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
            {
                var ext = DicomCharacterSets.GetEncoding(values[i]);
                extensions.Add(ext);
            }
        }

        return new DicomEncoding(primary, extensions.Count > 0 ? extensions : null);
    }

    /// <summary>
    /// Try to get UTF-8 bytes without transcoding.
    /// Returns true if the encoding is UTF-8 or ASCII compatible, allowing zero-copy access.
    /// Returns false if transcoding is required.
    /// </summary>
    /// <param name="bytes">The raw byte data.</param>
    /// <param name="utf8">The UTF-8 bytes (same as input if compatible, otherwise default).</param>
    /// <returns>True if UTF-8 compatible (zero-copy), false if transcoding required.</returns>
    public bool TryGetUtf8(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> utf8)
    {
        if (IsUtf8Compatible)
        {
            utf8 = bytes;
            return true;
        }

        utf8 = default;
        return false;
    }

    /// <summary>
    /// Decode a byte sequence to a string using this encoding.
    /// </summary>
    /// <param name="bytes">The raw byte data to decode.</param>
    /// <returns>The decoded string.</returns>
    /// <remarks>
    /// For ISO 2022 encodings with extensions, .NET's ISO2022Encoding class
    /// automatically handles escape sequences during decoding.
    /// No custom escape sequence parsing is needed - the .NET Encoding classes
    /// (code pages 50220-50227) handle this internally.
    /// </remarks>
    public string GetString(ReadOnlySpan<byte> bytes)
    {
#if NETSTANDARD2_0
        // Span overload not available on netstandard2.0
        return Primary.GetString(bytes.ToArray());
#else
        return Primary.GetString(bytes);
#endif
    }
}
