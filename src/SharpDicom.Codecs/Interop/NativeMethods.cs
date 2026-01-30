using System;
using System.Runtime.InteropServices;
#if NET7_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// P/Invoke declarations for the native codec library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides the managed interface to the native sharpdicom_codecs library.
    /// On .NET 7+, it uses LibraryImport for source-generated marshalling.
    /// On older frameworks, it uses DllImport with manual marshalling.
    /// </para>
    /// <para>
    /// The native library follows these conventions:
    /// - Return values: 0 = success, negative = error code
    /// - Output pointers: Must be freed using the appropriate *_free function
    /// - Error messages: Retrieved via sharpdicom_last_error()
    /// </para>
    /// </remarks>
    internal static unsafe partial class NativeMethods
    {
        /// <summary>
        /// Native library name (without platform-specific prefix/suffix).
        /// </summary>
        internal const string LibraryName = "sharpdicom_codecs";

#if NET7_0_OR_GREATER
        // =====================================================================
        // Version and Feature Detection
        // =====================================================================

        /// <summary>
        /// Gets the native library version.
        /// </summary>
        /// <returns>Version number (currently 1).</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_version")]
        internal static partial int sharpdicom_version();

        /// <summary>
        /// Gets the available codec features as a bitmask.
        /// </summary>
        /// <returns>Bitmask of available features.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_features")]
        internal static partial int sharpdicom_features();

        /// <summary>
        /// Gets the active SIMD features as a bitmask.
        /// </summary>
        /// <returns>Bitmask: 1=SSE2, 2=AVX2, 4=NEON.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_simd_features")]
        internal static partial int sharpdicom_simd_features();

        /// <summary>
        /// Gets the last error message.
        /// </summary>
        /// <returns>Pointer to null-terminated UTF-8 string, or null if no error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_last_error")]
        internal static partial IntPtr sharpdicom_last_error();

        // =====================================================================
        // JPEG Codec (libjpeg-turbo)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG compressed data.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg_decode")]
        internal static partial int jpeg_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            int colorspace);

        /// <summary>
        /// Gets JPEG header information without decoding.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg_decode_header")]
        internal static partial int jpeg_decode_header(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int colorspace);

        /// <summary>
        /// Encodes raw pixel data to JPEG.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg_encode")]
        internal static partial int jpeg_encode(
            byte* input,
            int width,
            int height,
            int components,
            out byte* output,
            out int outputLen,
            int quality,
            int subsamp);

        /// <summary>
        /// Frees JPEG-allocated memory.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg_free")]
        internal static partial void jpeg_free(byte* buffer);

        // =====================================================================
        // JPEG 2000 Codec (OpenJPEG)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG 2000 compressed data.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_decode")]
        internal static partial int j2k_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            int resolutionLevel);

        /// <summary>
        /// Gets JPEG 2000 header information without decoding.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_get_info")]
        internal static partial int j2k_get_info(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            out int numResolutions);

        /// <summary>
        /// Encodes raw pixel data to JPEG 2000.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_encode")]
        internal static partial int j2k_encode(
            byte* input,
            int width,
            int height,
            int components,
            int bitsPerSample,
            out byte* output,
            out int outputLen,
            int lossless,
            float compressionRatio,
            int tileSize);

        /// <summary>
        /// Frees JPEG 2000-allocated memory.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_free")]
        internal static partial void j2k_free(byte* buffer);

        // =====================================================================
        // JPEG-LS Codec (CharLS)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG-LS compressed data.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jls_decode")]
        internal static partial int jls_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample);

        /// <summary>
        /// Gets JPEG-LS header information without decoding.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jls_get_info")]
        internal static partial int jls_get_info(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            out int nearLossless);

        /// <summary>
        /// Encodes raw pixel data to JPEG-LS.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jls_encode")]
        internal static partial int jls_encode(
            byte* input,
            int width,
            int height,
            int components,
            int bitsPerSample,
            out byte* output,
            out int outputLen,
            int nearLossless);

        /// <summary>
        /// Frees JPEG-LS-allocated memory.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jls_free")]
        internal static partial void jls_free(byte* buffer);

        // =====================================================================
        // Video Codec (H.264/H.265 via FFmpeg)
        // =====================================================================

        /// <summary>
        /// Creates a video decoder instance.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "video_decoder_create")]
        internal static partial IntPtr video_decoder_create(
            int codecId,
            int width,
            int height,
            byte* extradata,
            int extradataLen);

        /// <summary>
        /// Decodes a video frame.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "video_decode_frame")]
        internal static partial int video_decode_frame(
            IntPtr decoder,
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int frameWidth,
            out int frameHeight);

        /// <summary>
        /// Destroys a video decoder instance.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "video_decoder_destroy")]
        internal static partial void video_decoder_destroy(IntPtr decoder);

        // =====================================================================
        // GPU Acceleration (nvJPEG2000)
        // =====================================================================

        /// <summary>
        /// Checks if GPU acceleration is available.
        /// </summary>
        /// <returns>Non-zero if GPU is available, 0 otherwise.</returns>
        [LibraryImport(LibraryName, EntryPoint = "gpu_available")]
        internal static partial int gpu_available();

        /// <summary>
        /// Gets GPU device information.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "gpu_get_device_name")]
        internal static partial IntPtr gpu_get_device_name();

        /// <summary>
        /// Decodes JPEG 2000 using GPU acceleration.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "gpu_j2k_decode")]
        internal static partial int gpu_j2k_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample);

        /// <summary>
        /// Batch decodes multiple JPEG 2000 images using GPU.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "gpu_j2k_decode_batch")]
        internal static partial int gpu_j2k_decode_batch(
            byte** inputs,
            int* inputLens,
            byte** outputs,
            int* outputLens,
            int count);

