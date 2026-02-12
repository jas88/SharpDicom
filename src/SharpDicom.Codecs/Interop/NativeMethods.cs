using System;
using System.Runtime.InteropServices;
#if NET7_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
#endif

namespace SharpDicom.Codecs.Native.Interop
{
    /// <summary>
    /// JPEG 2000 output format.
    /// </summary>
    internal enum J2kFormat : int
    {
        /// <summary>Raw J2K codestream (for DICOM).</summary>
        J2K = 0,
        /// <summary>JP2 file format with box structure.</summary>
        JP2 = 1
    }

    /// <summary>
    /// Decoding options for JPEG 2000.
    /// </summary>
    /// <remarks>
    /// Must match the native J2kDecodeOptions struct in j2k_wrapper.h.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct J2kDecodeOptions
    {
        /// <summary>Reduction factor (0=full, 1=half, 2=quarter, etc.).</summary>
        public int Reduce;
        /// <summary>Maximum quality layer to decode (0 = all layers).</summary>
        public int MaxQualityLayers;

        /// <summary>
        /// Creates default decode options (full resolution, all layers).
        /// </summary>
        public static J2kDecodeOptions Default => new()
        {
            Reduce = 0,
            MaxQualityLayers = 0
        };

        /// <summary>
        /// Creates decode options with the specified reduction factor.
        /// </summary>
        public static J2kDecodeOptions WithReduce(int reduce) => new()
        {
            Reduce = reduce,
            MaxQualityLayers = 0
        };
    }

    /// <summary>
    /// Encoding parameters for JPEG 2000 compression.
    /// </summary>
    /// <remarks>
    /// Must match the native J2kEncodeParams struct in j2k_wrapper.h.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct J2kEncodeParams
    {
        /// <summary>Lossless mode (1=lossless with 5/3 wavelet, 0=lossy with 9/7 wavelet).</summary>
        public int Lossless;
        /// <summary>Compression ratio for lossy mode (e.g., 10 = 10:1 compression, 0 = use quality).</summary>
        public float CompressionRatio;
        /// <summary>Quality for lossy mode (1-100, 100=best, only used if compression_ratio is 0).</summary>
        public float Quality;
        /// <summary>Number of resolution levels (0 = auto based on image size).</summary>
        public int NumResolutions;
        /// <summary>Number of quality layers (0 = single layer).</summary>
        public int NumQualityLayers;
        /// <summary>Tile width (0 = single tile covering whole image).</summary>
        public int TileWidth;
        /// <summary>Tile height (0 = single tile covering whole image).</summary>
        public int TileHeight;
        /// <summary>Output format (J2K or JP2).</summary>
        public J2kFormat Format;
        /// <summary>Code-block width exponent (4-10, 0 = default 6 = 64 pixels).</summary>
        public int CblkWidthExp;
        /// <summary>Code-block height exponent (4-10, 0 = default 6 = 64 pixels).</summary>
        public int CblkHeightExp;
        /// <summary>Progression order: LRCP=0, RLCP=1, RPCL=2, PCRL=3, CPRL=4.</summary>
        public int ProgressionOrder;

        /// <summary>
        /// Creates default lossless encoding parameters.
        /// </summary>
        public static J2kEncodeParams DefaultLossless => new()
        {
            Lossless = 1,
            CompressionRatio = 0,
            Quality = 0,
            NumResolutions = 0,
            NumQualityLayers = 0,
            TileWidth = 0,
            TileHeight = 0,
            Format = J2kFormat.J2K,
            CblkWidthExp = 0,
            CblkHeightExp = 0,
            ProgressionOrder = 0
        };

        /// <summary>
        /// Creates lossy encoding parameters with the given compression ratio.
        /// </summary>
        public static J2kEncodeParams Lossy(float compressionRatio, int tileSize = 0) => new()
        {
            Lossless = 0,
            CompressionRatio = compressionRatio,
            Quality = 0,
            NumResolutions = 0,
            NumQualityLayers = 0,
            TileWidth = tileSize,
            TileHeight = tileSize,
            Format = J2kFormat.J2K,
            CblkWidthExp = 0,
            CblkHeightExp = 0,
            ProgressionOrder = 0
        };
    }

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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int sharpdicom_version();

        /// <summary>
        /// Gets the available codec features as a bitmask.
        /// </summary>
        /// <returns>Bitmask of available features.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_features")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int sharpdicom_features();

        /// <summary>
        /// Gets the active SIMD features as a bitmask.
        /// </summary>
        /// <returns>Bitmask: 1=SSE2, 2=AVX2, 4=NEON.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_simd_features")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int sharpdicom_simd_features();

