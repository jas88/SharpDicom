using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Data;

/// <summary>
/// Collection of DICOM elements with O(1) lookup and sorted enumeration.
/// </summary>
public sealed partial class DicomDataset : IEnumerable<IDicomElement>
{
    private readonly Dictionary<DicomTag, IDicomElement> _elements = new();
    private readonly PrivateCreatorDictionary _privateCreators = new();
    private IDicomElement[]? _sortedCache;
    private bool _isDirty = true;

    // Cached context values for VR resolution (multi-VR tags like Pixel Data, US/SS tags)
    private ushort? _bitsAllocated;
    private ushort? _pixelRepresentation;

    // Cached character encoding
    private DicomEncoding _localEncoding = DicomEncoding.Default;

    /// <summary>
    /// Gets the parent dataset for sequence items.
    /// </summary>
    /// <remarks>
    /// This property is set when a dataset is created as an item within a sequence.
    /// For root-level datasets, this property is null. The parent reference enables
    /// context inheritance for nested sequences (e.g., BitsAllocated, PixelRepresentation).
    /// Note: <see cref="ToOwned"/> creates a copy without a parent reference.
    /// </remarks>
    public DicomDataset? Parent { get; internal set; }

    /// <summary>
    /// Gets the cached BitsAllocated value from tag (0028,0100).
    /// </summary>
    /// <remarks>
    /// Used for context-dependent VR resolution of multi-VR tags like Pixel Data (7FE0,0010).
    /// - OW if BitsAllocated > 8
    /// - OB if BitsAllocated &lt;= 8
    /// Falls back to parent dataset value if not present in this dataset (for nested sequences).
    /// </remarks>
    public ushort? BitsAllocated => _bitsAllocated ?? Parent?.BitsAllocated;

    /// <summary>
    /// Gets the cached PixelRepresentation value from tag (0028,0103).
    /// </summary>
    /// <remarks>
    /// Used for context-dependent VR resolution of US/SS ambiguous tags.
    /// - 0 = unsigned (US)
    /// - 1 = signed (SS)
    /// Falls back to parent dataset value if not present in this dataset (for nested sequences).
    /// </remarks>
    public ushort? PixelRepresentation => _pixelRepresentation ?? Parent?.PixelRepresentation;

    /// <summary>
    /// Gets the character encoding for this dataset.
    /// </summary>
    /// <remarks>
    /// Determined from Specific Character Set (0008,0005).
    /// If not present, inherits from parent dataset (for sequence items).
    /// Returns Default (ASCII) if no encoding is specified at any level.
    /// </remarks>
    public DicomEncoding Encoding =>
        Contains(DicomTag.SpecificCharacterSet)
            ? _localEncoding
            : (Parent?.Encoding ?? DicomEncoding.Default);

    /// <summary>
    /// Create an empty dataset.
    /// </summary>
    public DicomDataset() { }

    /// <summary>
    /// Create an empty dataset with the specified capacity.
    /// </summary>
    /// <param name="capacity">Initial capacity.</param>
    public DicomDataset(int capacity)
    {
        _elements = new Dictionary<DicomTag, IDicomElement>(capacity);
    }

    /// <summary>
    /// Number of elements in the dataset.
    /// </summary>
    public int Count => _elements.Count;

    /// <summary>
    /// Private creator dictionary for tracking private tag creators.
    /// </summary>
    public PrivateCreatorDictionary PrivateCreators => _privateCreators;

    /// <summary>
    /// Get element by tag. Returns null if not found.
    /// </summary>
    public IDicomElement? this[DicomTag tag]
        => _elements.TryGetValue(tag, out var e) ? e : null;

    /// <summary>
    /// Check if the dataset contains an element with the specified tag.
    /// </summary>
    public bool Contains(DicomTag tag) => _elements.ContainsKey(tag);

    /// <summary>
    /// Try to get an element by tag.
    /// </summary>
    public bool TryGetElement(DicomTag tag, out IDicomElement element)
        => _elements.TryGetValue(tag, out element!);

