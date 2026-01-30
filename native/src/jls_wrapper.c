/**
 * JPEG-LS Wrapper Implementation (CharLS)
 *
 * Wraps the CharLS library C API for JPEG-LS encoding and decoding.
 * Uses charls_jpegls_decoder/encoder APIs from charls/charls.h.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "jls_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>

#ifdef SHARPDICOM_HAS_CHARLS
#include <charls/charls.h>
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

#ifdef SHARPDICOM_HAS_CHARLS

/*============================================================================
 * CharLS error code to string conversion
 *============================================================================*/

static const char* charls_error_string(charls_jpegls_errc error) {
    switch (error) {
        case CHARLS_JPEGLS_ERRC_SUCCESS:
            return "Success";
        case CHARLS_JPEGLS_ERRC_INVALID_ARGUMENT:
            return "Invalid argument";
        case CHARLS_JPEGLS_ERRC_PARAMETER_VALUE_NOT_SUPPORTED:
            return "Parameter value not supported";
        case CHARLS_JPEGLS_ERRC_DESTINATION_BUFFER_TOO_SMALL:
            return "Destination buffer too small";
        case CHARLS_JPEGLS_ERRC_SOURCE_BUFFER_TOO_SMALL:
            return "Source buffer too small";
        case CHARLS_JPEGLS_ERRC_INVALID_ENCODED_DATA:
            return "Invalid encoded data";
        case CHARLS_JPEGLS_ERRC_TOO_MUCH_ENCODED_DATA:
            return "Too much encoded data";
        case CHARLS_JPEGLS_ERRC_INVALID_OPERATION:
            return "Invalid operation";
        case CHARLS_JPEGLS_ERRC_BIT_DEPTH_FOR_TRANSFORM_NOT_SUPPORTED:
            return "Bit depth for transform not supported";
        case CHARLS_JPEGLS_ERRC_COLOR_TRANSFORM_NOT_SUPPORTED:
            return "Color transform not supported";
        case CHARLS_JPEGLS_ERRC_ENCODING_NOT_SUPPORTED:
            return "Encoding not supported";
        case CHARLS_JPEGLS_ERRC_UNKNOWN_JPEG_MARKER_FOUND:
            return "Unknown JPEG marker found";
        case CHARLS_JPEGLS_ERRC_JPEG_MARKER_START_BYTE_NOT_FOUND:
            return "JPEG marker start byte not found";
        case CHARLS_JPEGLS_ERRC_NOT_ENOUGH_MEMORY:
            return "Not enough memory";
        case CHARLS_JPEGLS_ERRC_UNEXPECTED_FAILURE:
            return "Unexpected failure";
        case CHARLS_JPEGLS_ERRC_START_OF_IMAGE_MARKER_NOT_FOUND:
            return "Start of image marker not found";
        case CHARLS_JPEGLS_ERRC_UNEXPECTED_MARKER_FOUND:
            return "Unexpected marker found";
        case CHARLS_JPEGLS_ERRC_INVALID_MARKER_SEGMENT_SIZE:
            return "Invalid marker segment size";
        case CHARLS_JPEGLS_ERRC_DUPLICATE_START_OF_IMAGE_MARKER:
            return "Duplicate start of image marker";
        case CHARLS_JPEGLS_ERRC_DUPLICATE_START_OF_FRAME_MARKER:
            return "Duplicate start of frame marker";
        case CHARLS_JPEGLS_ERRC_DUPLICATE_COMPONENT_ID_IN_SOF_SEGMENT:
            return "Duplicate component ID in SOF segment";
        case CHARLS_JPEGLS_ERRC_UNEXPECTED_END_OF_IMAGE_MARKER:
            return "Unexpected end of image marker";
        case CHARLS_JPEGLS_ERRC_INVALID_JPEGLS_PRESET_PARAMETER_TYPE:
            return "Invalid JPEG-LS preset parameter type";
        case CHARLS_JPEGLS_ERRC_JPEGLS_PRESET_EXTENDED_PARAMETER_TYPE_NOT_SUPPORTED:
            return "JPEG-LS preset extended parameter type not supported";
        case CHARLS_JPEGLS_ERRC_MISSING_END_OF_SPIFF_DIRECTORY:
            return "Missing end of SPIFF directory";
        case CHARLS_JPEGLS_ERRC_UNEXPECTED_RESTART_MARKER:
            return "Unexpected restart marker";
        case CHARLS_JPEGLS_ERRC_RESTART_MARKER_NOT_FOUND:
            return "Restart marker not found";
        case CHARLS_JPEGLS_ERRC_CALLBACK_FAILED:
            return "Callback failed";
        case CHARLS_JPEGLS_ERRC_END_OF_IMAGE_MARKER_NOT_FOUND:
            return "End of image marker not found";
        default:
            return "Unknown CharLS error";
    }
}

