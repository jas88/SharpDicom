using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.IO
{
    /// <summary>
    /// Writes DICOM Part 10 files with automatic File Meta Information generation.
    /// </summary>
    /// <remarks>
    /// DicomFileWriter produces valid DICOM Part 10 files with:
    /// - 128-byte preamble (zeros or custom)
    /// - "DICM" prefix
    /// - File Meta Information (always Explicit VR Little Endian)
    /// - Dataset (using specified transfer syntax)
    /// </remarks>
    public sealed class DicomFileWriter : IAsyncDisposable, IDisposable
    {
        private readonly Stream _stream;
        private readonly DicomWriterOptions _options;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// The "DICM" prefix bytes.
        /// </summary>
        private static readonly byte[] DicmPrefix = Encoding.ASCII.GetBytes("DICM");

        /// <summary>
        /// Default 128-byte zero preamble.
        /// </summary>
        private static readonly byte[] ZeroPreamble = new byte[128];

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomFileWriter"/> class.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="options">Writer options, or null for defaults.</param>
        /// <param name="leaveOpen">If true, the stream is not closed when the writer is disposed.</param>
        public DicomFileWriter(Stream stream, DicomWriterOptions? options = null, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _options = options ?? DicomWriterOptions.Default;
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Writes a DicomFile to the stream.
        /// </summary>
        /// <param name="file">The DICOM file to write.</param>
        public void Write(DicomFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            Write(file.Dataset, file.TransferSyntax, file.FileMetaInfo);
        }

        /// <summary>
        /// Writes a dataset to the stream as a DICOM Part 10 file.
        /// </summary>
        /// <param name="dataset">The dataset to write.</param>
        /// <param name="transferSyntax">The transfer syntax to use. If null, uses Explicit VR Little Endian.</param>
        /// <param name="existingFmi">Existing FMI to use. If null and AutoGenerateFmi is true, generates FMI.</param>
        public void Write(DicomDataset dataset, TransferSyntax? transferSyntax = null, DicomDataset? existingFmi = null)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            transferSyntax ??= _options.TransferSyntax;

            using var bufferWriter = new StreamBufferWriter(_stream, _options.BufferSize);

            // Write preamble (128 bytes)
            WritePreamble(bufferWriter);

            // Write DICM prefix
            WriteDicmPrefix(bufferWriter);

            // Get or generate FMI
            var fmi = GetOrGenerateFmi(dataset, transferSyntax.Value, existingFmi);

            // Write FMI (always Explicit VR Little Endian)
            WriteFmi(bufferWriter, fmi);

            // Write dataset elements
            WriteDataset(bufferWriter, dataset, transferSyntax.Value);

            // Final flush
            bufferWriter.Flush();
        }

        /// <summary>
        /// Asynchronously writes a DicomFile to the stream.
        /// </summary>
        /// <param name="file">The DICOM file to write.</param>
        /// <param name="ct">Cancellation token.</param>
        public ValueTask WriteAsync(DicomFile file, CancellationToken ct = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            return WriteAsync(file.Dataset, file.TransferSyntax, file.FileMetaInfo, ct);
        }

        /// <summary>
        /// Asynchronously writes a dataset to the stream as a DICOM Part 10 file.
        /// </summary>
        /// <param name="dataset">The dataset to write.</param>
        /// <param name="transferSyntax">The transfer syntax to use. If null, uses Explicit VR Little Endian.</param>
        /// <param name="existingFmi">Existing FMI to use. If null and AutoGenerateFmi is true, generates FMI.</param>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask WriteAsync(
            DicomDataset dataset,
            TransferSyntax? transferSyntax = null,
            DicomDataset? existingFmi = null,
            CancellationToken ct = default)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            transferSyntax ??= _options.TransferSyntax;

            using var bufferWriter = new StreamBufferWriter(_stream, _options.BufferSize);

            // Write preamble (128 bytes)
            WritePreamble(bufferWriter);

            // Write DICM prefix
            WriteDicmPrefix(bufferWriter);

            // Get or generate FMI
            var fmi = GetOrGenerateFmi(dataset, transferSyntax.Value, existingFmi);

            // Write FMI (always Explicit VR Little Endian)
            WriteFmi(bufferWriter, fmi);

            // Flush to stream periodically
            await bufferWriter.FlushAsync(ct).ConfigureAwait(false);

            // Write dataset elements
            WriteDataset(bufferWriter, dataset, transferSyntax.Value);

            // Final flush
            await bufferWriter.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the 128-byte preamble.
        /// </summary>
        private void WritePreamble(StreamBufferWriter writer)
        {
            var preambleSpan = _options.Preamble.HasValue
                ? _options.Preamble.Value.Span
                : ZeroPreamble.AsSpan();

            // Ensure exactly 128 bytes
            var span = writer.GetSpan(128);
            if (preambleSpan.Length >= 128)
            {
                preambleSpan.Slice(0, 128).CopyTo(span);
            }
            else
            {
                // Pad with zeros if shorter
                span.Slice(0, 128).Clear();
                preambleSpan.CopyTo(span);
            }
            writer.Advance(128);
        }

        /// <summary>
        /// Writes the "DICM" prefix.
        /// </summary>
        private static void WriteDicmPrefix(StreamBufferWriter writer)
        {
            var span = writer.GetSpan(4);
            DicmPrefix.AsSpan().CopyTo(span);
            writer.Advance(4);
        }

        /// <summary>
        /// Gets existing FMI or generates new FMI.
        /// </summary>
        private DicomDataset GetOrGenerateFmi(DicomDataset dataset, TransferSyntax transferSyntax, DicomDataset? existingFmi)
        {
            // Use existing FMI if provided and has content and auto-generate is disabled
            if (!_options.AutoGenerateFmi && existingFmi != null && existingFmi.Count > 0)
            {
                return existingFmi;
            }

            // Generate FMI
            return FileMetaInfoGenerator.Generate(dataset, transferSyntax, _options);
        }

        /// <summary>
        /// Writes the File Meta Information (always Explicit VR Little Endian).
        /// </summary>
        private static void WriteFmi(StreamBufferWriter bufferWriter, DicomDataset fmi)
        {
            // FMI is always Explicit VR Little Endian
            var streamWriter = new DicomStreamWriter(bufferWriter, explicitVR: true, littleEndian: true);

            foreach (var element in fmi)
            {
                streamWriter.WriteElement(element);
            }
        }

        /// <summary>
        /// Writes the dataset elements using the specified transfer syntax.
        /// </summary>
        private void WriteDataset(StreamBufferWriter bufferWriter, DicomDataset dataset, TransferSyntax transferSyntax)
        {
            // Create writer with proper transfer syntax and sequence length mode
            var streamWriter = new DicomStreamWriter(
                bufferWriter,
                transferSyntax.IsExplicitVR,
                transferSyntax.IsLittleEndian,
                _options.SequenceLength);

            foreach (var element in dataset)
            {
                // Skip File Meta Information elements in dataset (group 0002)
                if (element.Tag.Group == 0x0002)
                    continue;

                // Write element (sequences are handled automatically by DicomStreamWriter)
                if (element is DicomSequence sequence)
                {
                    streamWriter.WriteSequence(sequence);
                }
                else
                {
                    streamWriter.WriteElement(element);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                if (!_leaveOpen)
                {
                    _stream.Dispose();
                }
                _disposed = true;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (!_leaveOpen)
                {
#if NETSTANDARD2_0
                    _stream.Dispose();
#else
                    await _stream.DisposeAsync().ConfigureAwait(false);
#endif
                }
                _disposed = true;
            }
#if NETSTANDARD2_0
            await default(ValueTask).ConfigureAwait(false);
#endif
        }
    }
}
