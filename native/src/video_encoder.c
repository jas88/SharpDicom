/**
 * Video Encoder Implementation (FFmpeg)
 *
 * Wraps FFmpeg libavcodec/libavformat/libswscale/libswresample for
 * video frame encoding. Supports MPEG-2, H.264/AVC, and HEVC/H.265
 * with GPU-accelerated encoding and automatic CPU fallback.
 *
 * When SHARPDICOM_WITH_FFMPEG_ENC is defined:
 *   Full encoding implementation using FFmpeg APIs.
 *
 * When SHARPDICOM_WITH_FFMPEG_ENC is NOT defined:
 *   Stub implementations returning SHARPDICOM_ERR_UNSUPPORTED.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "video_encoder.h"
#include "sharpdicom_codecs.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>

#ifdef SHARPDICOM_WITH_FFMPEG_ENC
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/frame.h>
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libavutil/mathematics.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#endif

/*============================================================================
 * Internal helper: Set error message
 *============================================================================*/

/* Forward declaration from sharpdicom_codecs.c */
extern void set_error(const char* message);
extern void set_error_fmt(const char* fmt, ...);

#ifdef SHARPDICOM_WITH_FFMPEG_ENC

/*============================================================================
 * Video encoder context structure
 *============================================================================*/

struct video_encoder {
    /* Video encoding */
    AVCodecContext* video_ctx;       /* Video codec context */
    const AVCodec* video_codec;     /* Video codec descriptor */
    AVFrame* video_frame;           /* Input frame buffer (YUV) */
    AVPacket* packet;               /* Encoded packet buffer */
    struct SwsContext* sws_ctx;     /* Pixel format converter */
    int last_src_fmt;               /* Last input pixel format for sws cache */

    /* Audio encoding (optional) */
    AVCodecContext* audio_ctx;      /* Audio codec context */
    const AVCodec* audio_codec;     /* Audio codec descriptor */
    AVFrame* audio_frame;           /* Audio frame buffer */
    struct SwrContext* swr_ctx;     /* Audio resampler */

    /* Muxer (in-memory output) */
    AVFormatContext* fmt_ctx;       /* Format/muxer context */
    AVStream* video_stream;         /* Video output stream */
    AVStream* audio_stream;         /* Audio output stream */
    uint8_t* output_buffer;         /* Dynamic output buffer (avio) */
    size_t output_size;             /* Size of output buffer */
    int header_written;             /* Flag: muxer header written */

    /* State */
    int codec_id;                   /* VIDEO_CODEC_* identifier */
    int width;                      /* Frame width */
    int height;                     /* Frame height */
    int64_t frame_count;            /* Frames encoded so far */
    int64_t audio_pts;              /* Audio presentation timestamp */
    int flushed;                    /* Flag: encoder has been flushed */
};

/*============================================================================
 * Codec selection helpers
 *============================================================================*/

/**
 * Map VIDEO_CODEC_* to FFmpeg AVCodecID for encoding.
 */
static enum AVCodecID video_codec_to_ffmpeg_enc(int codec_id) {
    switch (codec_id) {
        case VIDEO_CODEC_MPEG2:
            return AV_CODEC_ID_MPEG2VIDEO;
        case VIDEO_CODEC_H264:
            return AV_CODEC_ID_H264;
        case VIDEO_CODEC_HEVC:
            return AV_CODEC_ID_HEVC;
        default:
            return AV_CODEC_ID_NONE;
    }
}

/**
 * Get encoder codec name for error messages.
 */
static const char* video_enc_codec_name(int codec_id) {
    switch (codec_id) {
        case VIDEO_CODEC_MPEG2: return "MPEG-2";
        case VIDEO_CODEC_H264:  return "H.264";
        case VIDEO_CODEC_HEVC:  return "HEVC";
        default:                return "Unknown";
    }
}

/**
 * Try to find a GPU-accelerated encoder for the given codec.
 * Returns NULL if no GPU encoder is available.
 */
static const AVCodec* find_hw_encoder(int codec_id) {
    const char* hw_names[3];
    int hw_count = 0;

    switch (codec_id) {
        case VIDEO_CODEC_H264:
            hw_names[hw_count++] = "h264_videotoolbox";
            hw_names[hw_count++] = "h264_nvenc";
            hw_names[hw_count++] = "h264_vaapi";
            break;
        case VIDEO_CODEC_HEVC:
            hw_names[hw_count++] = "hevc_videotoolbox";
            hw_names[hw_count++] = "hevc_nvenc";
            hw_names[hw_count++] = "hevc_vaapi";
            break;
        default:
            /* No GPU encoders for MPEG-2 */
            return NULL;
    }

    for (int i = 0; i < hw_count; i++) {
        const AVCodec* codec = avcodec_find_encoder_by_name(hw_names[i]);
        if (codec != NULL) {
            return codec;
        }
    }

    return NULL;
}

