namespace SharpDicom.Cli.Configuration;

/// <summary>
/// A named PACS connection profile loaded from configuration.
/// </summary>
internal sealed record PacsProfile
{
    /// <summary>PACS host name or IP address.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>PACS port (default DICOM port 104).</summary>
    public int Port { get; init; } = 104;

    /// <summary>Called Application Entity title.</summary>
    public string CalledAE { get; init; } = string.Empty;

    /// <summary>Calling Application Entity title (optional).</summary>
    public string? CallingAE { get; init; }

    /// <summary>Whether to use TLS for this connection.</summary>
    public bool UseTls { get; init; }
}