        /// <summary>
        /// Gets the last error message.
        /// </summary>
        /// <returns>Pointer to null-terminated UTF-8 string, or null if no error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "sharpdicom_last_error")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial IntPtr sharpdicom_last_error();

        // =====================================================================
        // JPEG Codec (libjpeg-turbo)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG compressed data.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg_decode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void jpeg_free(byte* buffer);

        // =====================================================================
        // JPEG 12-bit Codec (libjpeg-turbo 12-bit build)
        // =====================================================================

        /// <summary>
        /// Decodes 12-bit JPEG compressed data.
        /// </summary>
        /// <remarks>
        /// Output samples are 16-bit (uint16_t), so the output buffer must be
        /// width * height * components * 2 bytes.
        /// </remarks>
        [LibraryImport(LibraryName, EntryPoint = "jpeg12_decode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int jpeg12_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components);

        /// <summary>
        /// Encodes raw 12-bit pixel data to JPEG.
        /// </summary>
        /// <remarks>
        /// Input samples are 16-bit (uint16_t), so the input buffer must be
        /// width * height * components * 2 bytes.
        /// </remarks>
        [LibraryImport(LibraryName, EntryPoint = "jpeg12_encode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int jpeg12_encode(
            byte* input,
            int width,
            int height,
            int components,
            out byte* output,
            out int outputLen,
            int quality);

        /// <summary>
        /// Frees memory allocated by jpeg12_encode.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jpeg12_free")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void jpeg12_free(byte* buffer);

        /// <summary>
        /// Checks if 12-bit JPEG support is available in the native library.
        /// </summary>
        /// <returns>Non-zero if 12-bit JPEG is supported, 0 otherwise.</returns>
        [LibraryImport(LibraryName, EntryPoint = "jpeg12_has_support")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int jpeg12_has_support();

        // =====================================================================
        // JPEG 2000 Codec (OpenJPEG)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG 2000 compressed data.
        /// </summary>
        /// <param name="input">Pointer to compressed J2K/JP2 data.</param>
        /// <param name="inputLen">Length of compressed data in bytes.</param>
        /// <param name="output">Pointer to output buffer for decoded pixels.</param>
        /// <param name="outputLen">Size of output buffer in bytes.</param>
        /// <param name="options">Decode options (can be null for defaults).</param>
        /// <param name="outWidth">Output: Actual decoded width.</param>
        /// <param name="outHeight">Output: Actual decoded height.</param>
        /// <param name="outComponents">Output: Number of components.</param>
        /// <returns>0 on success, negative error code on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "j2k_decode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int j2k_decode(
            byte* input,
            nuint inputLen,
            byte* output,
            nuint outputLen,
            J2kDecodeOptions* options,
            int* outWidth,
            int* outHeight,
            int* outComponents);

        /// <summary>
        /// Gets JPEG 2000 header information without decoding.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_get_info")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        /// <param name="input">Pointer to raw pixel data (component-interleaved).</param>
        /// <param name="inputLen">Length of input data in bytes.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="numComponents">Number of components (1, 3, or 4).</param>
        /// <param name="bitsPerComponent">Bits per component (8, 12, or 16).</param>
        /// <param name="isSigned">Whether samples are signed.</param>
        /// <param name="encodingParams">Encoding parameters (can be null for defaults).</param>
        /// <param name="output">Pointer to output buffer for compressed data.</param>
        /// <param name="outputLen">Size of output buffer in bytes.</param>
        /// <param name="outSize">Output: Actual size of compressed data.</param>
        /// <returns>0 on success, negative error code on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "j2k_encode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int j2k_encode(
            byte* input,
            nuint inputLen,
            int width,
            int height,
            int numComponents,
            int bitsPerComponent,
            int isSigned,
            J2kEncodeParams* encodingParams,
            byte* output,
            nuint outputLen,
            nuint* outSize);

        /// <summary>
        /// Frees JPEG 2000-allocated memory.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "j2k_free")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void j2k_free(byte* buffer);

        // =====================================================================
        // JPEG-LS Codec (CharLS)
        // =====================================================================

        /// <summary>
        /// Decodes JPEG-LS compressed data.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "jls_decode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void jls_free(byte* buffer);

        // =====================================================================
        // Video Codec (H.264/H.265 via FFmpeg)
        // =====================================================================

        /// <summary>
        /// Creates a video decoder instance.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "video_decoder_create")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void video_decoder_destroy(IntPtr decoder);

        // =====================================================================
        // Video Encoder (H.264/H.265/MPEG-2 via FFmpeg)
        // =====================================================================

        /// <summary>
        /// Creates a video encoder instance with the specified configuration.
        /// </summary>
        /// <param name="config">Pointer to a VideoEncoderConfig struct.</param>
        /// <returns>Opaque encoder handle, or IntPtr.Zero on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "video_encoder_create")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial IntPtr video_encoder_create(VideoEncoderConfig* config);

        /// <summary>
        /// Encodes a single video frame.
        /// </summary>
        /// <param name="encoder">Encoder handle from video_encoder_create.</param>
        /// <param name="pixels">Pointer to raw pixel data (RGB24 or YUV420P).</param>
        /// <param name="pixelsLen">Length of pixel data in bytes.</param>
        /// <param name="pixelFormat">Pixel format: 0=RGB24, 1=Gray8, 2=Gray16, 3=YUV420P.</param>
        /// <returns>0 on success, negative on error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "video_encode_frame")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int video_encode_frame(
            IntPtr encoder,
            byte* pixels,
            int pixelsLen,
            int pixelFormat);

        /// <summary>
        /// Encodes audio samples to be muxed with the video stream.
        /// </summary>
        /// <param name="encoder">Encoder handle.</param>
        /// <param name="samples">Pointer to audio sample data.</param>
        /// <param name="samplesLen">Length of audio data in bytes.</param>
        /// <param name="sampleFormat">Sample format: 0=PCM16, 1=IeeeFloat.</param>
        /// <returns>0 on success, negative on error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "video_encode_audio")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int video_encode_audio(
            IntPtr encoder,
            byte* samples,
            int samplesLen,
            int sampleFormat);

