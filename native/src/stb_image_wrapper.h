/**
 * stb_image Wrapper API
 *
 * Provides memory-based image loading for PNG, BMP, JPEG, and TGA formats
 * using the stb_image single-header library (v2.30, public domain).
 *
 * This wrapper is used by the video encoder to load image sequences
 * from memory buffers for encoding into DICOM video streams.
 *
 * Thread Safety: Each call is independently thread-safe (no shared state).
 */

#ifndef STB_IMAGE_WRAPPER_H
#define STB_IMAGE_WRAPPER_H

#include "sharpdicom_codecs.h"

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Image format constants
 *============================================================================*/

/** Desired output component count */
#define STBI_COMP_DEFAULT       0   /* Use image's native component count */
#define STBI_COMP_GREY          1   /* Force 1-component grayscale */
#define STBI_COMP_GREY_ALPHA    2   /* Force 2-component grey+alpha */
#define STBI_COMP_RGB           3   /* Force 3-component RGB */
#define STBI_COMP_RGBA          4   /* Force 4-component RGBA */

/*============================================================================
 * Image info structure
 *============================================================================*/

/**
 * Information about a loaded image.
 */
typedef struct {
    int width;          /**< Image width in pixels */
    int height;         /**< Image height in pixels */
    int channels;       /**< Number of channels (1=grey, 2=grey+alpha, 3=RGB, 4=RGBA) */
} stbi_image_info_t;

/*============================================================================
 * stb_image wrapper API functions
 *============================================================================*/

/**
 * Loads an image from a memory buffer.
 *
 * Supports PNG, BMP, JPEG, TGA, PSD, GIF, HDR, and PIC formats.
 * The returned pixel data must be freed with stbi_free_wrapper().
 *
 * @param buffer            Input image data
 * @param buffer_len        Length of input data in bytes
 * @param desired_channels  Desired output channels (STBI_COMP_*), 0 for native
 * @param pixels_out        Pointer to receive decoded pixel data
 * @param info_out          Pointer to receive image information
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: NULL parameters
 *         - SHARPDICOM_ERR_UNSUPPORTED: stb_image not compiled in
 *         - SHARPDICOM_ERR_DECODE_FAILED: Image could not be decoded
 */
SHARPDICOM_API int stbi_load_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    int desired_channels,
    uint8_t** pixels_out,
    stbi_image_info_t* info_out
);

/**
 * Frees pixel data returned by stbi_load_from_memory_wrapper().
 *
 * @param pixels  Pixel data to free (may be NULL)
 */
SHARPDICOM_API void stbi_free_wrapper(
    uint8_t* pixels
);

/**
 * Queries image information without decoding.
 *
 * Reads only the image header to determine dimensions and format.
 *
 * @param buffer        Input image data
 * @param buffer_len    Length of input data in bytes
 * @param info_out      Pointer to receive image information
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int stbi_info_from_memory_wrapper(
    const uint8_t* buffer,
    size_t buffer_len,
    stbi_image_info_t* info_out
);

#ifdef __cplusplus
}
#endif

#endif /* STB_IMAGE_WRAPPER_H */
