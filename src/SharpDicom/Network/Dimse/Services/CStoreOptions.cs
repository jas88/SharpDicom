using System;

namespace SharpDicom.Network.Dimse.Services;

/// <summary>
/// Options for C-STORE SCU operations.
/// </summary>
/// <remarks>
/// <para>
/// C-STORE options control the behavior of storage operations including timeouts,
/// priority, retry behavior, and progress reporting.
/// </para>
/// <para>
/// Priority values per DICOM PS3.7:
/// <list type="bullet">
///   <item><description>0 = MEDIUM (default)</description></item>
///   <item><description>1 = HIGH</description></item>
///   <item><description>2 = LOW</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class CStoreOptions
{
    /// <summary>
    /// Gets the default C-STORE options.
    /// </summary>
    public static CStoreOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the operation timeout. Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// This is the maximum time to wait for the SCP to respond to a C-STORE request.
    /// The timeout applies per-file, not for the entire batch.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the priority for the store operation. Default is MEDIUM (0).
    /// </summary>
    /// <remarks>
    /// Priority values per DICOM PS3.7:
    /// <list type="bullet">
    ///   <item><description>0 = MEDIUM (default)</description></item>
    ///   <item><description>1 = HIGH</description></item>
    ///   <item><description>2 = LOW</description></item>
    /// </list>
    /// The SCP may use priority to order processing of concurrent requests.
    /// </remarks>
    public ushort Priority { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retries on transient failures. Default is 0 (no retry).
    /// </summary>
    /// <remarks>
    /// Retries occur on transient failures such as 0xA7xx Out of Resources errors.
    /// Permanent failures (e.g., 0xA9xx No Such SOP Class) are not retried.
    /// </remarks>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the initial delay between retries. Default is 1 second.
    /// </summary>
    /// <remarks>
    /// Delay doubles with each retry (exponential backoff).
    /// For example, with 3 retries and 1s initial delay: 1s, 2s, 4s.
    /// </remarks>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to report progress during transfer. Default is true.
    /// </summary>
    /// <remarks>
    /// When false, progress callbacks are not invoked even if an <see cref="IProgress{T}"/>
    /// is provided. This can improve performance for small files.
    /// </remarks>
    public bool ReportProgress { get; set; } = true;
}
