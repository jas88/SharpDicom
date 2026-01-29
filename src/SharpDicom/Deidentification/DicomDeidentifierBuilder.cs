using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Fluent builder for configuring DicomDeidentifier instances.
    /// </summary>
    /// <remarks>
    /// The builder does not own the UidRemapper passed to <see cref="WithUidRemapper"/>.
    /// The caller is responsible for disposing the UidRemapper if needed.
    /// </remarks>
#pragma warning disable CA1001 // Type owns disposable field - by design, builder doesn't own remapper
    public sealed class DicomDeidentifierBuilder
#pragma warning restore CA1001
    {
        private bool _retainSafePrivate;
        private bool _retainUIDs;
        private bool _retainDeviceIdentity;
        private bool _retainInstitutionIdentity;
        private bool _retainPatientCharacteristics;
        private bool _retainLongitudinalFullDates;
        private bool _retainLongitudinalModifiedDates;
        private bool _cleanDescriptors;
        private bool _cleanStructuredContent;
        private bool _cleanGraphics;
        private PrivateTagAction _privateTagAction = PrivateTagAction.Remove;
        private readonly HashSet<string> _safePrivateCreators = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DicomTag, DeidentificationAction> _overrides = new();
        private DateShiftConfig? _dateShiftConfig;
        private UidRemapper? _uidRemapper;

        /// <summary>
        /// Applies the Basic Application Level Confidentiality Profile.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithBasicProfile()
        {
            // Basic profile uses defaults - most restrictive
            return this;
        }

        /// <summary>
        /// Enables the Retain Safe Private Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainSafePrivate()
        {
            _retainSafePrivate = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain UIDs Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainUIDs()
        {
            _retainUIDs = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain Device Identity Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainDeviceIdentity()
        {
            _retainDeviceIdentity = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain Institution Identity Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainInstitutionIdentity()
        {
            _retainInstitutionIdentity = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain Patient Characteristics Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainPatientCharacteristics()
        {
            _retainPatientCharacteristics = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain Longitudinal Temporal Information Full Dates Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainLongitudinalFullDates()
        {
            _retainLongitudinalFullDates = true;
            return this;
        }

        /// <summary>
        /// Enables the Retain Longitudinal Temporal Information Modified Dates Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RetainLongitudinalModifiedDates()
        {
            _retainLongitudinalModifiedDates = true;
            return this;
        }

        /// <summary>
        /// Enables the Clean Descriptors Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder CleanDescriptors()
        {
            _cleanDescriptors = true;
            return this;
        }

        /// <summary>
        /// Enables the Clean Structured Content Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder CleanStructuredContent()
        {
            _cleanStructuredContent = true;
            return this;
        }

        /// <summary>
        /// Enables the Clean Graphics Option.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder CleanGraphics()
        {
            _cleanGraphics = true;
            return this;
        }

        /// <summary>
        /// Applies a profile option.
        /// </summary>
        /// <param name="option">The option to apply.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithOption(DeidentificationProfileOption option)
        {
            if ((option & DeidentificationProfileOption.RetainSafePrivate) != 0)
                _retainSafePrivate = true;
            if ((option & DeidentificationProfileOption.RetainUIDs) != 0)
                _retainUIDs = true;
            if ((option & DeidentificationProfileOption.RetainDeviceIdentity) != 0)
                _retainDeviceIdentity = true;
            if ((option & DeidentificationProfileOption.RetainInstitutionIdentity) != 0)
                _retainInstitutionIdentity = true;
            if ((option & DeidentificationProfileOption.RetainPatientCharacteristics) != 0)
                _retainPatientCharacteristics = true;
            if ((option & DeidentificationProfileOption.RetainLongitudinalFullDates) != 0)
                _retainLongitudinalFullDates = true;
            if ((option & DeidentificationProfileOption.RetainLongitudinalModifiedDates) != 0)
                _retainLongitudinalModifiedDates = true;
            if ((option & DeidentificationProfileOption.CleanDescriptors) != 0)
                _cleanDescriptors = true;
            if ((option & DeidentificationProfileOption.CleanStructuredContent) != 0)
                _cleanStructuredContent = true;
            if ((option & DeidentificationProfileOption.CleanGraphics) != 0)
                _cleanGraphics = true;

            return this;
        }

        /// <summary>
        /// Adds a safe private creator that will be retained.
        /// </summary>
        /// <param name="creatorName">The private creator name.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithSafePrivateCreator(string creatorName)
        {
            _safePrivateCreators.Add(creatorName);
            _retainSafePrivate = true;
            return this;
        }

        /// <summary>
        /// Adds multiple safe private creators that will be retained.
        /// </summary>
        /// <param name="creatorNames">The private creator names.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithSafePrivateCreators(params string[] creatorNames)
        {
            foreach (var name in creatorNames)
            {
                _safePrivateCreators.Add(name);
            }
            _retainSafePrivate = true;
            return this;
        }

        /// <summary>
        /// Configures to remove all private tags.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder RemovePrivateTags()
        {
            _privateTagAction = PrivateTagAction.Remove;
            return this;
        }

        /// <summary>
        /// Configures to keep all private tags.
        /// </summary>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder KeepPrivateTags()
        {
            _privateTagAction = PrivateTagAction.Keep;
            return this;
        }

        /// <summary>
        /// Adds a tag-specific override.
        /// </summary>
        /// <param name="tag">The tag to override.</param>
        /// <param name="action">The action to apply.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithOverride(DicomTag tag, DeidentificationAction action)
        {
            _overrides[tag] = action;
            return this;
        }

        /// <summary>
        /// Configures date shifting with a fixed offset.
        /// </summary>
        /// <param name="offset">The offset to apply to all dates.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithDateShift(TimeSpan offset)
        {
            _dateShiftConfig = new DateShiftConfig
            {
                Strategy = DateShiftStrategy.Fixed,
                FixedOffset = offset
            };
            return this;
        }

        /// <summary>
        /// Configures date shifting with random offsets per patient.
        /// </summary>
        /// <param name="minDays">Minimum offset days (typically negative).</param>
        /// <param name="maxDays">Maximum offset days (typically negative).</param>
        /// <param name="seed">Optional random seed for reproducibility.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithRandomDateShift(int minDays = -365 * 5, int maxDays = -30, int? seed = null)
        {
            _dateShiftConfig = new DateShiftConfig
            {
                Strategy = DateShiftStrategy.RandomPerPatient,
                MinOffsetDays = minDays,
                MaxOffsetDays = maxDays,
                RandomSeed = seed
            };
            return this;
        }

        /// <summary>
        /// Configures a custom date shift configuration.
        /// </summary>
        /// <param name="config">The date shift configuration.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithDateShiftConfig(DateShiftConfig config)
        {
            _dateShiftConfig = config;
            return this;
        }

        /// <summary>
        /// Uses a shared UID remapper for consistent UID mapping across files.
        /// </summary>
        /// <param name="remapper">The UID remapper to use.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithUidRemapper(UidRemapper remapper)
        {
            _uidRemapper = remapper;
            return this;
        }

        /// <summary>
        /// Uses a shared UID store for consistent UID mapping across files.
        /// </summary>
        /// <param name="store">The UID store to use.</param>
        /// <returns>This builder for chaining.</returns>
        public DicomDeidentifierBuilder WithUidStore(IUidStore store)
        {
            _uidRemapper = new UidRemapper(store);
            return this;
        }

        /// <summary>
        /// Builds the configured DicomDeidentifier.
        /// </summary>
        /// <returns>A configured DicomDeidentifier instance.</returns>
        public DicomDeidentifier Build()
        {
            var options = new DeidentificationOptions
            {
                RetainSafePrivate = _retainSafePrivate,
                RetainUIDs = _retainUIDs,
                RetainDeviceIdentity = _retainDeviceIdentity,
                RetainInstitutionIdentity = _retainInstitutionIdentity,
                RetainPatientCharacteristics = _retainPatientCharacteristics,
                RetainLongitudinalFullDates = _retainLongitudinalFullDates,
                RetainLongitudinalModifiedDates = _retainLongitudinalModifiedDates,
                CleanDescriptors = _cleanDescriptors,
                CleanStructuredContent = _cleanStructuredContent,
                CleanGraphics = _cleanGraphics,
                DefaultPrivateTagAction = _privateTagAction,
                SafePrivateCreators = _safePrivateCreators.Count > 0 ? _safePrivateCreators : null,
                Overrides = _overrides.Count > 0 ? _overrides : null
            };

            DateShifter? dateShifter = null;
            if (_dateShiftConfig != null)
            {
                dateShifter = new DateShifter(_dateShiftConfig);
            }

            return new DicomDeidentifier(options, _uidRemapper, dateShifter);
        }
    }
}
