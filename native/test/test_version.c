/**
 * SharpDicom Native Codecs - Test Executable
 *
 * Tests the native library basic functions:
 * - Version detection
 * - Feature detection
 * - SIMD capability detection
 * - Error message handling
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "../src/sharpdicom_codecs.h"

/* Test result counters */
static int tests_passed = 0;
static int tests_failed = 0;

#define TEST(condition, message) do { \
    if (condition) { \
        printf("[PASS] %s\n", message); \
        tests_passed++; \
    } else { \
        printf("[FAIL] %s\n", message); \
        tests_failed++; \
    } \
} while (0)

/**
 * Print a description of SIMD features.
 */
static void print_simd_features(int features) {
    printf("  SIMD features detected: ");
    if (features == SHARPDICOM_SIMD_NONE) {
        printf("None");
    } else {
        int first = 1;
        if (features & SHARPDICOM_SIMD_SSE2) {
            printf("%sSSE2", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_SSE4_1) {
            printf("%sSSE4.1", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_SSE4_2) {
            printf("%sSSE4.2", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_AVX) {
            printf("%sAVX", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_AVX2) {
            printf("%sAVX2", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_AVX512F) {
            printf("%sAVX-512F", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_SIMD_NEON) {
            printf("%sNEON", first ? "" : ", ");
            first = 0;
        }
    }
    printf("\n");
}

/**
 * Print a description of codec features.
 */
static void print_codec_features(int features) {
    printf("  Codec features available: ");
    if (features == 0) {
        printf("None (base infrastructure only)");
    } else {
        int first = 1;
        if (features & SHARPDICOM_HAS_JPEG) {
            printf("%sJPEG", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_J2K) {
            printf("%sJPEG2000", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_JLS) {
            printf("%sJPEG-LS", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_RLE) {
            printf("%sRLE", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_VIDEO) {
            printf("%sVideo", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_DEFLATE) {
            printf("%sDeflate", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_GPU) {
            printf("%sGPU", first ? "" : ", ");
            first = 0;
        }
        if (features & SHARPDICOM_HAS_HTJ2K) {
            printf("%sHTJ2K", first ? "" : ", ");
            first = 0;
        }
    }
    printf("\n");
}

int main(void) {
    printf("=== SharpDicom Native Codecs Test ===\n\n");

    /* Test 1: Version */
    printf("Test 1: Version\n");
    int version = sharpdicom_version();
    printf("  Native library version: %d\n", version);
    TEST(version == SHARPDICOM_NATIVE_VERSION, "Version matches expected");
    printf("\n");

    /* Test 2: Features */
    printf("Test 2: Codec Features\n");
    int features = sharpdicom_features();
    print_codec_features(features);
    TEST(features >= 0, "Features returns non-negative value");
    printf("\n");

    /* Test 3: SIMD Features */
    printf("Test 3: SIMD Features\n");
    int simd = sharpdicom_simd_features();
    print_simd_features(simd);

    /* Platform-specific SIMD validation */
#if defined(__x86_64__) || defined(_M_X64)
    /* All x86_64 CPUs support SSE2 */
    TEST(simd & SHARPDICOM_SIMD_SSE2, "x86_64: SSE2 detected (expected on all x86_64)");
#elif defined(__aarch64__) || defined(_M_ARM64)
    /* All ARM64 CPUs support NEON */
    TEST(simd & SHARPDICOM_SIMD_NEON, "ARM64: NEON detected (expected on all ARM64)");
#else
    TEST(1, "Unknown architecture - SIMD check skipped");
#endif
    printf("\n");

    /* Test 4: Error message handling */
    printf("Test 4: Error Message Handling\n");

    /* Initially should be empty */
    const char* error = sharpdicom_last_error();
    TEST(error != NULL, "last_error returns non-NULL pointer");
    TEST(strlen(error) == 0, "last_error initially returns empty string");

    /* Clear error should work */
    sharpdicom_clear_error();
    error = sharpdicom_last_error();
    TEST(strlen(error) == 0, "clear_error clears the message");
    printf("\n");

    /* Summary */
    printf("=== Test Summary ===\n");
    printf("Passed: %d\n", tests_passed);
    printf("Failed: %d\n", tests_failed);
    printf("\n");

    return (tests_failed > 0) ? 1 : 0;
}
