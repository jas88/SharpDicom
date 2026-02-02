using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Deidentification;

/// <summary>
/// Applies DICOM de-identification according to PS3.15 profiles.
/// </summary>
/// <remarks>
/// <para>
/// This is the main de-identification engine that orchestrates action lookup,
/// UID remapping, date shifting, and sequence traversal. It uses the generated
/// <see cref="DeidentificationActionTable"/> for profile-based action lookup.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var options = new DeidentificationOptions
/// {
///     Profile = DeidentificationProfile.Basic,
///     DateShiftStrategy = DateShiftStrategy.PerPatient
/// };
/// var deidentifier = new DicomDeidentifier(options);
/// await deidentifier.ApplyAsync(dataset);
/// </code>
/// </para>
/// </remarks>
public sealed partial class DicomDeidentifier
{
    // Patient's Age (0010,1010) - AS VR
    private static readonly DicomTag PatientAgeTag = new DicomTag(0x0010, 0x1010);

    private readonly DeidentificationOptions _options;
    private readonly DeidentificationContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomDeidentifier"/> class.
    /// </summary>
    /// <param name="options">The de-identification options.</param>
    /// <param name="context">Optional context for UID/date mapping. If null, a new context is created.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public DicomDeidentifier(DeidentificationOptions options, DeidentificationContext? context = null)
    {
#if NETSTANDARD2_0
        if (options == null)
            throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(options);
#endif
        _options = options;
        _context = context ?? new DeidentificationContext(options);
    }

    /// <summary>
    /// Gets the context for UID/date mapping access.
    /// </summary>
    /// <remarks>
    /// Access the context to retrieve UID mappings, date offsets, or persist
    /// context state for batch processing across multiple sessions.
    /// </remarks>
    public DeidentificationContext Context => _context;

    /// <summary>
    /// Applies de-identification to the dataset in-place.
    /// </summary>
    /// <param name="dataset">The dataset to de-identify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when de-identification is done.</returns>
    /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
    public ValueTask ApplyAsync(DicomDataset dataset, CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (dataset == null)
            throw new ArgumentNullException(nameof(dataset));
#else
        ArgumentNullException.ThrowIfNull(dataset);
#endif

        ApplyCore(dataset, ct);
#if NETSTANDARD2_0
        return default;
#else
        return ValueTask.CompletedTask;
#endif
    }

    /// <summary>
    /// Core de-identification logic applied synchronously.
    /// </summary>
    private void ApplyCore(DicomDataset dataset, CancellationToken ct)
    {
        // Get patient/study identifiers for date offset lookup
        var patientId = GetStringValue(dataset, DicomTag.PatientID) ?? "";
        var studyUid = GetUidValue(dataset, DicomTag.StudyInstanceUID);

        // CRITICAL: Retrieve date offset from context based on strategy
        // This ensures consistent date shifting across all files in a study/patient
        var dateOffset = _options.DateShiftStrategy switch
        {
            // Per-patient: Same offset for all studies of same patient
            DateShiftStrategy.PerPatient => _context.GetDateOffset(patientId),
            // Per-study: Same offset for all files in same study
            DateShiftStrategy.PerStudy when studyUid != default =>
                _context.GetStudyDateOffset(studyUid),
            // Per-element or fallback: No consistent shifting
            _ => TimeSpan.Zero
        };

        // Track dates for PatientAge recalculation
#if NET6_0_OR_GREATER
        DateOnly? birthDate = null;
        DateOnly? studyDate = null;
#else
        DateTime? birthDate = null;
        DateTime? studyDate = null;
#endif

        // Collect elements to modify (can't modify during enumeration)
        var modifications = new List<(DicomTag Tag, IDicomElement? NewValue)>();

        foreach (var element in dataset)
        {
            ct.ThrowIfCancellationRequested();

            // Apply custom rules first
            if (TryApplyCustomRule(element, out var customResult))
            {
                modifications.Add((element.Tag, customResult));
                continue;
            }

            // Get standard action from generated table
            var action = DeidentificationActionTable.GetAction(element.Tag, _options.Profile);

            // Apply action
            var newValue = ApplyAction(element, action, dateOffset);
            if (!ReferenceEquals(newValue, element))
            {
                modifications.Add((element.Tag, newValue));
            }

            // Track birth/study dates for age recalculation
            if (element.Tag == DicomTag.PatientBirthDate && element is DicomStringElement birthEl)
            {
                var shifted = newValue as DicomStringElement ?? birthEl;
                birthDate = DateShifter.ParseDate(shifted.GetString());
            }
            if (element.Tag == DicomTag.StudyDate && element is DicomStringElement studyEl)
            {
                var shifted = newValue as DicomStringElement ?? studyEl;
                studyDate = DateShifter.ParseDate(shifted.GetString());
            }
        }

        // Apply modifications
        foreach (var (tag, newValue) in modifications)
        {
            if (newValue == null)
                dataset.Remove(tag);
            else
                dataset.Add(newValue);
        }

        // Recalculate PatientAge if enabled
        if (_options.RecalculatePatientAge && birthDate != null && studyDate != null)
        {
            var age = DateShifter.CalculateAge(birthDate, studyDate);
            if (age != null)
            {
                dataset.Add(CreateStringElement(PatientAgeTag, DicomVR.AS, age));
            }
        }

        // Remove private tags if configured
        if (_options.RemovePrivateTags)
        {
            Func<string, bool>? filter = null;
            if (_options.SafePrivateCreators != null)
            {
                var safeCreators = _options.SafePrivateCreators;
                filter = c => safeCreators.Contains(c);
            }
            dataset.StripPrivateTags(filter);
        }
    }

