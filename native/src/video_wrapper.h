/**
 * Video Codec Wrapper API (FFmpeg)
 *
 * Provides video frame decoding for MPEG-2, MPEG-4/H.264, and HEVC/H.265
 * video streams embedded in DICOM files.
 *
 * Thread Safety: Each video_decoder handle is NOT thread-safe.
 * Different handles may be used from different threads concurrently.
 */

#ifndef VIDEO_WRAPPER_H
#define VIDEO_WRAPPER_H

#include "sharpdicom_codecs.h"

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Video codec ID constants
 *============================================================================*/

/** DICOM-supported video codec identifiers */
#define VIDEO_CODEC_MPEG2       1   /* MPEG-2 Video (1.2.840.10008.1.2.4.100/101) */
#define VIDEO_CODEC_MPEG4       2   /* MPEG-4 Part 2 */
#define VIDEO_CODEC_H264        3   /* MPEG-4 Part 10 / H.264 / AVC (1.2.840.10008.1.2.4.102/103) */
#define VIDEO_CODEC_HEVC        4   /* MPEG-4 Part 15 / H.265 / HEVC (1.2.840.10008.1.2.4.104/105) */

/*============================================================================
 * Video pixel format constants
 *============================================================================*/

/** Output pixel format */
#define VIDEO_FORMAT_GRAY8      0   /* 8-bit grayscale */
#define VIDEO_FORMAT_GRAY16     1   /* 16-bit grayscale (little-endian) */
#define VIDEO_FORMAT_RGB24      2   /* 24-bit RGB (interleaved) */
#define VIDEO_FORMAT_YUV420P    3   /* YUV 4:2:0 planar (native format, fastest) */

/*============================================================================
 * Video decoder handle
 *============================================================================*/

/** Opaque handle to video decoder context */
typedef struct video_decoder video_decoder_t;

/*============================================================================
 * Video frame information
 *============================================================================*/

/**
 * Information about a decoded video frame.
 */
typedef struct {
    int width;              /* Frame width in pixels */
    int height;             /* Frame height in pixels */
    int format;             /* Pixel format (VIDEO_FORMAT_*) */
    int64_t pts;            /* Presentation timestamp (time_base units) */
    int64_t dts;            /* Decode timestamp (time_base units) */
    int key_frame;          /* 1 if this is a key frame (I-frame) */
    int frame_number;       /* Sequential frame number (0-based) */
} video_frame_info_t;

/*============================================================================
 * Video stream information
 *============================================================================*/

/**
 * Information about a video stream (from container/codec header).
 */
typedef struct {
    int width;              /* Video width in pixels */
    int height;             /* Video height in pixels */
    int codec_id;           /* VIDEO_CODEC_* identifier */
    int bit_depth;          /* Bits per sample (typically 8 or 10) */
    int frame_count;        /* Total frame count if known, -1 if unknown */
    double frame_rate;      /* Frame rate (fps), 0 if unknown */
    int64_t duration_us;    /* Duration in microseconds, -1 if unknown */
} video_stream_info_t;

/*============================================================================
 * Video decoder API functions
 *============================================================================*/

/**
 * Creates a video decoder for the specified codec.
 *
 * The decoder must be destroyed with video_decoder_destroy() when done.
 *
 * @param codec_id      VIDEO_CODEC_* identifier
 * @param extradata     Codec-specific extradata (SPS/PPS for H.264, etc.)
 * @param extradata_len Length of extradata in bytes (may be 0)
 * @param decoder_out   Pointer to receive decoder handle
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid codec_id
 *         - SHARPDICOM_ERR_UNSUPPORTED: Codec not supported
 *         - SHARPDICOM_ERR_OUT_OF_MEMORY: Allocation failed
 */
SHARPDICOM_API int video_decoder_create(
    int codec_id,
    const uint8_t* extradata,
    size_t extradata_len,
    video_decoder_t** decoder_out
);

/**
 * Gets information about the video stream.
 *
 * Note: Some information may not be available until after the first
 * frame is decoded.
 *
 * @param decoder       Decoder handle
 * @param info          Pointer to receive stream information
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_decoder_get_info(
    video_decoder_t* decoder,
    video_stream_info_t* info
);

/**
 * Decodes a video frame from compressed data.
 *
 * This function may need to be called multiple times before a frame
 * is available (for B-frame reordering). Check *frame_available to
 * determine if a frame was output.
 *
 * @param decoder           Decoder handle
 * @param input             Compressed video data (NAL units or packet)
 * @param input_len         Length of compressed data
 * @param output            Buffer for decoded frame pixels
 * @param output_len        Size of output buffer
 * @param output_format     Desired output format (VIDEO_FORMAT_*)
 * @param frame_info        Pointer to receive frame information (may be NULL)
 * @param frame_available   Pointer to receive flag (1=frame available, 0=need more data)
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid parameters
 *         - SHARPDICOM_ERR_CORRUPT_DATA: Invalid video data
 *         - SHARPDICOM_ERR_DECODE_FAILED: Decode operation failed
 */
SHARPDICOM_API int video_decode_frame(
    video_decoder_t* decoder,
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available
);

/**
 * Flushes the decoder to retrieve any buffered frames.
 *
 * Call this after all input data has been sent to retrieve any
 * remaining frames held in the decoder's buffer (due to B-frame
 * reordering).
 *
 * @param decoder           Decoder handle
 * @param output            Buffer for decoded frame pixels
 * @param output_len        Size of output buffer
 * @param output_format     Desired output format (VIDEO_FORMAT_*)
 * @param frame_info        Pointer to receive frame information (may be NULL)
 * @param frame_available   Pointer to receive flag (1=frame available, 0=no more frames)
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_decoder_flush(
    video_decoder_t* decoder,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available
);

/**
 * Seeks to a specific frame number.
 *
 * This resets the decoder state. The next decode operation should
 * start from a key frame at or before the target position.
 *
 * @param decoder       Decoder handle
 * @param frame_number  Target frame number (0-based)
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_UNSUPPORTED: Seeking not supported
 */
SHARPDICOM_API int video_decoder_seek(
    video_decoder_t* decoder,
    int64_t frame_number
);

/**
 * Gets the required output buffer size for a frame.
 *
 * @param decoder       Decoder handle
 * @param output_format Desired output format (VIDEO_FORMAT_*)
 * @param buffer_size   Pointer to receive required buffer size
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_decoder_get_frame_size(
    video_decoder_t* decoder,
    int output_format,
    size_t* buffer_size
);

/**
 * Resets the decoder to initial state.
 *
 * Use this to reuse the decoder for a new video stream without
 * creating a new handle.
 *
 * @param decoder       Decoder handle
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int video_decoder_reset(
    video_decoder_t* decoder
);

/**
 * Destroys a video decoder and frees all resources.
 *
 * @param decoder       Decoder handle (may be NULL)
 */
SHARPDICOM_API void video_decoder_destroy(
    video_decoder_t* decoder
);

#ifdef __cplusplus
}
#endif

#endif /* VIDEO_WRAPPER_H */
