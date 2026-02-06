using System.Collections.Generic;

namespace SharpDicom.Cli.Configuration;

/// <summary>
/// CLI configuration model, populated from config file / environment / flags.
/// </summary>
internal sealed record CliConfig
{
    /// <summary>Default output format: "text", "json", or "xml".</summary>
    public string OutputFormat { get; set; } = "text";

    /// <summary>Verbosity level: "quiet", "normal", "verbose", "debug".</summary>
    public string Verbosity { get; set; } = "normal";

    /// <summary>Whether to use colour output.</summary>
    public bool Color { get; set; } = true;

    /// <summary>Whether to continue processing on errors.</summary>
    public bool ContinueOnError { get; set; }

    /// <summary>Name of the default PACS profile.</summary>
    public string? DefaultProfile { get; set; }

    /// <summary>Named PACS connection profiles.</summary>
    public Dictionary<string, PacsProfile> Profiles { get; set; } = new();
}
