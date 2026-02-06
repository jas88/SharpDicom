using System;
using System.Collections.Generic;
using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace SharpDicom.Cli.Configuration;

/// <summary>
/// Loads CLI configuration with layered precedence: file &lt; env vars &lt; flags.
/// </summary>
internal static class ConfigLoader
{
    /// <summary>
    /// Load configuration from the config file, falling back to defaults.
    /// </summary>
    /// <param name="configPath">
    /// Explicit config file path. When <c>null</c>, uses <c>~/.sharpdcm/config.toml</c>.
    /// </param>
    /// <returns>Parsed configuration (never throws on config errors).</returns>
    public static CliConfig Load(string? configPath = null)
    {
        var config = new CliConfig();

        var path = configPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sharpdcm",
                "config.toml");

        if (!File.Exists(path))
            return config;

        try
        {
            var toml = File.ReadAllText(path);
            var model = Toml.ToModel(toml);

            if (model.TryGetValue("output_format", out var fmt) && fmt is string fmtStr)
                config.OutputFormat = fmtStr;

            if (model.TryGetValue("verbosity", out var verb) && verb is string verbStr)
                config.Verbosity = verbStr;

            if (model.TryGetValue("color", out var col) && col is bool colBool)
                config.Color = colBool;

            if (model.TryGetValue("continue_on_error", out var coe) && coe is bool coeBool)
                config.ContinueOnError = coeBool;

            if (model.TryGetValue("default_profile", out var dp) && dp is string dpStr)
                config.DefaultProfile = dpStr;

            if (model.TryGetValue("profiles", out var profiles) && profiles is TomlTable profilesTable)
            {
                foreach (var kvp in profilesTable)
                {
                    if (kvp.Value is TomlTable pt)
                    {
                        var profile = new PacsProfile
                        {
                            Host = pt.TryGetValue("host", out var h) && h is string hs ? hs : string.Empty,
                            Port = pt.TryGetValue("port", out var p) && p is long pl ? (int)pl : 104,
                            CalledAE = pt.TryGetValue("called_ae", out var cae) && cae is string caes ? caes : string.Empty,
                            CallingAE = pt.TryGetValue("calling_ae", out var cla) && cla is string clas ? clas : null,
                            UseTls = pt.TryGetValue("use_tls", out var tls) && tls is bool tlsb && tlsb,
                        };
                        config.Profiles[kvp.Key] = profile;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse config file '{path}': {ex.Message}");
        }

        return config;
    }

    /// <summary>
    /// Override configuration values with environment variables.
    /// </summary>
    public static CliConfig ApplyEnvironmentVariables(CliConfig config)
    {
        var envFormat = Environment.GetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT");
        if (!string.IsNullOrEmpty(envFormat))
            config.OutputFormat = envFormat;

        var envVerbosity = Environment.GetEnvironmentVariable("SHARPDCM_VERBOSITY");
        if (!string.IsNullOrEmpty(envVerbosity))
            config.Verbosity = envVerbosity;

        var envColor = Environment.GetEnvironmentVariable("SHARPDCM_COLOR");
        if (!string.IsNullOrEmpty(envColor) && bool.TryParse(envColor, out var colorVal))
            config.Color = colorVal;

        return config;
    }
}
