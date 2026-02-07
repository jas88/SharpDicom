/**
 * SharpDicom Native Codecs - 12-bit JPEG Wrapper
 *
 * High-performance 12-bit JPEG encoding/decoding using the raw libjpeg API
 * with symbol-prefixed libjpeg-turbo compiled with WITH_12BIT.
 *
 * Unlike the 8-bit path (which uses the TurboJPEG API with SIMD acceleration),
 * the 12-bit path uses the raw libjpeg API because building libjpeg-turbo with
 * WITH_12BIT disables TurboJPEG and SIMD. This is a libjpeg-turbo limitation.
 *
 * All libjpeg symbols in the 12-bit build are prefixed with "jpeg12_" to avoid
 * collisions with the 8-bit symbols in the same shared library.
 *
 * Thread Safety: All functions are thread-safe. Error messages stored in TLS.
 */

#ifndef JPEG12_WRAPPER_H
#define JPEG12_WRAPPER_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Error codes (shared with jpeg_wrapper.h)
 *============================================================================*/

#ifndef JPEG12_ERR_CODES_DEFINED
#define JPEG12_ERR_CODES_DEFINED
#define JPEG12_ERR_INVALID_HEADER      -100  /**< JPEG header parsing failed */
#define JPEG12_ERR_UNSUPPORTED_FORMAT  -101  /**< Unsupported pixel format */
#define JPEG12_ERR_OUTPUT_TOO_SMALL    -102  /**< Output buffer too small */
#endif

/*============================================================================
 * 12-bit JPEG decode
 *============================================================================*/

/**
 * Decode a 12-bit JPEG image to 16-bit pixel data.
 *
 * Uses the raw libjpeg API (symbol-prefixed 12-bit build) for decoding.
 * Each sample is stored as a uint16_t value in the range [0, 4095].
 *
 * @param input         Compressed 12-bit JPEG data
 * @param inputLen      Length of input in bytes
 * @param output        Output buffer for 16-bit pixel data (uint16_t array)
 * @param outputLen     Size of output buffer in bytes
 * @param width         [out] Image width in pixels
 * @param height        [out] Image height in pixels
 * @param components    [out] Number of color components (1=gray, 3=color)
 *
 * @return SHARPDICOM_OK on success, SHARPDICOM_ERR_UNSUPPORTED if 12-bit
 *         not compiled in, or other negative error code on failure
 *
 * Output buffer size should be at least: width * height * components * 2 bytes
 */
int jpeg12_decode(
    const uint8_t* input, size_t inputLen,
    uint16_t* output, size_t outputLen,
    int* width, int* height, int* components);

/*============================================================================
 * 12-bit JPEG encode
 *============================================================================*/

/**
 * Encode 16-bit pixel data (12-bit range) to 12-bit JPEG.
 *
 * Uses the raw libjpeg API (symbol-prefixed 12-bit build) for encoding.
 * Input samples should be in the range [0, 4095].
 *
 * @param input         16-bit pixel data (12-bit values in uint16_t)
 * @param width         Image width in pixels
 * @param height        Image height in pixels
 * @param components    Number of color components (1=gray, 3=color)
 * @param output        [out] Pointer to allocated output buffer
 * @param outputLen     [out] Length of compressed data in bytes
 * @param quality       JPEG quality (1-100, 90 recommended for medical)
 *
 * @return SHARPDICOM_OK on success, SHARPDICOM_ERR_UNSUPPORTED if 12-bit
 *         not compiled in, or other negative error code on failure
 *
 * The output buffer is allocated by this function. Call jpeg12_free() to release.
 */
int jpeg12_encode(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, size_t* outputLen,
    int quality);

/*============================================================================
 * Memory management
 *============================================================================*/

/**
 * Free a buffer allocated by jpeg12_encode().
 *
 * @param buffer        Buffer to free (may be NULL)
 */
void jpeg12_free(uint8_t* buffer);

/*============================================================================
 * Capability query
 *============================================================================*/

/**
 * Check if 12-bit JPEG support is available.
 *
 * @return 1 if 12-bit JPEG support is compiled in, 0 otherwise
 */
int jpeg12_has_support(void);

#ifdef __cplusplus
}
#endif

#endif /* JPEG12_WRAPPER_H */
