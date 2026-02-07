/**
 * Video Encoder API (FFmpeg)
 *
 * Provides video frame encoding for MPEG-2, H.264/AVC, and HEVC/H.265
 * video streams suitable for DICOM encapsulation.
 *
 * Mirrors the video_decoder handle pattern from video_wrapper.h.
 * Supports GPU-accelerated encoding with automatic CPU fallback.
 *
 * Thread Safety: Each video_encoder handle is NOT thread-safe.
 * Different handles may be used from different threads concurrently.
 */

#ifndef VIDEO_ENCODER_H
#define VIDEO_ENCODER_H

#include "sharpdicom_codecs.h"
#include "video_wrapper.h"

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Video quality preset constants
 *============================================================================*/

/** Quality presets for encoding (maps to CRF/bitrate defaults) */
#define VIDEO_QUALITY_DIAGNOSTIC    0   /* Highest quality (lossless-ish) */
#define VIDEO_QUALITY_REVIEW        1   /* Good quality for clinical review */
#define VIDEO_QUALITY_ARCHIVE       2   /* Smaller files for long-term storage */

/*============================================================================
 * Audio codec constants
 *============================================================================*/

/** Audio codec identifiers for interleaved audio tracks */
#define AUDIO_CODEC_NONE            0   /* No audio */
#define AUDIO_CODEC_AAC             1   /* AAC-LC audio */
#define AUDIO_CODEC_PCM             2   /* PCM (uncompressed) audio */

/*============================================================================
 * Hardware acceleration preference constants
 *============================================================================*/

/** Hardware acceleration preference for encoding */
#define VIDEO_HWACCEL_AUTO          0   /* Try GPU first, fall back to CPU */
#define VIDEO_HWACCEL_CPU           1   /* Force CPU encoding only */
#define VIDEO_HWACCEL_GPU           2   /* Prefer GPU, error if unavailable */

/*============================================================================
 * Audio sample format constants
 *============================================================================*/

/** Audio sample format for video_encode_audio input */
#define AUDIO_FMT_PCM16            0   /* Signed 16-bit integer PCM */
#define AUDIO_FMT_FLOAT            1   /* 32-bit IEEE float */

/*============================================================================
 * Video encoder handle
 *============================================================================*/

/** Opaque handle to video encoder context */
typedef struct video_encoder video_encoder_t;

/*============================================================================
 * Video encoder configuration
 *============================================================================*/

/**
 * Configuration for creating a video encoder.
 *
 * All fields with sensible defaults (0) can be left zero-initialized.
 * At minimum, codec_id, width, height, and frame_rate must be set.
 */
typedef struct {
    int codec_id;           /**< VIDEO_CODEC_* from video_wrapper.h */
    int width;              /**< Frame width in pixels */
    int height;             /**< Frame height in pixels */
    double frame_rate;      /**< Target frame rate (fps) */
    int bit_depth;          /**< Bits per sample: 8 or 10 (0 = 8) */
    int gop_size;           /**< Keyframe interval in frames (0 = codec default) */
    int quality_preset;     /**< VIDEO_QUALITY_* preset */
    int crf;                /**< Constant rate factor (-1 = use preset default) */
    int bitrate;            /**< Target bitrate in bps (0 = use CRF mode) */
    int hw_accel;           /**< VIDEO_HWACCEL_* preference */
    int audio_codec;        /**< AUDIO_CODEC_* for audio track */
    int audio_sample_rate;  /**< Audio sample rate, e.g. 48000 */
    int audio_channels;     /**< Audio channel count, e.g. 2 */
    int color_space;        /**< 0=auto, 1=monochrome, 2=ycbcr, 3=rgb */
} video_encoder_config_t;

/*============================================================================
 * Video encoder API functions
 *============================================================================*/

