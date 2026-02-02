using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Network.Pipes
{
    /// <summary>
    /// Wraps a Socket with PipeReader/PipeWriter for zero-copy I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SocketPipe manages background read/write pumps that transfer data
    /// between the socket and pipe buffers. Uses SlabMemoryPool for
    /// efficient buffer allocation.
    /// </para>
    /// <para>
    /// Backpressure: When the pipe buffer exceeds PauseWriterThreshold,
    /// socket reading pauses until the buffer drains below ResumeWriterThreshold.
    /// This allows TCP flow control to kick in naturally.
    /// </para>
    /// </remarks>
    internal sealed class SocketPipe : IAsyncDisposable
    {
        /// <summary>
        /// Default threshold at which the pipe pauses reading from socket (64KB).
        /// </summary>
        public const int DefaultPauseWriterThreshold = 65536;

        /// <summary>
        /// Default threshold at which the pipe resumes reading from socket (32KB).
        /// </summary>
        public const int DefaultResumeWriterThreshold = 32768;

        private readonly Socket _socket;
        private readonly Pipe _readPipe;
        private readonly Pipe _writePipe;
        private readonly Task _readTask;
        private readonly Task _writeTask;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of <see cref="SocketPipe"/>.
        /// </summary>
        /// <param name="socket">The socket to wrap. Must be connected.</param>
        /// <param name="pool">Optional memory pool. If null, uses MemoryPool&lt;byte&gt;.Shared.</param>
        /// <param name="pauseWriterThreshold">Threshold at which to pause reading from socket.</param>
        /// <param name="resumeWriterThreshold">Threshold at which to resume reading from socket.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="socket"/> is null.</exception>
        public SocketPipe(
            Socket socket,
            MemoryPool<byte>? pool = null,
            int pauseWriterThreshold = DefaultPauseWriterThreshold,
            int resumeWriterThreshold = DefaultResumeWriterThreshold)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(socket);
#else
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
#endif

            _socket = socket;

            var pipeOptions = new PipeOptions(
                pool: pool ?? MemoryPool<byte>.Shared,
                pauseWriterThreshold: pauseWriterThreshold,
                resumeWriterThreshold: resumeWriterThreshold,
                useSynchronizationContext: false);

            _readPipe = new Pipe(pipeOptions);
            _writePipe = new Pipe(pipeOptions);

            // Start background pumps
            _readTask = FillReadPipeAsync(_cts.Token);
            _writeTask = DrainWritePipeAsync(_cts.Token);
        }

        /// <summary>
        /// Gets the PipeReader for reading data received from the socket.
        /// </summary>
        public PipeReader Input => _readPipe.Reader;

        /// <summary>
        /// Gets the PipeWriter for writing data to send to the socket.
        /// </summary>
        public PipeWriter Output => _writePipe.Writer;

        /// <summary>
        /// Background task that reads from the socket and fills the read pipe.
        /// </summary>
        private async Task FillReadPipeAsync(CancellationToken ct)
        {
            var writer = _readPipe.Writer;
            Exception? exception = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Get memory from the pipe's buffer
                    var memory = writer.GetMemory(SlabMemoryPool.SlabSize);

#if NET6_0_OR_GREATER
                    var bytesRead = await _socket.ReceiveAsync(
                        memory, SocketFlags.None, ct).ConfigureAwait(false);
#else
                    // netstandard2.0: Socket.ReceiveAsync doesn't accept Memory<byte>
                    // Use a temporary buffer and copy
                    var buffer = ArrayPool<byte>.Shared.Rent(memory.Length);
                    int bytesRead;
                    try
                    {
                        var segment = new ArraySegment<byte>(buffer, 0, memory.Length);
                        bytesRead = await _socket.ReceiveAsync(segment, SocketFlags.None)
                            .ConfigureAwait(false);
                        if (bytesRead > 0)
                        {
                            buffer.AsSpan(0, bytesRead).CopyTo(memory.Span);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
#endif

                    if (bytesRead == 0)
                    {
                        // Socket closed gracefully
                        break;
                    }

                    writer.Advance(bytesRead);

                    // Make the data available to the reader and check backpressure
                    var flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flushResult.IsCompleted || flushResult.IsCanceled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (SocketException ex)
            {
                // Socket error - record for completion
                exception = ex;
            }
            catch (ObjectDisposedException)
            {
                // Socket disposed
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                await writer.CompleteAsync(exception).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Background task that drains the write pipe and sends to the socket.
        /// </summary>
        private async Task DrainWritePipeAsync(CancellationToken ct)
        {
            var reader = _writePipe.Reader;
            Exception? exception = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                    var buffer = result.Buffer;

                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }

                    // Send each segment to the socket
                    foreach (var segment in buffer)
                    {
                        if (segment.Length == 0)
                            continue;

#if NET6_0_OR_GREATER
                        await _socket.SendAsync(segment, SocketFlags.None, ct).ConfigureAwait(false);
#else
                        // netstandard2.0: Need to copy to array
                        var array = segment.ToArray();
                        var arraySegment = new ArraySegment<byte>(array);
                        await _socket.SendAsync(arraySegment, SocketFlags.None).ConfigureAwait(false);
#endif
                    }

                    reader.AdvanceTo(buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (SocketException ex)
            {
                exception = ex;
            }
            catch (ObjectDisposedException)
            {
                // Socket disposed
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                await reader.CompleteAsync(exception).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            // Signal cancellation to background tasks
#if NET8_0_OR_GREATER
            await _cts.CancelAsync().ConfigureAwait(false);
#else
            _cts.Cancel();
#endif

            // Complete the pipes to unblock any waiting operations
            await _readPipe.Reader.CompleteAsync().ConfigureAwait(false);
            await _writePipe.Writer.CompleteAsync().ConfigureAwait(false);

            // Wait for background tasks to complete
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions from the read task
            }

            try
            {
                await _writeTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions from the write task
            }

            _cts.Dispose();
        }
    }
}
