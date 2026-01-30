/**
 * Video Codec Wrapper Implementation (FFmpeg)
 *
 * Wraps FFmpeg libavcodec for video frame decoding.
 * Supports MPEG-2, MPEG-4/H.264, and HEVC/H.265.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "video_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>

#ifdef SHARPDICOM_HAS_FFMPEG
#include <libavcodec/avcodec.h>
#include <libavutil/frame.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
#endif

/*============================================================================
 * Internal helper: Set error message
 *============================================================================*/

/* Forward declaration from sharpdicom_codecs.c */
extern void set_error(const char* message);

/**
 * Format and set an error message.
 */
static void set_error_fmt(const char* format, ...) {
    char buffer[256];
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    set_error(buffer);
}

#ifdef SHARPDICOM_HAS_FFMPEG

/*============================================================================
 * Video decoder context structure
 *============================================================================*/

struct video_decoder {
    AVCodecContext* codec_ctx;      /* FFmpeg codec context */
    const AVCodec* codec;           /* FFmpeg codec descriptor */
    AVFrame* frame;                 /* Decoded frame buffer */
    AVPacket* packet;               /* Input packet buffer */
    struct SwsContext* sws_ctx;     /* Pixel format converter */
    int codec_id;                   /* VIDEO_CODEC_* identifier */
    int frame_number;               /* Sequential frame counter */
    int width;                      /* Video width (from codec) */
    int height;                     /* Video height (from codec) */
    int last_output_format;         /* Last requested output format */
};

/*============================================================================
 * Codec ID mapping
 *============================================================================*/

/**
 * Map VIDEO_CODEC_* to FFmpeg AVCodecID.
 */
static enum AVCodecID video_codec_to_ffmpeg(int codec_id) {
    switch (codec_id) {
        case VIDEO_CODEC_MPEG2:
            return AV_CODEC_ID_MPEG2VIDEO;
        case VIDEO_CODEC_MPEG4:
            return AV_CODEC_ID_MPEG4;
        case VIDEO_CODEC_H264:
            return AV_CODEC_ID_H264;
        case VIDEO_CODEC_HEVC:
            return AV_CODEC_ID_HEVC;
        default:
            return AV_CODEC_ID_NONE;
    }
}

/**
 * Get codec name for error messages.
 */
static const char* video_codec_name(int codec_id) {
    switch (codec_id) {
        case VIDEO_CODEC_MPEG2:
            return "MPEG-2";
        case VIDEO_CODEC_MPEG4:
            return "MPEG-4";
        case VIDEO_CODEC_H264:
            return "H.264";
        case VIDEO_CODEC_HEVC:
            return "HEVC";
        default:
            return "Unknown";
    }
}

/*============================================================================
 * Pixel format conversion
 *============================================================================*/

/**
 * Map VIDEO_FORMAT_* to FFmpeg AVPixelFormat.
 */
static enum AVPixelFormat video_format_to_ffmpeg(int format) {
    switch (format) {
        case VIDEO_FORMAT_GRAY8:
            return AV_PIX_FMT_GRAY8;
        case VIDEO_FORMAT_GRAY16:
            return AV_PIX_FMT_GRAY16LE;
        case VIDEO_FORMAT_RGB24:
            return AV_PIX_FMT_RGB24;
        case VIDEO_FORMAT_YUV420P:
            return AV_PIX_FMT_YUV420P;
        default:
            return AV_PIX_FMT_NONE;
    }
}

/**
 * Calculate output buffer size for given format.
 */
static size_t calculate_frame_size(int width, int height, int format) {
    switch (format) {
        case VIDEO_FORMAT_GRAY8:
            return (size_t)width * height;
        case VIDEO_FORMAT_GRAY16:
            return (size_t)width * height * 2;
        case VIDEO_FORMAT_RGB24:
            return (size_t)width * height * 3;
        case VIDEO_FORMAT_YUV420P:
            /* Y plane + Cb plane (1/4) + Cr plane (1/4) */
            return (size_t)width * height + (size_t)((width + 1) / 2) * ((height + 1) / 2) * 2;
        default:
            return 0;
    }
}

