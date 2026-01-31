/**
 * JPEG-LS Wrapper API (CharLS)
 *
 * Provides JPEG-LS lossless and near-lossless encoding/decoding
 * using the CharLS library (ISO 14495-1).
 *
 * Thread Safety: All functions are thread-safe.
 */

#ifndef JLS_WRAPPER_H
#define JLS_WRAPPER_H

#include "sharpdicom_codecs.h"

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * JPEG-LS color space constants
 *============================================================================*/

/** JPEG-LS interleave modes */
#define JLS_INTERLEAVE_NONE     0  /* Non-interleaved (planar) */
#define JLS_INTERLEAVE_LINE     1  /* Line interleaved */
#define JLS_INTERLEAVE_SAMPLE   2  /* Sample interleaved (pixel) */

/*============================================================================
 * JPEG-LS decode parameters (output from decode)
 *============================================================================*/

/**
 * Parameters extracted from JPEG-LS header during decode.
 */
typedef struct {
    int width;              /* Image width in pixels */
    int height;             /* Image height in pixels */
    int components;         /* Number of color components (1=grayscale, 3=RGB) */
    int bits_per_sample;    /* Bits per sample (2-16) */
    int near_lossless;      /* Near-lossless parameter (0=lossless) */
    int interleave_mode;    /* JLS_INTERLEAVE_* value */
} jls_decode_params_t;

/*============================================================================
 * JPEG-LS encode parameters (input to encode)
 *============================================================================*/

/**
 * Parameters for JPEG-LS encoding.
 */
typedef struct {
    int width;              /* Image width in pixels */
    int height;             /* Image height in pixels */
    int components;         /* Number of color components (1=grayscale, 3=RGB) */
    int bits_per_sample;    /* Bits per sample (2-16) */
    int near_lossless;      /* Near-lossless parameter (0=lossless, >0=lossy threshold) */
    int interleave_mode;    /* JLS_INTERLEAVE_* value for output */
} jls_encode_params_t;

/*============================================================================
 * JPEG-LS API functions
 *============================================================================*/

/**
 * Decodes JPEG-LS compressed data to raw pixel data.
 *
 * The output buffer must be pre-allocated by the caller with sufficient
 * space for the decoded image. Use jls_get_decode_size() to determine
 * the required output buffer size.
 *
 * @param input         Pointer to JPEG-LS compressed data
 * @param input_len     Length of compressed data in bytes
 * @param output        Pointer to output buffer for decoded pixels
 * @param output_len    Size of output buffer in bytes
 * @param params        Pointer to receive image parameters (may be NULL)
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: NULL input or output
 *         - SHARPDICOM_ERR_CORRUPT_DATA: Invalid JPEG-LS stream
 *         - SHARPDICOM_ERR_OUT_OF_MEMORY: Allocation failed
 *         - SHARPDICOM_ERR_DECODE_FAILED: Decode operation failed
 */
SHARPDICOM_API int jls_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    jls_decode_params_t* params
);

/**
 * Gets the required output buffer size for decoding.
 *
 * Call this before jls_decode() to determine the required output buffer size.
 *
 * @param input         Pointer to JPEG-LS compressed data
 * @param input_len     Length of compressed data in bytes
 * @param output_size   Pointer to receive required output buffer size
 * @param params        Pointer to receive image parameters (may be NULL)
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int jls_get_decode_size(
    const uint8_t* input,
    size_t input_len,
    size_t* output_size,
    jls_decode_params_t* params
);

/**
 * Encodes raw pixel data to JPEG-LS format.
 *
 * The output buffer must be pre-allocated by the caller. A safe size is
 * the input size plus a margin for header overhead. Use jls_get_encode_bound()
 * for a precise upper bound.
 *
 * @param input         Pointer to raw pixel data
 * @param input_len     Length of input data in bytes
 * @param output        Pointer to output buffer for compressed data
 * @param output_len    Size of output buffer in bytes
 * @param actual_size   Pointer to receive actual encoded size
 * @param params        Encoding parameters
 *
 * @return SHARPDICOM_OK on success, or negative error code:
 *         - SHARPDICOM_ERR_INVALID_ARGUMENT: Invalid parameters
 *         - SHARPDICOM_ERR_OUT_OF_MEMORY: Allocation failed
 *         - SHARPDICOM_ERR_ENCODE_FAILED: Encode operation failed
 */
SHARPDICOM_API int jls_encode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    size_t* actual_size,
    const jls_encode_params_t* params
);

/**
 * Gets the maximum encoded size for given parameters.
 *
 * Returns an upper bound on the encoded size. The actual encoded size
 * may be smaller (and typically is for compressible data).
 *
 * @param params        Encoding parameters
 * @param max_size      Pointer to receive maximum encoded size
 *
 * @return SHARPDICOM_OK on success, or negative error code
 */
SHARPDICOM_API int jls_get_encode_bound(
    const jls_encode_params_t* params,
    size_t* max_size
);

/**
 * Frees a buffer allocated by the JPEG-LS wrapper.
 *
 * Currently unused as all functions use caller-provided buffers,
 * but provided for future streaming API compatibility.
 *
 * @param buffer        Pointer to buffer to free (may be NULL)
 */
SHARPDICOM_API void jls_free(void* buffer);

#ifdef __cplusplus
}
#endif

#endif /* JLS_WRAPPER_H */
