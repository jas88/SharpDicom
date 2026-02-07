using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// A growable byte buffer for writing BSON documents with deferred-size document patterns.
/// </summary>
/// <remarks>
/// Uses <see cref="ArrayPool{T}"/> for buffers at or above 1024 bytes to reduce GC pressure.
/// All multi-byte writes are little-endian per the BSON specification.
/// </remarks>
internal sealed class BsonDocumentBuffer : IDisposable
{
    private byte[] _buffer;
    private int _position;
    private bool _fromPool;
    private bool _disposed;

    private const int PoolThreshold = 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="BsonDocumentBuffer"/> class.
    /// </summary>
    /// <param name="initialCapacity">The initial buffer capacity in bytes.</param>
    public BsonDocumentBuffer(int initialCapacity = 256)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
#else
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
#endif

        if (initialCapacity >= PoolThreshold)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _fromPool = true;
        }
        else
        {
            _buffer = new byte[initialCapacity == 0 ? 256 : initialCapacity];
            _fromPool = false;
        }

        _position = 0;
    }

    /// <summary>
    /// Gets the current write position in the buffer.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// Gets the current capacity of the underlying buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the written portion of the buffer as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    /// <summary>
    /// Ensures the buffer has room for at least <paramref name="additionalBytes"/> more bytes.
    /// </summary>
    /// <param name="additionalBytes">The number of additional bytes needed.</param>
    public void EnsureCapacity(int additionalBytes)
    {
        int required = _position + additionalBytes;
        if (required <= _buffer.Length)
            return;

        int newCapacity = Math.Max(_buffer.Length * 2, required);
        byte[] newBuffer;
        bool newFromPool;

        if (newCapacity >= PoolThreshold)
        {
            newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            newFromPool = true;
        }
        else
        {
            newBuffer = new byte[newCapacity];
            newFromPool = false;
        }

        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);

        if (_fromPool)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        _buffer = newBuffer;
        _fromPool = newFromPool;
    }

    /// <summary>
    /// Writes a 32-bit integer in little-endian format at the current position.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt32(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    /// <summary>
    /// Writes a 64-bit integer in little-endian format at the current position.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt64(long value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    /// <summary>
    /// Writes a 64-bit double in little-endian format at the current position.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteDouble(double value)
    {
        EnsureCapacity(8);
        long bits = BitConverter.DoubleToInt64Bits(value);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), bits);
        _position += 8;
    }

    /// <summary>
    /// Writes a single byte at the current position.
    /// </summary>
    /// <param name="value">The byte to write.</param>
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    /// <summary>
    /// Writes a span of bytes at the current position.
    /// </summary>
    /// <param name="data">The bytes to write.</param>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    /// <summary>
    /// Writes a UTF-8 C-string (null-terminated, no length prefix) at the current position.
    /// Used for BSON element names (e-name).
    /// </summary>
    /// <param name="value">The string to write.</param>
    public void WriteCString(string value)
    {
#if NETSTANDARD2_0
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        EnsureCapacity(bytes.Length + 1);
        Buffer.BlockCopy(bytes, 0, _buffer, _position, bytes.Length);
        _position += bytes.Length;
#else
        int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        EnsureCapacity(maxBytes + 1);
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.AsSpan(_position));
        _position += written;
#endif
        _buffer[_position++] = 0; // null terminator
    }

    /// <summary>
    /// Writes a BSON string (int32 length + UTF-8 bytes + null terminator) at the current position.
    /// </summary>
    /// <param name="value">The string to write.</param>
    public void WriteBsonString(string value)
    {
#if NETSTANDARD2_0
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int byteLen = bytes.Length;
        EnsureCapacity(4 + byteLen + 1);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), byteLen + 1); // length includes null
        _position += 4;
        Buffer.BlockCopy(bytes, 0, _buffer, _position, byteLen);
        _position += byteLen;
#else
        int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        EnsureCapacity(4 + maxBytes + 1);
        // Reserve 4 bytes for length, write string bytes after
        int stringStart = _position + 4;
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.AsSpan(stringStart));
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), written + 1); // length includes null
        _position = stringStart + written;
#endif
        _buffer[_position++] = 0; // null terminator
    }

    /// <summary>
    /// Patches a 32-bit integer at a specific offset in the buffer.
    /// Used to write deferred document sizes.
    /// </summary>
    /// <param name="offset">The byte offset to patch.</param>
    /// <param name="value">The value to write.</param>
    public void PatchInt32At(int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(offset), value);
    }

    /// <summary>
    /// Begins a new BSON document by writing a placeholder size field.
    /// </summary>
    /// <returns>The offset of the size field, to be passed to <see cref="EndDocument"/>.</returns>
    public int BeginDocument()
    {
        int sizeOffset = _position;
        WriteInt32(0); // placeholder size
        return sizeOffset;
    }

    /// <summary>
    /// Ends a BSON document by writing the null terminator and patching the size field.
    /// </summary>
    /// <param name="sizeOffset">The offset returned by <see cref="BeginDocument"/>.</param>
    public void EndDocument(int sizeOffset)
    {
        WriteByte(0x00); // document terminator
        int documentSize = _position - sizeOffset;
        PatchInt32At(sizeOffset, documentSize);
    }

    /// <summary>
    /// Copies the written portion of the buffer to a new byte array.
    /// </summary>
    /// <returns>A new byte array containing the written data.</returns>
    public byte[] ToArray()
    {
        var result = new byte[_position];
        Buffer.BlockCopy(_buffer, 0, result, 0, _position);
        return result;
    }

    /// <summary>
    /// Copies the written portion of the buffer to the specified <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <param name="target">The target buffer writer.</param>
    public void CopyTo(IBufferWriter<byte> target)
    {
        if (_position == 0)
            return;

        var span = target.GetSpan(_position);
        _buffer.AsSpan(0, _position).CopyTo(span);
        target.Advance(_position);
    }

    /// <summary>
    /// Resets the write position to zero without deallocating the buffer.
    /// </summary>
    public void Reset()
    {
        _position = 0;
    }

    /// <summary>
    /// Returns any pooled buffer to the array pool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_fromPool)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _fromPool = false;
        }

        _buffer = Array.Empty<byte>();
    }
}
