/**
 * SharpDicom JPEG 2000 Wrapper Implementation
 *
 * Wraps OpenJPEG library for JPEG 2000 encoding and decoding.
 * Supports resolution level decode (thumbnails), ROI decode, and tiled encoding.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "j2k_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>

/* Only compile OpenJPEG wrapper if available */
#ifdef SHARPDICOM_HAS_OPENJPEG

#include <openjpeg.h>

/*============================================================================
 * Thread-local error handling
 *============================================================================*/

extern void set_error_fmt(const char* fmt, ...);

/* Forward declare set_error if not already visible */
#ifndef SET_ERROR_DECLARED
#define SET_ERROR_DECLARED
extern void set_error(const char* message);
#endif

/*============================================================================
 * Memory stream for OpenJPEG
 *============================================================================*/

/** Stream user data for memory I/O */
typedef struct {
    const uint8_t* data;
    size_t size;
    size_t offset;
} MemoryStreamReader;

typedef struct {
    uint8_t* data;
    size_t capacity;
    size_t size;
} MemoryStreamWriter;

static OPJ_SIZE_T mem_stream_read(void* buffer, OPJ_SIZE_T nb_bytes, void* user_data) {
    MemoryStreamReader* reader = (MemoryStreamReader*)user_data;
    if (reader->offset >= reader->size) {
        return (OPJ_SIZE_T)-1; /* EOF */
    }
    OPJ_SIZE_T available = reader->size - reader->offset;
    OPJ_SIZE_T to_read = (nb_bytes < available) ? nb_bytes : available;
    memcpy(buffer, reader->data + reader->offset, to_read);
    reader->offset += to_read;
    return to_read;
}

static OPJ_OFF_T mem_stream_skip_read(OPJ_OFF_T nb_bytes, void* user_data) {
    MemoryStreamReader* reader = (MemoryStreamReader*)user_data;
    if (nb_bytes < 0) {
        /* Backward skip */
        OPJ_SIZE_T back = (OPJ_SIZE_T)(-nb_bytes);
        if (back > reader->offset) {
            reader->offset = 0;
            return -(OPJ_OFF_T)back;
        }
        reader->offset -= back;
        return nb_bytes;
    }
    /* Forward skip */
    OPJ_SIZE_T available = reader->size - reader->offset;
    OPJ_SIZE_T to_skip = ((OPJ_SIZE_T)nb_bytes < available) ? (OPJ_SIZE_T)nb_bytes : available;
    reader->offset += to_skip;
    return (OPJ_OFF_T)to_skip;
}

static OPJ_BOOL mem_stream_seek_read(OPJ_OFF_T nb_bytes, void* user_data) {
    MemoryStreamReader* reader = (MemoryStreamReader*)user_data;
    if (nb_bytes < 0 || (OPJ_SIZE_T)nb_bytes > reader->size) {
        return OPJ_FALSE;
    }
    reader->offset = (size_t)nb_bytes;
    return OPJ_TRUE;
}

static OPJ_SIZE_T mem_stream_write(void* buffer, OPJ_SIZE_T nb_bytes, void* user_data) {
    MemoryStreamWriter* writer = (MemoryStreamWriter*)user_data;
    if (writer->size + nb_bytes > writer->capacity) {
        return (OPJ_SIZE_T)-1; /* Buffer overflow */
    }
    memcpy(writer->data + writer->size, buffer, nb_bytes);
    writer->size += nb_bytes;
    return nb_bytes;
}

static OPJ_OFF_T mem_stream_skip_write(OPJ_OFF_T nb_bytes, void* user_data) {
    MemoryStreamWriter* writer = (MemoryStreamWriter*)user_data;
    if (nb_bytes < 0) {
        /* Backward skip */
        OPJ_SIZE_T back = (OPJ_SIZE_T)(-nb_bytes);
        if (back > writer->size) {
            writer->size = 0;
            return -(OPJ_OFF_T)back;
        }
        writer->size -= back;
        return nb_bytes;
    }
    /* Forward skip - fill with zeros */
    if (writer->size + (OPJ_SIZE_T)nb_bytes > writer->capacity) {
        OPJ_OFF_T available = (OPJ_OFF_T)(writer->capacity - writer->size);
        memset(writer->data + writer->size, 0, (size_t)available);
        writer->size = writer->capacity;
        return available;
    }
    memset(writer->data + writer->size, 0, (size_t)nb_bytes);
    writer->size += (size_t)nb_bytes;
    return nb_bytes;
}

