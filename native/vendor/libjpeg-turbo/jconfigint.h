/*
 * jconfigint.h - libjpeg-turbo internal configuration header
 *
 * Generated for SharpDicom cross-platform builds.
 * This file provides internal build configuration for libjpeg-turbo.
 */

#ifndef JCONFIGINT_H
#define JCONFIGINT_H

/* Package information */
#define PACKAGE_NAME "libjpeg-turbo"
#define VERSION "3.0.4"

/* Build ID for version reporting */
#define BUILD "SharpDicom-native"

/* Compiler visibility attributes */
#if defined(__GNUC__) || defined(__clang__)
  #define HIDDEN __attribute__((visibility("hidden")))
#else
  #define HIDDEN
#endif

/* Inline keyword */
#if defined(_MSC_VER) && !defined(__clang__)
  #define INLINE __inline
  #define FORCE_INLINE __forceinline
#elif defined(__GNUC__) || defined(__clang__)
  #define INLINE __inline__
  #define FORCE_INLINE __inline__ __attribute__((always_inline))
#else
  #define INLINE inline
  #define FORCE_INLINE inline
#endif

/* Thread-local storage */
#if defined(_MSC_VER) && !defined(__clang__)
  #define THREAD_LOCAL __declspec(thread)
#elif defined(__GNUC__) || defined(__clang__)
  #define THREAD_LOCAL __thread
#else
  #define THREAD_LOCAL
#endif

/* Size of various types - must be a preprocessor constant, not sizeof() expression */
#if defined(__x86_64__) || defined(_M_X64) || defined(__aarch64__) || defined(_M_ARM64)
  /* 64-bit platforms */
  #define SIZEOF_SIZE_T 8
#else
  /* 32-bit platforms */
  #define SIZEOF_SIZE_T 4
#endif

/* Platform-specific memory alignment for SIMD */
#if defined(__x86_64__) || defined(_M_X64)
  /* x86-64: 64-byte alignment for AVX-512, 32-byte for AVX2 */
  #define MAX_SIMD_ALIGN 64
#elif defined(__aarch64__) || defined(_M_ARM64)
  /* ARM64: 16-byte alignment for NEON */
  #define MAX_SIMD_ALIGN 16
#else
  /* Default alignment */
  #define MAX_SIMD_ALIGN 16
#endif

/* Memory allocation alignment */
#define MEMALIGN(A, T) (((T) + ((A) - 1)) & ~((A) - 1))

/* Fallthrough annotation for switch statements - MUST include semicolon */
#if defined(__clang__)
  #if __has_attribute(fallthrough)
    #define FALLTHROUGH __attribute__((fallthrough));
  #else
    #define FALLTHROUGH
  #endif
#elif defined(__GNUC__) && __GNUC__ >= 7
  #define FALLTHROUGH __attribute__((fallthrough));
#else
  #define FALLTHROUGH
#endif

/* Likely/unlikely branch prediction hints */
#if defined(__GNUC__) || defined(__clang__)
  #define LIKELY(x) __builtin_expect(!!(x), 1)
  #define UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
  #define LIKELY(x) (x)
  #define UNLIKELY(x) (x)
#endif

/* SIMD detection - disabled for Zig cross-compilation.
 * SIMD is normally auto-detected by libjpeg-turbo's build system,
 * but for portable cross-compiled builds, we use C fallbacks.
 * This still provides good performance for typical DICOM image sizes. */
#undef WITH_SIMD

/* Memory source/destination support */
#define HAVE_MEM_SRCDST 1

/* Boolean type for older libjpeg compatibility */
#ifndef FALSE
  #define FALSE 0
#endif
#ifndef TRUE
  #define TRUE 1
#endif

/* NULL definition */
#ifndef NULL
  #ifdef __cplusplus
    #define NULL 0
  #else
    #define NULL ((void *)0)
  #endif
#endif

#endif /* JCONFIGINT_H */
