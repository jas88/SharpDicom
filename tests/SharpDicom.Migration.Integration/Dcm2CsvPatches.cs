// Dcm2CsvPatches.cs
//
// This file contains the dcm2csv Entry class extracted from the dcm2csv project
// (https://github.com/jas88/dcm2csv), compiled against SharpDicom.FoDicom5.Compat
// instead of the fo-dicom NuGet package.
//
// PURPOSE: Proves the compat layer is a drop-in replacement for fo-dicom's
// FellowOakDicom namespace for file-I/O-only projects.
//
// PATCHES APPLIED:
// 1. Extracted Entry class from top-level Program.cs (cannot link top-level statements
//    into a library project). The class logic is identical to dcm2csv source.
// 2. Changed visibility from 'internal sealed' to 'public sealed' so tests can access it.
// 3. No other changes - all fo-dicom API usage (DicomFile.Open, DicomItem pattern matching,
//    DicomStringElement.Get<string>, DicomSequence.Items, DicomAttributeTag.Values,
//    DicomTag.DictionaryEntry.Name) compiles unmodified against compat layer.

using System.Collections.Generic;
using System.Linq;
using FellowOakDicom;

namespace SharpDicom.Migration.Integration;

/// <summary>
/// Extracted from dcm2csv Program.cs - represents a single CSV output row.
/// Uses fo-dicom API surface via SharpDicom.FoDicom5.Compat.
/// </summary>
public sealed class Entry
{
    public string Id { get; }
    public string Name { get; }
    public string Value { get; }

    internal Entry(string id, string name, string value)
    {
        Id = id;
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Processes a DICOM item into CSV entries.
    /// This is the exact logic from dcm2csv, exercising:
    /// - Pattern matching on DicomAttributeTag, DicomStringElement, DicomSequence
    /// - DicomAttributeTag.Values (DicomTag[])
    /// - DicomTag.DictionaryEntry.Name
    /// - DicomStringElement.Count and Get&lt;string&gt;(index)
    /// - DicomSequence.Items (IReadOnlyList&lt;DicomDataset&gt;)
    /// - DicomItem.Tag and DicomItem.ToString()
    /// </summary>
    public static IEnumerable<Entry> ProcessTag(string id, DicomItem item)
    {
        return item switch
        {
            DicomAttributeTag aTag => aTag.Values.Select(v => new Entry(id, aTag.Tag.DictionaryEntry.Name, v.DictionaryEntry.Name)),
            DicomStringElement s => StringEntries(id, s.Tag.DictionaryEntry.Name, s),
            DicomSequence seq => seq.Items.SelectMany(ds => ds.SelectMany(i => ProcessTag(id, i))),
            _ => [new Entry(id, item.Tag.DictionaryEntry.Name, item.ToString())]
        };
    }

    private static IEnumerable<Entry> StringEntries(string id, string tag, DicomStringElement e)
    {
        for (var i = 0; i < e.Count; i++)
            yield return new Entry(id, tag, e.Get<string>(i));
    }
}
