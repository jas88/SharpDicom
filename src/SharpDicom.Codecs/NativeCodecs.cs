using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDicom.Codecs.Native.Interop;
#if NET5_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace SharpDicom.Codecs.Native
{
    /// <summary>
    /// Entry point for native codec initialization and feature detection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides the main entry point for initializing and configuring
    /// the native codec library. Call <see cref="Initialize"/> explicitly or rely
    /// on automatic initialization via the ModuleInitializer attribute on .NET 5+.
    /// </para>
    /// <para>
    /// Automatic initialization can be disabled by setting the AppContext switch
    /// "SharpDicom.Codecs.DisableAutoInit" to true before the assembly loads.
    /// </para>
    /// </remarks>
    public static class NativeCodecs
    {
        /// <summary>
        /// Expected native library API version.
        /// </summary>
        public const int ExpectedVersion = 1;

        private static int _initialized;
        private static Exception? _initException;

        /// <summary>
        /// Gets whether native codecs are available and initialized successfully.
        /// </summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// Gets whether GPU acceleration is available.
        /// </summary>
        public static bool GpuAvailable { get; private set; }

        /// <summary>
        /// Gets the detected SIMD features of the native library.
        /// </summary>
        public static SimdFeatures ActiveSimdFeatures { get; private set; }

        /// <summary>
        /// Gets the codec features available in the native library.
        /// </summary>
        public static CodecFeatures AvailableFeatures { get; private set; }

        /// <summary>
        /// Gets or sets whether to prefer CPU over GPU for operations that support both.
        /// </summary>
        /// <remarks>
        /// When true, GPU-capable operations will use CPU implementations even when
        /// GPU is available. This can be useful for debugging or when GPU resources
        /// are needed for other purposes.
        /// </remarks>
        public static bool PreferCpu { get; set; }

        /// <summary>
        /// Gets or sets whether JPEG codec is enabled.
        /// </summary>
        public static bool EnableJpeg { get; set; } = true;

        /// <summary>
        /// Gets or sets whether JPEG 2000 codec is enabled.
        /// </summary>
        public static bool EnableJpeg2000 { get; set; } = true;

        /// <summary>
        /// Gets or sets whether JPEG-LS codec is enabled.
        /// </summary>
        public static bool EnableJpegLs { get; set; } = true;

        /// <summary>
        /// Gets or sets whether GPU acceleration is enabled.
        /// </summary>
        public static bool EnableGpu { get; set; } = true;

#if NET5_0_OR_GREATER
        /// <summary>
        /// Module initializer that attempts to initialize native codecs on assembly load.
        /// </summary>
        /// <remarks>
        /// This is called automatically when the assembly loads on .NET 5+.
        /// Errors are suppressed - call <see cref="Initialize"/> explicitly to see errors.
        /// </remarks>
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-initialization
        [ModuleInitializer]
        internal static void AutoInitialize()
#pragma warning restore CA2255
        {
            if (!AppContext.TryGetSwitch("SharpDicom.Codecs.DisableAutoInit", out var disabled) || !disabled)
            {
                try
                {
                    Initialize();
                }
                catch
                {
                    // Suppress initialization errors in auto-init
                    // User can call Initialize() explicitly for error details
                }
            }
        }
#endif

        /// <summary>
        /// Initializes native codecs. Throws if initialization fails.
        /// </summary>
        /// <param name="options">Optional initialization options.</param>
        /// <exception cref="NativeCodecException">Native library could not be loaded or version mismatch.</exception>
        /// <remarks>
        /// <para>
        /// This method is thread-safe and idempotent - subsequent calls after successful
        /// initialization return immediately. If initialization failed, subsequent calls
        /// will rethrow the original exception.
        /// </para>
        /// </remarks>
        public static void Initialize(NativeCodecOptions? options = null)
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1)
            {
                if (_initException != null)
                    throw _initException;
                return;
            }

            try
            {
                // Set custom resolver if needed
                SetupDllResolver();

                // Verify version
                int version = NativeMethods.GetVersion();
                if (version != ExpectedVersion && !(options?.SkipVersionCheck ?? false))
                {
                    throw new NativeCodecException(
                        $"Native library version mismatch: expected {ExpectedVersion}, got {version}");
                }

                // Detect features
                AvailableFeatures = (CodecFeatures)NativeMethods.GetFeatures();
                ActiveSimdFeatures = (SimdFeatures)NativeMethods.GetSimdFeatures();

                // Check GPU availability
                GpuAvailable = NativeMethods.GpuAvailable() != 0;

                // Apply options
                if (options != null)
                {
                    PreferCpu = options.PreferCpu;
                    if (options.ForceScalar)
                    {
                        // Note: ForceScalar would require native library support
                        // Currently just documented for future use
                    }
                }

                IsAvailable = true;

                // Register codecs with the global registry
                RegisterCodecs();
            }
            catch (DllNotFoundException ex)
            {
                _initException = new NativeCodecException(
                    $"Native library not found. Ensure SharpDicom.Codecs.runtime.{GetRuntimeIdentifier()} is installed.",
                    ex);
                throw _initException;
            }
            catch (BadImageFormatException ex)
            {
                _initException = new NativeCodecException(
                    $"Native library architecture mismatch. Expected {RuntimeInformation.ProcessArchitecture}.",
                    ex);
                throw _initException;
            }
            catch (Exception ex) when (ex is not NativeCodecException)
            {
                _initException = new NativeCodecException("Failed to initialize native codecs", ex);
                throw _initException;
            }
        }

        /// <summary>
        /// Resets initialization state. For testing purposes only.
        /// </summary>
        internal static void Reset()
        {
            _initialized = 0;
            _initException = null;
            IsAvailable = false;
            GpuAvailable = false;
            ActiveSimdFeatures = SimdFeatures.None;
            AvailableFeatures = CodecFeatures.None;
        }

        private static void SetupDllResolver()
        {
#if NET5_0_OR_GREATER
            NativeLibrary.SetDllImportResolver(typeof(NativeCodecs).Assembly, DllImportResolver);
#endif
        }

