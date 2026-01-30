/**
 * SharpDicom Native Codecs - JPEG Wrapper
 *
 * High-performance JPEG encoding/decoding using libjpeg-turbo's TurboJPEG API.
 * Supports DICOM-specific requirements including:
 * - 8-bit baseline JPEG (DCT-based lossy)
 * - 12-bit extended JPEG (DCT-based lossy, requires library built with -DWITH_12BIT)
 * - YBR colorspace conversion for DICOM PhotometricInterpretation
 *
 * Thread Safety: All functions are thread-safe. Handles are thread-local.
 */

#ifndef JPEG_WRAPPER_H
#define JPEG_WRAPPER_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Colorspace constants (matches DICOM PhotometricInterpretation)
 *============================================================================*/

/** JPEG colorspace for encoding/decoding */
typedef enum {
    JPEG_CS_RGB = 0,      /**< RGB (Photometric: RGB) */
    JPEG_CS_YBR = 1,      /**< YCbCr (Photometric: YBR_FULL, YBR_FULL_422, etc.) */
    JPEG_CS_GRAY = 2,     /**< Grayscale (Photometric: MONOCHROME1, MONOCHROME2) */
    JPEG_CS_CMYK = 3,     /**< CMYK (rare in DICOM) */
    JPEG_CS_UNKNOWN = -1  /**< Unknown/unspecified - let library detect */
} JpegColorspace;

/*============================================================================
 * Subsampling constants
 *============================================================================*/

/** Chroma subsampling for encoding */
typedef enum {
    JPEG_SAMP_444 = 0,    /**< 4:4:4 no subsampling (highest quality) */
    JPEG_SAMP_422 = 1,    /**< 4:2:2 horizontal subsampling */
    JPEG_SAMP_420 = 2,    /**< 4:2:0 both directions (most compression) */
    JPEG_SAMP_GRAY = 3,   /**< Grayscale (single component) */
    JPEG_SAMP_440 = 4,    /**< 4:4:0 vertical subsampling (rare) */
    JPEG_SAMP_411 = 5     /**< 4:1:1 (rare in JPEG) */
} JpegSubsampling;

/*============================================================================
 * Error codes (in addition to sharpdicom_codecs.h codes)
 *============================================================================*/

#define JPEG_ERR_INVALID_HEADER      -100  /**< JPEG header parsing failed */
#define JPEG_ERR_UNSUPPORTED_FORMAT  -101  /**< Unsupported pixel format */
#define JPEG_ERR_OUTPUT_TOO_SMALL    -102  /**< Output buffer too small */
#define JPEG_ERR_12BIT_NOT_SUPPORTED -103  /**< 12-bit not available (library limitation) */

/*============================================================================
 * 8-bit JPEG functions
 *============================================================================*/

/**
 * Decode a JPEG image to raw pixel data.
 *
 * @param input         Compressed JPEG data
 * @param inputLen      Length of input in bytes
 * @param output        Output buffer for raw pixel data (must be pre-allocated)
 * @param outputLen     Size of output buffer in bytes
 * @param width         [out] Image width in pixels
 * @param height        [out] Image height in pixels
 * @param components    [out] Number of color components (1=gray, 3=RGB)
 * @param colorspace    Desired output colorspace (JPEG_CS_RGB, JPEG_CS_GRAY, or JPEG_CS_UNKNOWN for auto)
 *
 * @return 0 on success, negative error code on failure
 *
 * Output buffer size should be at least: width * height * components bytes
 * Call jpeg_decode_header() first to determine required size.
 */
int jpeg_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components,
    int colorspace);

/**
 * Read JPEG header to determine dimensions without full decode.
 *
 * @param input         Compressed JPEG data
 * @param inputLen      Length of input in bytes
 * @param width         [out] Image width in pixels
 * @param height        [out] Image height in pixels
 * @param components    [out] Number of color components (1=gray, 3=RGB)
 * @param subsampling   [out] Chroma subsampling (JpegSubsampling enum)
 *
 * @return 0 on success, negative error code on failure
 */
int jpeg_decode_header(
    const uint8_t* input, int inputLen,
    int* width, int* height, int* components, int* subsampling);

/**
 * Encode raw pixel data to JPEG.
 *
 * @param input         Raw pixel data (RGB or grayscale)
 * @param width         Image width in pixels
 * @param height        Image height in pixels
 * @param components    Number of color components (1=gray, 3=RGB)
 * @param output        [out] Pointer to allocated output buffer (call jpeg_free after use)
 * @param outputLen     [out] Length of compressed data
 * @param quality       JPEG quality (1-100, 90 recommended for medical)
 * @param subsamp       Chroma subsampling (JPEG_SAMP_444 recommended for medical)
 *
 * @return 0 on success, negative error code on failure
 *
 * The output buffer is allocated by this function. Call jpeg_free() to release it.
 */
int jpeg_encode(
    const uint8_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality, int subsamp);

/**
 * Free a buffer allocated by jpeg_encode().
 *
 * @param buffer        Buffer to free (may be NULL)
 */
void jpeg_free(uint8_t* buffer);

/*============================================================================
 * 12-bit JPEG functions (DICOM Extended JPEG)
 *============================================================================*/

/**
 * Decode a 12-bit JPEG image to 16-bit pixel data.
 *
 * @param input         Compressed 12-bit JPEG data
 * @param inputLen      Length of input in bytes
 * @param output        Output buffer for 16-bit pixel data (uint16_t array)
 * @param outputLen     Size of output buffer in bytes
 * @param width         [out] Image width in pixels
 * @param height        [out] Image height in pixels
 * @param components    [out] Number of color components (typically 1 for grayscale)
 *
 * @return 0 on success, JPEG_ERR_12BIT_NOT_SUPPORTED if library lacks 12-bit support,
 *         or other negative error code on failure
 *
 * Note: 12-bit JPEG support requires libjpeg-turbo built with -DWITH_12BIT=1.
 * Most distributions do not include this flag.
 */
int jpeg_decode_12bit(
    const uint8_t* input, int inputLen,
    uint16_t* output, int outputLen,
    int* width, int* height, int* components);

/**
 * Encode 12-bit pixel data to JPEG.
 *
 * @param input         16-bit pixel data (12-bit values in uint16_t)
 * @param width         Image width in pixels
 * @param height        Image height in pixels
 * @param components    Number of color components (typically 1 for grayscale)
 * @param output        [out] Pointer to allocated output buffer
 * @param outputLen     [out] Length of compressed data
 * @param quality       JPEG quality (1-100)
 *
 * @return 0 on success, JPEG_ERR_12BIT_NOT_SUPPORTED if library lacks 12-bit support,
 *         or other negative error code on failure
 */
int jpeg_encode_12bit(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality);

/*============================================================================
 * Utility functions
 *============================================================================*/

/**
 * Check if 12-bit JPEG support is available.
 *
 * @return 1 if 12-bit support available, 0 otherwise
 */
int jpeg_has_12bit_support(void);

#ifdef __cplusplus
}
#endif

#endif /* JPEG_WRAPPER_H */