/**
 * Find the software (CPU) encoder for the given codec.
 */
static const AVCodec* find_sw_encoder(int codec_id) {
    switch (codec_id) {
        case VIDEO_CODEC_MPEG2:
            return avcodec_find_encoder(AV_CODEC_ID_MPEG2VIDEO);
        case VIDEO_CODEC_H264: {
            /* Prefer libx264 over native encoder */
            const AVCodec* codec = avcodec_find_encoder_by_name("libx264");
            if (codec) return codec;
            return avcodec_find_encoder(AV_CODEC_ID_H264);
        }
        case VIDEO_CODEC_HEVC: {
            /* Prefer libx265 over native encoder */
            const AVCodec* codec = avcodec_find_encoder_by_name("libx265");
            if (codec) return codec;
            return avcodec_find_encoder(AV_CODEC_ID_HEVC);
        }
        default:
            return NULL;
    }
}

/**
 * Apply quality preset to codec context.
 */
static void apply_quality_preset(
    AVCodecContext* ctx,
    int codec_id,
    int quality_preset,
    int crf_override,
    int bitrate_override)
{
    /* If bitrate is explicitly set, use bitrate mode */
    if (bitrate_override > 0) {
        ctx->bit_rate = bitrate_override;
        ctx->rc_max_rate = bitrate_override;
        ctx->rc_buffer_size = bitrate_override * 2;
        return;
    }

    /* Apply CRF-based quality presets */
    int crf = -1;
    int bitrate = 0;

    if (crf_override > 0) {
        crf = crf_override;
    } else {
        switch (quality_preset) {
            case VIDEO_QUALITY_DIAGNOSTIC:
                switch (codec_id) {
                    case VIDEO_CODEC_H264:  crf = 17; break;
                    case VIDEO_CODEC_HEVC:  crf = 20; break;
                    case VIDEO_CODEC_MPEG2: bitrate = 15000000; break;
                    default: break;
                }
                break;
            case VIDEO_QUALITY_REVIEW:
                switch (codec_id) {
                    case VIDEO_CODEC_H264:  crf = 23; break;
                    case VIDEO_CODEC_HEVC:  crf = 26; break;
                    case VIDEO_CODEC_MPEG2: bitrate = 8000000; break;
                    default: break;
                }
                break;
            case VIDEO_QUALITY_ARCHIVE:
                switch (codec_id) {
                    case VIDEO_CODEC_H264:  crf = 28; break;
                    case VIDEO_CODEC_HEVC:  crf = 31; break;
                    case VIDEO_CODEC_MPEG2: bitrate = 4000000; break;
                    default: break;
                }
                break;
            default:
                /* Default to review quality */
                switch (codec_id) {
                    case VIDEO_CODEC_H264:  crf = 23; break;
                    case VIDEO_CODEC_HEVC:  crf = 26; break;
                    case VIDEO_CODEC_MPEG2: bitrate = 8000000; break;
                    default: break;
                }
                break;
        }
    }

    if (crf >= 0 && codec_id != VIDEO_CODEC_MPEG2) {
        /* CRF mode for H.264/HEVC */
        char crf_str[16];
        snprintf(crf_str, sizeof(crf_str), "%d", crf);
        av_opt_set(ctx->priv_data, "crf", crf_str, 0);
    }

    if (bitrate > 0) {
        /* Bitrate mode for MPEG-2 (no CRF support) */
        ctx->bit_rate = bitrate;
        ctx->rc_max_rate = (int64_t)(bitrate * 1.5);
        ctx->rc_buffer_size = bitrate * 2;
    }
}

/**
 * Map VIDEO_FORMAT_* to FFmpeg AVPixelFormat for encoding input.
 */
static enum AVPixelFormat video_format_to_ffmpeg_enc(int format) {
    switch (format) {
        case VIDEO_FORMAT_GRAY8:   return AV_PIX_FMT_GRAY8;
        case VIDEO_FORMAT_RGB24:   return AV_PIX_FMT_RGB24;
        case VIDEO_FORMAT_YUV420P: return AV_PIX_FMT_YUV420P;
        default:                   return AV_PIX_FMT_NONE;
    }
}

/*============================================================================
 * In-memory I/O callbacks for AVFormatContext
 *============================================================================*/

/**
 * Write callback for avio dynamic buffer.
 */
