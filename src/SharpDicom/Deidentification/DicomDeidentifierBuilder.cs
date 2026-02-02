using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification;

/// <summary>
/// Fluent builder for configuring and creating <see cref="DicomDeidentifier"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Use this builder to configure de-identification options with a fluent, discoverable API.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var deidentifier = DicomDeidentifier.Create()
///     .WithProfile(DeidentificationProfile.Basic)
///     .WithDateShift(-365, 365)
///     .WithDateStrategy(DateShiftStrategy.PerPatient)
///     .WithZeroTime()
///     .Build();
///
/// await deidentifier.ApplyAsync(dataset);
/// </code>
/// </para>
/// </remarks>
public sealed class DicomDeidentifierBuilder
{
    private DeidentificationProfile _profile = DeidentificationProfile.Basic;
    private DateShiftStrategy _dateStrategy = DateShiftStrategy.PerPatient;
    private (int Min, int Max) _dateRange = (-365, 365);
    private bool _zeroTime = true;
    private bool _recalcAge = true;
    private string _uidPrefix = "2.25";
    private bool _removePrivate = true;
    private List<string>? _safePrivate;
    private List<IDeidentificationRule>? _customRules;
    private PixelCleaningOptions _pixelOptions = new();
    private DeidentificationContext? _context;

    private DicomDeidentifierBuilder() { }

    /// <summary>
    /// Creates a new builder instance.
    /// </summary>
    /// <returns>A new builder.</returns>
    public static DicomDeidentifierBuilder Create() => new();

    /// <summary>
    /// Sets the base profile (default: Basic).
    /// </summary>
    /// <param name="profile">The de-identification profile to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithProfile(DeidentificationProfile profile)
    {
        _profile = profile;
        return this;
    }

    /// <summary>
    /// Adds an option profile to the base profile using bitwise OR.
    /// </summary>
    /// <param name="option">The option to add.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithOption(DeidentificationProfile option)
    {
        _profile |= option;
        return this;
    }

    /// <summary>
    /// Configures date shifting range in days (default: -365 to +365).
    /// </summary>
    /// <param name="minDays">Minimum days to shift (can be negative).</param>
    /// <param name="maxDays">Maximum days to shift (can be negative).</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithDateShift(int minDays, int maxDays)
    {
        _dateRange = (minDays, maxDays);
        return this;
    }

