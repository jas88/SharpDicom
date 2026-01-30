using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
#if NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
#endif

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Loads de-identification configurations with $extends inheritance support.
    /// </summary>
    /// <remarks>
    /// JSON serialization features require .NET 6.0 or later.
    /// </remarks>
    public static class DeidentificationConfigLoader
    {
#if NET6_0_OR_GREATER
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // Built-in presets
        private static readonly Dictionary<string, Func<DeidentificationConfig>> PresetFactories =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["basic-profile"] = CreateBasicProfileConfig,
                ["research"] = CreateResearchConfig,
                ["clinical-trial"] = CreateClinicalTrialConfig,
                ["teaching"] = CreateTeachingConfig
            };

        /// <summary>
        /// Gets the available preset names.
        /// </summary>
        public static IEnumerable<string> PresetNames => PresetFactories.Keys;

        /// <summary>
        /// Loads configuration from a JSON file, resolving $extends references.
        /// </summary>
        /// <param name="path">Path to the JSON configuration file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The resolved configuration.</returns>
        [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("JSON serialization may require runtime code generation.")]
        public static async Task<DeidentificationConfig> LoadConfigAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<DeidentificationConfig>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to parse config: {path}");

            return ResolveInheritance(config, Path.GetDirectoryName(path));
        }

        /// <summary>
        /// Loads configuration from a JSON string, resolving $extends references.
        /// </summary>
        /// <param name="json">The JSON configuration string.</param>
        /// <param name="basePath">Optional base path for resolving relative $extends paths.</param>
        /// <returns>The resolved configuration.</returns>
        [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("JSON serialization may require runtime code generation.")]
        public static DeidentificationConfig LoadConfigFromJson(string json, string? basePath = null)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            var config = JsonSerializer.Deserialize<DeidentificationConfig>(json, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse config JSON");

            return ResolveInheritance(config, basePath);
        }

        /// <summary>
        /// Gets a built-in preset by name.
        /// </summary>
        /// <param name="name">The preset name (case-insensitive).</param>
        /// <returns>The preset configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when the preset name is unknown.</exception>
        public static DeidentificationConfig GetPreset(string name)
        {
            if (!PresetFactories.TryGetValue(name, out var factory))
                throw new ArgumentException($"Unknown preset: {name}. Available presets: {string.Join(", ", PresetNames)}", nameof(name));

            return factory();
        }

        /// <summary>
        /// Checks if a preset name is valid.
        /// </summary>
        /// <param name="name">The preset name to check.</param>
        /// <returns>True if the preset exists, false otherwise.</returns>
        public static bool IsValidPreset(string name)
        {
            return PresetFactories.ContainsKey(name);
        }

        /// <summary>
        /// Creates a DicomDeidentifierBuilder from configuration.
        /// </summary>
        /// <param name="config">The de-identification configuration.</param>
        /// <returns>A configured builder.</returns>
        public static DicomDeidentifierBuilder CreateBuilder(DeidentificationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var builder = new DicomDeidentifierBuilder().WithBasicProfile();

            // Apply options
            if (config.Options != null)
            {
                foreach (var optionName in config.Options)
                {
                    if (TryParseOption(optionName, out var option))
                    {
                        builder.WithOption(option);
                    }
                }
            }

            // Apply date shift
            if (config.DateShift != null)
            {
                var dateConfig = ParseDateShiftConfig(config.DateShift);
                builder.WithDateShiftConfig(dateConfig);
            }

            // Apply overrides
            if (config.Overrides != null)
            {
                foreach (var kvp in config.Overrides)
                {
                    var tag = ParseTagSpec(kvp.Key);
                    var action = ParseAction(kvp.Value);
                    builder.WithOverride(tag, action);
                }
            }

            // Apply private tag settings
            if (string.Equals(config.PrivateTagDefaults, "keep", StringComparison.OrdinalIgnoreCase))
            {
                builder.KeepPrivateTags();
            }
            else if (string.Equals(config.PrivateTagDefaults, "remove", StringComparison.OrdinalIgnoreCase))
            {
                builder.RemovePrivateTags();
            }

            // Apply safe private creators
            if (config.SafePrivateCreators?.Count > 0)
            {
                builder.WithSafePrivateCreators(config.SafePrivateCreators.ToArray());
            }

            return builder;
        }

        [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("JSON serialization may require runtime code generation.")]
        private static DeidentificationConfig ResolveInheritance(DeidentificationConfig config, string? basePath)
        {
            if (string.IsNullOrEmpty(config.Extends))
                return config;

            // Resolve base config
            DeidentificationConfig baseConfig;
            if (PresetFactories.TryGetValue(config.Extends!, out var factory))
            {
                baseConfig = factory();
            }
            else
            {
                // Load from file
                var path = basePath != null
                    ? Path.Combine(basePath, config.Extends!)
                    : config.Extends!;

                if (!File.Exists(path))
                    throw new FileNotFoundException($"Extended config file not found: {path}");

                var json = File.ReadAllText(path);
                baseConfig = LoadConfigFromJson(json, Path.GetDirectoryName(path));
            }

            // Merge: child overrides parent
            return MergeConfigs(baseConfig, config);
        }

        private static DeidentificationConfig MergeConfigs(DeidentificationConfig parent, DeidentificationConfig child)
        {
            return new DeidentificationConfig
            {
                // Schema from child (informational only)
                Schema = child.Schema ?? parent.Schema,

                // Don't propagate $extends in merged config
                Extends = null,

                // Merge lists (union, distinct)
                Options = MergeLists(parent.Options, child.Options),
                SafePrivateCreators = MergeLists(parent.SafePrivateCreators, child.SafePrivateCreators),

                // Child overrides parent for objects
                DateShift = child.DateShift ?? parent.DateShift,
                UidMapping = child.UidMapping ?? parent.UidMapping,
                ClinicalTrial = child.ClinicalTrial ?? parent.ClinicalTrial,

                // Merge dictionaries (child entries override parent)
                Overrides = MergeDicts(parent.Overrides, child.Overrides),
                DummyValues = MergeDicts(parent.DummyValues, child.DummyValues),

                // Child overrides parent for simple values
                PrivateTagDefaults = child.PrivateTagDefaults ?? parent.PrivateTagDefaults,
                RemoveUnknownTags = child.RemoveUnknownTags ?? parent.RemoveUnknownTags
            };
        }

        private static List<T>? MergeLists<T>(List<T>? parent, List<T>? child)
        {
            if (child == null) return parent;
            if (parent == null) return child;
            return parent.Concat(child).Distinct().ToList();
        }

        private static Dictionary<TKey, TValue>? MergeDicts<TKey, TValue>(
            Dictionary<TKey, TValue>? parent,
            Dictionary<TKey, TValue>? child)
            where TKey : notnull
        {
            if (child == null) return parent;
            if (parent == null) return child;
            var merged = new Dictionary<TKey, TValue>(parent);
            foreach (var kvp in child)
                merged[kvp.Key] = kvp.Value;  // Child overrides parent
            return merged;
        }

        private static DicomTag ParseTagSpec(string spec)
        {
            if (string.IsNullOrEmpty(spec))
                throw new ArgumentException("Tag specification cannot be empty", nameof(spec));

            // Handle "(GGGG,EEEE)" format
            if (spec.Length > 2 && spec[0] == '(' && spec[spec.Length - 1] == ')')
            {
                var inner = spec.Substring(1, spec.Length - 2);
                var parts = inner.Split(',');
                if (parts.Length == 2 &&
                    ushort.TryParse(parts[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var group) &&
                    ushort.TryParse(parts[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var element))
                {
                    return new DicomTag(group, element);
                }
                throw new ArgumentException($"Invalid tag format: {spec}. Expected (GGGG,EEEE)", nameof(spec));
            }

            // Try keyword lookup via dictionary
            var entry = DicomDictionary.Default.GetEntryByKeyword(spec);
            if (entry.HasValue)
            {
                return entry.Value.Tag;
            }

            throw new ArgumentException($"Unknown tag keyword: {spec}", nameof(spec));
        }

        private static DeidentificationAction ParseAction(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Action name cannot be empty", nameof(name));

            return name.ToUpperInvariant() switch
            {
                "D" or "DUMMY" => DeidentificationAction.Dummy,
                "Z" or "ZERO" => DeidentificationAction.ZeroOrDummy,
                "X" or "REMOVE" => DeidentificationAction.Remove,
                "K" or "KEEP" => DeidentificationAction.Keep,
                "C" or "CLEAN" => DeidentificationAction.Clean,
                "U" or "REMAP" or "UID" => DeidentificationAction.RemapUid,
                _ => throw new ArgumentException($"Unknown action: {name}. Valid actions: D, Z, X, K, C, U", nameof(name))
            };
        }

        private static bool TryParseOption(string name, out DeidentificationProfileOption option)
        {
            option = name.ToLowerInvariant() switch
            {
                "retainsafeprivate" => DeidentificationProfileOption.RetainSafePrivate,
                "retainuids" => DeidentificationProfileOption.RetainUIDs,
                "retaindeviceidentity" => DeidentificationProfileOption.RetainDeviceIdentity,
                "retaininstitutionidentity" => DeidentificationProfileOption.RetainInstitutionIdentity,
                "retainpatientcharacteristics" => DeidentificationProfileOption.RetainPatientCharacteristics,
                "retainlongitudinalfulldates" => DeidentificationProfileOption.RetainLongitudinalFullDates,
                "retainlongitudinalmodifieddates" => DeidentificationProfileOption.RetainLongitudinalModifiedDates,
                "cleandescriptors" => DeidentificationProfileOption.CleanDescriptors,
                "cleanstructuredcontent" => DeidentificationProfileOption.CleanStructuredContent,
                "cleangraphics" => DeidentificationProfileOption.CleanGraphics,
                _ => DeidentificationProfileOption.None
            };

            // RetainSafePrivate is value 1<<0 which equals 1, so explicit check needed
            return option != DeidentificationProfileOption.None ||
                   name.Equals("retainsafeprivate", StringComparison.OrdinalIgnoreCase);
        }

        private static DateShiftConfig ParseDateShiftConfig(DateShiftConfigJson json)
        {
            return new DateShiftConfig
            {
                Strategy = json.Strategy?.ToLowerInvariant() switch
                {
                    "fixed" => DateShiftStrategy.Fixed,
                    "randomperpatient" => DateShiftStrategy.RandomPerPatient,
                    "removetime" => DateShiftStrategy.RemoveTime,
                    "remove" => DateShiftStrategy.Remove,
                    "none" => DateShiftStrategy.None,
                    null => DateShiftStrategy.Fixed,
                    _ => throw new ArgumentException($"Unknown date shift strategy: {json.Strategy}")
                },
                FixedOffset = TimeSpan.FromDays(json.OffsetDays ?? -100),
                MinOffsetDays = json.MinOffsetDays ?? -365,
                MaxOffsetDays = json.MaxOffsetDays ?? 365,
                RandomSeed = json.RandomSeed
            };
        }

        // Preset factory methods
        private static DeidentificationConfig CreateBasicProfileConfig() => new()
        {
            Options = new List<string>(),
            PrivateTagDefaults = "remove",
            RemoveUnknownTags = true
        };

        private static DeidentificationConfig CreateResearchConfig() => new()
        {
            Extends = "basic-profile",
            Options = new List<string> { "RetainPatientCharacteristics", "CleanDescriptors" },
            DateShift = new DateShiftConfigJson
            {
                Strategy = "randomPerPatient",
                MinOffsetDays = -365,
                MaxOffsetDays = 365
            }
        };

        private static DeidentificationConfig CreateClinicalTrialConfig() => new()
        {
            Extends = "basic-profile",
            Options = new List<string> { "RetainLongitudinalModifiedDates", "CleanDescriptors" },
            DateShift = new DateShiftConfigJson
            {
                Strategy = "fixed",
                OffsetDays = -100
            }
        };

        private static DeidentificationConfig CreateTeachingConfig() => new()
        {
            Extends = "basic-profile",
            Options = new List<string> { "CleanDescriptors", "CleanGraphics" },
            PrivateTagDefaults = "remove"
        };
#endif
    }
}
