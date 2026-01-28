using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.Validation;

namespace SharpDicom.IO
{
    /// <summary>
    /// High-level async DICOM file reader with streaming support.
    /// </summary>
    /// <remarks>
    /// DicomFileReader provides convenient async APIs for reading DICOM files:
    /// <list type="bullet">
    /// <item><description><see cref="ReadFileMetaInfoAsync"/> parses the file header (preamble, DICM, FMI)</description></item>
    /// <item><description><see cref="ReadElementsAsync"/> streams elements via IAsyncEnumerable</description></item>
    /// <item><description><see cref="ReadDatasetAsync"/> loads the complete dataset into memory</description></item>
    /// </list>
    /// Use <see cref="DisposeAsync"/> to release resources when done.
    /// </remarks>
    public sealed class DicomFileReader : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private readonly DicomReaderOptions _options;
        private readonly ArrayPool<byte> _pool;
        private byte[]? _buffer;
        private int _bufferLength;
        private int _bufferConsumed;
        private Part10Reader? _part10Reader;
        private SequenceParser? _sequenceParser;
        private bool _headerParsed;
        private bool _disposed;

        // Track stream position for lazy loading and context dataset for VR resolution
        private long _streamPosition;
        private DicomDataset? _contextDataset;

        // Validation support - tracks validation issues during parsing
        private ValidationResult? _validationResult;

        private const int DefaultBufferSize = 64 * 1024; // 64 KB

        /// <summary>
        /// Gets the File Meta Information (available after reading header).
        /// </summary>
        public DicomDataset? FileMetaInfo => _part10Reader?.FileMetaInfo;

        /// <summary>
        /// Gets the Transfer Syntax (available after reading header).
        /// </summary>
        public TransferSyntax TransferSyntax => _part10Reader?.TransferSyntax ?? TransferSyntax.ImplicitVRLittleEndian;

        /// <summary>
        /// Gets the preamble bytes (available after reading header).
        /// </summary>
        public ReadOnlyMemory<byte> Preamble => _part10Reader?.Preamble ?? ReadOnlyMemory<byte>.Empty;

        /// <summary>
        /// Gets a value indicating whether the file header has been parsed.
        /// </summary>
        public bool IsHeaderParsed => _headerParsed;

        /// <summary>
        /// Gets the source stream for lazy loading. Internal use only.
        /// </summary>
        internal Stream? SourceStream => _disposed ? null : _stream;

        /// <summary>
        /// Gets the validation result from parsing, if validation was enabled.
        /// </summary>
        public ValidationResult? ValidationResult => _validationResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomFileReader"/> class.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="options">Reader options, or null for defaults.</param>
        /// <param name="leaveOpen">True to leave the stream open after disposal; otherwise, false.</param>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        public DicomFileReader(Stream stream, DicomReaderOptions? options = null, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _options = options ?? DicomReaderOptions.Default;
            _leaveOpen = leaveOpen;
            _pool = ArrayPool<byte>.Shared;
            _streamPosition = 0;
        }

        /// <summary>
        /// Read and parse the file header (preamble, DICM, File Meta Information).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the reader has been disposed.</exception>
        /// <exception cref="DicomFileException">Thrown when the file is empty or invalid.</exception>
        public async ValueTask ReadFileMetaInfoAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_headerParsed)
                return;

            // Read enough data for header detection
            _buffer = _pool.Rent(DefaultBufferSize);
#if NETSTANDARD2_0
            _bufferLength = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
#else
            _bufferLength = await _stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