    /// <summary>
    /// Get element by tag cast to specific type.
    /// </summary>
    public T? GetElement<T>(DicomTag tag) where T : class, IDicomElement
        => this[tag] as T;

    /// <summary>
    /// Add or update an element in the dataset.
    /// </summary>
    public void Add(IDicomElement element)
    {
        _elements[element.Tag] = element;
        _isDirty = true;

        // Track private creators
        if (element.Tag.IsPrivateCreator && element is DicomStringElement se)
        {
            _privateCreators.Register(element.Tag, se.GetString() ?? "");
        }

        // Cache context values for VR resolution
        CacheContextValue(element);

        // Cache encoding when Specific Character Set changes
        if (element.Tag == DicomTag.SpecificCharacterSet)
        {
            UpdateEncoding(element);
        }
    }

    /// <summary>
    /// Cache context values (BitsAllocated, PixelRepresentation) for VR resolution.
    /// </summary>
    private void CacheContextValue(IDicomElement element)
    {
        if (element.Tag == DicomTag.BitsAllocated)
        {
            if (element is DicomNumericElement ne)
            {
                // BitsAllocated is US VR (2 bytes), use GetUInt16
                var value = ne.GetUInt16();
                if (value.HasValue)
                {
                    _bitsAllocated = value.Value;
                }
            }
        }
        else if (element.Tag == DicomTag.PixelRepresentation)
        {
            if (element is DicomNumericElement ne)
            {
                // PixelRepresentation is US VR (2 bytes), use GetUInt16
                var value = ne.GetUInt16();
                if (value.HasValue)
                {
                    _pixelRepresentation = value.Value;
                }
            }
        }
    }

    /// <summary>
    /// Update the local encoding based on Specific Character Set element.
    /// </summary>
    private void UpdateEncoding(IDicomElement element)
    {
        if (element is DicomStringElement se)
        {
            var values = se.GetStrings(DicomEncoding.Default);
            _localEncoding = values != null && values.Length > 0
                ? DicomEncoding.FromSpecificCharacterSet(values)
                : DicomEncoding.Default;
        }
    }

    /// <summary>
    /// Add or update an element in the dataset.
    /// </summary>
    public void AddOrUpdate(IDicomElement element) => Add(element);

    /// <summary>
    /// Update an existing element (same as Add).
    /// </summary>
    public void Update(IDicomElement element) => Add(element);