static int avio_write_buffer(void* opaque, const uint8_t* buf, int buf_size) {
    video_encoder_t* enc = (video_encoder_t*)opaque;

    uint8_t* new_buf = realloc(enc->output_buffer, enc->output_size + (size_t)buf_size);
    if (!new_buf) {
        return AVERROR(ENOMEM);
    }

    memcpy(new_buf + enc->output_size, buf, (size_t)buf_size);
    enc->output_buffer = new_buf;
    enc->output_size += (size_t)buf_size;

    return buf_size;
}

/*============================================================================
 * Video encoder API implementation (FFmpeg available)
 *============================================================================*/

SHARPDICOM_API int video_encoder_create(
    const video_encoder_config_t* config,
    video_encoder_t** encoder_out)
{
    if (encoder_out == NULL) {
        set_error("Invalid argument: NULL encoder_out");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *encoder_out = NULL;

    if (config == NULL) {
        set_error("Invalid argument: NULL config");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (config->width <= 0 || config->height <= 0) {
        set_error("Invalid argument: width and height must be positive");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (config->frame_rate <= 0.0) {
        set_error("Invalid argument: frame_rate must be positive");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate codec ID */
    enum AVCodecID ff_codec_id = video_codec_to_ffmpeg_enc(config->codec_id);
    if (ff_codec_id == AV_CODEC_ID_NONE) {
        set_error_fmt("Unsupported video codec ID: %d", config->codec_id);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Allocate encoder structure */
    video_encoder_t* enc = calloc(1, sizeof(video_encoder_t));
    if (enc == NULL) {
        set_error("Failed to allocate encoder structure");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    enc->codec_id = config->codec_id;
    enc->width = config->width;
    enc->height = config->height;

    /* Find encoder: try GPU first if allowed, then CPU */
    const AVCodec* codec = NULL;

    if (config->hw_accel != VIDEO_HWACCEL_CPU) {
        codec = find_hw_encoder(config->codec_id);
    }

    if (codec == NULL) {
        if (config->hw_accel == VIDEO_HWACCEL_GPU) {
            set_error_fmt("GPU encoder not available for %s",
                          video_enc_codec_name(config->codec_id));
            free(enc);
            return SHARPDICOM_ERR_UNSUPPORTED;
        }
        codec = find_sw_encoder(config->codec_id);
    }

    if (codec == NULL) {
        set_error_fmt("No encoder found for %s",
                      video_enc_codec_name(config->codec_id));
        free(enc);
        return SHARPDICOM_ERR_UNSUPPORTED;
    }

    enc->video_codec = codec;

    /* Create video codec context */
    enc->video_ctx = avcodec_alloc_context3(codec);
    if (enc->video_ctx == NULL) {
        set_error("Failed to allocate video codec context");
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Configure codec context */
    AVCodecContext* ctx = enc->video_ctx;
    ctx->width = config->width;
    ctx->height = config->height;
    ctx->time_base = (AVRational){1, (int)(config->frame_rate + 0.5)};
    ctx->framerate = (AVRational){(int)(config->frame_rate + 0.5), 1};
    ctx->pix_fmt = (config->bit_depth == 10) ? AV_PIX_FMT_YUV420P10LE : AV_PIX_FMT_YUV420P;
    ctx->gop_size = (config->gop_size > 0) ? config->gop_size : 12;
    ctx->max_b_frames = (config->codec_id == VIDEO_CODEC_MPEG2) ? 2 : 3;
    ctx->thread_count = 0; /* Auto-detect thread count */
    ctx->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    /* Apply quality/bitrate settings */
    apply_quality_preset(ctx, config->codec_id, config->quality_preset,
                         config->crf, config->bitrate);

    /* Open video codec */
    int ret = avcodec_open2(ctx, codec, NULL);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to open %s encoder (%s): %s",
                      video_enc_codec_name(config->codec_id),
                      codec->name, errbuf);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Create pixel format converter (input -> encoder format) */
    enc->sws_ctx = sws_getContext(
        config->width, config->height, AV_PIX_FMT_RGB24,
        config->width, config->height, ctx->pix_fmt,
        SWS_BILINEAR, NULL, NULL, NULL);
    if (enc->sws_ctx == NULL) {
        set_error("Failed to create pixel format converter");
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Allocate video frame */
    enc->video_frame = av_frame_alloc();
    if (enc->video_frame == NULL) {
        set_error("Failed to allocate video frame");
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    enc->video_frame->format = ctx->pix_fmt;
    enc->video_frame->width = config->width;
    enc->video_frame->height = config->height;

    ret = av_frame_get_buffer(enc->video_frame, 0);
    if (ret < 0) {
        set_error("Failed to allocate video frame buffer");
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Allocate packet */
    enc->packet = av_packet_alloc();
    if (enc->packet == NULL) {
        set_error("Failed to allocate packet");
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Setup audio encoder if requested */
    if (config->audio_codec != AUDIO_CODEC_NONE) {
        enum AVCodecID audio_codec_id = AV_CODEC_ID_NONE;
        if (config->audio_codec == AUDIO_CODEC_AAC) {
            audio_codec_id = AV_CODEC_ID_AAC;
        } else if (config->audio_codec == AUDIO_CODEC_PCM) {
            audio_codec_id = AV_CODEC_ID_PCM_S16LE;
        }

        if (audio_codec_id != AV_CODEC_ID_NONE) {
            enc->audio_codec = avcodec_find_encoder(audio_codec_id);
            if (enc->audio_codec != NULL) {
                enc->audio_ctx = avcodec_alloc_context3(enc->audio_codec);
                if (enc->audio_ctx != NULL) {
                    enc->audio_ctx->sample_rate = config->audio_sample_rate > 0
                        ? config->audio_sample_rate : 48000;
                    enc->audio_ctx->ch_layout = (AVChannelLayout)AV_CHANNEL_LAYOUT_STEREO;
                    if (config->audio_channels == 1) {
                        enc->audio_ctx->ch_layout = (AVChannelLayout)AV_CHANNEL_LAYOUT_MONO;
                    }
                    /* sample_fmts is deprecated in FFmpeg 7.x but the replacement
                     * avcodec_get_supported_config() is complex; use deprecated API */
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wdeprecated-declarations"
                    enc->audio_ctx->sample_fmt = enc->audio_codec->sample_fmts
                        ? enc->audio_codec->sample_fmts[0] : AV_SAMPLE_FMT_FLTP;
#pragma GCC diagnostic pop
                    enc->audio_ctx->bit_rate = 128000;
                    enc->audio_ctx->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

                    ret = avcodec_open2(enc->audio_ctx, enc->audio_codec, NULL);
                    if (ret < 0) {
                        /* Audio encoder failure is non-fatal; proceed without audio */
                        avcodec_free_context(&enc->audio_ctx);
                        enc->audio_codec = NULL;
                    }
                }
            }
        }
    }

    /* Setup in-memory muxer (MPEG-TS for MPEG-2, raw Annex-B for H.264/HEVC) */
    const char* fmt_name;
    switch (config->codec_id) {
        case VIDEO_CODEC_MPEG2: fmt_name = "mpegts"; break;
        case VIDEO_CODEC_H264:  fmt_name = "h264";   break;
        case VIDEO_CODEC_HEVC:  fmt_name = "hevc";   break;
        default:                fmt_name = "mpegts";  break;
    }

    /* If audio is enabled, use MPEG-TS container for all codecs */
    if (enc->audio_ctx != NULL) {
        fmt_name = "mpegts";
    }

    ret = avformat_alloc_output_context2(&enc->fmt_ctx, NULL, fmt_name, NULL);
    if (ret < 0 || enc->fmt_ctx == NULL) {
        set_error("Failed to allocate muxer context");
        if (enc->audio_ctx) avcodec_free_context(&enc->audio_ctx);
        av_packet_free(&enc->packet);
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Add video stream */
    enc->video_stream = avformat_new_stream(enc->fmt_ctx, enc->video_codec);
    if (enc->video_stream == NULL) {
        set_error("Failed to create video stream");
        avformat_free_context(enc->fmt_ctx);
        if (enc->audio_ctx) avcodec_free_context(&enc->audio_ctx);
        av_packet_free(&enc->packet);
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_INTERNAL;
    }

    avcodec_parameters_from_context(enc->video_stream->codecpar, enc->video_ctx);
    enc->video_stream->time_base = enc->video_ctx->time_base;

    /* Add audio stream if encoder is available */
    if (enc->audio_ctx != NULL) {
        enc->audio_stream = avformat_new_stream(enc->fmt_ctx, enc->audio_codec);
        if (enc->audio_stream != NULL) {
            avcodec_parameters_from_context(enc->audio_stream->codecpar, enc->audio_ctx);
            enc->audio_stream->time_base = enc->audio_ctx->time_base;
        }
    }

    /* Setup in-memory I/O */
    size_t avio_buf_size = 4096;
    uint8_t* avio_buf = av_malloc(avio_buf_size);
    if (avio_buf == NULL) {
        set_error("Failed to allocate I/O buffer");
        avformat_free_context(enc->fmt_ctx);
        if (enc->audio_ctx) avcodec_free_context(&enc->audio_ctx);
        av_packet_free(&enc->packet);
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    AVIOContext* avio_ctx = avio_alloc_context(
        avio_buf, (int)avio_buf_size,
        1,  /* write flag */
        enc,
        NULL,  /* read callback */
        avio_write_buffer,
        NULL   /* seek callback */
    );
    if (avio_ctx == NULL) {
        set_error("Failed to allocate AVIO context");
        av_free(avio_buf);
        avformat_free_context(enc->fmt_ctx);
        if (enc->audio_ctx) avcodec_free_context(&enc->audio_ctx);
        av_packet_free(&enc->packet);
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    enc->fmt_ctx->pb = avio_ctx;
    enc->fmt_ctx->flags |= AVFMT_FLAG_CUSTOM_IO;

    /* Write muxer header */
    ret = avformat_write_header(enc->fmt_ctx, NULL);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to write muxer header: %s", errbuf);
        if (enc->fmt_ctx->pb) {
            av_free(enc->fmt_ctx->pb->buffer);
            avio_context_free(&enc->fmt_ctx->pb);
        }
        avformat_free_context(enc->fmt_ctx);
        if (enc->audio_ctx) avcodec_free_context(&enc->audio_ctx);
        av_packet_free(&enc->packet);
        av_frame_free(&enc->video_frame);
        sws_freeContext(enc->sws_ctx);
        avcodec_free_context(&enc->video_ctx);
        free(enc);
        return SHARPDICOM_ERR_INTERNAL;
    }
    enc->header_written = 1;

    *encoder_out = enc;
    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_encode_frame(
    video_encoder_t* encoder,
    const uint8_t* pixels,
    size_t pixel_len,
    int pixel_format,
    uint8_t** output,
    size_t* output_len,
    int* packet_available)
{
    if (encoder == NULL) {
        set_error("Invalid argument: NULL encoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (packet_available == NULL) {
        set_error("Invalid argument: NULL packet_available");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *packet_available = 0;
    if (output) *output = NULL;
    if (output_len) *output_len = 0;

    if (pixels == NULL || pixel_len == 0) {
        set_error("Invalid argument: NULL or empty pixel data");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Determine input pixel format */
    enum AVPixelFormat src_fmt = video_format_to_ffmpeg_enc(pixel_format);
    if (src_fmt == AV_PIX_FMT_NONE) {
        set_error_fmt("Unsupported input pixel format: %d", pixel_format);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Recreate scaler only if input format changed */
    if (encoder->sws_ctx == NULL || encoder->last_src_fmt != (int)src_fmt) {
        if (encoder->sws_ctx != NULL) {
            sws_freeContext(encoder->sws_ctx);
        }
        encoder->sws_ctx = sws_getContext(
            encoder->width, encoder->height, src_fmt,
            encoder->width, encoder->height, encoder->video_ctx->pix_fmt,
            SWS_BILINEAR, NULL, NULL, NULL);
        if (encoder->sws_ctx == NULL) {
            set_error("Failed to create pixel format converter");
            return SHARPDICOM_ERR_INTERNAL;
        }
        encoder->last_src_fmt = (int)src_fmt;
    }

    /* Make frame writable */
    int ret = av_frame_make_writable(encoder->video_frame);
    if (ret < 0) {
        set_error("Failed to make frame writable");
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Set up source data pointers */
    const uint8_t* src_data[4] = {pixels, NULL, NULL, NULL};
    int src_linesize[4] = {0, 0, 0, 0};

    switch (pixel_format) {
        case VIDEO_FORMAT_RGB24:
            src_linesize[0] = encoder->width * 3;
            break;
        case VIDEO_FORMAT_GRAY8:
            src_linesize[0] = encoder->width;
            break;
        case VIDEO_FORMAT_YUV420P: {
            int uv_width = (encoder->width + 1) / 2;
            int uv_height = (encoder->height + 1) / 2;
            src_linesize[0] = encoder->width;
            src_linesize[1] = uv_width;
            src_linesize[2] = uv_width;
            src_data[1] = pixels + (size_t)encoder->width * encoder->height;
            src_data[2] = src_data[1] + (size_t)uv_width * uv_height;
            break;
        }
        default:
            break;
    }

    /* Convert to encoder's pixel format */
    sws_scale(encoder->sws_ctx,
              src_data, src_linesize,
              0, encoder->height,
              encoder->video_frame->data,
              encoder->video_frame->linesize);

    /* Set presentation timestamp */
    encoder->video_frame->pts = encoder->frame_count;
    encoder->frame_count++;

    /* Send frame to encoder */
    ret = avcodec_send_frame(encoder->video_ctx, encoder->video_frame);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to send frame to encoder: %s", errbuf);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    /* Receive encoded packets */
    size_t prev_size = encoder->output_size;
    while (1) {
        ret = avcodec_receive_packet(encoder->video_ctx, encoder->packet);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            break;
        }
        if (ret < 0) {
            char errbuf[256];
            av_strerror(ret, errbuf, sizeof(errbuf));
            set_error_fmt("Failed to receive encoded packet: %s", errbuf);
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }

        /* Rescale packet timestamps */
        av_packet_rescale_ts(encoder->packet,
                             encoder->video_ctx->time_base,
                             encoder->video_stream->time_base);
        encoder->packet->stream_index = encoder->video_stream->index;

        /* Write to muxer */
        ret = av_interleaved_write_frame(encoder->fmt_ctx, encoder->packet);
        if (ret < 0) {
            char errbuf[256];
            av_strerror(ret, errbuf, sizeof(errbuf));
            set_error_fmt("Failed to write packet: %s", errbuf);
            av_packet_unref(encoder->packet);
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }

        av_packet_unref(encoder->packet);
    }

    /* Check if any data was produced */
    if (encoder->output_size > prev_size) {
        *packet_available = 1;
        if (output && output_len) {
            size_t new_bytes = encoder->output_size - prev_size;
            uint8_t* copy = malloc(new_bytes);
            if (copy) {
                memcpy(copy, encoder->output_buffer + prev_size, new_bytes);
                *output = copy;
                *output_len = new_bytes;
            }
        }
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_encode_audio(
    video_encoder_t* encoder,
    const uint8_t* samples,
    size_t samples_len,
    int sample_format)
{
    if (encoder == NULL) {
        set_error("Invalid argument: NULL encoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (encoder->audio_ctx == NULL) {
        set_error("Audio encoding not configured");
        return SHARPDICOM_ERR_UNSUPPORTED;
    }

    if (samples == NULL || samples_len == 0) {
        set_error("Invalid argument: NULL or empty audio data");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Allocate audio frame if needed */
    if (encoder->audio_frame == NULL) {
        encoder->audio_frame = av_frame_alloc();
        if (encoder->audio_frame == NULL) {
            set_error("Failed to allocate audio frame");
            return SHARPDICOM_ERR_OUT_OF_MEMORY;
        }

        encoder->audio_frame->format = encoder->audio_ctx->sample_fmt;
        encoder->audio_frame->ch_layout = encoder->audio_ctx->ch_layout;
        encoder->audio_frame->sample_rate = encoder->audio_ctx->sample_rate;
        encoder->audio_frame->nb_samples = encoder->audio_ctx->frame_size;
        if (encoder->audio_frame->nb_samples == 0) {
            encoder->audio_frame->nb_samples = 1024;
        }

        int ret = av_frame_get_buffer(encoder->audio_frame, 0);
        if (ret < 0) {
            set_error("Failed to allocate audio frame buffer");
            av_frame_free(&encoder->audio_frame);
            return SHARPDICOM_ERR_OUT_OF_MEMORY;
        }
    }

    /* Determine input format */
    enum AVSampleFormat in_fmt;
    switch (sample_format) {
        case AUDIO_FMT_PCM16: in_fmt = AV_SAMPLE_FMT_S16;  break;
        case AUDIO_FMT_FLOAT: in_fmt = AV_SAMPLE_FMT_FLT;  break;
        default:
            set_error_fmt("Unsupported audio format: %d", sample_format);
            return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Create/update resampler if needed */
    if (encoder->swr_ctx == NULL) {
        int ret = swr_alloc_set_opts2(&encoder->swr_ctx,
            &encoder->audio_ctx->ch_layout,
            encoder->audio_ctx->sample_fmt,
            encoder->audio_ctx->sample_rate,
            &encoder->audio_ctx->ch_layout,
            in_fmt,
            encoder->audio_ctx->sample_rate,
            0, NULL);
        if (ret < 0 || encoder->swr_ctx == NULL) {
            set_error("Failed to create audio resampler");
            return SHARPDICOM_ERR_INTERNAL;
        }

        ret = swr_init(encoder->swr_ctx);
        if (ret < 0) {
            set_error("Failed to initialize audio resampler");
            swr_free(&encoder->swr_ctx);
            return SHARPDICOM_ERR_INTERNAL;
        }
    }

    /* Calculate sample count from buffer size */
    int bytes_per_sample = (sample_format == AUDIO_FMT_PCM16) ? 2 : 4;
    int channels = encoder->audio_ctx->ch_layout.nb_channels;
    int nb_samples = (int)(samples_len / ((size_t)bytes_per_sample * channels));

    /* Convert samples */
    int ret = av_frame_make_writable(encoder->audio_frame);
    if (ret < 0) {
        set_error("Failed to make audio frame writable");
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Prevent buffer overflow: input must not exceed allocated frame size */
    int max_samples = encoder->audio_ctx->frame_size;
    if (max_samples == 0) max_samples = 1024;
    if (nb_samples > max_samples) {
        set_error_fmt("Audio input (%d samples) exceeds frame size (%d)", nb_samples, max_samples);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    encoder->audio_frame->nb_samples = nb_samples;
    const uint8_t* in_data[1] = {samples};
    ret = swr_convert(encoder->swr_ctx,
                      encoder->audio_frame->data,
                      nb_samples,
                      in_data,
                      nb_samples);
    if (ret < 0) {
        set_error("Audio conversion failed");
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    encoder->audio_frame->pts = encoder->audio_pts;
    encoder->audio_pts += nb_samples;

    /* Send to encoder */
    ret = avcodec_send_frame(encoder->audio_ctx, encoder->audio_frame);
    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error_fmt("Failed to send audio frame: %s", errbuf);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    /* Receive and mux audio packets */
    while (1) {
        ret = avcodec_receive_packet(encoder->audio_ctx, encoder->packet);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            break;
        }
        if (ret < 0) {
            char errbuf[256];
            av_strerror(ret, errbuf, sizeof(errbuf));
            set_error_fmt("Failed to receive audio packet: %s", errbuf);
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }

        if (encoder->audio_stream) {
            av_packet_rescale_ts(encoder->packet,
                                 encoder->audio_ctx->time_base,
                                 encoder->audio_stream->time_base);
            encoder->packet->stream_index = encoder->audio_stream->index;

            ret = av_interleaved_write_frame(encoder->fmt_ctx, encoder->packet);
            if (ret < 0) {
                av_packet_unref(encoder->packet);
                set_error("Failed to write audio packet");
                return SHARPDICOM_ERR_ENCODE_FAILED;
            }
        }

        av_packet_unref(encoder->packet);
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_encoder_flush(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len,
    int* packet_available)
{
    if (encoder == NULL) {
        set_error("Invalid argument: NULL encoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (packet_available == NULL) {
        set_error("Invalid argument: NULL packet_available");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *packet_available = 0;
    if (output) *output = NULL;
    if (output_len) *output_len = 0;

    size_t prev_size = encoder->output_size;

    if (!encoder->flushed) {
        /* Send NULL frame to flush video encoder */
        int ret = avcodec_send_frame(encoder->video_ctx, NULL);
        if (ret < 0 && ret != AVERROR_EOF) {
            char errbuf[256];
            av_strerror(ret, errbuf, sizeof(errbuf));
            set_error_fmt("Failed to flush video encoder: %s", errbuf);
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }
    }

    /* Drain remaining video packets */
    while (1) {
        int ret = avcodec_receive_packet(encoder->video_ctx, encoder->packet);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            break;
        }
        if (ret < 0) {
            char errbuf[256];
            av_strerror(ret, errbuf, sizeof(errbuf));
            set_error_fmt("Failed to receive flushed packet: %s", errbuf);
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }

        av_packet_rescale_ts(encoder->packet,
                             encoder->video_ctx->time_base,
                             encoder->video_stream->time_base);
        encoder->packet->stream_index = encoder->video_stream->index;

        ret = av_interleaved_write_frame(encoder->fmt_ctx, encoder->packet);
        av_packet_unref(encoder->packet);
        if (ret < 0) {
            set_error("Failed to write flushed packet");
            return SHARPDICOM_ERR_ENCODE_FAILED;
        }
    }

    /* Flush audio encoder if present */
    if (encoder->audio_ctx && !encoder->flushed) {
        avcodec_send_frame(encoder->audio_ctx, NULL);
        while (1) {
            int ret = avcodec_receive_packet(encoder->audio_ctx, encoder->packet);
            if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
                break;
            }
            if (ret < 0) break;

            if (encoder->audio_stream) {
                av_packet_rescale_ts(encoder->packet,
                                     encoder->audio_ctx->time_base,
                                     encoder->audio_stream->time_base);
                encoder->packet->stream_index = encoder->audio_stream->index;
                av_interleaved_write_frame(encoder->fmt_ctx, encoder->packet);
            }
            av_packet_unref(encoder->packet);
        }
    }

    if (!encoder->flushed) {
        /* Write muxer trailer */
        if (encoder->header_written) {
            av_write_trailer(encoder->fmt_ctx);
        }
        encoder->flushed = 1;
    }

    /* Check if any data was produced */
    if (encoder->output_size > prev_size) {
        *packet_available = 1;
        if (output && output_len) {
            size_t new_bytes = encoder->output_size - prev_size;
            uint8_t* copy = malloc(new_bytes);
            if (copy) {
                memcpy(copy, encoder->output_buffer + prev_size, new_bytes);
                *output = copy;
                *output_len = new_bytes;
            }
        }
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API int video_encoder_get_output(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len)
{
    if (encoder == NULL) {
        set_error("Invalid argument: NULL encoder");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || output_len == NULL) {
        set_error("Invalid argument: NULL output parameters");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *output = NULL;
    *output_len = 0;

    if (encoder->output_buffer == NULL || encoder->output_size == 0) {
        set_error("No output data available (encoding not started or no frames encoded)");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Return a copy of the complete output buffer */
    uint8_t* copy = malloc(encoder->output_size);
    if (copy == NULL) {
        set_error("Failed to allocate output buffer copy");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    memcpy(copy, encoder->output_buffer, encoder->output_size);
    *output = copy;
    *output_len = encoder->output_size;

    return SHARPDICOM_OK;
}

SHARPDICOM_API void video_encoder_destroy(
    video_encoder_t* encoder)
{
    if (encoder == NULL) {
        return;
    }

    /* Free audio resampler */
    if (encoder->swr_ctx != NULL) {
        swr_free(&encoder->swr_ctx);
    }

    /* Free audio frame */
    if (encoder->audio_frame != NULL) {
        av_frame_free(&encoder->audio_frame);
    }

    /* Free audio codec context */
    if (encoder->audio_ctx != NULL) {
        avcodec_free_context(&encoder->audio_ctx);
    }

    /* Free format context (including AVIO) */
    if (encoder->fmt_ctx != NULL) {
        if (encoder->fmt_ctx->pb != NULL) {
            /* The buffer was allocated with av_malloc, free it */
            av_free(encoder->fmt_ctx->pb->buffer);
            avio_context_free(&encoder->fmt_ctx->pb);
        }
        avformat_free_context(encoder->fmt_ctx);
    }

    /* Free scaler */
    if (encoder->sws_ctx != NULL) {
        sws_freeContext(encoder->sws_ctx);
    }

    /* Free packet */
    if (encoder->packet != NULL) {
        av_packet_free(&encoder->packet);
    }

    /* Free video frame */
    if (encoder->video_frame != NULL) {
        av_frame_free(&encoder->video_frame);
    }

    /* Free video codec context */
    if (encoder->video_ctx != NULL) {
        avcodec_free_context(&encoder->video_ctx);
    }

    /* Free output buffer */
    free(encoder->output_buffer);

    /* Free encoder structure */
    free(encoder);
}

SHARPDICOM_API void video_encoder_free(
    uint8_t* buffer)
{
    free(buffer);
}

#else /* !SHARPDICOM_WITH_FFMPEG_ENC */

/*============================================================================
 * Stub implementations when FFmpeg encoding is not available
 *============================================================================*/

SHARPDICOM_API int video_encoder_create(
    const video_encoder_config_t* config,
    video_encoder_t** encoder_out)
{
    (void)config;
    (void)encoder_out;
    set_error("Video encoding not available (FFmpeg encoding not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_encode_frame(
    video_encoder_t* encoder,
    const uint8_t* pixels,
    size_t pixel_len,
    int pixel_format,
    uint8_t** output,
    size_t* output_len,
    int* packet_available)
{
    (void)encoder;
    (void)pixels;
    (void)pixel_len;
    (void)pixel_format;
    (void)output;
    (void)output_len;
    (void)packet_available;
    set_error("Video encoding not available (FFmpeg encoding not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_encode_audio(
    video_encoder_t* encoder,
    const uint8_t* samples,
    size_t samples_len,
    int sample_format)
{
    (void)encoder;
    (void)samples;
    (void)samples_len;
    (void)sample_format;
    set_error("Video encoding not available (FFmpeg encoding not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_encoder_flush(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len,
    int* packet_available)
{
    (void)encoder;
    (void)output;
    (void)output_len;
    (void)packet_available;
    set_error("Video encoding not available (FFmpeg encoding not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int video_encoder_get_output(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len)
{
    (void)encoder;
    (void)output;
    (void)output_len;
    set_error("Video encoding not available (FFmpeg encoding not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API void video_encoder_destroy(
    video_encoder_t* encoder)
{
    (void)encoder;
    /* Nothing to do */
}

SHARPDICOM_API void video_encoder_free(
    uint8_t* buffer)
{
    (void)buffer;
    /* Nothing to do */
}

#endif /* SHARPDICOM_WITH_FFMPEG_ENC */