    /// <summary>
    /// Configures date shift strategy (default: PerPatient).
    /// </summary>
    /// <param name="strategy">The date shift strategy to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithDateStrategy(DateShiftStrategy strategy)
    {
        _dateStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Controls whether time components are zeroed (default: true).
    /// </summary>
    /// <param name="zero">True to zero time components; false to preserve them.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithZeroTime(bool zero = true)
    {
        _zeroTime = zero;
        return this;
    }

    /// <summary>
    /// Controls PatientAge recalculation (default: true).
    /// </summary>
    /// <param name="recalc">True to recalculate PatientAge from shifted dates.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithRecalculateAge(bool recalc = true)
    {
        _recalcAge = recalc;
        return this;
    }

    /// <summary>
    /// Sets UID prefix for generated UIDs (default: 2.25).
    /// </summary>
    /// <param name="prefix">The UID prefix. "2.25" allows UUID-based generation without registration.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when prefix is null.</exception>
    public DicomDeidentifierBuilder WithUidPrefix(string prefix)
    {
#if NETSTANDARD2_0
        if (prefix == null)
            throw new ArgumentNullException(nameof(prefix));
#else
        ArgumentNullException.ThrowIfNull(prefix);
#endif
        _uidPrefix = prefix;
        return this;
    }

    /// <summary>
    /// Controls private tag removal (default: true).
    /// </summary>
    /// <param name="remove">True to remove private tags not in safe list.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithRemovePrivateTags(bool remove = true)
    {
        _removePrivate = remove;
        return this;
    }

    /// <summary>
    /// Adds a safe private creator to the whitelist.
    /// </summary>
    /// <param name="creator">The private creator identification string to keep.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// Private tags from safe creators are retained during de-identification.
    /// Only use this for creators you have verified do not contain PHI.
    /// </remarks>
    public DicomDeidentifierBuilder WithSafePrivateCreator(string creator)
    {
        _safePrivate ??= new List<string>();
        _safePrivate.Add(creator);
        return this;
    }

    /// <summary>
    /// Adds multiple safe private creators to the whitelist.
    /// </summary>
    /// <param name="creators">The private creator identification strings to keep.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithSafePrivateCreators(IEnumerable<string> creators)
    {
        _safePrivate ??= new List<string>();
        _safePrivate.AddRange(creators);
        return this;
    }

    /// <summary>
    /// Adds a custom de-identification rule.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// Custom rules are evaluated before the standard profile actions.
    /// Use to override behavior for specific tags.
    /// </remarks>
    public DicomDeidentifierBuilder WithCustomRule(IDeidentificationRule rule)
    {
        _customRules ??= new List<IDeidentificationRule>();
        _customRules.Add(rule);
        return this;
    }

    /// <summary>
    /// Configures pixel cleaning options.
    /// </summary>
    /// <param name="configure">Action to configure pixel cleaning.</param>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithPixelCleaning(Action<PixelCleaningOptions> configure)
    {
        var options = new PixelCleaningOptions();
        configure(options);
        _pixelOptions = options;
        return this;
    }

    /// <summary>
    /// Enables pixel cleaning with default options.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public DicomDeidentifierBuilder WithPixelCleaning()
    {
        _pixelOptions = new PixelCleaningOptions { Enabled = true };
        return this;
    }

    /// <summary>
    /// Uses an existing context for UID/date mapping.
    /// </summary>
    /// <param name="context">The context to use.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// Use this when processing multiple files and need consistent UID/date mappings
    /// across all files. The context can also be loaded from a previous session.
    /// </remarks>
    public DicomDeidentifierBuilder WithContext(DeidentificationContext context)
    {
        _context = context;
        return this;
    }

    /// <summary>
    /// Builds the configured deidentifier.
    /// </summary>
    /// <returns>A new <see cref="DicomDeidentifier"/> with the configured options.</returns>
    public DicomDeidentifier Build()
    {
        var options = new DeidentificationOptions
        {
            Profile = _profile,
            DateShiftStrategy = _dateStrategy,
            DateShiftRange = _dateRange,
            ZeroTimeComponents = _zeroTime,
            RecalculatePatientAge = _recalcAge,
            UidPrefix = _uidPrefix,
            RemovePrivateTags = _removePrivate,
#if NETSTANDARD2_0
            SafePrivateCreators = _safePrivate != null
                ? (IReadOnlyList<string>)_safePrivate.AsReadOnly()
                : null,
            CustomRules = _customRules != null
                ? (IReadOnlyList<IDeidentificationRule>)_customRules.AsReadOnly()
                : null,
#else
            SafePrivateCreators = _safePrivate?.AsReadOnly(),
            CustomRules = _customRules?.AsReadOnly(),
#endif
            PixelCleaning = _pixelOptions
        };

        return new DicomDeidentifier(options, _context);
    }
}

/// <summary>
/// Extension methods for <see cref="DicomDeidentifier"/> to provide the fluent API entry point.
/// </summary>
public partial class DicomDeidentifier
{
    /// <summary>
    /// Creates a new fluent builder for de-identification configuration.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    /// <example>
    /// <code>
    /// var deidentifier = DicomDeidentifier.Create()
    ///     .WithProfile(DeidentificationProfile.Basic)
    ///     .WithDateShift(-365, 365)
    ///     .Build();
    /// </code>
    /// </example>
    public static DicomDeidentifierBuilder Create() => DicomDeidentifierBuilder.Create();
}
