using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Deidentification;

/// <summary>
/// Tracks UID mappings and date offsets for consistent de-identification across a study or batch.
/// </summary>
/// <remarks>
/// <para>
/// This class maintains state during de-identification to ensure:
/// </para>
/// <list type="bullet">
/// <item>UIDs are remapped consistently (same original UID always maps to same new UID)</item>
/// <item>Date shifts are applied consistently per patient or study</item>
/// <item>Referential integrity is preserved across multiple files</item>
/// </list>
/// <para>
/// Thread-safe for concurrent multi-file processing. Use <see cref="SaveAsync"/> and
/// <see cref="LoadAsync"/> to persist state between sessions when processing large batches.
/// </para>
/// </remarks>
public sealed class DeidentificationContext : IDisposable
{
    private readonly ConcurrentDictionary<DicomUID, DicomUID> _uidMap = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _patientDateOffsets = new();
    private readonly ConcurrentDictionary<DicomUID, TimeSpan> _studyDateOffsets = new();
    private readonly string _uidPrefix;
    private readonly (int Min, int Max) _dateShiftRange;
    private readonly DateShiftStrategy _strategy;
    private readonly Random _random;
    private readonly object _randomLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DeidentificationContext"/> class.
    /// </summary>
    /// <param name="options">The de-identification options.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public DeidentificationContext(DeidentificationOptions options)
    {
#if NETSTANDARD2_0
        if (options == null)
            throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(options);
#endif

        _uidPrefix = options.UidPrefix;
        _dateShiftRange = options.DateShiftRange;
        _strategy = options.DateShiftStrategy;
        _random = new Random();
    }

    private DeidentificationContext(
        string uidPrefix,
        (int Min, int Max) dateShiftRange,
        DateShiftStrategy strategy)
    {
        _uidPrefix = uidPrefix;
        _dateShiftRange = dateShiftRange;
        _strategy = strategy;
        _random = new Random();
    }

    /// <summary>
    /// Gets the UID prefix used for generating new UIDs.
    /// </summary>
    public string UidPrefix => _uidPrefix;

    /// <summary>
    /// Gets the date shift strategy being used.
    /// </summary>
    public DateShiftStrategy DateShiftStrategy => _strategy;

    /// <summary>
    /// Gets the number of UID mappings currently tracked.
    /// </summary>
    public int UidMappingCount => _uidMap.Count;

    /// <summary>
    /// Gets or creates a remapped UID for the original UID.
    /// </summary>
    /// <param name="original">The original UID to remap.</param>
    /// <returns>
    /// A new UID that consistently maps to the original.
    /// The same original UID will always return the same remapped UID.
    /// </returns>
    public DicomUID RemapUID(DicomUID original)
    {
        return _uidMap.GetOrAdd(original, _ => DicomUID.Generate());
    }

    /// <summary>
    /// Checks if a UID has already been remapped.
    /// </summary>
    /// <param name="original">The original UID to check.</param>
    /// <returns>true if the UID has been remapped; otherwise, false.</returns>
    public bool HasUidMapping(DicomUID original)
    {
        return _uidMap.ContainsKey(original);
    }

    /// <summary>
    /// Gets the remapped UID if it exists.
    /// </summary>
    /// <param name="original">The original UID.</param>
    /// <param name="remapped">When this method returns, contains the remapped UID if found.</param>
    /// <returns>true if a mapping exists; otherwise, false.</returns>
    public bool TryGetRemappedUID(DicomUID original, out DicomUID remapped)
    {
        return _uidMap.TryGetValue(original, out remapped);
    }

    /// <summary>
    /// Gets the date offset for a patient (creates if not exists).
    /// </summary>
    /// <param name="patientId">The patient ID to get/create an offset for.</param>
    /// <returns>The date offset to apply for this patient.</returns>
    public TimeSpan GetDateOffset(string patientId)
    {
        return _patientDateOffsets.GetOrAdd(patientId ?? string.Empty, _ => CreateRandomOffset());
    }

    /// <summary>
    /// Gets the date offset for a study (creates if not exists).
    /// </summary>
    /// <param name="studyInstanceUid">The Study Instance UID to get/create an offset for.</param>
    /// <returns>The date offset to apply for this study.</returns>
    public TimeSpan GetStudyDateOffset(DicomUID studyInstanceUid)
    {
        return _studyDateOffsets.GetOrAdd(studyInstanceUid, _ => CreateRandomOffset());
    }

    /// <summary>
    /// Creates a new random date offset within the configured range.
    /// </summary>
    /// <returns>A TimeSpan representing the date shift in days.</returns>
    /// <remarks>
    /// This is called by PerElement date shift strategy for each date element.
    /// </remarks>
    public TimeSpan CreateRandomOffset()
    {
        int days;
        lock (_randomLock)
        {
            days = _random.Next(_dateShiftRange.Min, _dateShiftRange.Max + 1);
        }
        return TimeSpan.FromDays(days);
    }

