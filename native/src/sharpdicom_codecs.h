/**
 * SharpDicom Native Codecs API
 *
 * Cross-platform native library providing high-performance codec implementations
 * for DICOM image compression/decompression.
 *
 * Thread Safety: All functions are thread-safe unless otherwise noted.
 * Error messages are stored in thread-local storage.
 */

#ifndef SHARPDICOM_CODECS_H
#define SHARPDICOM_CODECS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Platform-specific export macros
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
    #ifdef SHARPDICOM_CODECS_EXPORTS
        #define SHARPDICOM_API __declspec(dllexport)
    #else
        #define SHARPDICOM_API __declspec(dllimport)
    #endif
#else
    #define SHARPDICOM_API __attribute__((visibility("default")))
#endif

/*============================================================================
 * Version constants
 *============================================================================*/

/** Native library version (incremented on ABI-breaking changes) */
#define SHARPDICOM_NATIVE_VERSION 1

/*============================================================================
 * Feature bitmap constants
 *============================================================================*/

/** Feature flags indicating available codec support */
#define SHARPDICOM_HAS_JPEG         (1 << 0)  /* libjpeg-turbo: JPEG baseline/extended/lossless */
#define SHARPDICOM_HAS_J2K          (1 << 1)  /* OpenJPEG: JPEG 2000 lossless/lossy */
#define SHARPDICOM_HAS_JLS          (1 << 2)  /* CharLS: JPEG-LS lossless/near-lossless */
#define SHARPDICOM_HAS_RLE          (1 << 3)  /* Native RLE codec */
#define SHARPDICOM_HAS_VIDEO        (1 << 4)  /* FFmpeg: MPEG2/MPEG4/HEVC */
#define SHARPDICOM_HAS_DEFLATE      (1 << 5)  /* zlib-ng: Deflate compression */
#define SHARPDICOM_HAS_GPU          (1 << 6)  /* GPU acceleration available */
#define SHARPDICOM_HAS_HTJ2K        (1 << 7)  /* High-Throughput JPEG 2000 */

/*============================================================================
 * SIMD feature bitmap constants
 *============================================================================*/

/** SIMD instruction set flags detected at runtime */
#define SHARPDICOM_SIMD_NONE        0
#define SHARPDICOM_SIMD_SSE2        (1 << 0)  /* x86_64: SSE2 */
#define SHARPDICOM_SIMD_SSE4_1      (1 << 1)  /* x86_64: SSE4.1 */
#define SHARPDICOM_SIMD_SSE4_2      (1 << 2)  /* x86_64: SSE4.2 */
#define SHARPDICOM_SIMD_AVX         (1 << 3)  /* x86_64: AVX */
#define SHARPDICOM_SIMD_AVX2        (1 << 4)  /* x86_64: AVX2 */
#define SHARPDICOM_SIMD_AVX512F     (1 << 5)  /* x86_64: AVX-512 Foundation */
#define SHARPDICOM_SIMD_NEON        (1 << 6)  /* aarch64: NEON (always available on ARM64) */

/*============================================================================
 * Error codes
 *============================================================================*/

/** Success */
#define SHARPDICOM_OK                      0

/** Error codes (negative values) */
#define SHARPDICOM_ERR_INVALID_ARGUMENT   -1  /* Invalid parameter passed */
#define SHARPDICOM_ERR_OUT_OF_MEMORY      -2  /* Memory allocation failed */
#define SHARPDICOM_ERR_DECODE_FAILED      -3  /* Decoding operation failed */
#define SHARPDICOM_ERR_ENCODE_FAILED      -4  /* Encoding operation failed */
#define SHARPDICOM_ERR_UNSUPPORTED        -5  /* Feature not supported */
#define SHARPDICOM_ERR_CORRUPT_DATA       -6  /* Input data is corrupted */
#define SHARPDICOM_ERR_TIMEOUT            -7  /* Operation timed out */
#define SHARPDICOM_ERR_INTERNAL           -8  /* Internal library error */

/*============================================================================
 * Core API functions
 *============================================================================*/

/**
 * Returns the native library version number.
 * Used for version validation at load time.
 *
 * @return Version number (SHARPDICOM_NATIVE_VERSION)
 */
SHARPDICOM_API int sharpdicom_version(void);

/**
 * Returns a bitmap of available codec features.
 * Each bit corresponds to a SHARPDICOM_HAS_* constant.
 *
 * @return Feature bitmap
 */
SHARPDICOM_API int sharpdicom_features(void);

/**
 * Returns a bitmap of available SIMD instruction sets.
 * Detected at runtime based on CPU capabilities.
 * Each bit corresponds to a SHARPDICOM_SIMD_* constant.
 *
 * @return SIMD feature bitmap
 */
SHARPDICOM_API int sharpdicom_simd_features(void);

/**
 * Returns the last error message for the current thread.
 * The returned string is valid until the next function call on this thread.
 *
 * @return Error message string, or empty string if no error
 */
SHARPDICOM_API const char* sharpdicom_last_error(void);

/**
 * Clears the last error message for the current thread.
 */
SHARPDICOM_API void sharpdicom_clear_error(void);

#ifdef __cplusplus
}
#endif

#endif /* SHARPDICOM_CODECS_H */
