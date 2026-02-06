using System;
using System.Runtime.InteropServices;
#if NET7_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// P/Invoke declarations for the Tesseract OCR native wrapper functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These functions wrap the Tesseract 5.x C API for OCR-based burned-in
    /// PHI detection in DICOM pixel data. All functions are exported from the
    /// same sharpdicom_codecs native library.
    /// </para>
    /// <para>
    /// When the native library is compiled without Tesseract support (stub mode):
    /// - tess_create() returns IntPtr.Zero
    /// - tess_init() returns -1
    /// - tess_recognize() returns -1
    /// - tess_available() returns 0
    /// </para>
    /// </remarks>
    internal static unsafe partial class TesseractNativeMethods
    {
        /// <summary>
        /// Native library name (same library as other codecs).
        /// </summary>
        internal const string LibraryName = "sharpdicom_codecs";

#if NET7_0_OR_GREATER
        // =====================================================================
        // Lifecycle Functions
        // =====================================================================

        /// <summary>
        /// Creates a new TessBaseAPI instance.
        /// </summary>
        /// <returns>Handle to the Tesseract instance, or IntPtr.Zero on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "tess_create")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial IntPtr tess_create();

        /// <summary>
        /// Destroys a TessBaseAPI instance.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        [LibraryImport(LibraryName, EntryPoint = "tess_delete")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void tess_delete(IntPtr handle);

        // =====================================================================
        // Initialization and Configuration
        // =====================================================================

        /// <summary>
        /// Initializes Tesseract with language data.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        /// <param name="datapath">Path to tessdata directory.</param>
        /// <param name="language">Language code (e.g. "eng").</param>
        /// <returns>0 on success, -1 on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "tess_init",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int tess_init(IntPtr handle, string? datapath, string? language);

        /// <summary>
        /// Sets the image to recognize.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        /// <param name="imagedata">Raw pixel data.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="bytes_per_pixel">Bytes per pixel (1=grayscale, 3=RGB).</param>
        /// <param name="bytes_per_line">Bytes per line (stride).</param>
        [LibraryImport(LibraryName, EntryPoint = "tess_set_image")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void tess_set_image(
            IntPtr handle, byte* imagedata,
            int width, int height,
            int bytes_per_pixel, int bytes_per_line);

        /// <summary>
        /// Sets the page segmentation mode.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        /// <param name="mode">Page segmentation mode value.</param>
        [LibraryImport(LibraryName, EntryPoint = "tess_set_page_seg_mode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void tess_set_page_seg_mode(IntPtr handle, int mode);

        // =====================================================================
        // Recognition
        // =====================================================================

        /// <summary>
        /// Runs OCR recognition on the current image.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        /// <returns>0 on success, -1 on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "tess_recognize")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int tess_recognize(IntPtr handle);

        /// <summary>
        /// Gets word-level detection results after recognition.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        /// <param name="results">Output array for detection results.</param>
        /// <param name="maxResults">Maximum number of results to fill.</param>
        /// <param name="actualCount">Number of results actually written.</param>
        /// <returns>0 on success, -1 on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "tess_get_detections")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int tess_get_detections(
            IntPtr handle,
            TessDetection* results, int maxResults,
            out int actualCount);

        // =====================================================================
        // Cleanup
        // =====================================================================

        /// <summary>
        /// Frees text returned in detection results.
        /// </summary>
        /// <param name="text">Text pointer from TessDetection.</param>
        [LibraryImport(LibraryName, EntryPoint = "tess_free_text")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void tess_free_text(IntPtr text);

        /// <summary>
        /// Clears recognition results, preserving initialization.
        /// </summary>
        /// <param name="handle">Handle returned by tess_create.</param>
        [LibraryImport(LibraryName, EntryPoint = "tess_clear")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void tess_clear(IntPtr handle);

        // =====================================================================
        // Feature Detection
        // =====================================================================

        /// <summary>
        /// Checks whether Tesseract OCR support is compiled in.
        /// </summary>
        /// <returns>1 if available, 0 if stub.</returns>
        [LibraryImport(LibraryName, EntryPoint = "tess_available")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int tess_available();

#else
        // =====================================================================
        // DllImport versions for netstandard2.0 and older frameworks
        // =====================================================================

        // Lifecycle
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_create")]
        internal static extern IntPtr tess_create();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_delete")]
        internal static extern void tess_delete(IntPtr handle);

        // Initialization
        // UnmanagedType.LPUTF8Str = 48; use raw value for netstandard2.0 compat.
        // CA2101 cannot statically verify the raw integer is UTF-8 safe.
#pragma warning disable CA2101 // Marshalling is UTF-8 via (UnmanagedType)48
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_init")]
        internal static extern int tess_init(IntPtr handle,
            [MarshalAs((UnmanagedType)48)] string? datapath,
            [MarshalAs((UnmanagedType)48)] string? language);
#pragma warning restore CA2101

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_set_image")]
        internal static extern void tess_set_image(
            IntPtr handle, byte* imagedata,
            int width, int height,
            int bytes_per_pixel, int bytes_per_line);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_set_page_seg_mode")]
        internal static extern void tess_set_page_seg_mode(IntPtr handle, int mode);

        // Recognition
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_recognize")]
        internal static extern int tess_recognize(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_get_detections")]
        internal static extern int tess_get_detections(
            IntPtr handle,
            TessDetection* results, int maxResults,
            out int actualCount);

        // Cleanup
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_free_text")]
        internal static extern void tess_free_text(IntPtr text);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_clear")]
        internal static extern void tess_clear(IntPtr handle);

        // Feature Detection
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "tess_available")]
        internal static extern int tess_available();
#endif
    }

    /// <summary>
    /// A single OCR detection result with bounding box, confidence, and text pointer.
    /// </summary>
    /// <remarks>
    /// Maps to the native TessDetectionResult struct. The <see cref="Text"/> field
    /// is a pointer to UTF-8 null-terminated text that must be freed via
    /// <see cref="TesseractNativeMethods.tess_free_text"/>.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TessDetection
    {
        /// <summary>Left edge of bounding box (pixels).</summary>
        public int Left;

        /// <summary>Top edge of bounding box (pixels).</summary>
        public int Top;

        /// <summary>Right edge of bounding box (pixels).</summary>
        public int Right;

        /// <summary>Bottom edge of bounding box (pixels).</summary>
        public int Bottom;

        /// <summary>Recognition confidence (0.0 - 100.0).</summary>
        public float Confidence;

        /// <summary>UTF-8 null-terminated text. Must be freed via tess_free_text.</summary>
        public IntPtr Text;
    }
}
