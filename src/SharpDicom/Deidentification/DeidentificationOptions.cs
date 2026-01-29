using System.Collections.Generic;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Options for configuring de-identification behavior.
    /// </summary>
    public sealed class DeidentificationOptions
    {
        /// <summary>
        /// Gets or sets whether to retain known-safe private tags.
        /// </summary>
        public bool RetainSafePrivate { get; init; }

        /// <summary>
        /// Gets or sets whether to retain original UIDs (skip UID remapping).
        /// </summary>
        public bool RetainUIDs { get; init; }

        /// <summary>
        /// Gets or sets whether to retain device identity information.
        /// </summary>
        public bool RetainDeviceIdentity { get; init; }

        /// <summary>
        /// Gets or sets whether to retain institution identity information.
        /// </summary>
        public bool RetainInstitutionIdentity { get; init; }

        /// <summary>
        /// Gets or sets whether to retain patient characteristics (age, sex, etc.).
        /// </summary>
        public bool RetainPatientCharacteristics { get; init; }

        /// <summary>
        /// Gets or sets whether to retain full dates without modification.
        /// </summary>
        public bool RetainLongitudinalFullDates { get; init; }

        /// <summary>
        /// Gets or sets whether to retain dates with offset modification.
        /// </summary>
        public bool RetainLongitudinalModifiedDates { get; init; }

        /// <summary>
        /// Gets or sets whether to clean description fields.
        /// </summary>
        public bool CleanDescriptors { get; init; }

        /// <summary>
        /// Gets or sets whether to clean structured content (SR).
        /// </summary>
        public bool CleanStructuredContent { get; init; }

        /// <summary>
        /// Gets or sets whether to clean graphics overlays.
        /// </summary>
        public bool CleanGraphics { get; init; }

        /// <summary>
        /// Gets or sets the default action for private tags.
        /// </summary>
        public PrivateTagAction DefaultPrivateTagAction { get; init; } = PrivateTagAction.Remove;

        /// <summary>
        /// Gets or sets the set of private creator strings considered safe to retain.
        /// </summary>
#if NET5_0_OR_GREATER
        public IReadOnlySet<string>? SafePrivateCreators { get; init; }
#else
        public IReadOnlyCollection<string>? SafePrivateCreators { get; init; }
#endif

        /// <summary>
        /// Gets or sets whether to remove unknown tags (tags not in the profile table).
        /// </summary>
        public bool RemoveUnknownTags { get; init; } = true;

        /// <summary>
        /// Gets or sets tag-specific action overrides.
        /// </summary>
        public IReadOnlyDictionary<Data.DicomTag, DeidentificationAction>? Overrides { get; init; }

        /// <summary>
        /// Gets the profile options as a flags enum for use with generated profiles.
        /// </summary>
        internal DeidentificationProfileOption ToProfileOptions()
        {
            var options = DeidentificationProfileOption.None;

            if (RetainSafePrivate)
                options |= DeidentificationProfileOption.RetainSafePrivate;
            if (RetainUIDs)
                options |= DeidentificationProfileOption.RetainUIDs;
            if (RetainDeviceIdentity)
                options |= DeidentificationProfileOption.RetainDeviceIdentity;
            if (RetainInstitutionIdentity)
                options |= DeidentificationProfileOption.RetainInstitutionIdentity;
            if (RetainPatientCharacteristics)
                options |= DeidentificationProfileOption.RetainPatientCharacteristics;
            if (RetainLongitudinalFullDates)
                options |= DeidentificationProfileOption.RetainLongitudinalFullDates;
            if (RetainLongitudinalModifiedDates)
                options |= DeidentificationProfileOption.RetainLongitudinalModifiedDates;
            if (CleanDescriptors)
                options |= DeidentificationProfileOption.CleanDescriptors;
            if (CleanStructuredContent)
                options |= DeidentificationProfileOption.CleanStructuredContent;
            if (CleanGraphics)
                options |= DeidentificationProfileOption.CleanGraphics;

            return options;
        }

        /// <summary>
        /// Default options for Basic Profile (most restrictive).
        /// </summary>
        public static DeidentificationOptions BasicProfile { get; } = new();

        /// <summary>
        /// Options for research scenarios that retain patient characteristics.
        /// </summary>
        public static DeidentificationOptions Research { get; } = new()
        {
            RetainPatientCharacteristics = true,
            CleanDescriptors = true
        };

        /// <summary>
        /// Options for clinical trial submissions with date shifting.
        /// </summary>
        public static DeidentificationOptions ClinicalTrial { get; } = new()
        {
            RetainLongitudinalModifiedDates = true,
            CleanDescriptors = true
        };

        /// <summary>
        /// Options for teaching files with maximum de-identification.
        /// </summary>
        public static DeidentificationOptions Teaching { get; } = new()
        {
            CleanDescriptors = true,
            CleanGraphics = true,
            DefaultPrivateTagAction = PrivateTagAction.Remove
        };
    }

    /// <summary>
    /// Action to apply to private tags during de-identification.
    /// </summary>
    public enum PrivateTagAction
    {
        /// <summary>Keep private tags unchanged.</summary>
        Keep,

        /// <summary>Remove all private tags.</summary>
        Remove
    }
}
