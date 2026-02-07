namespace SharpDicom.Codecs
{
    /// <summary>
    /// Provides explicit codec registration for AOT/trimming compatibility.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class registers all built-in codecs explicitly without reflection.
    /// Call <see cref="RegisterAll"/> at application startup to ensure all codecs
    /// are available for use with <see cref="CodecRegistry"/>.
    /// </para>
    /// <para>
    /// The method is idempotent and thread-safe, so it can be called multiple times
    /// without side effects.
    /// </para>
    /// <para>
    /// Registered codecs:
    /// <list type="bullet">
    /// <item><description>RLE Lossless (1.2.840.10008.1.2.5)</description></item>
    /// <item><description>JPEG Baseline (1.2.840.10008.1.2.4.50)</description></item>
    /// <item><description>JPEG Extended (Process 2,4) (1.2.840.10008.1.2.4.51)</description></item>
    /// <item><description>JPEG Lossless (1.2.840.10008.1.2.4.70)</description></item>
    /// <item><description>JPEG 2000 Lossless (1.2.840.10008.1.2.4.90)</description></item>
    /// <item><description>JPEG 2000 Lossy (1.2.840.10008.1.2.4.91)</description></item>
    /// <item><description>JPEG-LS Lossless (1.2.840.10008.1.2.4.80)</description></item>
    /// <item><description>JPEG-LS Near-Lossless (1.2.840.10008.1.2.4.81)</description></item>
    /// <item><description>HTJ2K Lossless (1.2.840.10008.1.2.4.201)</description></item>
    /// <item><description>HTJ2K Lossless RPCL (1.2.840.10008.1.2.4.202)</description></item>
    /// <item><description>HTJ2K Lossy (1.2.840.10008.1.2.4.203)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class CodecInitializer
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        /// <summary>
        /// Registers all built-in codecs with the CodecRegistry.
        /// </summary>
        /// <remarks>
        /// This method is idempotent - calling it multiple times has no effect.
        /// </remarks>
        public static void RegisterAll()
        {
            if (_initialized)
            {
                return;
            }

            lock (_lock)
            {
                if (_initialized)
                {
                    return;
                }

                // RLE Lossless (from Phase 9)
                CodecRegistry.Register(new Rle.RleCodec());

                // JPEG Baseline (8-bit lossy)
                CodecRegistry.Register(new Jpeg.JpegBaselineCodec());

                // JPEG Extended (Process 2,4) - 8/12-bit lossy
                CodecRegistry.Register(new Jpeg.JpegExtendedCodec());

                // JPEG Lossless (Process 14, SV1)
                CodecRegistry.Register(new JpegLossless.JpegLosslessCodec());

                // JPEG 2000 Lossless
                CodecRegistry.Register(new Jpeg2000.Jpeg2000LosslessCodec());

                // JPEG 2000 Lossy
                CodecRegistry.Register(new Jpeg2000.Jpeg2000LossyCodec());

                // JPEG-LS Lossless
                CodecRegistry.Register(new JpegLs.JpegLsLosslessCodec());

                // JPEG-LS Near-Lossless
                CodecRegistry.Register(new JpegLs.JpegLsNearLosslessCodec());

                // HTJ2K Lossless
                CodecRegistry.Register(new Htj2k.Htj2kLosslessCodec());

                // HTJ2K Lossless RPCL
                CodecRegistry.Register(new Htj2k.Htj2kLosslessRpclCodec());

                // HTJ2K Lossy
                CodecRegistry.Register(new Htj2k.Htj2kLossyCodec());

                _initialized = true;
            }
        }

        /// <summary>
        /// Gets a value indicating whether codecs have been registered.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _initialized;
                }
            }
        }

        /// <summary>
        /// Resets the registration state. For testing purposes only.
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                _initialized = false;
            }

            CodecRegistry.Reset();
        }
    }
}