/**
 * Creates a video encoder with the specified configuration.
 *
 * The encoder must be destroyed with video_encoder_destroy() when done.
 * For H.264 and HEVC, attempts GPU-accelerated encoding first
 * (NVENC, VideoToolbox, VAAPI) unless hw_accel is set to CPU.
 *
 * @param config       Encoder configuration (must not be NULL)
 * @param encoder_out  Pointer to receive encoder handle
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid config or NULL pointers
 *         - SHARPDICOM_ERR_UNSUPPORTED: Codec not supported / not compiled in
 *         - SHARPDICOM_ERR_OUT_OF_MEMORY: Allocation failed
 *         - SHARPDICOM_ERR_INTERNAL: FFmpeg initialization failed
 */
SHARPDICOM_API int video_encoder_create(
    const video_encoder_config_t* config,
    video_encoder_t** encoder_out
);

/**
 * Encodes a single video frame.
 *
 * Input pixels are converted to the codec's internal format (typically
 * YUV420P) automatically via libswscale.
 *
 * The output buffer is allocated internally and must be freed with
 * video_encoder_free(). Check *packet_available to determine if
 * encoded data was produced (encoding may buffer frames internally).
 *
 * @param encoder          Encoder handle
 * @param pixels           Input pixel data
 * @param pixel_len        Length of pixel data in bytes
 * @param pixel_format     Input pixel format (VIDEO_FORMAT_*)
 * @param output           Pointer to receive output buffer (caller frees with video_encoder_free)
 * @param output_len       Pointer to receive output length
 * @param packet_available Pointer to receive flag (1=data produced, 0=buffered)
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid parameters
 *         - SHARPDICOM_ERR_ENCODE_FAILED: Encoding operation failed
 */
SHARPDICOM_API int video_encode_frame(
    video_encoder_t* encoder,
    const uint8_t* pixels,
    size_t pixel_len,
    int pixel_format,
    uint8_t** output,
    size_t* output_len,
    int* packet_available
);

/**
 * Encodes audio samples for interleaving with the video stream.
 *
 * Audio is encoded using the codec specified in the encoder config.
 * Samples are automatically resampled if the format doesn't match
 * the encoder's expected format.
 *
 * @param encoder       Encoder handle
 * @param samples       Input audio sample data
 * @param samples_len   Length of sample data in bytes
 * @param sample_format Audio sample format (AUDIO_FMT_*)
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid parameters
 *         - SHARPDICOM_ERR_UNSUPPORTED: No audio codec configured
 *         - SHARPDICOM_ERR_ENCODE_FAILED: Audio encoding failed
 */
SHARPDICOM_API int video_encode_audio(
    video_encoder_t* encoder,
    const uint8_t* samples,
    size_t samples_len,
    int sample_format
);

/**
 * Flushes the encoder to produce any remaining buffered packets.
 *
 * Call this after all frames have been submitted. May need to be
 * called multiple times until *packet_available returns 0.
 *
 * The output buffer is allocated internally and must be freed with
 * video_encoder_free().
 *
 * @param encoder          Encoder handle
 * @param output           Pointer to receive output buffer (caller frees with video_encoder_free)
 * @param output_len       Pointer to receive output length
 * @param packet_available Pointer to receive flag (1=more data, 0=flush complete)
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_encoder_flush(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len,
    int* packet_available
);

/**
 * Gets the final muxed output bitstream after flushing.
 *
 * For in-memory encoding, this returns the complete muxed bitstream
 * (with container format headers/trailers). Must be called after
 * video_encoder_flush() returns with *packet_available == 0.
 *
 * The output buffer is allocated internally and must be freed with
 * video_encoder_free().
 *
 * @param encoder    Encoder handle
 * @param output     Pointer to receive output buffer (caller frees with video_encoder_free)
 * @param output_len Pointer to receive output length
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_encoder_get_output(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len
);

/**
 * Destroys a video encoder and frees all resources.
 *
 * @param encoder  Encoder handle (may be NULL)
 */
SHARPDICOM_API void video_encoder_destroy(
    video_encoder_t* encoder
);

/**
 * Frees a buffer allocated by the encoder (output from encode/flush/get_output).
 *
 * @param buffer  Buffer to free (may be NULL)
 */
SHARPDICOM_API void video_encoder_free(
    uint8_t* buffer
);

#ifdef __cplusplus
}
#endif

#endif /* VIDEO_ENCODER_H */
