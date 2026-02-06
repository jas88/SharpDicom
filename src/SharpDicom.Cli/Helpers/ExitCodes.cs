namespace SharpDicom.Cli.Helpers;

/// <summary>
/// Standardised exit codes for the sharpdcm CLI.
/// </summary>
internal static class ExitCodes
{
    /// <summary>Command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>Invalid arguments or usage error.</summary>
    public const int UsageError = 1;

    /// <summary>Runtime error (file not found, network failure, etc.).</summary>
    public const int RuntimeError = 2;

    /// <summary>Validation error (invalid DICOM, constraint violations).</summary>
    public const int ValidationError = 3;
}
