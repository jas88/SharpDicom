using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM Unique Identifier (UID) with zero-allocation inline storage.
    /// </summary>
    /// <remarks>
    /// UIDs are unique identifiers up to 64 characters in length, consisting of digits (0-9) and periods (.).
    /// This struct stores the UID bytes inline without heap allocation.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct DicomUID : IEquatable<DicomUID>
    {
        // 64 bytes of inline storage for UID (8 longs)
        private readonly long _p0;
        private readonly long _p1;
        private readonly long _p2;
        private readonly long _p3;
        private readonly long _p4;
        private readonly long _p5;
        private readonly long _p6;
        private readonly long _p7;
        private readonly byte _length;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomUID"/> struct from a string.
        /// </summary>
        /// <param name="value">The UID string (max 64 characters).</param>
        /// <exception cref="ArgumentException">Thrown when the UID exceeds 64 characters.</exception>
        public DicomUID(string value)
        {
            if (value == null)
            {
                _p0 = _p1 = _p2 = _p3 = _p4 = _p5 = _p6 = _p7 = 0;
                _length = 0;
                return;
            }

            if (value.Length > 64)
                throw new ArgumentException("UID exceeds 64 characters", nameof(value));

            _length = (byte)value.Length;

            // Zero-initialize all fields
            _p0 = _p1 = _p2 = _p3 = _p4 = _p5 = _p6 = _p7 = 0;

            // Copy ASCII bytes into inline storage
            Span<byte> storage = stackalloc byte[64];
#if NETSTANDARD2_0
            var byteCount = Encoding.ASCII.GetByteCount(value);
            if (byteCount > 64)
                throw new ArgumentException("UID exceeds 64 bytes when encoded", nameof(value));

            unsafe
            {
                fixed (char* pChars = value)
                fixed (byte* pBytes = storage)
                {
                    Encoding.ASCII.GetBytes(pChars, value.Length, pBytes, 64);
                }
            }
#else
            Encoding.ASCII.GetBytes(value.AsSpan(), storage);
#endif

            unsafe
            {
                fixed (long* pLong = &_p0)
                {
                    var pByte = (byte*)pLong;
                    for (int i = 0; i < _length; i++)
                    {
                        pByte[i] = storage[i];
                    }
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomUID"/> struct from bytes.
        /// </summary>
        /// <param name="bytes">The UID bytes (max 64 bytes).</param>
        /// <exception cref="ArgumentException">Thrown when the byte span exceeds 64 bytes.</exception>
        public DicomUID(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > 64)
                throw new ArgumentException("UID exceeds 64 bytes", nameof(bytes));

            _length = (byte)bytes.Length;

            // Zero-initialize all fields
            _p0 = _p1 = _p2 = _p3 = _p4 = _p5 = _p6 = _p7 = 0;

            // Copy bytes into inline storage
            unsafe
            {
                fixed (long* pLong = &_p0)
                {
                    var pByte = (byte*)pLong;
                    for (int i = 0; i < _length; i++)
                    {
                        pByte[i] = bytes[i];
                    }
                }
            }
        }

        /// <summary>
        /// Gets the length of the UID in bytes.
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Gets a value indicating whether this UID is empty.
        /// </summary>
        public bool IsEmpty => _length == 0;

        /// <summary>
        /// Gets a value indicating whether this UID has a valid format.
        /// </summary>
        public bool IsValid => ValidateFormat(AsSpan());

        /// <summary>
        /// Returns the UID as a read-only span of bytes.
        /// </summary>
        /// <returns>A read-only span containing the UID bytes.</returns>
        public ReadOnlySpan<byte> AsSpan()
        {
            if (_length == 0)
                return ReadOnlySpan<byte>.Empty;

            unsafe
            {
                fixed (long* pLong = &_p0)
                {
                    return new ReadOnlySpan<byte>(pLong, _length);
                }
            }
        }

        /// <summary>
        /// Returns the string representation of this UID.
        /// </summary>
        /// <returns>The UID as a string.</returns>
        public override string ToString()
        {
            if (_length == 0)
                return string.Empty;

#if NETSTANDARD2_0
            return Encoding.ASCII.GetString(AsSpan().ToArray());
#else
            return Encoding.ASCII.GetString(AsSpan());
#endif
        }

        /// <summary>
        /// Determines whether this UID equals another UID.
        /// </summary>
        /// <param name="other">The UID to compare with.</param>
        /// <returns>true if the UIDs are equal; otherwise, false.</returns>
        public bool Equals(DicomUID other)
        {
            if (_length != other._length)
                return false;

            return AsSpan().SequenceEqual(other.AsSpan());
        }

        /// <summary>
        /// Determines whether this UID equals another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the object is a DicomUID and equals this UID; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is DicomUID other && Equals(other);

        /// <summary>
        /// Returns the hash code for this UID.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            var hash = new System.HashCode();
            hash.AddBytes(AsSpan());
            return hash.ToHashCode();
#else
            var hash = new HashCode();
            hash.AddBytes(AsSpan());
            return hash.ToHashCode();
#endif
        }

        /// <summary>
        /// Determines whether two UIDs are equal.
        /// </summary>
        /// <param name="left">The first UID.</param>
        /// <param name="right">The second UID.</param>
        /// <returns>true if the UIDs are equal; otherwise, false.</returns>
        public static bool operator ==(DicomUID left, DicomUID right) => left.Equals(right);

        /// <summary>
        /// Determines whether two UIDs are not equal.
        /// </summary>
        /// <param name="left">The first UID.</param>
        /// <param name="right">The second UID.</param>
        /// <returns>true if the UIDs are not equal; otherwise, false.</returns>
        public static bool operator !=(DicomUID left, DicomUID right) => !left.Equals(right);

        /// <summary>
        /// Validates the format of a UID byte span.
        /// </summary>
        /// <param name="bytes">The UID bytes to validate.</param>
        /// <returns>true if the UID format is valid; otherwise, false.</returns>
        private static bool ValidateFormat(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 64)
                return false;

            // UIDs consist only of digits (0-9) and periods (.)
            // Each component (separated by .) cannot have leading zeros except for "0" itself
            bool expectingDigit = true;
            bool isFirstInComponent = true;
            bool hasZeroStart = false;

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                if (b == (byte)'.')
                {
                    if (expectingDigit || i == 0 || i == bytes.Length - 1)
                        return false; // Cannot start/end with period, or have consecutive periods

                    expectingDigit = true;
                    isFirstInComponent = true;
                    hasZeroStart = false;
                }
                else if (b >= (byte)'0' && b <= (byte)'9')
                {
                    if (isFirstInComponent && b == (byte)'0')
                    {
                        hasZeroStart = true;
                    }
                    else if (isFirstInComponent)
                    {
                        hasZeroStart = false;
                    }
                    else if (hasZeroStart)
                    {
                        // Leading zero on multi-digit component
                        return false;
                    }

                    expectingDigit = false;
                    isFirstInComponent = false;
                }
                else
                {
                    // Invalid character
                    return false;
                }
            }

            return !expectingDigit; // Must end with a digit
        }

        /// <summary>
        /// Generates a new UID using UUID-based format (2.25.{uuid-as-decimal}).
        /// </summary>
        /// <returns>A new globally unique UID.</returns>
        public static DicomUID Generate()
        {
            var uuid = Guid.NewGuid();
            var bytes = uuid.ToByteArray();

            // Convert UUID bytes to a 128-bit integer (BigInteger)
            // Add a byte to ensure it's always positive
            var bigIntBytes = new byte[17];
            Array.Copy(bytes, 0, bigIntBytes, 0, 16);
            bigIntBytes[16] = 0;

            var bigInt = new BigInteger(bigIntBytes);
            var decimalString = bigInt.ToString();

            return new DicomUID($"2.25.{decimalString}");
        }

        /// <summary>
        /// Generates a new UID based on a root UID with timestamp and random suffix.
        /// </summary>
        /// <param name="root">The root UID prefix.</param>
        /// <returns>A new UID based on the root.</returns>
        public static DicomUID Generate(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Root UID cannot be null or whitespace", nameof(root));

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if NETSTANDARD2_0
            var rng = new Random();
            var random = (((ulong)(uint)rng.Next()) << 32) | (ulong)(uint)rng.Next();
            random &= 0x7FFFFFFFFFFFFFFF; // Positive values only
#else
            var random = (ulong)Random.Shared.NextInt64() & 0x7FFFFFFFFFFFFFFF; // Positive values only
#endif

            return new DicomUID($"{root}.{timestamp}.{random}");
        }

        /// <summary>
        /// Generates a deterministic UID from a name using hash-based generation.
        /// </summary>
        /// <param name="root">The root UID prefix.</param>
        /// <param name="name">The name to hash for deterministic generation.</param>
        /// <returns>A deterministic UID based on the name.</returns>
        public static DicomUID GenerateFromName(string root, string name)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Root UID cannot be null or whitespace", nameof(root));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be null or whitespace", nameof(name));

            byte[] hash;
#if NETSTANDARD2_0
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(name));
            }
#else
            hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
#endif

            // Take first 16 bytes of hash and convert to decimal
            var bigIntBytes = new byte[17];
            Array.Copy(hash, 0, bigIntBytes, 0, 16);
            bigIntBytes[16] = 0;

            var bigInt = new BigInteger(bigIntBytes);
            var decimalString = bigInt.ToString();

            return new DicomUID($"{root}.{decimalString}");
        }
    }
}