    /// <summary>
    /// Gets the appropriate date offset based on the configured strategy.
    /// </summary>
    /// <param name="patientId">The patient ID (used for PerPatient strategy).</param>
    /// <param name="studyInstanceUid">The Study Instance UID (used for PerStudy strategy).</param>
    /// <returns>The date offset to apply.</returns>
    public TimeSpan GetDateOffsetForStrategy(string? patientId, DicomUID studyInstanceUid)
    {
        return _strategy switch
        {
            DateShiftStrategy.PerPatient => GetDateOffset(patientId ?? string.Empty),
            DateShiftStrategy.PerStudy => GetStudyDateOffset(studyInstanceUid),
            DateShiftStrategy.PerElement => CreateRandomOffset(),
            _ => GetDateOffset(patientId ?? string.Empty)
        };
    }

    /// <summary>
    /// Gets all UID mappings (for serialization, audit, or debug purposes).
    /// </summary>
    /// <returns>A read-only dictionary of original to remapped UIDs as strings.</returns>
    public IReadOnlyDictionary<string, string> GetUidMappings()
    {
        return _uidMap.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => kvp.Value.ToString());
    }

    /// <summary>
    /// Gets all patient date offsets (for serialization or audit purposes).
    /// </summary>
    /// <returns>A read-only dictionary of patient ID to offset days.</returns>
    public IReadOnlyDictionary<string, double> GetPatientDateOffsets()
    {
        return _patientDateOffsets.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.TotalDays);
    }

    /// <summary>
    /// Gets all study date offsets (for serialization or audit purposes).
    /// </summary>
    /// <returns>A read-only dictionary of Study Instance UID to offset days.</returns>
    public IReadOnlyDictionary<string, double> GetStudyDateOffsets()
    {
        return _studyDateOffsets.ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => kvp.Value.TotalDays);
    }

    /// <summary>
    /// Saves the context state to a stream for persistence between sessions.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task SaveAsync(Stream stream, CancellationToken ct = default)
    {
        var data = new ContextData
        {
            UidPrefix = _uidPrefix,
            DateShiftRangeMin = _dateShiftRange.Min,
            DateShiftRangeMax = _dateShiftRange.Max,
            Strategy = _strategy.ToString(),
            UidMappings = _uidMap.ToDictionary(k => k.Key.ToString(), v => v.Value.ToString()),
            PatientOffsets = _patientDateOffsets.ToDictionary(k => k.Key, v => v.Value.TotalDays),
            StudyOffsets = _studyDateOffsets.ToDictionary(k => k.Key.ToString(), v => v.Value.TotalDays)
        };

        await JsonSerializer.SerializeAsync(stream, data, ContextDataJsonContext.Default.ContextData, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a context from a stream, restoring previous state.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="options">The de-identification options (for settings not persisted).</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A new context with the loaded state.</returns>
    public static async Task<DeidentificationContext> LoadAsync(
        Stream stream,
        DeidentificationOptions options,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (options == null)
            throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(options);
#endif

        var context = new DeidentificationContext(options);

        var data = await JsonSerializer.DeserializeAsync(stream, ContextDataJsonContext.Default.ContextData, ct)
            .ConfigureAwait(false);

        if (data != null)
        {
            if (data.UidMappings != null)
            {
                foreach (var kvp in data.UidMappings)
                {
                    context._uidMap[new DicomUID(kvp.Key)] = new DicomUID(kvp.Value);
                }
            }

            if (data.PatientOffsets != null)
            {
                foreach (var kvp in data.PatientOffsets)
                {
                    context._patientDateOffsets[kvp.Key] = TimeSpan.FromDays(kvp.Value);
                }
            }

            if (data.StudyOffsets != null)
            {
                foreach (var kvp in data.StudyOffsets)
                {
                    context._studyDateOffsets[new DicomUID(kvp.Key)] = TimeSpan.FromDays(kvp.Value);
                }
            }
        }

        return context;
    }

    /// <summary>
    /// Clears all stored mappings and offsets.
    /// </summary>
    /// <remarks>
    /// Use with caution - clearing during batch processing will break referential integrity.
    /// </remarks>
    public void Clear()
    {
        _uidMap.Clear();
        _patientDateOffsets.Clear();
        _studyDateOffsets.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources, but IDisposable allows using statement pattern
        // for scoped context lifetime
    }
}

/// <summary>
/// Internal data class for JSON serialization of context state.
/// </summary>
internal sealed class ContextData
{
    /// <summary>The UID prefix used for generating new UIDs.</summary>
    public string? UidPrefix { get; set; }

    /// <summary>Minimum days for date shift range.</summary>
    public int DateShiftRangeMin { get; set; }

    /// <summary>Maximum days for date shift range.</summary>
    public int DateShiftRangeMax { get; set; }

    /// <summary>The date shift strategy name.</summary>
    public string? Strategy { get; set; }

    /// <summary>Dictionary of original UID to remapped UID.</summary>
    public Dictionary<string, string>? UidMappings { get; set; }

    /// <summary>Dictionary of patient ID to date offset in days.</summary>
    public Dictionary<string, double>? PatientOffsets { get; set; }

    /// <summary>Dictionary of Study Instance UID to date offset in days.</summary>
    public Dictionary<string, double>? StudyOffsets { get; set; }
}

/// <summary>
/// Source-generated JSON serializer context for AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ContextData))]
internal sealed partial class ContextDataJsonContext : JsonSerializerContext
{
}
