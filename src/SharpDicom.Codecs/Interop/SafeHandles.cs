using System;
using System.Runtime.InteropServices;

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// Safe handle for video decoder state.
    /// </summary>
    /// <remarks>
    /// This handle wraps the native video decoder instance and ensures
    /// proper cleanup when disposed or finalized.
    /// </remarks>
    internal sealed class VideoDecoderHandle : SafeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VideoDecoderHandle"/> class.
        /// </summary>
        public VideoDecoderHandle() : base(IntPtr.Zero, true)
        {
        }

        /// <summary>
        /// Initializes a new instance with an existing handle.
        /// </summary>
        /// <param name="existingHandle">The existing native handle.</param>
        /// <param name="ownsHandle">Whether this instance owns the handle.</param>
        public VideoDecoderHandle(IntPtr existingHandle, bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
            SetHandle(existingHandle);
        }

        /// <inheritdoc/>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <inheritdoc/>
        protected override bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.VideoDecoderDestroy(handle);
            }
            return true;
        }
    }

    /// <summary>
    /// Safe handle for native codec output buffers.
    /// </summary>
    /// <remarks>
    /// This handle wraps buffers allocated by native encode functions
    /// and ensures they are freed using the appropriate codec-specific free function.
    /// </remarks>
    internal sealed class NativeBufferHandle : SafeHandle
    {
        private readonly NativeBufferType _bufferType;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeBufferHandle"/> class.
        /// </summary>
        /// <param name="buffer">The native buffer pointer.</param>
        /// <param name="bufferType">The type of buffer (determines which free function to call).</param>
        public NativeBufferHandle(IntPtr buffer, NativeBufferType bufferType) : base(IntPtr.Zero, true)
        {
            SetHandle(buffer);
            _bufferType = bufferType;
        }

        /// <inheritdoc/>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <inheritdoc/>
        protected override unsafe bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                switch (_bufferType)
                {
                    case NativeBufferType.Jpeg:
                        NativeMethods.JpegFree((byte*)handle);
                        break;
                    case NativeBufferType.Jpeg2000:
                        NativeMethods.J2kFree((byte*)handle);
                        break;
                    case NativeBufferType.JpegLs:
                        NativeMethods.JlsFree((byte*)handle);
                        break;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Types of native buffers for proper cleanup.
    /// </summary>
    internal enum NativeBufferType
    {
        /// <summary>Buffer allocated by JPEG encode.</summary>
        Jpeg,

        /// <summary>Buffer allocated by JPEG 2000 encode.</summary>
        Jpeg2000,

        /// <summary>Buffer allocated by JPEG-LS encode.</summary>
        JpegLs
    }
}
