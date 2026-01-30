using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Network.Dimse.Services;

/// <summary>
/// C-STORE Service Class User (SCU) for sending DICOM files to remote AEs.
/// </summary>
/// <remarks>
/// <para>
/// CStoreScu provides methods to send DICOM objects to a remote Storage SCP.
/// It supports sending from various sources:
/// <list type="bullet">
///   <item><description>DicomFile - For in-memory DICOM files</description></item>
///   <item><description>Stream - For streaming from disk without full buffering</description></item>
///   <item><description>DicomDataset + IPixelDataSource - For transcoding scenarios</description></item>
/// </list>
/// </para>
/// <para>
/// Progress can be reported via <see cref="IProgress{T}"/> with <see cref="DicomTransferProgress"/>
/// providing bytes transferred, transfer rate, and estimated time remaining.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var client = new DicomClient(options);
/// await client.ConnectAsync(contexts, ct);
///
/// var storeScu = new CStoreScu(client);
/// var response = await storeScu.SendAsync(dicomFile, progress, ct);
///
/// if (response.IsSuccess)
///     Console.WriteLine($"Stored: {response.SOPInstanceUID}");
/// </code>
/// </para>
/// </remarks>
public sealed class CStoreScu
{
    private readonly DicomClient _client;
    private readonly CStoreOptions _options;
    private int _messageIdCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CStoreScu"/> class.
    /// </summary>
    /// <param name="client">Connected DicomClient with active association.</param>
    /// <param name="options">Optional store options. Uses <see cref="CStoreOptions.Default"/> if null.</param>
    /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
    public CStoreScu(DicomClient client, CStoreOptions? options = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(client);
#else
        if (client == null)
            throw new ArgumentNullException(nameof(client));
#endif
        _client = client;
        _options = options ?? CStoreOptions.Default;
    }

    /// <summary>
    /// Gets the options for this C-STORE SCU.
    /// </summary>
    public CStoreOptions Options => _options;