/**
 * Map CharLS error to SharpDicom error code.
 */
static int charls_to_sharpdicom_error(charls_jpegls_errc error) {
    switch (error) {
        case CHARLS_JPEGLS_ERRC_SUCCESS:
            return SHARPDICOM_OK;
        case CHARLS_JPEGLS_ERRC_INVALID_ARGUMENT:
            return SHARPDICOM_ERR_INVALID_ARGUMENT;
        case CHARLS_JPEGLS_ERRC_NOT_ENOUGH_MEMORY:
            return SHARPDICOM_ERR_OUT_OF_MEMORY;
        case CHARLS_JPEGLS_ERRC_DESTINATION_BUFFER_TOO_SMALL:
        case CHARLS_JPEGLS_ERRC_SOURCE_BUFFER_TOO_SMALL:
            return SHARPDICOM_ERR_INVALID_ARGUMENT;
        case CHARLS_JPEGLS_ERRC_INVALID_ENCODED_DATA:
        case CHARLS_JPEGLS_ERRC_START_OF_IMAGE_MARKER_NOT_FOUND:
        case CHARLS_JPEGLS_ERRC_UNEXPECTED_MARKER_FOUND:
        case CHARLS_JPEGLS_ERRC_INVALID_MARKER_SEGMENT_SIZE:
        case CHARLS_JPEGLS_ERRC_END_OF_IMAGE_MARKER_NOT_FOUND:
            return SHARPDICOM_ERR_CORRUPT_DATA;
        case CHARLS_JPEGLS_ERRC_PARAMETER_VALUE_NOT_SUPPORTED:
        case CHARLS_JPEGLS_ERRC_BIT_DEPTH_FOR_TRANSFORM_NOT_SUPPORTED:
        case CHARLS_JPEGLS_ERRC_COLOR_TRANSFORM_NOT_SUPPORTED:
        case CHARLS_JPEGLS_ERRC_ENCODING_NOT_SUPPORTED:
            return SHARPDICOM_ERR_UNSUPPORTED;
        default:
            return SHARPDICOM_ERR_DECODE_FAILED;
    }
}

/**
 * Map CharLS interleave mode to JLS_INTERLEAVE_* constant.
 */
static int charls_to_jls_interleave(charls_interleave_mode mode) {
    switch (mode) {
        case CHARLS_INTERLEAVE_MODE_NONE:
            return JLS_INTERLEAVE_NONE;
        case CHARLS_INTERLEAVE_MODE_LINE:
            return JLS_INTERLEAVE_LINE;
        case CHARLS_INTERLEAVE_MODE_SAMPLE:
            return JLS_INTERLEAVE_SAMPLE;
        default:
            return JLS_INTERLEAVE_NONE;
    }
}

/**
 * Map JLS_INTERLEAVE_* constant to CharLS interleave mode.
 */
static charls_interleave_mode jls_to_charls_interleave(int mode) {
    switch (mode) {
        case JLS_INTERLEAVE_NONE:
            return CHARLS_INTERLEAVE_MODE_NONE;
        case JLS_INTERLEAVE_LINE:
            return CHARLS_INTERLEAVE_MODE_LINE;
        case JLS_INTERLEAVE_SAMPLE:
            return CHARLS_INTERLEAVE_MODE_SAMPLE;
        default:
            return CHARLS_INTERLEAVE_MODE_NONE;
    }
}

/*============================================================================
 * JPEG-LS decode implementation
 *============================================================================*/

