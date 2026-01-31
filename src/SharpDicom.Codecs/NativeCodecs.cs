using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpDicom.Codecs;
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
    /// The NativeCodecs class provides a centralized way to initialize and configure
    /// the native codec library. It handles library loading, version checking,
    /// and feature detection.
    /// </para>
    /// <para>
    /// On .NET 5+, the library is automatically initialized when the assembly loads
    /// via a module initializer. This can be disabled by setting the AppContext switch
    /// "SharpDicom.Codecs.DisableAutoInit" to true before any SharpDicom.Codecs code runs.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Check if native codecs are available
    /// if (NativeCodecs.IsAvailable)
    /// {
    ///     Console.WriteLine($"GPU available: {NativeCodecs.GpuAvailable}");
    ///     Console.WriteLine($"SIMD features: {NativeCodecs.ActiveSimdFeatures}");
    /// }
    ///
    /// // Explicit initialization with options
    /// NativeCodecs.Initialize(new NativeCodecOptions
    /// {
    ///     PreferCpu = true,  // Disable GPU even if available
    ///     ForceScalar = true // Disable SIMD optimizations
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static class NativeCodecs
    {
        /// <summary>
        /// Expected native library version. Mismatch will cause initialization to fail.
        /// </summary>
        private const int ExpectedVersion = 1;

        /// <summary>
        /// Initialization state: 0 = not started, 1 = in progress, 2 = complete.
        /// </summary>
        private static int _initializationState;

        /// <summary>
        /// Exception that occurred during initialization, if any.
        /// </summary>
        private static Exception? _initializationException;

        /// <summary>
        /// Lock for initialization synchronization.
        /// </summary>
        private static readonly object _initLock = new object();

        /// <summary>
        /// The native library version, or 0 if not initialized.
        /// </summary>
        private static int _nativeVersion;

        /// <summary>
        /// Available features from the native library.
        /// </summary>
        private static NativeFeatures _availableFeatures;

        /// <summary>
        /// Gets a value indicating whether native codecs are available.
        /// </summary>
        /// <value>
        /// <c>true</c> if the native library was loaded successfully; otherwise, <c>false</c>.
        /// </value>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// Gets a value indicating whether GPU acceleration is available.
        /// </summary>
        /// <value>
        /// <c>true</c> if GPU acceleration (nvJPEG2000) is available; otherwise, <c>false</c>.
        /// </value>
        public static bool GpuAvailable { get; private set; }

        /// <summary>
        /// Gets the active SIMD features detected by the native library.
        /// </summary>
        public static SimdFeatures ActiveSimdFeatures { get; private set; }

        /// <summary>
        /// Gets the native library version, or 0 if not initialized.
        /// </summary>
        public static int NativeVersion => _nativeVersion;

        /// <summary>
        /// Gets or sets a value indicating whether to prefer CPU over GPU for decoding.
        /// </summary>
        /// <remarks>
        /// When set to <c>true</c>, GPU-accelerated codecs will not be used even if available.
        /// This can be useful for debugging or when GPU performance is actually worse for
        /// small images.
        /// </remarks>
        public static bool PreferCpu { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the JPEG codec is enabled.
        /// </summary>
        public static bool EnableJpeg { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the JPEG 2000 codec is enabled.
        /// </summary>
        public static bool EnableJpeg2000 { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the JPEG-LS codec is enabled.
        /// </summary>
        public static bool EnableJpegLs { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether video codecs (H.264/H.265) are enabled.
        /// </summary>
        public static bool EnableVideo { get; set; } = true;

        /// <summary>
        /// Gets the name of the GPU device, if available.
        /// </summary>
        public static string? GpuDeviceName { get; private set; }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Module initializer that auto-initializes native codecs when the assembly loads.
        /// </summary>
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute is intentionally used for auto-init
        [ModuleInitializer]
        internal static void AutoInitialize()
#pragma warning restore CA2255
        {
            // Check if auto-init is disabled
            if (AppContext.TryGetSwitch("SharpDicom.Codecs.DisableAutoInit", out bool disabled) && disabled)
            {
                return;
            }

            try
            {
                // Attempt initialization, but suppress errors
                // User can call Initialize() explicitly to get error details
                Initialize(new NativeCodecOptions { SuppressInitializationErrors = true });
            }
            catch
            {
                // Intentionally suppress - user can call Initialize() for details
            }
        }
#endif

        /// <summary>
        /// Initializes native codecs with default options.
        /// </summary>
        /// <exception cref="NativeCodecException">
        /// Thrown when the native library cannot be loaded or initialized.
        /// </exception>
        public static void Initialize()
        {
            Initialize(null);
        }

        /// <summary>
        /// Initializes native codecs with the specified options.
        /// </summary>
        /// <param name="options">Initialization options, or <c>null</c> for defaults.</param>
        /// <exception cref="NativeCodecException">
        /// Thrown when the native library cannot be loaded or initialized.
        /// </exception>
        public static void Initialize(NativeCodecOptions? options)
        {
            // Fast path: already initialized successfully
            if (_initializationState == 2 && IsAvailable)
            {
                return;
            }

            // Re-throw cached initialization error
            if (_initializationState == 2 && _initializationException != null)
            {
                if (options?.SuppressInitializationErrors != true)
                {
                    throw _initializationException;
                }
                return;
            }

            lock (_initLock)
            {
                // Double-check after acquiring lock
                if (_initializationState == 2)
                {
                    if (_initializationException != null && options?.SuppressInitializationErrors != true)
                    {
                        throw _initializationException;
                    }
                    return;
                }

                // Mark as in-progress
                _initializationState = 1;

                try
                {
                    // Set up custom library resolver
                    SetupDllResolver(options);

                    // Apply options before probing
                    if (options != null)
                    {
                        PreferCpu = options.PreferCpu;
                        if (options.EnableJpeg.HasValue)
                            EnableJpeg = options.EnableJpeg.Value;
                        if (options.EnableJpeg2000.HasValue)
                            EnableJpeg2000 = options.EnableJpeg2000.Value;
                        if (options.EnableJpegLs.HasValue)
                            EnableJpegLs = options.EnableJpegLs.Value;
                        if (options.EnableVideo.HasValue)
                            EnableVideo = options.EnableVideo.Value;
                    }

                    // Probe the native library
                    _nativeVersion = NativeMethods.sharpdicom_version();

                    // Verify version
                    if (_nativeVersion != ExpectedVersion && options?.SkipVersionCheck != true)
                    {
                        throw NativeCodecException.VersionMismatch(ExpectedVersion, _nativeVersion);
                    }

                    // Get available features
                    _availableFeatures = (NativeFeatures)NativeMethods.sharpdicom_features();
                    ActiveSimdFeatures = (SimdFeatures)NativeMethods.sharpdicom_simd_features();

                    // Check GPU availability
                    if ((_availableFeatures & NativeFeatures.Gpu) != 0)
                    {
                        GpuAvailable = NativeMethods.gpu_available() != 0;
                        if (GpuAvailable)
                        {
                            IntPtr deviceNamePtr = NativeMethods.gpu_get_device_name();
                            if (deviceNamePtr != IntPtr.Zero)
                            {
                                GpuDeviceName = Marshal.PtrToStringAnsi(deviceNamePtr);
                            }
                        }
                    }

                    IsAvailable = true;

                    // Register codecs with the CodecRegistry
                    RegisterCodecs();

                    _initializationState = 2;
                }
                catch (DllNotFoundException ex)
                {
                    _initializationException = NativeCodecException.LibraryNotFound(
                        NativeMethods.LibraryName,
                        GetRuntimeIdentifier());
                    _initializationState = 2;

                    if (options?.SuppressInitializationErrors != true)
                    {
                        throw new NativeCodecException(_initializationException.Message, ex);
                    }
                }
                catch (NativeCodecException ex)
                {
                    _initializationException = ex;
                    _initializationState = 2;

                    if (options?.SuppressInitializationErrors != true)
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _initializationException = new NativeCodecException(
                        "Failed to initialize native codecs", ex);
                    _initializationState = 2;

                    if (options?.SuppressInitializationErrors != true)
                    {
                        throw _initializationException;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a specific codec feature is available in the native library.
        /// </summary>
        /// <param name="feature">The feature to check.</param>
        /// <returns><c>true</c> if the feature is available; otherwise, <c>false</c>.</returns>
        public static bool HasFeature(NativeCodecFeature feature)
        {
            if (!IsAvailable)
            {
                return false;
            }

            return feature switch
            {
                NativeCodecFeature.Jpeg => (_availableFeatures & NativeFeatures.Jpeg) != 0 && EnableJpeg,
                NativeCodecFeature.Jpeg2000 => (_availableFeatures & NativeFeatures.Jpeg2000) != 0 && EnableJpeg2000,
                NativeCodecFeature.JpegLs => (_availableFeatures & NativeFeatures.JpegLs) != 0 && EnableJpegLs,
                NativeCodecFeature.Video => (_availableFeatures & NativeFeatures.Video) != 0 && EnableVideo,
                NativeCodecFeature.Gpu => GpuAvailable && !PreferCpu,
                _ => false
            };
        }

        /// <summary>
        /// Gets the last error message from the native library.
        /// </summary>
        /// <returns>The error message, or an empty string if no error.</returns>
        internal static string GetLastError()
        {
            if (!IsAvailable)
            {
                return string.Empty;
            }

            IntPtr ptr = NativeMethods.sharpdicom_last_error();
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        /// <summary>
        /// Throws a NativeCodecException for the given error code, including the native error message.
        /// </summary>
        /// <param name="errorCode">The native error code.</param>
        /// <param name="operation">Description of the operation that failed.</param>
        /// <exception cref="NativeCodecException">Always thrown.</exception>
        internal static void ThrowForError(int errorCode, string operation)
        {
            string nativeMessage = GetLastError();
            throw new NativeCodecException(operation, errorCode, nativeMessage);
        }

        /// <summary>
        /// Resets the initialization state. For testing purposes only.
        /// </summary>
        internal static void Reset()
        {
            lock (_initLock)
            {
                _initializationState = 0;
                _initializationException = null;
                IsAvailable = false;
                GpuAvailable = false;
                GpuDeviceName = null;
                ActiveSimdFeatures = SimdFeatures.None;
                _nativeVersion = 0;
                _availableFeatures = NativeFeatures.None;

                // Reset configuration options to defaults
                PreferCpu = false;
                EnableJpeg = true;
                EnableJpeg2000 = true;
                EnableJpegLs = true;
                EnableVideo = true;
            }
        }

        private static void SetupDllResolver(NativeCodecOptions? options)
        {
#if NET5_0_OR_GREATER
            NativeLibrary.SetDllImportResolver(
                typeof(NativeCodecs).Assembly,
                (libraryName, assembly, searchPath) => DllImportResolver(libraryName, assembly, searchPath, options));
#else
            // On netstandard2.0, we rely on the default P/Invoke resolution
            _ = options; // Suppress unused parameter warning
#endif
        }

        /// <summary>
        /// Gets the runtime identifier for the current platform.
        /// </summary>
        private static string GetRuntimeIdentifier()
        {
#if NET5_0_OR_GREATER
            return RuntimeInformation.RuntimeIdentifier;
#else
            // Construct a reasonable RID for netstandard2.0
            string os;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "win";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "osx";
            else
                os = "linux";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown"
            };

            return $"{os}-{arch}";
#endif
        }

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file",
            Justification = "Fallback to AppContext.BaseDirectory if Location is empty")]
        private static IntPtr DllImportResolver(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath,
            NativeCodecOptions? options)
        {
            if (libraryName != NativeMethods.LibraryName)
            {
                // Let default resolution handle other libraries
                return IntPtr.Zero;
            }

            // Try custom path first if specified
            if (!string.IsNullOrEmpty(options?.CustomLibraryPath))
            {
                if (NativeLibrary.TryLoad(options.CustomLibraryPath, out IntPtr customHandle))
                {
                    return customHandle;
                }
            }

            // Get base directory - handle single-file deployment
            string assemblyDir = assembly.Location;
            if (string.IsNullOrEmpty(assemblyDir))
            {
                // Single-file deployment - use AppContext.BaseDirectory
                assemblyDir = AppContext.BaseDirectory;
            }
            else
            {
                assemblyDir = System.IO.Path.GetDirectoryName(assemblyDir) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(assemblyDir))
            {
                return IntPtr.Zero;
            }

            // Try RID-specific paths
            string rid = RuntimeInformation.RuntimeIdentifier;

            // Try runtimes/{rid}/native/{library}
            string ridPath = System.IO.Path.Combine(assemblyDir, "runtimes", rid, "native", GetLibraryFileName());
            if (NativeLibrary.TryLoad(ridPath, out IntPtr ridHandle))
            {
                return ridHandle;
            }

            // Try native directory next to assembly
            string nativePath = System.IO.Path.Combine(assemblyDir, GetLibraryFileName());
            if (NativeLibrary.TryLoad(nativePath, out IntPtr nativeHandle))
            {
                return nativeHandle;
            }

            // Let default resolution take over
            return IntPtr.Zero;
        }

        private static string GetLibraryFileName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeMethods.LibraryName + ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "lib" + NativeMethods.LibraryName + ".dylib";
            }
            else
            {
                return "lib" + NativeMethods.LibraryName + ".so";
            }
        }
#endif

        /// <summary>
        /// Registers native codec implementations with the CodecRegistry.
        /// </summary>
        /// <remarks>
        /// Native codecs are registered with priority 100, which is higher than
        /// pure C# implementations (priority 50). This means native codecs will
        /// be preferred when available.
        /// </remarks>
        private static void RegisterCodecs()
        {
            // Registration of native codecs with CodecRegistry
            // Priority 100 to take precedence over pure C# implementations (priority 50)

            // JPEG codec registration
            if (HasFeature(NativeCodecFeature.Jpeg))
            {
                CodecRegistry.Register(new NativeJpegCodec(), CodecRegistry.PriorityNative);
            }

            // JPEG 2000 codec registration
            if (HasFeature(NativeCodecFeature.Jpeg2000))
            {
                CodecRegistry.Register(NativeJpeg2000Codec.Lossless, CodecRegistry.PriorityNative);
                CodecRegistry.Register(NativeJpeg2000Codec.Lossy, CodecRegistry.PriorityNative);
            }

            // JPEG-LS codec registration
            if (HasFeature(NativeCodecFeature.JpegLs))
            {
                CodecRegistry.Register(NativeJpegLsCodec.Lossless, CodecRegistry.PriorityNative);
                CodecRegistry.Register(NativeJpegLsCodec.NearLossless, CodecRegistry.PriorityNative);
            }

            // Video codec registration - to be implemented in future plan
            // if (HasFeature(NativeCodecFeature.Video))
            // {
            //     CodecRegistry.Register(new NativeVideoCodec(), CodecRegistry.PriorityNative);
            // }
        }
    }

    /// <summary>
    /// Options for native codec initialization.
    /// </summary>
    public sealed class NativeCodecOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to skip the version check.
        /// </summary>
        /// <remarks>
        /// By default, initialization fails if the native library version doesn't
        /// match the expected version. Set this to <c>true</c> to skip the check.
        /// </remarks>
        public bool SkipVersionCheck { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to force scalar (non-SIMD) code paths.
        /// </summary>
        /// <remarks>
        /// This is primarily useful for debugging or benchmarking.
        /// </remarks>
        public bool ForceScalar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to prefer CPU over GPU.
        /// </summary>
        public bool PreferCpu { get; set; }

        /// <summary>
        /// Gets or sets a custom path to the native library.
        /// </summary>
        /// <remarks>
        /// If set, this path is tried first before the default search paths.
        /// </remarks>
        public string? CustomLibraryPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to suppress initialization errors.
        /// </summary>
        /// <remarks>
        /// When <c>true</c>, initialization errors are suppressed and <see cref="NativeCodecs.IsAvailable"/>
        /// will be <c>false</c>. This is useful for auto-initialization where failure
        /// should not prevent the application from running.
        /// </remarks>
        public bool SuppressInitializationErrors { get; set; }

        /// <summary>
        /// Gets or sets whether the JPEG codec should be enabled.
        /// </summary>
        public bool? EnableJpeg { get; set; }

        /// <summary>
        /// Gets or sets whether the JPEG 2000 codec should be enabled.
        /// </summary>
        public bool? EnableJpeg2000 { get; set; }

        /// <summary>
        /// Gets or sets whether the JPEG-LS codec should be enabled.
        /// </summary>
        public bool? EnableJpegLs { get; set; }

        /// <summary>
        /// Gets or sets whether video codecs should be enabled.
        /// </summary>
        public bool? EnableVideo { get; set; }
    }

    /// <summary>
    /// SIMD feature flags supported by the native library.
    /// </summary>
    [Flags]
    public enum SimdFeatures
    {
        /// <summary>
        /// No SIMD features available.
        /// </summary>
        None = 0,

        /// <summary>
        /// SSE2 instructions available (x86/x64).
        /// </summary>
        Sse2 = 1 << 0,

        /// <summary>
        /// AVX2 instructions available (x86/x64).
        /// </summary>
        Avx2 = 1 << 1,

        /// <summary>
        /// NEON instructions available (ARM).
        /// </summary>
        Neon = 1 << 2,

        /// <summary>
        /// AVX-512 instructions available (x86/x64).
        /// </summary>
        Avx512 = 1 << 3
    }

    /// <summary>
    /// Native codec features that can be queried.
    /// </summary>
    public enum NativeCodecFeature
    {
        /// <summary>
        /// JPEG codec (libjpeg-turbo).
        /// </summary>
        Jpeg,

        /// <summary>
        /// JPEG 2000 codec (OpenJPEG).
        /// </summary>
        Jpeg2000,

        /// <summary>
        /// JPEG-LS codec (CharLS).
        /// </summary>
        JpegLs,

        /// <summary>
        /// Video codecs (H.264/H.265 via FFmpeg).
        /// </summary>
        Video,

        /// <summary>
        /// GPU acceleration (nvJPEG2000).
        /// </summary>
        Gpu
    }
}