    /// <summary>
    /// Sends a DICOM file to the remote AE.
    /// </summary>
    /// <param name="file">The DICOM file to send.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response from the SCP.</returns>
    /// <exception cref="ArgumentNullException">Thrown when file is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when client is not connected.</exception>
    public async ValueTask<CStoreResponse> SendAsync(
        DicomFile file,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(file);
#else
        if (file == null)
            throw new ArgumentNullException(nameof(file));
#endif

        // Extract SOP Class and Instance UIDs from file
        var sopClassUid = GetSOPClassUID(file);
        var sopInstanceUid = GetSOPInstanceUID(file);

        return await SendCoreAsync(
            sopClassUid,
            sopInstanceUid,
            file.Dataset,
            progress,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends DICOM data from a stream to the remote AE.
    /// </summary>
    /// <param name="stream">Stream containing Part 10 DICOM file.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response from the SCP.</returns>
    /// <remarks>
    /// <para>
    /// The stream must contain a complete DICOM Part 10 file with File Meta Information.
    /// The file is read into memory to extract metadata and dataset.
    /// </para>
    /// <para>
    /// For true streaming without full buffering, future implementations will
    /// read the FMI first, then stream the dataset in chunks.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when client is not connected.</exception>
    public async ValueTask<CStoreResponse> SendAsync(
        Stream stream,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        // For now, load the file to extract UIDs and dataset
        // Future optimization: stream FMI separately, then dataset in chunks
        var file = await DicomFile.OpenAsync(stream, ct: ct).ConfigureAwait(false);
        return await SendAsync(file, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a dataset with optional separate pixel data source to the remote AE.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to send.</param>
    /// <param name="pixels">Optional pixel data source. If null, pixel data from dataset is used.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response from the SCP.</returns>
    /// <remarks>
    /// <para>
    /// This overload is useful for transcoding scenarios where the metadata
    /// and pixel data come from different sources.
    /// </para>
    /// <para>
    /// The SOP Class and Instance UIDs are extracted from the dataset.
    /// If pixel data source is provided, it replaces any pixel data in the dataset.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when client is not connected.</exception>
    public async ValueTask<CStoreResponse> SendAsync(
        DicomDataset dataset,
        IPixelDataSource? pixels,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(dataset);
#else
        if (dataset == null)
            throw new ArgumentNullException(nameof(dataset));
#endif

        // Separate pixel source is not yet implemented
        if (pixels != null)
        {
            throw new NotImplementedException(
                "Separate pixel data source is not yet supported. " +
                "Include pixel data in the dataset, or pass null for the pixels parameter.");
        }

        // Extract SOP Class and Instance UIDs from dataset
        var sopClassUid = GetSOPClassUIDFromDataset(dataset);
        var sopInstanceUid = GetSOPInstanceUIDFromDataset(dataset);

        return await SendCoreAsync(
            sopClassUid,
            sopInstanceUid,
            dataset,
            progress,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Core send implementation used by all overloads.
    /// </summary>
    private async ValueTask<CStoreResponse> SendCoreAsync(
        DicomUID sopClassUid,
        DicomUID sopInstanceUid,
        DicomDataset dataset,
        IProgress<DicomTransferProgress>? progress,
        CancellationToken ct)
    {
        // Find presentation context for this SOP Class
        var context = _client.GetAcceptedContext(sopClassUid);
        if (context == null)
        {
            return new CStoreResponse(
                DicomStatus.NoSuchSOPClass,
                sopClassUid,
                sopInstanceUid,
                $"No accepted presentation context for SOP Class {sopClassUid}");
        }

        // Apply timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);

        var sw = Stopwatch.StartNew();
        var totalBytes = EstimateDatasetSize(dataset);
        ReportInitialProgress(progress, totalBytes);

        int attempt = 0;
        var maxAttempts = _options.MaxRetries + 1;
        var delay = _options.RetryDelay;

        while (attempt < maxAttempts)
        {
            attempt++;

            try
            {
                // Create C-STORE request command
                var messageId = NextMessageId();
                var command = DicomCommand.CreateCStoreRequest(
                    messageId,
                    sopClassUid,
                    sopInstanceUid,
                    _options.Priority);

                // Send command + dataset
                await _client.SendDimseRequestAsync(context.Id, command, dataset, timeoutCts.Token)
                    .ConfigureAwait(false);

                // Receive response
                var (responseCmd, _) = await _client.ReceiveDimseResponseAsync(timeoutCts.Token)
                    .ConfigureAwait(false);

                if (!responseCmd.IsCStoreResponse)
                {
                    throw new InvalidOperationException(
                        $"Expected C-STORE-RSP, got command field 0x{responseCmd.CommandFieldValue:X4}");
                }

                if (responseCmd.MessageIDBeingRespondedTo != messageId)
                {
                    throw new InvalidOperationException(
                        $"Message ID mismatch: expected {messageId}, got {responseCmd.MessageIDBeingRespondedTo}");
                }

                sw.Stop();
                var bytesPerSecond = totalBytes > 0 && sw.Elapsed.TotalSeconds > 0
                    ? totalBytes / sw.Elapsed.TotalSeconds
                    : 0;
                ReportCompletedProgress(progress, totalBytes, bytesPerSecond);

                // Build response
                var status = responseCmd.Status;
                var errorComment = status.ErrorComment;

                // Check if retry is needed for transient failures
                if (ShouldRetry(status) && attempt < maxAttempts)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromTicks(delay.Ticks * 2); // Exponential backoff
                    continue;
                }

                return new CStoreResponse(status, sopClassUid, sopInstanceUid, errorComment);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout occurred
                return new CStoreResponse(
                    new DicomStatus(0xA700, "Operation timed out"),
                    sopClassUid,
                    sopInstanceUid,
                    "Operation timed out");
            }
        }

        // Should not reach here
        return new CStoreResponse(
            DicomStatus.ProcessingFailure,
            sopClassUid,
            sopInstanceUid,
            "Unexpected error during retry loop");
    }

    /// <summary>
    /// Gets the next unique message ID.
    /// </summary>
    internal ushort NextMessageId() => (ushort)Interlocked.Increment(ref _messageIdCounter);

    /// <summary>
    /// Determines if a status code indicates a retryable failure.
    /// </summary>
    private static bool ShouldRetry(DicomStatus status)
    {
        // 0xA7xx Out of Resources is retryable
        return status.Code >= 0xA700 && status.Code <= 0xA7FF;
    }

    /// <summary>
    /// Estimates the size of the dataset for progress reporting.
    /// </summary>
    private static long EstimateDatasetSize(DicomDataset dataset)
    {
        // Simple estimation: count raw bytes in elements
        long total = 0;
        foreach (var element in dataset)
        {
            total += element.RawValue.Length + 8; // value + tag + VL
        }
        return total;
    }

    /// <summary>
    /// Reports initial progress.
    /// </summary>
    private void ReportInitialProgress(IProgress<DicomTransferProgress>? progress, long totalBytes)
    {
        if (progress != null && _options.ReportProgress)
        {
            progress.Report(DicomTransferProgress.Initial(totalBytes));
        }
    }

    /// <summary>
    /// Reports completed progress.
    /// </summary>
    private void ReportCompletedProgress(IProgress<DicomTransferProgress>? progress, long totalBytes, double bytesPerSecond)
    {
        if (progress != null && _options.ReportProgress)
        {
            progress.Report(DicomTransferProgress.Completed(totalBytes, bytesPerSecond));
        }
    }

    /// <summary>
    /// Gets the SOP Class UID from a DicomFile.
    /// </summary>
    private static DicomUID GetSOPClassUID(DicomFile file)
    {
        // Try File Meta Information first
        var uid = file.FileMetaInfo?.GetString(DicomTag.MediaStorageSOPClassUID);
        if (!string.IsNullOrEmpty(uid))
            return new DicomUID(uid!.TrimEnd('\0', ' '));

        // Fall back to dataset
        return GetSOPClassUIDFromDataset(file.Dataset);
    }

    /// <summary>
    /// Gets the SOP Instance UID from a DicomFile.
    /// </summary>
    private static DicomUID GetSOPInstanceUID(DicomFile file)
    {
        // Try File Meta Information first
        var uid = file.FileMetaInfo?.GetString(DicomTag.MediaStorageSOPInstanceUID);
        if (!string.IsNullOrEmpty(uid))
            return new DicomUID(uid!.TrimEnd('\0', ' '));

        // Fall back to dataset
        return GetSOPInstanceUIDFromDataset(file.Dataset);
    }

    /// <summary>
    /// Gets the SOP Class UID from a dataset.
    /// </summary>
    private static DicomUID GetSOPClassUIDFromDataset(DicomDataset dataset)
    {
        var uid = dataset.GetString(DicomTag.SOPClassUID);
        return uid != null ? new DicomUID(uid.TrimEnd('\0', ' ')) : default;
    }

    /// <summary>
    /// Gets the SOP Instance UID from a dataset.
    /// </summary>
    private static DicomUID GetSOPInstanceUIDFromDataset(DicomDataset dataset)
    {
        var uid = dataset.GetString(DicomTag.SOPInstanceUID);
        return uid != null ? new DicomUID(uid.TrimEnd('\0', ' ')) : default;
    }
}