static OPJ_BOOL mem_stream_seek_write(OPJ_OFF_T nb_bytes, void* user_data) {
    MemoryStreamWriter* writer = (MemoryStreamWriter*)user_data;
    if (nb_bytes < 0 || (OPJ_SIZE_T)nb_bytes > writer->capacity) {
        return OPJ_FALSE;
    }
    /* Extend with zeros if seeking past current size */
    if ((OPJ_SIZE_T)nb_bytes > writer->size) {
        memset(writer->data + writer->size, 0, (size_t)nb_bytes - writer->size);
    }
    writer->size = (size_t)nb_bytes;
    return OPJ_TRUE;
}

/*============================================================================
 * OpenJPEG message handlers
 *============================================================================*/

static void opj_error_callback(const char* msg, void* client_data) {
    (void)client_data;
    /* Store error message in thread-local storage */
    if (msg != NULL) {
        /* Remove trailing newline if present */
        size_t len = strlen(msg);
        if (len > 0 && msg[len - 1] == '\n') {
            char* clean = (char*)malloc(len);
            if (clean) {
                memcpy(clean, msg, len - 1);
                clean[len - 1] = '\0';
                set_error(clean);
                free(clean);
            }
        } else {
            set_error(msg);
        }
    }
}

static void opj_warning_callback(const char* msg, void* client_data) {
    (void)msg;
    (void)client_data;
    /* Warnings are ignored for now */
}

static void opj_info_callback(const char* msg, void* client_data) {
    (void)msg;
    (void)client_data;
    /* Info messages are ignored */
}

/*============================================================================
 * Helper: Detect format from magic bytes
 *============================================================================*/

static J2kFormat detect_format(const uint8_t* data, size_t size) {
    if (size < 12) {
        return J2K_FORMAT_J2K; /* Assume raw codestream */
    }

    /* JP2 file format signature: 12 bytes */
    static const uint8_t jp2_signature[] = {
        0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A
    };

    /* J2K codestream magic: FF 4F (SOC marker) */
    if (data[0] == 0xFF && data[1] == 0x4F) {
        return J2K_FORMAT_J2K;
    }

    /* Check JP2 signature */
    if (memcmp(data, jp2_signature, 12) == 0) {
        return J2K_FORMAT_JP2;
    }

    /* Alternative JP2 check: 'jP  ' box type at offset 4 */
    if (data[4] == 0x6A && data[5] == 0x50 && data[6] == 0x20 && data[7] == 0x20) {
        return J2K_FORMAT_JP2;
    }

    /* Default to raw codestream */
    return J2K_FORMAT_J2K;
}

/*============================================================================
 * Helper: Create stream for reading
 *============================================================================*/

static opj_stream_t* create_read_stream(MemoryStreamReader* reader) {
    opj_stream_t* stream = opj_stream_default_create(OPJ_TRUE /* input */);
    if (!stream) {
        return NULL;
    }

    opj_stream_set_user_data(stream, reader, NULL);
    opj_stream_set_user_data_length(stream, reader->size);
    opj_stream_set_read_function(stream, mem_stream_read);
    opj_stream_set_skip_function(stream, mem_stream_skip_read);
    opj_stream_set_seek_function(stream, mem_stream_seek_read);

    return stream;
}

/*============================================================================
 * Helper: Create stream for writing
 *============================================================================*/

static opj_stream_t* create_write_stream(MemoryStreamWriter* writer) {
    opj_stream_t* stream = opj_stream_default_create(OPJ_FALSE /* output */);
    if (!stream) {
        return NULL;
    }

    opj_stream_set_user_data(stream, writer, NULL);
    opj_stream_set_write_function(stream, mem_stream_write);
    opj_stream_set_skip_function(stream, mem_stream_skip_write);
    opj_stream_set_seek_function(stream, mem_stream_seek_write);

    return stream;
}

/*============================================================================
 * Helper: Map OpenJPEG color space to our enum
 *============================================================================*/

static J2kColorSpace map_colorspace(OPJ_COLOR_SPACE opj_cs) {
    switch (opj_cs) {
        case OPJ_CLRSPC_GRAY:  return J2K_COLORSPACE_GRAY;
        case OPJ_CLRSPC_SRGB:  return J2K_COLORSPACE_RGB;
        case OPJ_CLRSPC_SYCC:  return J2K_COLORSPACE_SYCC;
        default:              return J2K_COLORSPACE_UNKNOWN;
    }
}

