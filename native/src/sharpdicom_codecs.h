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
 * Safe arithmetic helpers (overflow protection)
 *============================================================================*/

/**
 * Safely multiply two size_t values, returning 0 on overflow.
 * Uses compiler intrinsics when available for optimal code generation.
 */
static inline size_t safe_mul_size(size_t a, size_t b) {
#if defined(__GNUC__) || defined(__clang__)
    size_t result;
    if (__builtin_mul_overflow(a, b, &result)) {
        return 0; /* Overflow occurred */
    }
    return result;
#elif defined(_MSC_VER) && defined(_WIN64)
    /* MSVC 64-bit: use intrinsic */
    unsigned __int64 high;
    unsigned __int64 low = _umul128(a, b, &high);
    if (high != 0) return 0; /* Overflow */
    return (size_t)low;
#else
    /* Fallback: check before multiply */
    if (a != 0 && b > SIZE_MAX / a) {
        return 0; /* Would overflow */
    }
    return a * b;
#endif
}

/**
 * Safely multiply three size_t values, returning 0 on overflow.
 */
static inline size_t safe_mul3_size(size_t a, size_t b, size_t c) {
    size_t ab = safe_mul_size(a, b);
    if (ab == 0 && (a != 0 && b != 0)) return 0;
    return safe_mul_size(ab, c);
}

/**
 * Safely multiply four size_t values, returning 0 on overflow.
 */
static inline size_t safe_mul4_size(size_t a, size_t b, size_t c, size_t d) {
    size_t abc = safe_mul3_size(a, b, c);
    if (abc == 0 && (a != 0 && b != 0 && c != 0)) return 0;
    return safe_mul_size(abc, d);
}

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

/*============================================================================
 * GPU acceleration functions
 *============================================================================*/

/** GPU type constants */
#define SHARPDICOM_GPU_NONE     0
#define SHARPDICOM_GPU_NVIDIA   1
#define SHARPDICOM_GPU_OPENCL   2

/**
 * Check if GPU acceleration is available.
 *
 * @return 1 if GPU is available, 0 if not
 */
SHARPDICOM_API int sharpdicom_gpu_available(void);

/**
 * Get the type of GPU acceleration available.
 *
 * @return SHARPDICOM_GPU_NONE, SHARPDICOM_GPU_NVIDIA, or SHARPDICOM_GPU_OPENCL
 */
SHARPDICOM_API int sharpdicom_gpu_type(void);

/**
 * Decode JPEG 2000 using GPU if available, falling back to CPU.
 *
 * @param input       Input compressed data
 * @param input_len   Length of input data in bytes
 * @param output      Output buffer for decoded pixels
 * @param output_len  Size of output buffer in bytes
 * @param width       Output: image width
 * @param height      Output: image height
 * @param components  Output: number of components
 * @return SHARPDICOM_OK on success, error code on failure
 */
SHARPDICOM_API int sharpdicom_gpu_j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int* width,
    int* height,
    int* components
);

#ifdef __cplusplus
}
#endif

#endif /* SHARPDICOM_CODECS_H */