SHARPDICOM_API int jls_get_decode_size(
    const uint8_t* input,
    size_t input_len,
    size_t* output_size,
    jls_decode_params_t* params)
{
    if (input == NULL || input_len == 0 || output_size == NULL) {
        set_error("Invalid argument: NULL input or output_size");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Create decoder */
    charls_jpegls_decoder* decoder = charls_jpegls_decoder_create();
    if (decoder == NULL) {
        set_error("Failed to create JPEG-LS decoder");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set source buffer */
    charls_jpegls_errc error = charls_jpegls_decoder_set_source_buffer(
        decoder, input, input_len);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to set source buffer: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Read SPIFF header (optional, may fail) */
    charls_spiff_header spiff_header;
    int32_t header_found = 0;
    charls_jpegls_decoder_read_spiff_header(decoder, &spiff_header, &header_found);

    /* Read frame info from JPEG-LS header */
    error = charls_jpegls_decoder_read_header(decoder);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to read JPEG-LS header: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Get frame info */
    charls_frame_info frame_info;
    error = charls_jpegls_decoder_get_frame_info(decoder, &frame_info);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to get frame info: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Calculate output size */
    size_t bytes_per_sample = (frame_info.bits_per_sample + 7) / 8;
    *output_size = (size_t)frame_info.width * frame_info.height *
                   frame_info.component_count * bytes_per_sample;

    /* Fill params if requested */
    if (params != NULL) {
        params->width = frame_info.width;
        params->height = frame_info.height;
        params->components = frame_info.component_count;
        params->bits_per_sample = frame_info.bits_per_sample;

        /* Get near-lossless parameter */
        int32_t near_lossless = 0;
        charls_jpegls_decoder_get_near_lossless(decoder, 0, &near_lossless);
        params->near_lossless = near_lossless;

        /* Get interleave mode */
        charls_interleave_mode interleave_mode;
        charls_jpegls_decoder_get_interleave_mode(decoder, &interleave_mode);
        params->interleave_mode = charls_to_jls_interleave(interleave_mode);
    }

    charls_jpegls_decoder_destroy(decoder);
    return SHARPDICOM_OK;
}

SHARPDICOM_API int jls_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    jls_decode_params_t* params)
{
    if (input == NULL || input_len == 0) {
        set_error("Invalid argument: NULL or empty input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || output_len == 0) {
        set_error("Invalid argument: NULL or empty output buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Create decoder */
    charls_jpegls_decoder* decoder = charls_jpegls_decoder_create();
    if (decoder == NULL) {
        set_error("Failed to create JPEG-LS decoder");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set source buffer */
    charls_jpegls_errc error = charls_jpegls_decoder_set_source_buffer(
        decoder, input, input_len);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to set source buffer: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Read SPIFF header (optional) */
    charls_spiff_header spiff_header;
    int32_t header_found = 0;
    charls_jpegls_decoder_read_spiff_header(decoder, &spiff_header, &header_found);

    /* Read frame info */
    error = charls_jpegls_decoder_read_header(decoder);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to read JPEG-LS header: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Get frame info for validation and params */
    charls_frame_info frame_info;
    error = charls_jpegls_decoder_get_frame_info(decoder, &frame_info);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to get frame info: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Fill params if requested */
    if (params != NULL) {
        params->width = frame_info.width;
        params->height = frame_info.height;
        params->components = frame_info.component_count;
        params->bits_per_sample = frame_info.bits_per_sample;

        int32_t near_lossless = 0;
        charls_jpegls_decoder_get_near_lossless(decoder, 0, &near_lossless);
        params->near_lossless = near_lossless;

        charls_interleave_mode interleave_mode;
        charls_jpegls_decoder_get_interleave_mode(decoder, &interleave_mode);
        params->interleave_mode = charls_to_jls_interleave(interleave_mode);
    }

    /* Validate output buffer size */
    size_t bytes_per_sample = (frame_info.bits_per_sample + 7) / 8;
    size_t required_size = (size_t)frame_info.width * frame_info.height *
                           frame_info.component_count * bytes_per_sample;
    if (output_len < required_size) {
        set_error_fmt("Output buffer too small: need %zu bytes, have %zu",
                      required_size, output_len);
        charls_jpegls_decoder_destroy(decoder);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Decode to output buffer */
    error = charls_jpegls_decoder_decode_to_buffer(
        decoder, output, output_len, 0);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to decode JPEG-LS data: %s", charls_error_string(error));
        charls_jpegls_decoder_destroy(decoder);
        return charls_to_sharpdicom_error(error);
    }

    charls_jpegls_decoder_destroy(decoder);
    return SHARPDICOM_OK;
}

/*============================================================================
 * JPEG-LS encode implementation
 *============================================================================*/

SHARPDICOM_API int jls_get_encode_bound(
    const jls_encode_params_t* params,
    size_t* max_size)
{
    if (params == NULL || max_size == NULL) {
        set_error("Invalid argument: NULL params or max_size");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate parameters */
    if (params->width <= 0 || params->height <= 0) {
        set_error("Invalid argument: width and height must be positive");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params->components <= 0 || params->components > 255) {
        set_error("Invalid argument: components must be 1-255");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params->bits_per_sample < 2 || params->bits_per_sample > 16) {
        set_error("Invalid argument: bits_per_sample must be 2-16");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Calculate worst-case size:
     * - Raw pixel data size
     * - Plus header overhead (~100 bytes)
     * - Plus potential expansion for incompressible data
     */
    size_t bytes_per_sample = (params->bits_per_sample + 7) / 8;
    size_t raw_size = (size_t)params->width * params->height *
                      params->components * bytes_per_sample;

    /* JPEG-LS can expand incompressible data slightly */
    *max_size = raw_size + (raw_size / 16) + 1024;

    return SHARPDICOM_OK;
}

SHARPDICOM_API int jls_encode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    size_t* actual_size,
    const jls_encode_params_t* params)
{
    if (input == NULL || input_len == 0) {
        set_error("Invalid argument: NULL or empty input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || output_len == 0) {
        set_error("Invalid argument: NULL or empty output buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (actual_size == NULL) {
        set_error("Invalid argument: NULL actual_size pointer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params == NULL) {
        set_error("Invalid argument: NULL params");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate parameters */
    if (params->width <= 0 || params->height <= 0) {
        set_error("Invalid argument: width and height must be positive");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params->components <= 0 || params->components > 255) {
        set_error("Invalid argument: components must be 1-255");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params->bits_per_sample < 2 || params->bits_per_sample > 16) {
        set_error("Invalid argument: bits_per_sample must be 2-16");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (params->near_lossless < 0 || params->near_lossless > 255) {
        set_error("Invalid argument: near_lossless must be 0-255");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate input size */
    size_t bytes_per_sample = (params->bits_per_sample + 7) / 8;
    size_t expected_input = (size_t)params->width * params->height *
                            params->components * bytes_per_sample;
    if (input_len < expected_input) {
        set_error_fmt("Input buffer too small: expected %zu bytes, have %zu",
                      expected_input, input_len);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Create encoder */
    charls_jpegls_encoder* encoder = charls_jpegls_encoder_create();
    if (encoder == NULL) {
        set_error("Failed to create JPEG-LS encoder");
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Set frame info */
    charls_frame_info frame_info = {
        .width = params->width,
        .height = params->height,
        .bits_per_sample = params->bits_per_sample,
        .component_count = params->components
    };

    charls_jpegls_errc error = charls_jpegls_encoder_set_frame_info(encoder, &frame_info);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to set frame info: %s", charls_error_string(error));
        charls_jpegls_encoder_destroy(encoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Set near-lossless parameter */
    if (params->near_lossless > 0) {
        error = charls_jpegls_encoder_set_near_lossless(encoder, params->near_lossless);
        if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
            set_error_fmt("Failed to set near-lossless: %s", charls_error_string(error));
            charls_jpegls_encoder_destroy(encoder);
            return charls_to_sharpdicom_error(error);
        }
    }

    /* Set interleave mode */
    error = charls_jpegls_encoder_set_interleave_mode(
        encoder, jls_to_charls_interleave(params->interleave_mode));
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to set interleave mode: %s", charls_error_string(error));
        charls_jpegls_encoder_destroy(encoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Set destination buffer */
    error = charls_jpegls_encoder_set_destination_buffer(encoder, output, output_len);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to set destination buffer: %s", charls_error_string(error));
        charls_jpegls_encoder_destroy(encoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Encode */
    error = charls_jpegls_encoder_encode_from_buffer(
        encoder, input, input_len, 0);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to encode JPEG-LS data: %s", charls_error_string(error));
        charls_jpegls_encoder_destroy(encoder);
        return charls_to_sharpdicom_error(error);
    }

    /* Get actual encoded size */
    error = charls_jpegls_encoder_get_bytes_written(encoder, actual_size);
    if (error != CHARLS_JPEGLS_ERRC_SUCCESS) {
        set_error_fmt("Failed to get bytes written: %s", charls_error_string(error));
        charls_jpegls_encoder_destroy(encoder);
        return charls_to_sharpdicom_error(error);
    }

    charls_jpegls_encoder_destroy(encoder);
    return SHARPDICOM_OK;
}

/*============================================================================
 * Memory management
 *============================================================================*/

SHARPDICOM_API void jls_free(void* buffer) {
    if (buffer != NULL) {
        free(buffer);
    }
}

#else /* !SHARPDICOM_HAS_CHARLS */

/*============================================================================
 * Stub implementations when CharLS is not available
 *============================================================================*/

SHARPDICOM_API int jls_get_decode_size(
    const uint8_t* input,
    size_t input_len,
    size_t* output_size,
    jls_decode_params_t* params)
{
    (void)input;
    (void)input_len;
    (void)output_size;
    (void)params;
    set_error("JPEG-LS support not available (CharLS not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int jls_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    jls_decode_params_t* params)
{
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)params;
    set_error("JPEG-LS support not available (CharLS not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int jls_get_encode_bound(
    const jls_encode_params_t* params,
    size_t* max_size)
{
    (void)params;
    (void)max_size;
    set_error("JPEG-LS support not available (CharLS not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API int jls_encode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    size_t* actual_size,
    const jls_encode_params_t* params)
{
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)actual_size;
    (void)params;
    set_error("JPEG-LS support not available (CharLS not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API void jls_free(void* buffer) {
    if (buffer != NULL) {
        free(buffer);
    }
}

#endif /* SHARPDICOM_HAS_CHARLS */
