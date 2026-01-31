using System;
using System.Runtime.InteropServices;

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// Safe handle for video decoder state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Video decoders maintain state between frames for efficient decoding.
    /// This safe handle ensures proper cleanup when the decoder is no longer needed.
    /// </para>
    /// </remarks>
    internal sealed class VideoDecoderHandle : SafeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoDecoderHandle"/> class.
        /// </summary>
        public VideoDecoderHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoDecoderHandle"/> class with the specified handle.
        /// </summary>
        /// <param name="existingHandle">The pre-existing handle to wrap.</param>
        /// <param name="ownsHandle">True if the handle should be released when the safe handle is disposed.</param>
        public VideoDecoderHandle(IntPtr existingHandle, bool ownsHandle)
            : base(IntPtr.Zero, ownsHandle)
        {
            SetHandle(existingHandle);
        }

        /// <summary>
        /// Gets a value indicating whether the handle value is invalid.
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>
        /// Executes the code required to free the handle.
        /// </summary>
        /// <returns>true if the handle is released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                NativeMethods.video_decoder_destroy(handle);
            }
            return true;
        }
    }

    /// <summary>
    /// Safe handle for native-allocated memory that must be freed with a specific deallocator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Different codecs allocate memory in different ways, so each codec has its own
    /// free function. This class provides a base for codec-specific memory handles.
    /// </para>
    /// </remarks>
    internal abstract class NativeMemoryHandle : SafeHandle
    {
        /// <summary>
        /// The length of the allocated memory in bytes.
        /// </summary>
        private readonly int _length;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeMemoryHandle"/> class.
        /// </summary>
        /// <param name="pointer">Pointer to the allocated memory.</param>
        /// <param name="length">Length of the allocated memory in bytes.</param>
        protected NativeMemoryHandle(IntPtr pointer, int length)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(pointer);
            _length = length;
        }

        /// <summary>
        /// Gets a value indicating whether the handle value is invalid.
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>
        /// Gets the length of the allocated memory in bytes.
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Gets a span over the allocated memory.
        /// </summary>
        /// <returns>A span representing the allocated memory.</returns>
        public unsafe Span<byte> AsSpan()
        {
            if (IsInvalid || IsClosed)
            {
                return Span<byte>.Empty;
            }
            return new Span<byte>((void*)handle, _length);
        }
    }

    /// <summary>
    /// Safe handle for JPEG-allocated memory.
    /// </summary>
    internal sealed class JpegMemoryHandle : NativeMemoryHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JpegMemoryHandle"/> class.
        /// </summary>
        /// <param name="pointer">Pointer to the allocated memory.</param>
        /// <param name="length">Length of the allocated memory in bytes.</param>
        public JpegMemoryHandle(IntPtr pointer, int length)
            : base(pointer, length)
        {
        }

        /// <summary>
        /// Executes the code required to free the handle.
        /// </summary>
        /// <returns>true if the handle is released successfully.</returns>
        protected override unsafe bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                NativeMethods.jpeg_free((byte*)handle);
            }
            return true;
        }
    }

    /// <summary>
    /// Safe handle for JPEG 2000-allocated memory.
    /// </summary>
    internal sealed class Jpeg2000MemoryHandle : NativeMemoryHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Jpeg2000MemoryHandle"/> class.
        /// </summary>
        /// <param name="pointer">Pointer to the allocated memory.</param>
        /// <param name="length">Length of the allocated memory in bytes.</param>
        public Jpeg2000MemoryHandle(IntPtr pointer, int length)
            : base(pointer, length)
        {
        }

        /// <summary>
        /// Executes the code required to free the handle.
        /// </summary>
        /// <returns>true if the handle is released successfully.</returns>
        protected override unsafe bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                NativeMethods.j2k_free((byte*)handle);
            }
            return true;
        }
    }

    /// <summary>
    /// Safe handle for JPEG-LS-allocated memory.
    /// </summary>
    internal sealed class JpegLsMemoryHandle : NativeMemoryHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JpegLsMemoryHandle"/> class.
        /// </summary>
        /// <param name="pointer">Pointer to the allocated memory.</param>
        /// <param name="length">Length of the allocated memory in bytes.</param>
        public JpegLsMemoryHandle(IntPtr pointer, int length)
            : base(pointer, length)
        {
        }

        /// <summary>
        /// Executes the code required to free the handle.
        /// </summary>
        /// <returns>true if the handle is released successfully.</returns>
        protected override unsafe bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                NativeMethods.jls_free((byte*)handle);
            }
            return true;
        }
    }
}
