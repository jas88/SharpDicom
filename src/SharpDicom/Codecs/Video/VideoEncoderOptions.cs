using System.Collections.Generic;

namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Configuration options for video encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides presets for common medical imaging scenarios:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Diagnostic"/>: Highest quality for primary diagnosis.</description></item>
    /// <item><description><see cref="Review"/>: Good quality for clinical review.</description></item>
    /// <item><description><see cref="Archive"/>: Balanced quality and compression for long-term storage.</description></item>
    /// </list>
    /// <para>
    /// The <see cref="RawParameters"/> dictionary allows passing codec-specific parameters
    /// directly to the underlying encoder (e.g., FFmpeg x264/x265 options).
    /// </para>
    /// </remarks>
    public sealed class VideoEncoderOptions
    {
        /// <summary>
        /// Gets or sets the video codec to use.
        /// </summary>
        public VideoCodecType Codec { get; init; } = VideoCodecType.H264;

        /// <summary>
        /// Gets or sets the quality preset.
        /// </summary>
        /// <remarks>
        /// Quality presets control the CRF (Constant Rate Factor) value and
        /// encoder-specific tuning parameters. Higher quality produces larger output.
        /// </remarks>
        public VideoQualityPreset Preset { get; init; } = VideoQualityPreset.Diagnostic;

        /// <summary>
        /// Gets or sets the frame rate in frames per second.
        /// </summary>
        /// <remarks>
        /// DICOM commonly uses frame rates from 1 fps (slow cine) to 30 fps (real-time).
        /// Values above 60 fps are unusual in medical imaging.
        /// </remarks>
        public double FrameRate { get; init; } = 30.0;

        /// <summary>
        /// Gets or sets the audio codec to use, or <see cref="AudioCodecType.None"/> for no audio.
        /// </summary>
        public AudioCodecType AudioCodec { get; init; } = AudioCodecType.None;

        /// <summary>
        /// Gets or sets the audio sample rate in Hz.
        /// </summary>
        public int AudioSampleRate { get; init; } = 48000;

        /// <summary>
        /// Gets or sets the number of audio channels.
        /// </summary>
        public int AudioChannels { get; init; } = 2;

        /// <summary>
        /// Gets or sets the hardware acceleration mode.
        /// </summary>
        /// <remarks>
        /// <see cref="HardwareAcceleration.Auto"/> will attempt GPU encoding if available,
        /// falling back to CPU encoding if not. Use <see cref="HardwareAcceleration.ForceCpu"/>
        /// to ensure deterministic output across platforms.
        /// </remarks>
        public HardwareAcceleration HwAccel { get; init; } = HardwareAcceleration.Auto;

        /// <summary>
        /// Gets or sets the Group of Pictures size (number of frames between key frames).
        /// </summary>
        /// <remarks>
        /// A value of 0 uses the encoder's default GOP size.
        /// Smaller values allow faster random access but reduce compression efficiency.
        /// For DICOM video, typical values are 12-30.
        /// </remarks>
        public int GopSize { get; init; }

        /// <summary>
        /// Gets or sets additional codec-specific parameters.
        /// </summary>
        /// <remarks>
        /// These are passed directly to the underlying encoder as key-value pairs.
        /// For example, x264 parameters like "profile=high", "level=4.1", etc.
        /// </remarks>
        public Dictionary<string, string>? RawParameters { get; init; }

        /// <summary>
        /// Gets preset options optimized for diagnostic quality (highest quality, larger files).
        /// </summary>
        /// <remarks>
        /// Uses CRF 18 (near-lossless) with high profile and 4:2:0 chroma subsampling.
        /// Suitable for primary diagnosis and reporting.
        /// </remarks>
        public static VideoEncoderOptions Diagnostic => new() { Preset = VideoQualityPreset.Diagnostic };

        /// <summary>
        /// Gets preset options for clinical review (good quality, moderate file size).
        /// </summary>
        /// <remarks>
        /// Uses CRF 23 with main profile. Suitable for clinical review and comparison.
        /// </remarks>
        public static VideoEncoderOptions Review => new() { Preset = VideoQualityPreset.Review };

        /// <summary>
        /// Gets preset options for archival storage (balanced quality and compression).
        /// </summary>
        /// <remarks>
        /// Uses CRF 28 with baseline profile for maximum compatibility.
        /// Suitable for long-term storage where space is a concern.
        /// </remarks>
        public static VideoEncoderOptions Archive => new() { Preset = VideoQualityPreset.Archive };
    }

    /// <summary>
    /// Specifies the video codec to use for encoding.
    /// </summary>
    public enum VideoCodecType
    {
        /// <summary>
        /// MPEG-2 Video (DICOM Transfer Syntax 1.2.840.10008.1.2.4.100/101).
        /// </summary>
        MPEG2,

        /// <summary>
        /// H.264/AVC (DICOM Transfer Syntax 1.2.840.10008.1.2.4.102-106).
        /// </summary>
        H264,

        /// <summary>
        /// H.265/HEVC (DICOM Transfer Syntax 1.2.840.10008.1.2.4.107-108).
        /// </summary>
        HEVC
    }

    /// <summary>
    /// Quality presets for video encoding.
    /// </summary>
    public enum VideoQualityPreset
    {
        /// <summary>
        /// Highest quality suitable for primary diagnosis (CRF ~18).
        /// </summary>
        Diagnostic,

        /// <summary>
        /// Good quality for clinical review and comparison (CRF ~23).
        /// </summary>
        Review,

        /// <summary>
        /// Balanced quality and compression for archival storage (CRF ~28).
        /// </summary>
        Archive
    }

    /// <summary>
    /// Specifies the audio codec to use when the video contains audio.
    /// </summary>
    public enum AudioCodecType
    {
        /// <summary>
        /// No audio encoding.
        /// </summary>
        None,

        /// <summary>
        /// AAC audio codec.
        /// </summary>
        AAC,

        /// <summary>
        /// Uncompressed PCM audio.
        /// </summary>
        PCM
    }

    /// <summary>
    /// Hardware acceleration mode for video encoding.
    /// </summary>
    public enum HardwareAcceleration
    {
        /// <summary>
        /// Automatically detect and use GPU encoding if available.
        /// </summary>
        Auto,

        /// <summary>
        /// Force CPU-only encoding (software codec).
        /// </summary>
        ForceCpu,

        /// <summary>
        /// Prefer GPU encoding; fail if unavailable.
        /// </summary>
        PreferGpu
    }
}
