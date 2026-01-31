using System;
using System.Runtime.InteropServices;
#if NET7_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// P/Invoke declarations for the native sharpdicom_codecs library.
    /// </summary>
    /// <remarks>
    /// This class uses LibraryImport on .NET 7+ for source-generated P/Invoke,
    /// and falls back to DllImport on older runtimes.
    /// </remarks>
    internal static unsafe partial class NativeMethods
    {
        private const string LibName = "sharpdicom_codecs";

#if NET7_0_OR_GREATER
        // ============================================================
        // Core functions
        // ============================================================

        /// <summary>Returns the API version of the native library.</summary>
        [LibraryImport(LibName, EntryPoint = "sharpdicom_version")]
        internal static partial int GetVersion();

        /// <summary>Returns a bitmask of available codec features.</summary>
        [LibraryImport(LibName, EntryPoint = "sharpdicom_features")]
        internal static partial int GetFeatures();

        /// <summary>Returns a bitmask of detected SIMD features.</summary>
        [LibraryImport(LibName, EntryPoint = "sharpdicom_simd_features")]
        internal static partial int GetSimdFeatures();

        /// <summary>Returns a pointer to the last error message (thread-local).</summary>
        [LibraryImport(LibName, EntryPoint = "sharpdicom_last_error")]
        internal static partial IntPtr GetLastError();

        // ============================================================
        // JPEG functions (libjpeg-turbo wrapper)
        // ============================================================

        /// <summary>Decodes JPEG data to raw pixel data.</summary>
        [LibraryImport(LibName, EntryPoint = "jpeg_decode")]
        internal static partial int JpegDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components,
            int colorspace);

        /// <summary>Encodes raw pixel data to JPEG.</summary>
        [LibraryImport(LibName, EntryPoint = "jpeg_encode")]
        internal static partial int JpegEncode(
            byte* input, int width, int height, int components,
            out byte* output, out int outputLen,
            int quality, int subsamp);

        /// <summary>Frees a buffer allocated by JPEG encode.</summary>
        [LibraryImport(LibName, EntryPoint = "jpeg_free")]
        internal static partial void JpegFree(byte* buffer);

        // ============================================================
        // JPEG 2000 functions (OpenJPEG wrapper)
        // ============================================================

        /// <summary>Decodes JPEG 2000 data to raw pixel data.</summary>
        [LibraryImport(LibName, EntryPoint = "j2k_decode")]
        internal static partial int J2kDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample,
            int resolutionLevel);

        /// <summary>Encodes raw pixel data to JPEG 2000.</summary>
        [LibraryImport(LibName, EntryPoint = "j2k_encode")]
        internal static partial int J2kEncode(
            byte* input, int width, int height, int components, int bitsPerSample,
            out byte* output, out int outputLen,
            int lossless, float compressionRatio, int tileSize);

        /// <summary>Frees a buffer allocated by J2K encode.</summary>
        [LibraryImport(LibName, EntryPoint = "j2k_free")]
        internal static partial void J2kFree(byte* buffer);

        // ============================================================
        // JPEG-LS functions (CharLS wrapper)
        // ============================================================

        /// <summary>Decodes JPEG-LS data to raw pixel data.</summary>
        [LibraryImport(LibName, EntryPoint = "jls_decode")]
        internal static partial int JlsDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample);

        /// <summary>Encodes raw pixel data to JPEG-LS.</summary>
        [LibraryImport(LibName, EntryPoint = "jls_encode")]
        internal static partial int JlsEncode(
            byte* input, int width, int height, int components, int bitsPerSample,
            out byte* output, out int outputLen,
            int nearLossless);

        /// <summary>Frees a buffer allocated by JPEG-LS encode.</summary>
        [LibraryImport(LibName, EntryPoint = "jls_free")]
        internal static partial void JlsFree(byte* buffer);

        // ============================================================
        // GPU functions (nvJPEG2000 wrapper)
        // ============================================================

        /// <summary>Returns 1 if GPU acceleration is available, 0 otherwise.</summary>
        [LibraryImport(LibName, EntryPoint = "gpu_available")]
        internal static partial int GpuAvailable();

        /// <summary>Decodes JPEG 2000 data using GPU acceleration.</summary>
        [LibraryImport(LibName, EntryPoint = "gpu_j2k_decode")]
        internal static partial int GpuJ2kDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample);

        // ============================================================
        // Video decoder functions (FFmpeg wrapper)
        // ============================================================

        /// <summary>Creates a video decoder instance.</summary>
        [LibraryImport(LibName, EntryPoint = "video_decoder_create")]
        internal static partial IntPtr VideoDecoderCreate(int codecId, int width, int height);

        /// <summary>Destroys a video decoder instance.</summary>
        [LibraryImport(LibName, EntryPoint = "video_decoder_destroy")]
        internal static partial void VideoDecoderDestroy(IntPtr decoder);

        /// <summary>Decodes a single video frame.</summary>
        [LibraryImport(LibName, EntryPoint = "video_decode_frame")]
        internal static partial int VideoDecodeFrame(
            IntPtr decoder,
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int frameWidth, out int frameHeight);

#else
        // ============================================================
        // Core functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "sharpdicom_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetVersion();

        [DllImport(LibName, EntryPoint = "sharpdicom_features", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetFeatures();

        [DllImport(LibName, EntryPoint = "sharpdicom_simd_features", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetSimdFeatures();

        [DllImport(LibName, EntryPoint = "sharpdicom_last_error", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetLastError();

        // ============================================================
        // JPEG functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "jpeg_decode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JpegDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components,
            int colorspace);

        [DllImport(LibName, EntryPoint = "jpeg_encode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JpegEncode(
            byte* input, int width, int height, int components,
            out byte* output, out int outputLen,
            int quality, int subsamp);

        [DllImport(LibName, EntryPoint = "jpeg_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JpegFree(byte* buffer);

        // ============================================================
        // JPEG 2000 functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "j2k_decode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int J2kDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample,
            int resolutionLevel);

        [DllImport(LibName, EntryPoint = "j2k_encode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int J2kEncode(
            byte* input, int width, int height, int components, int bitsPerSample,
            out byte* output, out int outputLen,
            int lossless, float compressionRatio, int tileSize);

        [DllImport(LibName, EntryPoint = "j2k_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void J2kFree(byte* buffer);

        // ============================================================
        // JPEG-LS functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "jls_decode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JlsDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample);

        [DllImport(LibName, EntryPoint = "jls_encode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JlsEncode(
            byte* input, int width, int height, int components, int bitsPerSample,
            out byte* output, out int outputLen,
            int nearLossless);

        [DllImport(LibName, EntryPoint = "jls_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JlsFree(byte* buffer);

        // ============================================================
        // GPU functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "gpu_available", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GpuAvailable();

        [DllImport(LibName, EntryPoint = "gpu_j2k_decode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GpuJ2kDecode(
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int width, out int height, out int components, out int bitsPerSample);

        // ============================================================
        // Video decoder functions (DllImport fallback)
        // ============================================================

        [DllImport(LibName, EntryPoint = "video_decoder_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr VideoDecoderCreate(int codecId, int width, int height);

        [DllImport(LibName, EntryPoint = "video_decoder_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void VideoDecoderDestroy(IntPtr decoder);

        [DllImport(LibName, EntryPoint = "video_decode_frame", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int VideoDecodeFrame(
            IntPtr decoder,
            byte* input, int inputLen,
            byte* output, int outputLen,
            out int frameWidth, out int frameHeight);
#endif
    }
}
