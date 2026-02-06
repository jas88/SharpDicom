using System;
using System.IO;
using CsToml;

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
            var bytes = File.ReadAllBytes(path);
            var doc = CsTomlSerializer.Deserialize<TomlDocument>(bytes);
            var root = doc.RootNode;

            var fmtNode = root["output_format"u8];
            if (fmtNode.TryGetString(out var fmtStr))
                config.OutputFormat = fmtStr;

            var verbNode = root["verbosity"u8];
            if (verbNode.TryGetString(out var verbStr))
                config.Verbosity = verbStr;

            var colNode = root["color"u8];
            if (colNode.TryGetBool(out var colBool))
                config.Color = colBool;

            var coeNode = root["continue_on_error"u8];
            if (coeNode.TryGetBool(out var coeBool))
                config.ContinueOnError = coeBool;

            var dpNode = root["default_profile"u8];
            if (dpNode.TryGetString(out var dpStr))
                config.DefaultProfile = dpStr;

            var profilesNode = root["profiles"u8];
            if (profilesNode.HasValue)
            {
                foreach (var kvp in profilesNode)
                {
                    var name = kvp.Key.ToString() ?? string.Empty;
                    var pt = kvp.Value;

                    var profile = new PacsProfile
                    {
                        Host = pt["host"u8].TryGetString(out var hs) ? hs : string.Empty,
                        Port = pt["port"u8].TryGetInt64(out var pl) ? (int)pl : 104,
                        CalledAE = pt["called_ae"u8].TryGetString(out var caes) ? caes : string.Empty,
                        CallingAE = pt["calling_ae"u8].TryGetString(out var clas) ? clas : null,
                        UseTls = pt["use_tls"u8].TryGetBool(out var tlsb) && tlsb,
                    };
                    config.Profiles[name] = profile;
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