#if NET5_0_OR_GREATER
#pragma warning disable IL3000 // AppContext.BaseDirectory is the recommended alternative for single-file
        private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "sharpdicom_codecs")
            {
                // Try platform-specific paths
                string? customPath = Environment.GetEnvironmentVariable("SHARPDICOM_NATIVE_PATH");
                if (!string.IsNullOrEmpty(customPath))
                {
                    string fullPath = System.IO.Path.Combine(customPath, GetNativeLibraryName());
                    if (NativeLibrary.TryLoad(fullPath, out IntPtr handle))
                    {
                        return handle;
                    }
                }

                // Try runtimes folder structure (NuGet layout)
                // Use AppContext.BaseDirectory for single-file compatibility
                string baseDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string rid = GetRuntimeIdentifier();
                    string runtimePath = System.IO.Path.Combine(
                        baseDir, "runtimes", rid, "native", GetNativeLibraryName());
                    if (NativeLibrary.TryLoad(runtimePath, out IntPtr handle))
                    {
                        return handle;
                    }
                }
            }

            // Let default resolution handle it
            return IntPtr.Zero;
        }
#pragma warning restore IL3000
#endif

        private static void RegisterCodecs()
        {
            // Registration with priority 100 (above pure C# implementations at 50)
            const int NativePriority = CodecRegistry.NativePriority;

            // Register JPEG codec if available and enabled
            if (EnableJpeg && AvailableFeatures.HasFlag(CodecFeatures.Jpeg))
            {
                CodecRegistry.Register(NativeJpegCodec.CreateBaseline(), NativePriority);
            }

            // Register JPEG 2000 codecs if available and enabled
            if (EnableJpeg2000 && AvailableFeatures.HasFlag(CodecFeatures.Jpeg2000))
            {
                CodecRegistry.Register(NativeJpeg2000Codec.CreateLossless(), NativePriority);
                CodecRegistry.Register(NativeJpeg2000Codec.CreateLossy(), NativePriority);
            }

            // Register JPEG-LS codecs if available and enabled
            if (EnableJpegLs && AvailableFeatures.HasFlag(CodecFeatures.JpegLs))
            {
                CodecRegistry.Register(NativeJpegLsCodec.CreateLossless(), NativePriority);
                CodecRegistry.Register(NativeJpegLsCodec.CreateNearLossless(), NativePriority);
            }
        }

        /// <summary>
        /// Gets the last error message from the native library.
        /// </summary>
        /// <returns>The error message, or an empty string if no error.</returns>
        internal static string GetLastError()
        {
            IntPtr ptr = NativeMethods.GetLastError();
            if (ptr == IntPtr.Zero)
                return string.Empty;

            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        private static string GetRuntimeIdentifier()
        {
            // Build RID from OS and architecture
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                       RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                       RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown"
            };

            return $"{os}-{arch}";
        }

        private static string GetNativeLibraryName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "sharpdicom_codecs.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libsharpdicom_codecs.so";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libsharpdicom_codecs.dylib";

            return "sharpdicom_codecs";
        }
    }

    /// <summary>
    /// Options for native codec initialization.
    /// </summary>
    public sealed class NativeCodecOptions
    {
        /// <summary>
        /// Gets or sets whether to skip version checking.
        /// </summary>
        /// <remarks>
        /// This should only be used for testing or when you know the native library
        /// is compatible despite a version mismatch.
        /// </remarks>
        public bool SkipVersionCheck { get; set; }

        /// <summary>
        /// Gets or sets whether to force scalar (non-SIMD) code paths.
        /// </summary>
        /// <remarks>
        /// This can be useful for debugging or benchmarking SIMD performance.
        /// </remarks>
        public bool ForceScalar { get; set; }

        /// <summary>
        /// Gets or sets whether to prefer CPU over GPU for operations.
        /// </summary>
        public bool PreferCpu { get; set; }
    }

    /// <summary>
    /// SIMD instruction set features detected in the native library.
    /// </summary>
    [Flags]
    public enum SimdFeatures
    {
        /// <summary>No SIMD features.</summary>
        None = 0,

        /// <summary>SSE2 instructions available (x86/x64).</summary>
        Sse2 = 1,

        /// <summary>AVX2 instructions available (x86/x64).</summary>
        Avx2 = 2,

        /// <summary>NEON instructions available (ARM).</summary>
        Neon = 4,

        /// <summary>AVX-512 instructions available (x86/x64).</summary>
        Avx512 = 8
    }

    /// <summary>
    /// Codec features available in the native library.
    /// </summary>
    [Flags]
    public enum CodecFeatures
    {
        /// <summary>No features.</summary>
        None = 0,

        /// <summary>JPEG codec (libjpeg-turbo).</summary>
        Jpeg = 1,

        /// <summary>JPEG 2000 codec (OpenJPEG).</summary>
        Jpeg2000 = 2,

        /// <summary>JPEG-LS codec (CharLS).</summary>
        JpegLs = 4,

        /// <summary>GPU JPEG 2000 decoding (nvJPEG2000).</summary>
        GpuJpeg2000 = 8,

        /// <summary>Video codecs (FFmpeg).</summary>
        Video = 16
    }
}