/*============================================================================
 * API Implementation
 *============================================================================*/

SHARPDICOM_API int j2k_get_info(
    const uint8_t* input,
    size_t input_len,
    J2kImageInfo* info
) {
    if (!input || input_len == 0 || !info) {
        set_error("Invalid parameters: input, input_len, or info is NULL/zero");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    memset(info, 0, sizeof(J2kImageInfo));

    /* Detect format */
    J2kFormat format = detect_format(input, input_len);
    info->format = format;

    /* Create codec based on format */
    OPJ_CODEC_FORMAT codec_format = (format == J2K_FORMAT_JP2) ? OPJ_CODEC_JP2 : OPJ_CODEC_J2K;
    opj_codec_t* codec = opj_create_decompress(codec_format);
    if (!codec) {
        set_error("Failed to create OpenJPEG decompressor");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set up message handlers */
    opj_set_error_handler(codec, opj_error_callback, NULL);
    opj_set_warning_handler(codec, opj_warning_callback, NULL);
    opj_set_info_handler(codec, opj_info_callback, NULL);

    /* Set decode parameters */
    opj_dparameters_t params;
    opj_set_default_decoder_parameters(&params);

    if (!opj_setup_decoder(codec, &params)) {
        set_error("Failed to setup decoder parameters");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Create memory stream */
    MemoryStreamReader reader = { input, input_len, 0 };
    opj_stream_t* stream = create_read_stream(&reader);
    if (!stream) {
        set_error("Failed to create memory stream");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Read header */
    opj_image_t* image = NULL;
    if (!opj_read_header(stream, codec, &image)) {
        set_error("Failed to read JPEG 2000 header");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_CORRUPT_DATA;
    }

    /* Extract information */
    info->width = (int32_t)(image->x1 - image->x0);
    info->height = (int32_t)(image->y1 - image->y0);
    info->num_components = (int32_t)image->numcomps;
    info->color_space = map_colorspace(image->color_space);

    if (image->numcomps > 0) {
        info->bits_per_component = (int32_t)image->comps[0].prec;
        info->is_signed = image->comps[0].sgnd ? 1 : 0;
    }

    /* Get codestream info for resolution levels */
    opj_codestream_info_v2_t* cs_info = opj_get_cstr_info(codec);
    if (cs_info) {
        /* Get tile info for resolution count */
        if (cs_info->m_default_tile_info.tccp_info) {
            info->num_resolutions = (int32_t)cs_info->m_default_tile_info.tccp_info[0].numresolutions;
        }
        info->num_quality_layers = (int32_t)cs_info->m_default_tile_info.numlayers;
        info->tile_width = (int32_t)cs_info->tdx;
        info->tile_height = (int32_t)cs_info->tdy;
        info->num_tiles_x = (int32_t)cs_info->tw;
        info->num_tiles_y = (int32_t)cs_info->th;
        opj_destroy_cstr_info(&cs_info);
    }

    /* Cleanup */
    opj_image_destroy(image);
    opj_stream_destroy(stream);
    opj_destroy_codec(codec);

    return SHARPDICOM_OK;
}

SHARPDICOM_API int j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
) {
    if (!input || input_len == 0 || !output || output_len == 0) {
        set_error("Invalid parameters: input or output buffer is NULL/zero");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Detect format */
    J2kFormat format = detect_format(input, input_len);

    /* Create codec */
    OPJ_CODEC_FORMAT codec_format = (format == J2K_FORMAT_JP2) ? OPJ_CODEC_JP2 : OPJ_CODEC_J2K;
    opj_codec_t* codec = opj_create_decompress(codec_format);
    if (!codec) {
        set_error("Failed to create OpenJPEG decompressor");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set up message handlers */
    opj_set_error_handler(codec, opj_error_callback, NULL);
    opj_set_warning_handler(codec, opj_warning_callback, NULL);
    opj_set_info_handler(codec, opj_info_callback, NULL);

    /* Set decode parameters */
    opj_dparameters_t params;
    opj_set_default_decoder_parameters(&params);

    /* Apply decode options */
    if (options) {
        params.cp_reduce = (OPJ_UINT32)options->reduce;
        params.cp_layer = (OPJ_UINT32)options->max_quality_layers;
    }

    if (!opj_setup_decoder(codec, &params)) {
        set_error("Failed to setup decoder parameters");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Create memory stream */
    MemoryStreamReader reader = { input, input_len, 0 };
    opj_stream_t* stream = create_read_stream(&reader);
    if (!stream) {
        set_error("Failed to create memory stream");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Read header */
    opj_image_t* image = NULL;
    if (!opj_read_header(stream, codec, &image)) {
        set_error("Failed to read JPEG 2000 header");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_CORRUPT_DATA;
    }

    /* Decode image */
    if (!opj_decode(codec, stream, image)) {
        set_error("Failed to decode JPEG 2000 image");
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* End decoding */
    if (!opj_end_decompress(codec, stream)) {
        /* Non-fatal - some files don't have proper end marker */
    }

    /* Calculate output dimensions */
    int32_t width = (int32_t)(image->comps[0].w);
    int32_t height = (int32_t)(image->comps[0].h);
    int32_t num_comps = (int32_t)image->numcomps;
    int32_t bits = (int32_t)image->comps[0].prec;
    int32_t bytes_per_sample = (bits <= 8) ? 1 : 2;

    /* Check output buffer size */
    size_t required_size = (size_t)width * (size_t)height * (size_t)num_comps * (size_t)bytes_per_sample;
    if (output_len < required_size) {
        set_error("Output buffer too small");
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Copy decoded data to output buffer (component-interleaved) */
    if (bytes_per_sample == 1) {
        /* 8-bit output */
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t out_idx = pixel_idx * (size_t)num_comps;
                for (int32_t c = 0; c < num_comps; c++) {
                    int32_t val = image->comps[c].data[pixel_idx];
                    /* Handle signed values */
                    if (image->comps[c].sgnd) {
                        val += (1 << (bits - 1));
                    }
                    /* Clamp to [0, 255] */
                    if (val < 0) val = 0;
                    if (val > 255) val = 255;
                    output[out_idx + c] = (uint8_t)val;
                }
            }
        }
    } else {
        /* 16-bit output (little-endian) */
        uint16_t* out16 = (uint16_t*)output;
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t out_idx = pixel_idx * (size_t)num_comps;
                for (int32_t c = 0; c < num_comps; c++) {
                    int32_t val = image->comps[c].data[pixel_idx];
                    /* Handle signed values */
                    if (image->comps[c].sgnd) {
                        val += (1 << (bits - 1));
                    }
                    /* Clamp to [0, 65535] */
                    if (val < 0) val = 0;
                    if (val > 65535) val = 65535;
                    out16[out_idx + c] = (uint16_t)val;
                }
            }
        }
    }

    /* Return output dimensions */
    if (out_width) *out_width = width;
    if (out_height) *out_height = height;
    if (out_components) *out_components = num_comps;

    /* Cleanup */
    opj_image_destroy(image);
    opj_stream_destroy(stream);
    opj_destroy_codec(codec);

    return SHARPDICOM_OK;
}

SHARPDICOM_API int j2k_decode_region(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int32_t x0,
    int32_t y0,
    int32_t x1,
    int32_t y1,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
) {
    if (!input || input_len == 0 || !output || output_len == 0) {
        set_error("Invalid parameters: input or output buffer is NULL/zero");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (x0 >= x1 || y0 >= y1) {
        set_error("Invalid region: x0 >= x1 or y0 >= y1");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Detect format */
    J2kFormat format = detect_format(input, input_len);

    /* Create codec */
    OPJ_CODEC_FORMAT codec_format = (format == J2K_FORMAT_JP2) ? OPJ_CODEC_JP2 : OPJ_CODEC_J2K;
    opj_codec_t* codec = opj_create_decompress(codec_format);
    if (!codec) {
        set_error("Failed to create OpenJPEG decompressor");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set up message handlers */
    opj_set_error_handler(codec, opj_error_callback, NULL);
    opj_set_warning_handler(codec, opj_warning_callback, NULL);
    opj_set_info_handler(codec, opj_info_callback, NULL);

    /* Set decode parameters */
    opj_dparameters_t params;
    opj_set_default_decoder_parameters(&params);

    /* Apply decode options */
    if (options) {
        params.cp_reduce = (OPJ_UINT32)options->reduce;
        params.cp_layer = (OPJ_UINT32)options->max_quality_layers;
    }

    if (!opj_setup_decoder(codec, &params)) {
        set_error("Failed to setup decoder parameters");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Create memory stream */
    MemoryStreamReader reader = { input, input_len, 0 };
    opj_stream_t* stream = create_read_stream(&reader);
    if (!stream) {
        set_error("Failed to create memory stream");
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Read header */
    opj_image_t* image = NULL;
    if (!opj_read_header(stream, codec, &image)) {
        set_error("Failed to read JPEG 2000 header");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_CORRUPT_DATA;
    }

    /* Set decode area (ROI) */
    if (!opj_set_decode_area(codec, image,
                            (OPJ_INT32)x0, (OPJ_INT32)y0,
                            (OPJ_INT32)x1, (OPJ_INT32)y1)) {
        set_error("Failed to set decode area");
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Decode region */
    if (!opj_decode(codec, stream, image)) {
        set_error("Failed to decode JPEG 2000 region");
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* End decoding */
    opj_end_decompress(codec, stream);

    /* Calculate output dimensions (region may be smaller due to reduction) */
    int32_t width = (int32_t)(image->comps[0].w);
    int32_t height = (int32_t)(image->comps[0].h);
    int32_t num_comps = (int32_t)image->numcomps;
    int32_t bits = (int32_t)image->comps[0].prec;
    int32_t bytes_per_sample = (bits <= 8) ? 1 : 2;

    /* Check output buffer size */
    size_t required_size = (size_t)width * (size_t)height * (size_t)num_comps * (size_t)bytes_per_sample;
    if (output_len < required_size) {
        set_error("Output buffer too small for region");
        opj_image_destroy(image);
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Copy decoded data to output buffer (component-interleaved) */
    if (bytes_per_sample == 1) {
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t out_idx = pixel_idx * (size_t)num_comps;
                for (int32_t c = 0; c < num_comps; c++) {
                    int32_t val = image->comps[c].data[pixel_idx];
                    if (image->comps[c].sgnd) {
                        val += (1 << (bits - 1));
                    }
                    if (val < 0) val = 0;
                    if (val > 255) val = 255;
                    output[out_idx + c] = (uint8_t)val;
                }
            }
        }
    } else {
        uint16_t* out16 = (uint16_t*)output;
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t out_idx = pixel_idx * (size_t)num_comps;
                for (int32_t c = 0; c < num_comps; c++) {
                    int32_t val = image->comps[c].data[pixel_idx];
                    if (image->comps[c].sgnd) {
                        val += (1 << (bits - 1));
                    }
                    if (val < 0) val = 0;
                    if (val > 65535) val = 65535;
                    out16[out_idx + c] = (uint16_t)val;
                }
            }
        }
    }

    /* Return output dimensions */
    if (out_width) *out_width = width;
    if (out_height) *out_height = height;
    if (out_components) *out_components = num_comps;

    /* Cleanup */
    opj_image_destroy(image);
    opj_stream_destroy(stream);
    opj_destroy_codec(codec);

    return SHARPDICOM_OK;
}

SHARPDICOM_API int j2k_encode(
    const uint8_t* input,
    size_t input_len,
    int32_t width,
    int32_t height,
    int32_t num_components,
    int32_t bits_per_component,
    int32_t is_signed,
    const J2kEncodeParams* params,
    uint8_t* output,
    size_t output_len,
    size_t* out_size
) {
    if (!input || input_len == 0 || !output || output_len == 0 || !out_size) {
        set_error("Invalid parameters: input, output, or out_size is NULL/zero");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (width <= 0 || height <= 0) {
        set_error("Invalid dimensions: width and height must be positive");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (num_components < 1 || num_components > 4) {
        set_error("Invalid components: must be 1-4");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (bits_per_component < 1 || bits_per_component > 16) {
        set_error("Invalid bits_per_component: must be 1-16");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Use defaults if params is NULL */
    J2kEncodeParams default_params = {
        .lossless = 1,
        .compression_ratio = 0,
        .quality = 0,
        .num_resolutions = 0,
        .num_quality_layers = 0,
        .tile_width = 0,
        .tile_height = 0,
        .format = J2K_FORMAT_J2K,
        .cblk_width_exp = 0,
        .cblk_height_exp = 0,
        .progression_order = 0
    };
    if (!params) {
        params = &default_params;
    }

    /* Create image component parameters */
    opj_image_cmptparm_t* cmptparms = (opj_image_cmptparm_t*)calloc(
        (size_t)num_components, sizeof(opj_image_cmptparm_t));
    if (!cmptparms) {
        set_error("Failed to allocate component parameters");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    for (int32_t c = 0; c < num_components; c++) {
        cmptparms[c].dx = 1;
        cmptparms[c].dy = 1;
        cmptparms[c].w = (OPJ_UINT32)width;
        cmptparms[c].h = (OPJ_UINT32)height;
        cmptparms[c].x0 = 0;
        cmptparms[c].y0 = 0;
        cmptparms[c].prec = (OPJ_UINT32)bits_per_component;
        cmptparms[c].bpp = (OPJ_UINT32)bits_per_component;
        cmptparms[c].sgnd = (OPJ_UINT32)is_signed;
    }

    /* Determine color space */
    OPJ_COLOR_SPACE color_space;
    if (num_components == 1) {
        color_space = OPJ_CLRSPC_GRAY;
    } else if (num_components == 3) {
        color_space = OPJ_CLRSPC_SRGB;
    } else {
        color_space = OPJ_CLRSPC_UNKNOWN;
    }

    /* Create image */
    opj_image_t* image = opj_image_create((OPJ_UINT32)num_components, cmptparms, color_space);
    free(cmptparms);

    if (!image) {
        set_error("Failed to create OpenJPEG image");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    image->x0 = 0;
    image->y0 = 0;
    image->x1 = (OPJ_UINT32)width;
    image->y1 = (OPJ_UINT32)height;

    /* Copy input data to image components */
    int32_t bytes_per_sample = (bits_per_component <= 8) ? 1 : 2;

    if (bytes_per_sample == 1) {
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t in_idx = pixel_idx * (size_t)num_components;
                for (int32_t c = 0; c < num_components; c++) {
                    int32_t val = (int32_t)input[in_idx + c];
                    if (is_signed) {
                        val -= (1 << (bits_per_component - 1));
                    }
                    image->comps[c].data[pixel_idx] = val;
                }
            }
        }
    } else {
        const uint16_t* in16 = (const uint16_t*)input;
        for (int32_t y = 0; y < height; y++) {
            for (int32_t x = 0; x < width; x++) {
                size_t pixel_idx = (size_t)y * (size_t)width + (size_t)x;
                size_t in_idx = pixel_idx * (size_t)num_components;
                for (int32_t c = 0; c < num_components; c++) {
                    int32_t val = (int32_t)in16[in_idx + c];
                    if (is_signed) {
                        val -= (1 << (bits_per_component - 1));
                    }
                    image->comps[c].data[pixel_idx] = val;
                }
            }
        }
    }

    /* Set encoder parameters */
    opj_cparameters_t cparams;
    opj_set_default_encoder_parameters(&cparams);

    /* Apply encoding parameters */
    if (params->lossless) {
        cparams.irreversible = 0;  /* Use 5/3 reversible DWT */
        cparams.tcp_numlayers = 1;
        cparams.tcp_rates[0] = 0; /* Lossless */
    } else {
        cparams.irreversible = 1;  /* Use 9/7 irreversible DWT */
        if (params->compression_ratio > 0) {
            cparams.tcp_numlayers = 1;
            cparams.tcp_rates[0] = params->compression_ratio;
            cparams.cp_disto_alloc = 1;
        } else if (params->quality > 0) {
            cparams.tcp_numlayers = 1;
            /* Map quality 1-100 to distortion (higher quality = lower distortion) */
            cparams.tcp_distoratio[0] = params->quality;
            cparams.cp_fixed_quality = 1;
        }
    }

    /* Resolution levels */
    if (params->num_resolutions > 0) {
        cparams.numresolution = params->num_resolutions;
    } else {
        /* Calculate based on image size */
        int32_t min_dim = (width < height) ? width : height;
        int32_t num_res = 1;
        while ((min_dim >> num_res) >= 32 && num_res < 7) {
            num_res++;
        }
        cparams.numresolution = num_res;
    }

    /* Quality layers */
    if (params->num_quality_layers > 0 && !params->lossless) {
        cparams.tcp_numlayers = (int)params->num_quality_layers;
    }

    /* Tiling */
    if (params->tile_width > 0 && params->tile_height > 0) {
        cparams.tile_size_on = OPJ_TRUE;
        cparams.cp_tdx = params->tile_width;
        cparams.cp_tdy = params->tile_height;
    }

    /* Code-block size */
    if (params->cblk_width_exp >= 4 && params->cblk_width_exp <= 10) {
        cparams.cblockw_init = (1 << params->cblk_width_exp);
    }
    if (params->cblk_height_exp >= 4 && params->cblk_height_exp <= 10) {
        cparams.cblockh_init = (1 << params->cblk_height_exp);
    }

    /* Progression order */
    cparams.prog_order = (OPJ_PROG_ORDER)params->progression_order;

    /* Create codec */
    OPJ_CODEC_FORMAT codec_format = (params->format == J2K_FORMAT_JP2) ? OPJ_CODEC_JP2 : OPJ_CODEC_J2K;
    opj_codec_t* codec = opj_create_compress(codec_format);
    if (!codec) {
        set_error("Failed to create OpenJPEG compressor");
        opj_image_destroy(image);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set up message handlers */
    opj_set_error_handler(codec, opj_error_callback, NULL);
    opj_set_warning_handler(codec, opj_warning_callback, NULL);
    opj_set_info_handler(codec, opj_info_callback, NULL);

    /* Setup encoder */
    if (!opj_setup_encoder(codec, &cparams, image)) {
        set_error("Failed to setup encoder parameters");
        opj_destroy_codec(codec);
        opj_image_destroy(image);
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Create memory stream for output */
    MemoryStreamWriter writer = { output, output_len, 0 };
    opj_stream_t* stream = create_write_stream(&writer);
    if (!stream) {
        set_error("Failed to create output stream");
        opj_destroy_codec(codec);
        opj_image_destroy(image);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Encode */
    if (!opj_start_compress(codec, image, stream)) {
        set_error("Failed to start compression");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        opj_image_destroy(image);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    if (!opj_encode(codec, stream)) {
        set_error("Failed to encode image");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        opj_image_destroy(image);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    if (!opj_end_compress(codec, stream)) {
        set_error("Failed to finish compression");
        opj_stream_destroy(stream);
        opj_destroy_codec(codec);
        opj_image_destroy(image);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    /* Return size */
    *out_size = writer.size;

    /* Cleanup */
    opj_stream_destroy(stream);
    opj_destroy_codec(codec);
    opj_image_destroy(image);

    return SHARPDICOM_OK;
}

SHARPDICOM_API void j2k_free(void* ptr) {
    if (ptr) {
        free(ptr);
    }
}

SHARPDICOM_API const char* j2k_version(void) {
    return opj_version();
}

#else /* SHARPDICOM_HAS_OPENJPEG not defined */

/*============================================================================
 * Stub implementations when OpenJPEG is not available
 *============================================================================*/

/* Forward declare set_error if not already visible */
#ifndef SET_ERROR_DECLARED
#define SET_ERROR_DECLARED
static void set_error(const char* message);
#endif

SHARPDICOM_API int j2k_get_info(
    const uint8_t* input,
    size_t input_len,
    J2kImageInfo* info
) {
    (void)input;
    (void)input_len;
    (void)info;
    set_error("JPEG 2000 support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
) {
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)options;
    (void)out_width;
    (void)out_height;
    (void)out_components;
    set_error("JPEG 2000 support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int j2k_decode_region(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int32_t x0,
    int32_t y0,
    int32_t x1,
    int32_t y1,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
) {
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)x0;
    (void)y0;
    (void)x1;
    (void)y1;
    (void)options;
    (void)out_width;
    (void)out_height;
    (void)out_components;
    set_error("JPEG 2000 support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int j2k_encode(
    const uint8_t* input,
    size_t input_len,
    int32_t width,
    int32_t height,
    int32_t num_components,
    int32_t bits_per_component,
    int32_t is_signed,
    const J2kEncodeParams* params,
    uint8_t* output,
    size_t output_len,
    size_t* out_size
) {
    (void)input;
    (void)input_len;
    (void)width;
    (void)height;
    (void)num_components;
    (void)bits_per_component;
    (void)is_signed;
    (void)params;
    (void)output;
    (void)output_len;
    (void)out_size;
    set_error("JPEG 2000 support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API void j2k_free(void* ptr) {
    if (ptr) {
        free(ptr);
    }
}

SHARPDICOM_API const char* j2k_version(void) {
    return NULL;
}

#endif /* SHARPDICOM_HAS_OPENJPEG */
