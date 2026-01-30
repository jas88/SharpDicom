/**
 * SharpDicom Native Codecs - Core Implementation
 *
 * Provides version detection, feature detection, SIMD capability detection,
 * and thread-local error message handling.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "sharpdicom_codecs.h"

#include <string.h>
#include <stdio.h>

/*============================================================================
 * Platform detection
 *============================================================================*/

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
    #define SHARPDICOM_ARCH_X86 1
#elif defined(__aarch64__) || defined(_M_ARM64)
    #define SHARPDICOM_ARCH_ARM64 1
#endif

/*============================================================================
 * Thread-local error message storage
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
    #define THREAD_LOCAL __declspec(thread)
#else
    #define THREAD_LOCAL __thread
#endif

/** Thread-local error message buffer (256 bytes as per spec) */
static THREAD_LOCAL char tls_error_message[256] = {0};

/**
 * Internal: Set the thread-local error message.
 * Used by codec wrappers to report errors.
 *
 * @param message Error message to store
 */
__attribute__((unused))
static void set_error(const char* message) {
    if (message != NULL) {
        size_t len = strlen(message);
        if (len >= sizeof(tls_error_message)) {
            len = sizeof(tls_error_message) - 1;
        }
        memcpy(tls_error_message, message, len);
        tls_error_message[len] = '\0';
    } else {
        tls_error_message[0] = '\0';
    }
}

/* set_error_fmt will be added when codec wrappers need formatted errors */

/*============================================================================
 * SIMD detection
 *============================================================================*/

#if SHARPDICOM_ARCH_X86

#if defined(_MSC_VER)
    #include <intrin.h>
    static void cpuid(int info[4], int level) {
        __cpuid(info, level);
    }
    static void cpuidex(int info[4], int level, int count) {
        __cpuidex(info, level, count);
    }
#elif defined(__GNUC__) || defined(__clang__)
    #include <cpuid.h>
    static void cpuid(int info[4], int level) {
        __cpuid(level, info[0], info[1], info[2], info[3]);
    }
    static void cpuidex(int info[4], int level, int count) {
        __cpuid_count(level, count, info[0], info[1], info[2], info[3]);
    }
#endif

/**
 * Detect x86_64 SIMD features using CPUID.
 */
static int detect_x86_simd(void) {
    int features = SHARPDICOM_SIMD_NONE;
    int info[4] = {0};

    /* Check CPUID support and get max function level */
    cpuid(info, 0);
    int max_level = info[0];

    if (max_level >= 1) {
        cpuid(info, 1);
        int ecx = info[2];
        int edx = info[3];

        /* EDX flags */
        if (edx & (1 << 26)) features |= SHARPDICOM_SIMD_SSE2;

        /* ECX flags */
        if (ecx & (1 << 19)) features |= SHARPDICOM_SIMD_SSE4_1;
        if (ecx & (1 << 20)) features |= SHARPDICOM_SIMD_SSE4_2;
        if (ecx & (1 << 28)) features |= SHARPDICOM_SIMD_AVX;
    }

    if (max_level >= 7) {
        cpuidex(info, 7, 0);
        int ebx = info[1];

        /* EBX flags */
        if (ebx & (1 << 5)) features |= SHARPDICOM_SIMD_AVX2;
        if (ebx & (1 << 16)) features |= SHARPDICOM_SIMD_AVX512F;
    }

    return features;
}

#elif SHARPDICOM_ARCH_ARM64

/**
 * Detect ARM64 SIMD features.
 * NEON is always available on AArch64.
 */
static int detect_arm64_simd(void) {
    /* NEON is mandatory on ARM64 */
    return SHARPDICOM_SIMD_NEON;
}

#endif

/**
 * Cached SIMD features (detected once).
 */
static int cached_simd_features = -1;

static int get_simd_features(void) {
    if (cached_simd_features < 0) {
#if SHARPDICOM_ARCH_X86
        cached_simd_features = detect_x86_simd();
#elif SHARPDICOM_ARCH_ARM64
        cached_simd_features = detect_arm64_simd();
#else
        cached_simd_features = SHARPDICOM_SIMD_NONE;
#endif
    }
    return cached_simd_features;
}

/*============================================================================
 * Public API implementation
 *============================================================================*/

SHARPDICOM_API int sharpdicom_version(void) {
    return SHARPDICOM_NATIVE_VERSION;
}

SHARPDICOM_API int sharpdicom_features(void) {
    /* Currently no codecs are linked - returns 0
     * Future plans will add:
     * - SHARPDICOM_HAS_JPEG when libjpeg-turbo is linked
     * - SHARPDICOM_HAS_J2K when OpenJPEG is linked
     * - SHARPDICOM_HAS_JLS when CharLS is linked
     * - etc.
     */
    return 0;
}

SHARPDICOM_API int sharpdicom_simd_features(void) {
    return get_simd_features();
}

SHARPDICOM_API const char* sharpdicom_last_error(void) {
    return tls_error_message;
}

SHARPDICOM_API void sharpdicom_clear_error(void) {
    tls_error_message[0] = '\0';
}

/*============================================================================
 * Placeholder codec functions (to be implemented in future plans)
 *============================================================================*/

/* These will be added in plans 13-02 through 13-05:
 *
 * int jpeg_decode(const uint8_t* input, size_t input_len,
 *                 uint8_t* output, size_t output_len,
 *                 int* width, int* height, int* components, int colorspace);
 *
 * int jpeg_encode(const uint8_t* input, size_t input_len,
 *                 uint8_t* output, size_t output_len,
 *                 int width, int height, int components, int quality);
 *
 * int j2k_decode(const uint8_t* input, size_t input_len,
 *                uint8_t* output, size_t output_len,
 *                int* width, int* height, int* components);
 *
 * int j2k_encode(const uint8_t* input, size_t input_len,
 *                uint8_t* output, size_t output_len,
 *                int width, int height, int components, int lossless, float quality);
 *
 * int jls_decode(const uint8_t* input, size_t input_len,
 *                uint8_t* output, size_t output_len,
 *                int* width, int* height, int* components, int* bits_per_sample);
 *
 * int jls_encode(const uint8_t* input, size_t input_len,
 *                uint8_t* output, size_t output_len,
 *                int width, int height, int components, int bits_per_sample, int near_lossless);
 */