        /// <summary>
        /// Flushes the encoder, finalizing the output bitstream.
        /// </summary>
        /// <param name="encoder">Encoder handle.</param>
        /// <returns>0 on success, negative on error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "video_encoder_flush")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int video_encoder_flush(IntPtr encoder);

        /// <summary>
        /// Gets the encoded output data after flushing.
        /// </summary>
        /// <param name="encoder">Encoder handle.</param>
        /// <param name="output">Receives pointer to output data (owned by native library).</param>
        /// <param name="outputLen">Receives length of output data in bytes.</param>
        /// <returns>0 on success, negative on error.</returns>
        [LibraryImport(LibraryName, EntryPoint = "video_encoder_get_output")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int video_encoder_get_output(
            IntPtr encoder,
            out byte* output,
            out int outputLen);

        /// <summary>
        /// Destroys a video encoder instance and releases all resources.
        /// </summary>
        /// <param name="encoder">Encoder handle to destroy.</param>
        [LibraryImport(LibraryName, EntryPoint = "video_encoder_destroy")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void video_encoder_destroy(IntPtr encoder);

        /// <summary>
        /// Frees output buffer returned by video_encoder_get_output.
        /// </summary>
        /// <param name="buffer">Pointer to free.</param>
        [LibraryImport(LibraryName, EntryPoint = "video_encoder_free")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void video_encoder_free(byte* buffer);

        // =====================================================================
        // stb_image (Image Loading)
        // =====================================================================

        /// <summary>
        /// Loads an image from memory using stb_image.
        /// </summary>
        /// <param name="data">Pointer to image file data (PNG, JPEG, BMP, TGA, etc.).</param>
        /// <param name="dataLen">Length of image file data in bytes.</param>
        /// <param name="desiredChannels">Desired channel count (0=auto, 1=gray, 3=RGB, 4=RGBA).</param>
        /// <param name="width">Receives the image width.</param>
        /// <param name="height">Receives the image height.</param>
        /// <param name="channels">Receives the actual number of channels.</param>
        /// <returns>Pointer to decoded pixel data, or null on failure.</returns>
        [LibraryImport(LibraryName, EntryPoint = "stbi_load_from_memory_wrapper")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial byte* stbi_load_from_memory_wrapper(
            byte* data,
            int dataLen,
            int desiredChannels,
            out int width,
            out int height,
            out int channels);

        /// <summary>
        /// Frees memory allocated by stbi_load_from_memory_wrapper.
        /// </summary>
        /// <param name="pixels">Pointer to free.</param>
        [LibraryImport(LibraryName, EntryPoint = "stbi_free_wrapper")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void stbi_free_wrapper(byte* pixels);

        // =====================================================================
        // GPU Acceleration (nvJPEG2000)
        // =====================================================================

        /// <summary>
        /// Checks if GPU acceleration is available.
        /// </summary>
        /// <returns>Non-zero if GPU is available, 0 otherwise.</returns>
        [LibraryImport(LibraryName, EntryPoint = "gpu_available")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int gpu_available();

        /// <summary>
        /// Gets GPU device information.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "gpu_get_device_name")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial IntPtr gpu_get_device_name();

        /// <summary>
        /// Decodes JPEG 2000 using GPU acceleration.
        /// </summary>
        [LibraryImport(LibraryName, EntryPoint = "gpu_j2k_decode")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
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

        // JPEG 12-bit Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg12_decode")]
        internal static extern int jpeg12_decode(
            byte* input,
            int inputLen,
            byte* output,
            int outputLen,
            out int width,
            out int height,
            out int components);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg12_encode")]
        internal static extern int jpeg12_encode(
            byte* input,
            int width,
            int height,
            int components,
            out byte* output,
            out int outputLen,
            int quality);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg12_free")]
        internal static extern void jpeg12_free(byte* buffer);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "jpeg12_has_support")]
        internal static extern int jpeg12_has_support();

        // JPEG 2000 Codec
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "j2k_decode")]
        internal static extern int j2k_decode(
            byte* input,
            UIntPtr inputLen,
            byte* output,
            UIntPtr outputLen,
            J2kDecodeOptions* options,
            int* outWidth,
            int* outHeight,
            int* outComponents);

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
            UIntPtr inputLen,
            int width,
            int height,
            int numComponents,
            int bitsPerComponent,
            int isSigned,
            J2kEncodeParams* encodingParams,
            byte* output,
            UIntPtr outputLen,
            UIntPtr* outSize);

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

        // Video Encoder
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encoder_create")]
        internal static extern IntPtr video_encoder_create(VideoEncoderConfig* config);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encode_frame")]
        internal static extern int video_encode_frame(
            IntPtr encoder,
            byte* pixels,
            int pixelsLen,
            int pixelFormat);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encode_audio")]
        internal static extern int video_encode_audio(
            IntPtr encoder,
            byte* samples,
            int samplesLen,
            int sampleFormat);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encoder_flush")]
        internal static extern int video_encoder_flush(IntPtr encoder);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encoder_get_output")]
        internal static extern int video_encoder_get_output(
            IntPtr encoder,
            out byte* output,
            out int outputLen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encoder_destroy")]
        internal static extern void video_encoder_destroy(IntPtr encoder);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "video_encoder_free")]
        internal static extern void video_encoder_free(byte* buffer);

        // stb_image
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "stbi_load_from_memory_wrapper")]
        internal static extern byte* stbi_load_from_memory_wrapper(
            byte* data,
            int dataLen,
            int desiredChannels,
            out int width,
            out int height,
            out int channels);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "stbi_free_wrapper")]
        internal static extern void stbi_free_wrapper(byte* pixels);

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
        Gpu = 1 << 4,

        /// <summary>Tesseract OCR available.</summary>
        Tesseract = 1 << 5,

        /// <summary>12-bit JPEG codec available (libjpeg-turbo 12-bit build).</summary>
        Jpeg12Bit = 1 << 9,

        /// <summary>Video encoder available (FFmpeg encoding support).</summary>
        VideoEnc = 1 << 10,

        /// <summary>stb_image available for loading common image formats.</summary>
        StbImage = 1 << 11
    }

    /// <summary>
    /// Configuration struct passed to video_encoder_create.
    /// Must match the native VideoEncoderConfig layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VideoEncoderConfig
    {
        /// <summary>Codec ID: 0=MPEG2, 1=H264, 2=HEVC.</summary>
        public int CodecId;

        /// <summary>Frame width in pixels.</summary>
        public int Width;

        /// <summary>Frame height in pixels.</summary>
        public int Height;

        /// <summary>Frame rate numerator (e.g. 30 for 30fps).</summary>
        public int FrameRateNum;

        /// <summary>Frame rate denominator (e.g. 1 for 30fps).</summary>
        public int FrameRateDen;

        /// <summary>Quality preset: 0=Diagnostic, 1=Review, 2=Archive.</summary>
        public int QualityPreset;

        /// <summary>GOP size (0 = encoder default).</summary>
        public int GopSize;

        /// <summary>Hardware acceleration: 0=Auto, 1=ForceCpu, 2=PreferGpu.</summary>
        public int HwAccel;

        /// <summary>Audio codec: 0=None, 1=AAC, 2=PCM.</summary>
        public int AudioCodec;

        /// <summary>Audio sample rate in Hz (e.g. 48000).</summary>
        public int AudioSampleRate;

        /// <summary>Number of audio channels (e.g. 2).</summary>
        public int AudioChannels;
    }
}