    private IDicomElement? ApplyAction(IDicomElement element, DeidentificationAction action,
        TimeSpan dateOffset)
    {
        return action switch
        {
            DeidentificationAction.Remove => null,
            DeidentificationAction.Zero => CreateZeroLength(element),
            DeidentificationAction.Dummy => CreateDummy(element),
            DeidentificationAction.UidRemap => RemapUid(element),
            DeidentificationAction.Clean => CleanElement(element, dateOffset),
            DeidentificationAction.Keep => ProcessKeep(element, dateOffset),
            _ => element
        };
    }

    private static IDicomElement CreateZeroLength(IDicomElement element)
    {
        // Create element with empty value
        var vrInfo = DicomVRInfo.GetInfo(element.VR);
        if (vrInfo.IsStringVR)
            return new DicomStringElement(element.Tag, element.VR, Array.Empty<byte>());
        return new DicomBinaryElement(element.Tag, element.VR, Array.Empty<byte>());
    }

    private static IDicomElement CreateDummy(IDicomElement element)
    {
        // Type-1 safe dummy values per VR
        var vr = element.VR;
        if (vr == DicomVR.PN)
            return CreateStringElement(element.Tag, vr, "ANONYMOUS");
        if (vr == DicomVR.LO || vr == DicomVR.SH)
            return CreateStringElement(element.Tag, vr, "REMOVED");
        if (vr == DicomVR.DA)
            return CreateStringElement(element.Tag, vr, "19000101");
        if (vr == DicomVR.TM)
            return CreateStringElement(element.Tag, vr, "000000");
        if (vr == DicomVR.DT)
            return CreateStringElement(element.Tag, vr, "19000101000000");
        if (vr == DicomVR.UI)
            return CreateStringElement(element.Tag, vr, DicomUID.Generate().ToString());
        if (vr == DicomVR.AS)
            return CreateStringElement(element.Tag, vr, "000Y");
        if (vr == DicomVR.CS)
            return CreateStringElement(element.Tag, vr, "UNKNOWN");
        if (vr == DicomVR.IS || vr == DicomVR.DS)
            return CreateStringElement(element.Tag, vr, "0");
        if (vr == DicomVR.LT || vr == DicomVR.ST || vr == DicomVR.UT)
            return CreateStringElement(element.Tag, vr, "");

        // For other VRs, return zero-length
        return CreateZeroLength(element);
    }

    private IDicomElement? RemapUid(IDicomElement element)
    {
        if (element is not DicomStringElement se)
            return element;

        var original = se.GetString();
        if (string.IsNullOrEmpty(original))
            return element;

        var originalUid = new DicomUID(original!);
        var newUid = _context.RemapUID(originalUid);
        return CreateStringElement(element.Tag, element.VR, newUid.ToString());
    }

    private IDicomElement CleanElement(IDicomElement element, TimeSpan dateOffset)
    {
        // Clean action varies by VR - dates get shifted, text gets dummy
        var vr = element.VR;
        if (vr == DicomVR.DA || vr == DicomVR.TM || vr == DicomVR.DT)
        {
            return DateShifter.Shift(element, dateOffset, _options.ZeroTimeComponents);
        }
        return CreateDummy(element);
    }

    private IDicomElement ProcessKeep(IDicomElement element, TimeSpan dateOffset)
    {
        // Keep the element but process nested sequences
        if (element is DicomSequence seq)
        {
            foreach (var item in seq.Items)
            {
                // Recursively apply de-identification to sequence items
                ApplyCore(item, default);
            }
        }
        return element;
    }

    private bool TryApplyCustomRule(IDicomElement element, out IDicomElement? result)
    {
        result = null;
        if (_options.CustomRules == null)
            return false;

        foreach (var rule in _options.CustomRules)
        {
            if (rule.AppliesTo(element.Tag))
            {
                result = rule.Transform(element, _context);
                return true;
            }
        }
        return false;
    }

    private static string? GetStringValue(DicomDataset dataset, DicomTag tag)
    {
        return dataset[tag] is DicomStringElement se ? se.GetString() : null;
    }

    private static DicomUID GetUidValue(DicomDataset dataset, DicomTag tag)
    {
        var str = GetStringValue(dataset, tag);
        return string.IsNullOrEmpty(str) ? default : new DicomUID(str!);
    }

    /// <summary>
    /// Creates a string element with properly padded value.
    /// </summary>
    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        // Pad to even length if necessary
        if (bytes.Length % 2 != 0)
        {
            var padded = new byte[bytes.Length + 1];
            bytes.CopyTo(padded, 0);
            padded[padded.Length - 1] = DicomVRInfo.GetInfo(vr).PaddingByte;
            bytes = padded;
        }
        return new DicomStringElement(tag, vr, bytes);
    }
}
