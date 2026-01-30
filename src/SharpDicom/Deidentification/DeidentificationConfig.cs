using System;
using System.Collections.Generic;
#if NET6_0_OR_GREATER
using System.Text.Json.Serialization;
#endif

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// JSON-serializable configuration for de-identification.
    /// </summary>
    public sealed class DeidentificationConfig
    {
        /// <summary>
        /// Schema URL for validation (informational).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("$schema")]
#endif
        public string? Schema { get; init; }

        /// <summary>
        /// Base configuration to extend. Can be a preset name or file path.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("$extends")]
#endif
        public string? Extends { get; init; }

        /// <summary>
        /// Profile options to enable.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("options")]
#endif
        public List<string>? Options { get; init; }

        /// <summary>
        /// Date shifting configuration.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("dateShift")]
#endif
        public DateShiftConfigJson? DateShift { get; init; }

        /// <summary>
        /// UID mapping configuration.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("uidMapping")]
#endif
        public UidMappingConfigJson? UidMapping { get; init; }

        /// <summary>
        /// Tag-specific action overrides. Keys are tags like "(0008,0080)" or "InstitutionName".
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("overrides")]
#endif
        public Dictionary<string, string>? Overrides { get; init; }

        /// <summary>
        /// Default action for private tags: "remove" or "keep".
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("privateTagDefaults")]
#endif
        public string? PrivateTagDefaults { get; init; }

        /// <summary>
        /// List of private creator strings considered safe to retain.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("safePrivateCreators")]
#endif
        public List<string>? SafePrivateCreators { get; init; }

        /// <summary>
        /// Whether to remove tags not in the profile table.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("removeUnknownTags")]
#endif
        public bool? RemoveUnknownTags { get; init; }

        /// <summary>
        /// Clinical trial attributes to add.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("clinicalTrial")]
#endif
        public ClinicalTrialConfigJson? ClinicalTrial { get; init; }

        /// <summary>
        /// Dummy value overrides per VR or tag.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("dummyValues")]
#endif
        public Dictionary<string, string>? DummyValues { get; init; }
    }

    /// <summary>
    /// JSON configuration for date shifting.
    /// </summary>
    public sealed class DateShiftConfigJson
    {
        /// <summary>
        /// Shift strategy: "fixed", "randomPerPatient", "removeTime", or "remove".
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("strategy")]
#endif
        public string? Strategy { get; init; }

        /// <summary>
        /// Fixed offset in days (used with "fixed" strategy).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("offsetDays")]
#endif
        public int? OffsetDays { get; init; }

        /// <summary>
        /// Minimum offset in days for random strategy.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("minOffsetDays")]
#endif
        public int? MinOffsetDays { get; init; }

        /// <summary>
        /// Maximum offset in days for random strategy.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("maxOffsetDays")]
#endif
        public int? MaxOffsetDays { get; init; }

        /// <summary>
        /// Whether to preserve the time of day component.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("preserveTimeOfDay")]
#endif
        public bool? PreserveTimeOfDay { get; init; }

        /// <summary>
        /// Random seed for reproducible random offsets.
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("randomSeed")]
#endif
        public int? RandomSeed { get; init; }
    }

    /// <summary>
    /// JSON configuration for UID mapping.
    /// </summary>
    public sealed class UidMappingConfigJson
    {
        /// <summary>
        /// UID mapping scope: "study", "series", "batch", or "global".
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("scope")]
#endif
        public string? Scope { get; init; }

        /// <summary>
        /// Persistence type: "memory" or "sqlite".
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("persistence")]
#endif
        public string? Persistence { get; init; }

        /// <summary>
        /// Database file path (used with "sqlite" persistence).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("dbPath")]
#endif
        public string? DbPath { get; init; }
    }

    /// <summary>
    /// JSON configuration for clinical trial attributes.
    /// </summary>
    public sealed class ClinicalTrialConfigJson
    {
        /// <summary>
        /// Clinical Trial Protocol ID (0012,0020).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("protocolId")]
#endif
        public string? ProtocolId { get; init; }

        /// <summary>
        /// Clinical Trial Protocol Name (0012,0021).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("protocolName")]
#endif
        public string? ProtocolName { get; init; }

        /// <summary>
        /// Clinical Trial Site ID (0012,0030).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("siteId")]
#endif
        public string? SiteId { get; init; }

        /// <summary>
        /// Clinical Trial Site Name (0012,0031).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("siteName")]
#endif
        public string? SiteName { get; init; }

        /// <summary>
        /// Clinical Trial Sponsor Name (0012,0010).
        /// </summary>
#if NET6_0_OR_GREATER
        [JsonPropertyName("sponsorName")]
#endif
        public string? SponsorName { get; init; }
    }
}