    /// <summary>
    /// Remove an element from the dataset.
    /// </summary>
    /// <returns>True if the element was removed, false if not found.</returns>
    public bool Remove(DicomTag tag)
    {
        if (_elements.Remove(tag))
        {
            _isDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove all elements from the dataset.
    /// </summary>
    public void Clear()
    {
        _elements.Clear();
        _privateCreators.Clear();
        _bitsAllocated = null;
        _pixelRepresentation = null;
        _localEncoding = DicomEncoding.Default;
        _isDirty = true;
    }

    /// <summary>
    /// Enumerate elements in sorted tag order.
    /// </summary>
    public IEnumerator<IDicomElement> GetEnumerator()
    {
        if (_isDirty)
        {
            _sortedCache = _elements.Values.OrderBy(e => e.Tag).ToArray();
            _isDirty = false;
        }
        return ((IEnumerable<IDicomElement>)_sortedCache!).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Typed convenience accessors

    /// <summary>
    /// Get string value for the specified tag.
    /// </summary>
    public string? GetString(DicomTag tag, DicomEncoding? encoding = null)
        => (this[tag] as DicomStringElement)?.GetString(encoding ?? Encoding);

    /// <summary>
    /// Get string value or throw if not found.
    /// </summary>
    public string GetStringOrThrow(DicomTag tag, DicomEncoding? encoding = null)
        => (this[tag] as DicomStringElement)?.GetString(encoding ?? Encoding)
           ?? throw new DicomDataException($"Tag {tag} not found or not a string");

    /// <summary>
    /// Get multiple string values (split by backslash).
    /// </summary>
    public string[]? GetStrings(DicomTag tag, DicomEncoding? encoding = null)
        => (this[tag] as DicomStringElement)?.GetStrings(encoding ?? Encoding);

    /// <summary>
    /// Get integer value for the specified tag.
    /// </summary>
    public int? GetInt32(DicomTag tag)
    {
        var element = this[tag];
        if (element is DicomNumericElement ne) return ne.GetInt32();
        if (element is DicomStringElement se) return se.GetInt32();
        return null;
    }

    /// <summary>
    /// Get double value for the specified tag.
    /// </summary>
    public double? GetFloat64(DicomTag tag)
    {
        var element = this[tag];
        if (element is DicomNumericElement ne) return ne.GetFloat64();
        if (element is DicomStringElement se) return se.GetFloat64();
        return null;
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Get date value for the specified tag (DA VR).
    /// </summary>
    public DateOnly? GetDate(DicomTag tag)
        => (this[tag] as DicomStringElement)?.GetDate();

    /// <summary>
    /// Get time value for the specified tag (TM VR).
    /// </summary>
    public TimeOnly? GetTime(DicomTag tag)
        => (this[tag] as DicomStringElement)?.GetTime();
#else
    /// <summary>
    /// Get date value for the specified tag (DA VR).
    /// </summary>
    /// <remarks>
    /// On .NET Standard 2.0, returns DateTime instead of DateOnly.
    /// The time component is always midnight (00:00:00).
    /// </remarks>
    public DateTime? GetDate(DicomTag tag)
        => (this[tag] as DicomStringElement)?.GetDate();

    /// <summary>
    /// Get time value for the specified tag (TM VR).
    /// </summary>
    /// <remarks>
    /// On .NET Standard 2.0, returns TimeSpan instead of TimeOnly.
    /// </remarks>
    public TimeSpan? GetTime(DicomTag tag)
        => (this[tag] as DicomStringElement)?.GetTime();
#endif

    /// <summary>
    /// Get UID value for the specified tag.
    /// </summary>
    public DicomUID? GetUID(DicomTag tag)
    {
        var str = GetString(tag);
        return str != null ? new DicomUID(str.TrimEnd()) : null;
    }

    /// <summary>
    /// Get sequence for the specified tag.
    /// </summary>
    public DicomSequence? GetSequence(DicomTag tag)
        => this[tag] as DicomSequence;

    /// <summary>
    /// Get pixel data element from the dataset.
    /// </summary>
    /// <returns>The pixel data element, or null if not present.</returns>
    /// <remarks>
    /// Returns the element at tag (7FE0,0010) as a <see cref="DicomPixelDataElement"/>.
    /// The pixel data may be native (uncompressed) or encapsulated (compressed).
    /// Use <see cref="DicomPixelDataElement.LoadAsync"/> to retrieve the actual pixel bytes.
    /// For encapsulated data, use <see cref="DicomPixelDataElement.Fragments"/> to access individual fragments.
    /// </remarks>
    public DicomPixelDataElement? GetPixelData()
        => this[DicomTag.PixelData] as DicomPixelDataElement;

    /// <summary>
    /// Gets a value indicating whether this dataset contains pixel data.
    /// </summary>
    public bool HasPixelData => Contains(DicomTag.PixelData);

    /// <summary>
    /// Create a deep copy of this dataset.
    /// </summary>
    /// <remarks>
    /// The copy is independent and does not have a parent reference.
    /// Cached context values (BitsAllocated, PixelRepresentation, Encoding) are copied.
    /// </remarks>
    public DicomDataset ToOwned()
    {
        var copy = new DicomDataset(_elements.Count);
        foreach (var element in _elements.Values)
        {
            copy.Add(element.ToOwned());
        }
        // Context values are cached during Add(), so they will be populated automatically
        // But in case they were set directly on the source, copy them explicitly
        copy._bitsAllocated = _bitsAllocated;
        copy._pixelRepresentation = _pixelRepresentation;
        copy._localEncoding = _localEncoding;
        return copy;
    }

    /// <summary>
    /// Add an element and return this dataset (fluent API).
    /// </summary>
    public DicomDataset WithElement(IDicomElement element)
    {
        Add(element);
        return this;
    }
}
