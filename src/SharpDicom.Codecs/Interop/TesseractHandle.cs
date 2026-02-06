using System;
using System.Runtime.InteropServices;

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// Safe handle for TessBaseAPI lifecycle management.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps the native TessBaseAPI handle returned by <see cref="TesseractNativeMethods.tess_create"/>.
    /// Ensures proper cleanup via <see cref="TesseractNativeMethods.tess_delete"/> when disposed or finalized.
    /// </para>
    /// <para>
    /// TessBaseAPI handles are NOT thread-safe. Each thread should create and use its own handle.
    /// </para>
    /// </remarks>
    internal sealed class TesseractHandle : SafeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TesseractHandle"/> class.
        /// </summary>
        internal TesseractHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        /// <summary>
        /// Gets a value indicating whether the handle value is invalid.
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>
        /// Creates a new Tesseract handle by calling the native tess_create function.
        /// </summary>
        /// <returns>A new <see cref="TesseractHandle"/> wrapping the native TessBaseAPI.</returns>
        /// <remarks>
        /// When Tesseract is compiled as a stub, tess_create returns NULL,
        /// so <see cref="IsInvalid"/> will be true.
        /// </remarks>
        public static TesseractHandle Create()
        {
            var handle = new TesseractHandle();
            handle.SetHandle(TesseractNativeMethods.tess_create());
            return handle;
        }

        /// <summary>
        /// Executes the code required to free the native handle.
        /// </summary>
        /// <returns>true if the handle is released successfully.</returns>
        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                TesseractNativeMethods.tess_delete(handle);
            }
            return true;
        }
    }
}