#else
        // =====================================================================
        // DllImport versions for netstandard2.0 and older frameworks
        // =====================================================================

        // Version and Feature Detection
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sharpdicom_version")]
        internal static extern int sharpdicom_version();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sharpdicom_features")]
        internal static extern int sharpdicom_features();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sharpdicom_simd_features")]
        internal static extern int sharpdicom_simd_features();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sharpdicom_last_error")]
        internal static extern IntPtr sharpdicom_last_error();

        // JPEG Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg_decode")]
        internal static extern int jpeg_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            int colorspace);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg_decode_header")]
        internal static extern int jpeg_decode_header(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int colorspace);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg_encode")]
        internal static extern int jpeg_encode(
            byte* input,
            int width,
            int height,
            int components,
            out byte* output,
            out int outputLen,
            int quality,
            int subsamp);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg_free")]
        internal static extern void jpeg_free(byte* buffer);

        // JPEG 2000 Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "j2k_decode")]
        internal static extern int j2k_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            int resolutionLevel);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "j2k_get_info")]
        internal static extern int j2k_get_info(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            out int numResolutions);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "j2k_encode")]
        internal static extern int j2k_encode(
            byte* input,
            int width,
            int height,
            int components,
            int bitsPerSample,
            out byte* output,
            out int outputLen,
            int lossless,
            float compressionRatio,
            int tileSize);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "j2k_free")]
        internal static extern void j2k_free(byte* buffer);

        // JPEG-LS Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jls_decode")]
        internal static extern int jls_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jls_get_info")]
        internal static extern int jls_get_info(
            byte* input,
            int inputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample,
            out int nearLossless);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jls_encode")]
        internal static extern int jls_encode(
            byte* input,
            int width,
            int height,
            int components,
            int bitsPerSample,
            out byte* output,
            out int outputLen,
            int nearLossless);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jls_free")]
        internal static extern void jls_free(byte* buffer);

        // Video Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_decoder_create")]
        internal static extern IntPtr video_decoder_create(
            int codecId,
            int width,
            int height,
            byte* extradata,
            int extradataLen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_decode_frame")]
        internal static extern int video_decode_frame(
            IntPtr decoder,
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int frameWidth,
            out int frameHeight);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_decoder_destroy")]
        internal static extern void video_decoder_destroy(IntPtr decoder);

        // GPU Acceleration
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "gpu_available")]
        internal static extern int gpu_available();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "gpu_get_device_name")]
        internal static extern IntPtr gpu_get_device_name();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "gpu_j2k_decode")]
        internal static extern int gpu_j2k_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components,
            out int bitsPerSample);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "gpu_j2k_decode_batch")]
        internal static extern int gpu_j2k_decode_batch(
            byte** inputs,
            int* inputLens,
            byte** outputs,
            int* outputLens,
            int count);
#endif
    }

    /// <summary>
    /// Feature flags from the native library.
    /// </summary>
    [Flags]
    internal enum NativeFeatures
    {
        /// <summary>No features.</summary>
        None = 0,

        /// <summary>JPEG codec available.</summary>
        Jpeg = 1 << 0,

        /// <summary>JPEG 2000 codec available.</summary>
        Jpeg2000 = 1 << 1,

        /// <summary>JPEG-LS codec available.</summary>
        JpegLs = 1 << 2,

        /// <summary>Video codecs available.</summary>
        Video = 1 << 3,

        /// <summary>GPU acceleration available.</summary>
        Gpu = 1 << 4
    }
}