#endif

            if (_bufferLength == 0)
                throw new DicomFileException("Empty file");

            _part10Reader = new Part10Reader(_options);
            _part10Reader.TryParseHeader(_buffer.AsSpan(0, _bufferLength));
            _bufferConsumed = _part10Reader.DatasetStartPosition;
            _streamPosition = _bufferLength;
            _headerParsed = true;
        }

        /// <summary>
        /// Stream dataset elements asynchronously.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An async enumerable of DICOM elements.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the reader has been disposed.</exception>
        public async IAsyncEnumerable<IDicomElement> ReadElementsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (!_headerParsed)
                await ReadFileMetaInfoAsync(ct).ConfigureAwait(false);

            var ts = TransferSyntax;
            var explicitVR = ts.IsExplicitVR;
            var littleEndian = ts.IsLittleEndian;

            // Process buffered data first (data after FMI in initial read)
            int dataStart = _bufferConsumed;
            int dataLength = _bufferLength - dataStart;

            if (dataLength > 0)
            {
                // Parse elements from buffer (sync) and yield each one
                var elements = ParseElementsFromBuffer(_buffer!, dataStart, dataLength, explicitVR, littleEndian);
                foreach (var element in elements)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return element;
                }
            }

            // Continue reading from stream
            while (true)
            {
                ct.ThrowIfCancellationRequested();

#if NETSTANDARD2_0
                _bufferLength = await _stream.ReadAsync(_buffer!, 0, _buffer!.Length, ct).ConfigureAwait(false);
#else
                _bufferLength = await _stream.ReadAsync(_buffer!.AsMemory(), ct).ConfigureAwait(false);
#endif
                if (_bufferLength == 0)
                    break;

                _streamPosition += _bufferLength;

                // Parse elements from buffer (sync) and yield each one
                var elements = ParseElementsFromBuffer(_buffer!, 0, _bufferLength, explicitVR, littleEndian);
                foreach (var element in elements)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return element;
                }
            }
        }

        /// <summary>
        /// Read the complete dataset into memory.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task containing the complete dataset.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the reader has been disposed.</exception>
        public async ValueTask<DicomDataset> ReadDatasetAsync(CancellationToken ct = default)
        {
            var dataset = new DicomDataset();

            await foreach (var element in ReadElementsAsync(ct).ConfigureAwait(false))
            {
                dataset.Add(element);
            }

            return dataset;
        }

        /// <summary>
        /// Parse elements from a byte array buffer (synchronous, returns list to avoid ref struct across yield).
        /// </summary>
        private List<IDicomElement> ParseElementsFromBuffer(byte[] buffer, int offset, int length, bool explicitVR, bool littleEndian)
        {
            var elements = new List<IDicomElement>();
            var reader = new DicomStreamReader(buffer.AsSpan(offset, length), explicitVR, littleEndian, _options);

            // Ensure sequence parser is initialized with correct transfer syntax settings
            _sequenceParser ??= new SequenceParser(explicitVR, littleEndian, _options);

            // Ensure context dataset is initialized for accumulating elements before pixel data
            _contextDataset ??= new DicomDataset();

            while (!reader.IsAtEnd)
            {
                if (!reader.TryReadElementHeader(out var tag, out var vr, out var valueLength))
                    break;

                // Handle sequences (SQ VR or undefined length that might be a sequence)
                if (vr == DicomVR.SQ)
                {
                    // Sequence element - parse with SequenceParser
                    var sequenceBuffer = buffer.AsSpan(offset + reader.Position);

                    if (valueLength == SequenceParser.UndefinedLength)
                    {
                        // Undefined length sequence - find the delimiter to determine content length
                        int contentLength = FindSequenceDelimiter(sequenceBuffer);
                        if (contentLength < 0)
                        {
                            // Could not find delimiter in buffer - skip for now
                            // (Full streaming of huge sequences is a later optimization)
                            break;
                        }

                        var sequence = _sequenceParser.ParseSequence(
                            sequenceBuffer.Slice(0, contentLength),
                            tag,
                            valueLength,
                            parent: null);
                        ValidateElement(sequence);
                        elements.Add(sequence);
                        _contextDataset.Add(sequence);

                        // Skip past the sequence content and delimiter (8 bytes for SequenceDelimitationItem)
                        reader.Skip(contentLength + 8);
                    }
                    else
                    {
                        // Defined length sequence
                        if ((int)valueLength > reader.Remaining)
                            break;

                        var sequence = _sequenceParser.ParseSequence(
                            sequenceBuffer.Slice(0, (int)valueLength),
                            tag,
                            valueLength,
                            parent: null);
                        ValidateElement(sequence);
                        elements.Add(sequence);
                        _contextDataset.Add(sequence);

                        reader.Skip((int)valueLength);
                    }

                    continue;
                }

                // Handle Pixel Data tag (7FE0,0010) based on PixelDataHandling option
                if (tag == DicomTag.PixelData)
                {
                    var pixelDataElement = HandlePixelData(buffer, offset, ref reader, vr, valueLength, littleEndian);
                    if (pixelDataElement != null)
                    {
                        elements.Add(pixelDataElement);
                    }
                    continue;
                }

                // Handle undefined length for non-SQ elements (non-pixel undefined length)
                if (valueLength == SequenceParser.UndefinedLength)
                {
                    // Non-pixel undefined length - find the sequence delimiter and skip past it
                    var remainingBuffer = buffer.AsSpan(offset + reader.Position);
                    int contentLength = FindSequenceDelimiter(remainingBuffer);
                    if (contentLength < 0)
                        break;

                    // Store as binary element with the raw encapsulated data
                    var value = remainingBuffer.Slice(0, contentLength).ToArray();
                    var binaryElement = new DicomBinaryElement(tag, vr, value);
                    elements.Add(binaryElement);
                    _contextDataset.Add(binaryElement);

                    // Skip past content and delimiter
                    reader.Skip(contentLength + 8);
                    continue;
                }

                if (!reader.TryReadValue(valueLength, out var valueSpan))
                    break;

                var regularElement = CreateElement(tag, vr, valueSpan.ToArray());
                ValidateElement(regularElement);
                elements.Add(regularElement);
                _contextDataset.Add(regularElement);
            }

            return elements;
        }

        /// <summary>
        /// Handles pixel data based on the configured PixelDataHandling option.
        /// </summary>
        private DicomPixelDataElement? HandlePixelData(byte[] buffer, int offset, ref DicomStreamReader reader, DicomVR vr, uint valueLength, bool littleEndian)
        {
            var ts = TransferSyntax;
            bool isEncapsulated = valueLength == SequenceParser.UndefinedLength || ts.IsEncapsulated;

            // Build pixel data context for callback mode
            var context = PixelDataContext.FromDataset(_contextDataset!, ts, isEncapsulated, valueLength);

            // Determine actual handling mode
            var handling = _options.PixelDataHandling;
            if (handling == PixelDataHandling.Callback && _options.PixelDataCallback != null)
            {
                handling = _options.PixelDataCallback(context);
            }

            // Resolve VR: OW if BitsAllocated > 8, OB if <= 8 or encapsulated
            var resolvedVR = ResolvePixelDataVR(vr, isEncapsulated);

            // Build PixelDataInfo from context dataset
            var info = PixelDataInfo.FromDataset(_contextDataset!);

            if (isEncapsulated)
            {
                return HandleEncapsulatedPixelData(buffer, offset, ref reader, resolvedVR, info, handling, littleEndian);
            }
            else
            {
                return HandleNativePixelData(buffer, offset, ref reader, resolvedVR, valueLength, info, handling);
            }
        }

        /// <summary>
        /// Handles native (uncompressed) pixel data.
        /// </summary>
        private DicomPixelDataElement? HandleNativePixelData(byte[] buffer, int offset, ref DicomStreamReader reader, DicomVR vr, uint valueLength, PixelDataInfo info, PixelDataHandling handling)
        {
            // Calculate absolute stream offset for the pixel data value
            // _streamPosition tracks total bytes read so far, but we're working within a buffer
            // The current buffer starts at (_streamPosition - _bufferLength) in the file
            long bufferStartInFile = _streamPosition - _bufferLength;
            long pixelDataOffset = bufferStartInFile + offset + reader.Position;

            switch (handling)
            {
                case PixelDataHandling.LoadInMemory:
                    {
                        if (!reader.TryReadValue(valueLength, out var valueSpan))
                            return null;

                        var source = new ImmediatePixelDataSource(valueSpan.ToArray());
                        return new DicomPixelDataElement(source, vr, info, isEncapsulated: false);
                    }

                case PixelDataHandling.LazyLoad:
                    {
                        // For lazy load, we need a seekable stream
                        if (!_stream.CanSeek)
                        {
                            // Fall back to load in memory for non-seekable streams
                            if (!reader.TryReadValue(valueLength, out var valueSpan))
                                return null;

                            var source = new ImmediatePixelDataSource(valueSpan.ToArray());
                            return new DicomPixelDataElement(source, vr, info, isEncapsulated: false);
                        }

                        // Create lazy source with stream reference
                        var lazySource = new LazyPixelDataSource(_stream, pixelDataOffset, valueLength);

                        // Skip past the pixel data in the buffer
                        if (!reader.TrySkipValue(valueLength))
                            return null;

                        return new DicomPixelDataElement(lazySource, vr, info, isEncapsulated: false);
                    }

                case PixelDataHandling.Skip:
                    {
                        // Create skipped source with metadata
                        var skippedSource = new SkippedPixelDataSource(pixelDataOffset, valueLength);

                        // Skip past the pixel data in the buffer
                        if (!reader.TrySkipValue(valueLength))
                            return null;

                        return new DicomPixelDataElement(skippedSource, vr, info, isEncapsulated: false);
                    }

                default:
                    // Unknown handling mode, default to load in memory
                    {
                        if (!reader.TryReadValue(valueLength, out var valueSpan))
                            return null;

                        var source = new ImmediatePixelDataSource(valueSpan.ToArray());
                        return new DicomPixelDataElement(source, vr, info, isEncapsulated: false);
                    }
            }
        }

        /// <summary>
        /// Handles encapsulated (compressed) pixel data.
        /// </summary>
        private DicomPixelDataElement? HandleEncapsulatedPixelData(byte[] buffer, int offset, ref DicomStreamReader reader, DicomVR vr, PixelDataInfo info, PixelDataHandling handling, bool littleEndian)
        {
            // Find the sequence delimiter to determine content length
            var remainingBuffer = buffer.AsSpan(offset + reader.Position);
            int contentLength = FindSequenceDelimiter(remainingBuffer);
            if (contentLength < 0)
                return null;

            // Calculate absolute stream offset
            long bufferStartInFile = _streamPosition - _bufferLength;
            long pixelDataOffset = bufferStartInFile + offset + reader.Position;

            switch (handling)
            {
                case PixelDataHandling.LoadInMemory:
                    {
                        // Parse encapsulated data with FragmentParser
                        var fragmentSequence = FragmentParser.ParseEncapsulated(
                            remainingBuffer.Slice(0, contentLength),
                            DicomTag.PixelData,
                            vr,
                            littleEndian);

                        var source = new ImmediatePixelDataSource(ReadOnlyMemory<byte>.Empty);
                        var element = new DicomPixelDataElement(source, vr, info, isEncapsulated: true, fragmentSequence);

                        // Skip past content and delimiter
                        reader.Skip(contentLength + 8);
                        return element;
                    }

                case PixelDataHandling.LazyLoad:
                    {
                        // For encapsulated data, we still need to parse the fragment structure
                        // to know frame boundaries. Load it immediately for v1 simplification.
                        var fragmentSequence = FragmentParser.ParseEncapsulated(
                            remainingBuffer.Slice(0, contentLength),
                            DicomTag.PixelData,
                            vr,
                            littleEndian);

                        var source = new ImmediatePixelDataSource(ReadOnlyMemory<byte>.Empty);
                        var element = new DicomPixelDataElement(source, vr, info, isEncapsulated: true, fragmentSequence);

                        reader.Skip(contentLength + 8);
                        return element;
                    }

                case PixelDataHandling.Skip:
                    {
                        // Create skipped source with metadata
                        // For encapsulated, we include the full length including delimiter
                        var skippedSource = new SkippedPixelDataSource(pixelDataOffset, contentLength + 8);
                        var element = new DicomPixelDataElement(skippedSource, vr, info, isEncapsulated: true);

                        reader.Skip(contentLength + 8);
                        return element;
                    }

                default:
                    {
                        // Default to load in memory
                        var fragmentSequence = FragmentParser.ParseEncapsulated(
                            remainingBuffer.Slice(0, contentLength),
                            DicomTag.PixelData,
                            vr,
                            littleEndian);

                        var source = new ImmediatePixelDataSource(ReadOnlyMemory<byte>.Empty);
                        var element = new DicomPixelDataElement(source, vr, info, isEncapsulated: true, fragmentSequence);

                        reader.Skip(contentLength + 8);
                        return element;
                    }
            }
        }

        /// <summary>
        /// Resolves the VR for pixel data based on context.
        /// </summary>
        private DicomVR ResolvePixelDataVR(DicomVR declaredVR, bool isEncapsulated)
        {
            // Encapsulated pixel data is always OB
            if (isEncapsulated)
            {
                return DicomVR.OB;
            }

            // If explicit VR and already OB or OW, use it
            if (declaredVR == DicomVR.OB || declaredVR == DicomVR.OW)
            {
                return declaredVR;
            }

            // Resolve based on BitsAllocated from context
            var bitsAllocated = _contextDataset?.BitsAllocated;
            if (bitsAllocated.HasValue)
            {
                return bitsAllocated.Value > 8 ? DicomVR.OW : DicomVR.OB;
            }

            // Default to OW for native pixel data
            return DicomVR.OW;
        }

        /// <summary>
        /// Finds the position of the SequenceDelimitationItem (FFFE,E0DD) in the buffer.
        /// </summary>
        /// <param name="buffer">The buffer to search.</param>
        /// <returns>The offset of the delimiter, or -1 if not found.</returns>
        private int FindSequenceDelimiter(ReadOnlySpan<byte> buffer)
        {
            // Scan for SequenceDelimitationItem (FFFE,E0DD) at depth 0
            // Must properly parse all elements to track nesting depth
            int position = 0;
            int depth = 0;
            bool littleEndian = TransferSyntax.IsLittleEndian;
            bool explicitVR = TransferSyntax.IsExplicitVR;

            while (position + 8 <= buffer.Length)
            {
                ushort group = littleEndian
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position))
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position));
                ushort element = littleEndian
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position + 2))
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position + 2));

                var tag = new DicomTag(group, element);

                // Delimiter tags (group FFFE) always have 4-byte length after tag
                if (group == 0xFFFE)
                {
                    if (tag == DicomTag.SequenceDelimitationItem)
                    {
                        if (depth == 0)
                            return position;
                        // Nested sequence delimiter - decrement depth
                        depth--;
                        position += 8;
                    }
                    else if (tag == DicomTag.Item)
                    {
                        uint itemLength = littleEndian
                            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 4))
                            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 4));
                        position += 8;

                        if (itemLength == SequenceParser.UndefinedLength)
                        {
                            depth++;
                        }
                        else
                        {
                            position += (int)itemLength;
                        }
                    }
                    else if (tag == DicomTag.ItemDelimitationItem)
                    {
                        if (depth > 0)
                            depth--;
                        position += 8;
                    }
                    else
                    {
                        // Unknown FFFE tag - skip 8 bytes
                        position += 8;
                    }
                }
                else
                {
                    // Regular element - parse header to get length
                    int headerSize;
                    uint valueLength;

                    if (explicitVR)
                    {
                        if (position + 8 > buffer.Length)
                            return -1; // Not enough data

                        var vr = DicomVR.FromBytes(buffer.Slice(position + 4, 2));
                        bool isLongVR = vr == DicomVR.OB || vr == DicomVR.OD || vr == DicomVR.OF ||
                                        vr == DicomVR.OL || vr == DicomVR.OV || vr == DicomVR.OW ||
                                        vr == DicomVR.SQ || vr == DicomVR.UC || vr == DicomVR.UN ||
                                        vr == DicomVR.UR || vr == DicomVR.UT || vr == DicomVR.SV ||
                                        vr == DicomVR.UV;

                        if (isLongVR)
                        {
                            if (position + 12 > buffer.Length)
                                return -1; // Not enough data
                            headerSize = 12; // tag(4) + VR(2) + reserved(2) + length(4)
                            valueLength = littleEndian
                                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 8))
                                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 8));
                        }
                        else
                        {
                            headerSize = 8; // tag(4) + VR(2) + length(2)
                            valueLength = littleEndian
                                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position + 6))
                                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position + 6));
                        }

                        // Check for nested sequence with undefined length
                        if (vr == DicomVR.SQ && valueLength == SequenceParser.UndefinedLength)
                        {
                            depth++;
                            position += headerSize;
                            continue;
                        }
                    }
                    else
                    {
                        // Implicit VR: tag(4) + length(4)
                        headerSize = 8;
                        valueLength = littleEndian
                            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 4))
                            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 4));

                        // In implicit VR, we can't know if it's a sequence without dictionary lookup
                        // If undefined length, assume it's a sequence and increment depth
                        if (valueLength == SequenceParser.UndefinedLength)
                        {
                            depth++;
                            position += headerSize;
                            continue;
                        }
                    }

                    position += headerSize;
                    if (valueLength != SequenceParser.UndefinedLength)
                        position += (int)valueLength;
                }
            }

            return -1; // Not found
        }

        /// <summary>
        /// Create an appropriate element type based on VR.
        /// </summary>
        private static IDicomElement CreateElement(DicomTag tag, DicomVR vr, byte[] value)
        {
            if (vr.IsStringVR)
                return new DicomStringElement(tag, vr, value);

            if (vr.IsNumericVR)
                return new DicomNumericElement(tag, vr, value);

            return new DicomBinaryElement(tag, vr, value);
        }

        /// <summary>
        /// Validates an element according to the configured validation profile.
        /// </summary>
        /// <param name="element">The element to validate.</param>
        /// <exception cref="DicomDataException">Thrown when validation fails in strict mode.</exception>
        private void ValidateElement(IDicomElement element)
        {
            var profile = _options.ValidationProfile;
            if (profile == null || profile.Rules.Count == 0)
                return;

            var behavior = profile.GetBehavior(element.Tag);
            if (behavior == ValidationBehavior.Skip)
                return;

            // Build validation context
            var context = new ElementValidationContext
            {
                Tag = element.Tag,
                DeclaredVR = element.VR,
                ExpectedVR = DicomDictionary.Default.GetEntry(element.Tag)?.DefaultVR,
                RawValue = element.RawValue,
                Dataset = _contextDataset ?? new DicomDataset(),
                Encoding = _contextDataset?.Encoding ?? DicomEncoding.Default,
                StreamPosition = _streamPosition,
                IsPrivate = element.Tag.IsPrivate,
                PrivateCreator = null // Could be enhanced to track private creators
            };

            // Run each rule
            foreach (var rule in profile.Rules)
            {
                var issue = rule.Validate(in context);
                if (issue.HasValue)
                {
                    HandleValidationIssue(issue.Value, behavior);
                }
            }
        }

        /// <summary>
        /// Handles a validation issue according to the configured behavior.
        /// </summary>
        private void HandleValidationIssue(ValidationIssue issue, ValidationBehavior behavior)
        {
            // Collect issue if enabled
            if (_options.CollectValidationIssues)
            {
                _validationResult ??= new ValidationResult();
                _validationResult.Add(issue);
            }

            // Invoke callback if set
            if (_options.ValidationCallback != null)
            {
                bool continueProcessing = _options.ValidationCallback(issue);
                if (!continueProcessing)
                {
                    throw new DicomDataException(
                        $"Validation callback aborted parsing: [{issue.Code}] {issue.Message}")
                    {
                        Tag = issue.Tag,
                        VR = issue.DeclaredVR
                    };
                }
            }

            // Handle based on behavior
            if (behavior == ValidationBehavior.Validate && issue.Severity == ValidationSeverity.Error)
            {
                throw new DicomDataException(
                    $"Validation failed: [{issue.Code}] {issue.Message}")
                {
                    Tag = issue.Tag,
                    VR = issue.DeclaredVR
                };
            }
        }

        private void ThrowIfDisposed()
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
                throw new ObjectDisposedException(nameof(DicomFileReader));
#endif
        }

        /// <summary>
        /// Disposes the reader and releases resources.
        /// </summary>
        /// <returns>A task representing the async dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_buffer != null)
            {
                _pool.Return(_buffer);
                _buffer = null;
            }

            if (!_leaveOpen)
            {
#if NETSTANDARD2_0
                _stream.Dispose();
                await Task.CompletedTask.ConfigureAwait(false);
#else
                await _stream.DisposeAsync().ConfigureAwait(false);
#endif
            }
        }
    }
}