/**
 * Convert decoded frame to output format.
 */
static int convert_frame(
    video_decoder_t* decoder,
    uint8_t* output,
    size_t output_len,
    int output_format)
{
    AVFrame* frame = decoder->frame;
    int width = frame->width;
    int height = frame->height;

    /* Check output buffer size */
    size_t required = calculate_frame_size(width, height, output_format);
    if (output_len < required) {
        set_error_fmt("Output buffer too small: need %zu bytes, have %zu",
                      required, output_len);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Get target pixel format */
    enum AVPixelFormat dst_format = video_format_to_ffmpeg(output_format);
    if (dst_format == AV_PIX_FMT_NONE) {
        set_error("Invalid output format");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Check if conversion is needed */
    if (frame->format == dst_format) {
        /* Direct copy for matching format */
        if (output_format == VIDEO_FORMAT_YUV420P) {
            /* Copy Y plane */
            size_t y_size = (size_t)width * height;
            for (int y = 0; y < height; y++) {
                memcpy(output + y * width,
                       frame->data[0] + y * frame->linesize[0],
                       width);
            }
            /* Copy U plane */
            int uv_width = (width + 1) / 2;
            int uv_height = (height + 1) / 2;
            uint8_t* u_dst = output + y_size;
            for (int y = 0; y < uv_height; y++) {
                memcpy(u_dst + y * uv_width,
                       frame->data[1] + y * frame->linesize[1],
                       uv_width);
            }
            /* Copy V plane */
            uint8_t* v_dst = u_dst + (size_t)uv_width * uv_height;
            for (int y = 0; y < uv_height; y++) {
                memcpy(v_dst + y * uv_width,
                       frame->data[2] + y * frame->linesize[2],
                       uv_width);
            }
        } else {
            /* Single-plane format */
            int bytes_per_pixel = (output_format == VIDEO_FORMAT_GRAY16) ? 2 :
                                  (output_format == VIDEO_FORMAT_RGB24) ? 3 : 1;
            int row_bytes = width * bytes_per_pixel;
            for (int y = 0; y < height; y++) {
                memcpy(output + y * row_bytes,
                       frame->data[0] + y * frame->linesize[0],
                       row_bytes);
            }
        }
        return SHARPDICOM_OK;
    }

    /* Create or update scaler context */
    if (decoder->sws_ctx == NULL || decoder->last_output_format != output_format) {
        if (decoder->sws_ctx != NULL) {
            sws_freeContext(decoder->sws_ctx);
        }
        decoder->sws_ctx = sws_getContext(
            width, height, frame->format,
            width, height, dst_format,
            SWS_BILINEAR, NULL, NULL, NULL);
        if (decoder->sws_ctx == NULL) {
            set_error("Failed to create pixel format converter");
            return SHARPDICOM_ERR_INTERNAL;
        }
        decoder->last_output_format = output_format;
    }

    /* Set up output pointers */
    uint8_t* dst_data[4] = {0};
    int dst_linesize[4] = {0};

    if (output_format == VIDEO_FORMAT_YUV420P) {
        int uv_width = (width + 1) / 2;
        int uv_height = (height + 1) / 2;
        dst_data[0] = output;
        dst_linesize[0] = width;
        dst_data[1] = output + (size_t)width * height;
        dst_linesize[1] = uv_width;
        dst_data[2] = dst_data[1] + (size_t)uv_width * uv_height;
        dst_linesize[2] = uv_width;
    } else {
        int bytes_per_pixel = (output_format == VIDEO_FORMAT_GRAY16) ? 2 :
                              (output_format == VIDEO_FORMAT_RGB24) ? 3 : 1;
        dst_data[0] = output;
        dst_linesize[0] = width * bytes_per_pixel;
    }

    /* Perform conversion */
    int result = sws_scale(decoder->sws_ctx,
                           (const uint8_t* const*)frame->data,
                           frame->linesize,
                           0, height,
                           dst_data, dst_linesize);
    if (result <= 0) {
        set_error("Pixel format conversion failed");
        return SHARPDICOM_ERR_INTERNAL;
    }

    return SHARPDICOM_OK;
}

/*============================================================================
 * Video decoder API implementation
 *============================================================================*/

SHARPDICOM_API int video_decoder_create(
    int codec_id,
    const uint8_t* extradata,
    size_t extradata_len,
    video_decoder_t** decoder_out)
{
    if (decoder_out == NULL) {
        set_error("Invalid argument: NULL decoder_out");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *decoder_out = NULL;

    /* Map codec ID */
    enum AVCodecID ff_codec_id = video_codec_to_ffmpeg(codec_id);
    if (ff_codec_id == AV_CODEC_ID_NONE) {
        set_error_fmt("Invalid codec ID: %d", codec_id);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Find decoder */
    const AVCodec* codec = avcodec_find_decoder(ff_codec_id);
    if (codec == NULL) {
        set_error_fmt("Codec not found: %s", video_codec_name(codec_id));
        return SHARPDICOM_ERR_UNSUPPORTED;
    }

    /* Allocate decoder structure */
    video_decoder_t* decoder = calloc(1, sizeof(video_decoder_t));
    if (decoder == NULL) {
        set_error("Failed to allocate decoder structure");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    decoder->codec_id = codec_id;
    decoder->codec = codec;

    /* Allocate codec context */
    decoder->codec_ctx = avcodec_alloc_context3(codec);
    if (decoder->codec_ctx == NULL) {
        set_error("Failed to allocate codec context");
        free(decoder);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set extradata if provided */
    if (extradata != NULL && extradata_len > 0) {
        decoder->codec_ctx->extradata = av_malloc(extradata_len + AV_INPUT_BUFFER_PADDING_SIZE);
        if (decoder->codec_ctx->extradata == NULL) {
            set_error("Failed to allocate extradata");
            avcodec_free_context(&decoder->codec_ctx);
            free(decoder);
            return SHARPDICOM_ERR_OUT_OF_MEMORY;
        }
        memcpy(decoder->codec_ctx->extradata, extradata, extradata_len);
        memset(decoder->codec_ctx->extradata + extradata_len, 0, AV_INPUT_BUFFER_PADDING_SIZE);
        decoder->codec_ctx->extradata_size = (int)extradata_len;
    }

    /* Open codec */
    int ret = avcodec_open2(decoder->codec_ctx, codec, NULL);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to open %s codec: %s",
                      video_codec_name(codec_id), errbuf);
        if (decoder->codec_ctx->extradata) {
            av_free(decoder->codec_ctx->extradata);
        }
        avcodec_free_context(&decoder->codec_ctx);
        free(decoder);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Allocate frame */
    decoder->frame = av_frame_alloc();
    if (decoder->frame == NULL) {
        set_error("Failed to allocate frame");
        avcodec_free_context(&decoder->codec_ctx);
        free(decoder);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Allocate packet */
    decoder->packet = av_packet_alloc();
    if (decoder->packet == NULL) {
        set_error("Failed to allocate packet");
        av_frame_free(&decoder->frame);
        avcodec_free_context(&decoder->codec_ctx);
        free(decoder);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    *decoder_out = decoder;
    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_decoder_get_info(
    video_decoder_t* decoder,
    video_stream_info_t* info)
{
    if (decoder == NULL || info == NULL) {
        set_error("Invalid argument: NULL decoder or info");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    AVCodecContext* ctx = decoder->codec_ctx;

    info->codec_id = decoder->codec_id;
    info->width = ctx->width > 0 ? ctx->width : decoder->width;
    info->height = ctx->height > 0 ? ctx->height : decoder->height;
    info->bit_depth = ctx->bits_per_raw_sample > 0 ? ctx->bits_per_raw_sample : 8;
    info->frame_count = -1; /* Unknown without container */
    info->duration_us = -1; /* Unknown without container */

    /* Calculate frame rate */
    if (ctx->framerate.num > 0 && ctx->framerate.den > 0) {
        info->frame_rate = (double)ctx->framerate.num / ctx->framerate.den;
    } else if (ctx->time_base.num > 0 && ctx->time_base.den > 0) {
        info->frame_rate = (double)ctx->time_base.den / ctx->time_base.num;
    } else {
        info->frame_rate = 0;
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_decode_frame(
    video_decoder_t* decoder,
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available)
{
    if (decoder == NULL) {
        set_error("Invalid argument: NULL decoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (frame_available == NULL) {
        set_error("Invalid argument: NULL frame_available");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *frame_available = 0;

    /* Set up packet */
    decoder->packet->data = (uint8_t*)input;
    decoder->packet->size = (int)input_len;

    /* Send packet to decoder */
    int ret = avcodec_send_packet(decoder->codec_ctx, decoder->packet);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to send packet: %s", errbuf);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Receive frame */
    ret = avcodec_receive_frame(decoder->codec_ctx, decoder->frame);
    if (ret == AVERROR(EAGAIN)) {
        /* Need more data */
        return SHARPDICOM_OK;
    }
    if (ret == AVERROR_EOF) {
        /* End of stream */
        return SHARPDICOM_OK;
    }
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to receive frame: %s", errbuf);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Frame available - update dimensions */
    decoder->width = decoder->frame->width;
    decoder->height = decoder->frame->height;

    /* Convert and copy to output if buffer provided */
    if (output != NULL && output_len > 0) {
        int conv_ret = convert_frame(decoder, output, output_len, output_format);
        if (conv_ret != SHARPDICOM_OK) {
            return conv_ret;
        }
    }

    /* Fill frame info if requested */
    if (frame_info != NULL) {
        frame_info->width = decoder->frame->width;
        frame_info->height = decoder->frame->height;
        frame_info->format = output_format;
        frame_info->pts = decoder->frame->pts;
        frame_info->dts = decoder->frame->pkt_dts;
        frame_info->key_frame = (decoder->frame->flags & AV_FRAME_FLAG_KEY) != 0;
        frame_info->frame_number = decoder->frame_number;
    }

    decoder->frame_number++;
    *frame_available = 1;

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_decoder_flush(
    video_decoder_t* decoder,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available)
{
    if (decoder == NULL) {
        set_error("Invalid argument: NULL decoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (frame_available == NULL) {
        set_error("Invalid argument: NULL frame_available");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *frame_available = 0;

    /* Send NULL packet to flush */
    int ret = avcodec_send_packet(decoder->codec_ctx, NULL);
    if (ret < 0 && ret != AVERROR_EOF) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to flush decoder: %s", errbuf);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Receive flushed frame */
    ret = avcodec_receive_frame(decoder->codec_ctx, decoder->frame);
    if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
        /* No more frames */
        return SHARPDICOM_OK;
    }
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to receive flushed frame: %s", errbuf);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Convert and copy to output */
    if (output != NULL && output_len > 0) {
        int conv_ret = convert_frame(decoder, output, output_len, output_format);
        if (conv_ret != SHARPDICOM_OK) {
            return conv_ret;
        }
    }

    /* Fill frame info */
    if (frame_info != NULL) {
        frame_info->width = decoder->frame->width;
        frame_info->height = decoder->frame->height;
        frame_info->format = output_format;
        frame_info->pts = decoder->frame->pts;
        frame_info->dts = decoder->frame->pkt_dts;
        frame_info->key_frame = (decoder->frame->flags & AV_FRAME_FLAG_KEY) != 0;
        frame_info->frame_number = decoder->frame_number;
    }

    decoder->frame_number++;
    *frame_available = 1;

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_decoder_seek(
    video_decoder_t* decoder,
    int64_t frame_number)
{
    if (decoder == NULL) {
        set_error("Invalid argument: NULL decoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    (void)frame_number;

    /* Flush decoder buffers */
    avcodec_flush_buffers(decoder->codec_ctx);
    decoder->frame_number = 0;

    /* Note: Actual seeking requires container support (demuxer).
     * Without a demuxer, we can only reset the decoder state.
     * The caller must provide data starting from a key frame. */
    set_error("Seek requires caller to provide key frame data");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_get_frame_size(
    video_decoder_t* decoder,
    int output_format,
    size_t* buffer_size)
{
    if (decoder == NULL || buffer_size == NULL) {
        set_error("Invalid argument: NULL decoder or buffer_size");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    int width = decoder->codec_ctx->width > 0 ? decoder->codec_ctx->width : decoder->width;
    int height = decoder->codec_ctx->height > 0 ? decoder->codec_ctx->height : decoder->height;

    if (width <= 0 || height <= 0) {
        set_error("Frame dimensions not yet known; decode at least one frame first");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *buffer_size = calculate_frame_size(width, height, output_format);
    if (*buffer_size == 0) {
        set_error("Invalid output format");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_decoder_reset(
    video_decoder_t* decoder)
{
    if (decoder == NULL) {
        set_error("Invalid argument: NULL decoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    avcodec_flush_buffers(decoder->codec_ctx);
    decoder->frame_number = 0;

    return SHARPDICOM_OK;
}

SHARPDICOM_API void video_decoder_destroy(
    video_decoder_t* decoder)
{
    if (decoder == NULL) {
        return;
    }

    if (decoder->sws_ctx != NULL) {
        sws_freeContext(decoder->sws_ctx);
    }

    if (decoder->packet != NULL) {
        av_packet_free(&decoder->packet);
    }

    if (decoder->frame != NULL) {
        av_frame_free(&decoder->frame);
    }

    if (decoder->codec_ctx != NULL) {
        if (decoder->codec_ctx->extradata != NULL) {
            av_free(decoder->codec_ctx->extradata);
        }
        avcodec_free_context(&decoder->codec_ctx);
    }

    free(decoder);
}

#else /* !SHARPDICOM_HAS_FFMPEG */

/*============================================================================
 * Stub implementations when FFmpeg is not available
 *============================================================================*/

SHARPDICOM_API int video_decoder_create(
    int codec_id,
    const uint8_t* extradata,
    size_t extradata_len,
    video_decoder_t** decoder_out)
{
    (void)codec_id;
    (void)extradata;
    (void)extradata_len;
    (void)decoder_out;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_get_info(
    video_decoder_t* decoder,
    video_stream_info_t* info)
{
    (void)decoder;
    (void)info;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decode_frame(
    video_decoder_t* decoder,
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available)
{
    (void)decoder;
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)output_format;
    (void)frame_info;
    (void)frame_available;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_flush(
    video_decoder_t* decoder,
    uint8_t* output,
    size_t output_len,
    int output_format,
    video_frame_info_t* frame_info,
    int* frame_available)
{
    (void)decoder;
    (void)output;
    (void)output_len;
    (void)output_format;
    (void)frame_info;
    (void)frame_available;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_seek(
    video_decoder_t* decoder,
    int64_t frame_number)
{
    (void)decoder;
    (void)frame_number;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_get_frame_size(
    video_decoder_t* decoder,
    int output_format,
    size_t* buffer_size)
{
    (void)decoder;
    (void)output_format;
    (void)buffer_size;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_decoder_reset(
    video_decoder_t* decoder)
{
    (void)decoder;
    set_error("Video support not available (FFmpeg not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API void video_decoder_destroy(
    video_decoder_t* decoder)
{
    (void)decoder;
    /* Nothing to do */
}

#endif /* SHARPDICOM_HAS_FFMPEG */
