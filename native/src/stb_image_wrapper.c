/**
 * stb_image Wrapper Implementation
 *
 * Provides memory-based image loading using stb_image when
 * SHARPDICOM_WITH_STB_IMAGE is defined, and stubs otherwise.
 *
 * stb_image configuration:
 *   - STBI_NO_STDIO: Disable file I/O (memory-only loading)
 *   - STBI_NO_HDR: Disable HDR format (not needed for DICOM)
 *   - STBI_NO_LINEAR: Disable linear float conversion
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "stb_image_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdlib.h>
#include <string.h>

/* Forward declaration from sharpdicom_codecs.c */
extern void set_error(const char* message);
extern void set_error_fmt(const char* fmt, ...);

#ifdef SHARPDICOM_WITH_STB_IMAGE

/* Configure stb_image before including the implementation */
#define STBI_NO_STDIO       /* No file I/O - memory buffers only */
#define STBI_NO_HDR         /* No HDR format support */
#define STBI_NO_LINEAR      /* No linear float conversion */

/* Use the implementation in this translation unit */
#define STB_IMAGE_IMPLEMENTATION
#include "../vendor/stb/stb_image.h"

/*============================================================================
 * stb_image wrapper implementation (stb_image available)
 *============================================================================*/

SHARPDICOM_API int stbi_load_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    int desired_channels,
    uint8_t** pixels_out,
    stbi_image_info_t* info_out)
{
    if (buffer == NULL || buffer_len == 0) {
        set_error("Invalid argument: NULL or empty image buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (pixels_out == NULL) {
        set_error("Invalid argument: NULL pixels_out");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    *pixels_out = NULL;

    /* Validate desired_channels range */
    if (desired_channels < 0 || desired_channels > 4) {
        set_error_fmt("Invalid desired_channels: %d (must be 0-4)", desired_channels);
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate buffer_len fits in int (stb_image uses int for length) */
    if (buffer_len > (size_t)INT_MAX) {
        set_error("Image buffer too large for stb_image");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    int width = 0, height = 0, channels = 0;

    /* Load image from memory */
    unsigned char* pixels = stbi_load_from_memory(
        buffer,
        (int)buffer_len,
        &width,
        &height,
        &channels,
        desired_channels
    );

    if (pixels == NULL) {
        const char* reason = stbi_failure_reason();
        if (reason != NULL) {
            set_error_fmt("Failed to decode image: %s", reason);
        } else {
            set_error("Failed to decode image: unknown error");
        }
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    *pixels_out = pixels;

    if (info_out != NULL) {
        info_out->width = width;
        info_out->height = height;
        info_out->channels = (desired_channels > 0) ? desired_channels : channels;
    }

    return SHARPDICOM_OK;
}

SHARPDICOM_API void stbi_free_wrapper(
    uint8_t* pixels)
{
    if (pixels != NULL) {
        stbi_image_free(pixels);
    }
}

SHARPDICOM_API int stbi_info_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    stbi_image_info_t* info_out)
{
    if (buffer == NULL || buffer_len == 0) {
        set_error("Invalid argument: NULL or empty image buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    if (info_out == NULL) {
        set_error("Invalid argument: NULL info_out");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Validate buffer_len fits in int */
    if (buffer_len > (size_t)INT_MAX) {
        set_error("Image buffer too large for stb_image");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    int width = 0, height = 0, channels = 0;

    int result = stbi_info_from_memory(
        buffer,
        (int)buffer_len,
        &width,
        &height,
        &channels
    );

    if (!result) {
        const char* reason = stbi_failure_reason();
        if (reason != NULL) {
            set_error_fmt("Failed to read image info: %s", reason);
        } else {
            set_error("Failed to read image info: unknown error");
        }
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    info_out->width = width;
    info_out->height = height;
    info_out->channels = channels;

    return SHARPDICOM_OK;
}

#else /* !SHARPDICOM_WITH_STB_IMAGE */

/*============================================================================
 * Stub implementations when stb_image is not available
 *============================================================================*/

SHARPDICOM_API int stbi_load_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    int desired_channels,
    uint8_t** pixels_out,
    stbi_image_info_t* info_out)
{
    (void)buffer;
    (void)buffer_len;
    (void)desired_channels;
    (void)pixels_out;
    (void)info_out;
    set_error("Image loading not available (stb_image not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

SHARPDICOM_API void stbi_free_wrapper(
    uint8_t* pixels)
{
    (void)pixels;
    /* Nothing to do */
}

SHARPDICOM_API int stbi_info_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    stbi_image_info_t* info_out)
{
    (void)buffer;
    (void)buffer_len;
    (void)info_out;
    set_error("Image info not available (stb_image not linked)");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

#endif /* SHARPDICOM_WITH_STB_IMAGE */
