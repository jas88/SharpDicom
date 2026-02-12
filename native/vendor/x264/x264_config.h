/*
 * x264_config.h - x264 build configuration for SharpDicom
 *
 * This header configures x264 for cross-platform builds via Zig.
 * Generated to replace the normal configure script output.
 */

#ifndef X264_CONFIG_H
#define X264_CONFIG_H

/* x264 version */
#define X264_VERSION " r3200 stable"
#define X264_POINTVER "0.164.3200 stable"

/* Bit depth configuration (8-bit only for DICOM compatibility) */
#define X264_BIT_DEPTH 8
#define BIT_DEPTH 8

/* Chroma format (0 = all formats supported) */
#define X264_CHROMA_FORMAT 0
#define CHROMA_FORMAT 0

/* GPL license compliance */
#define X264_GPL 1

/* Threading support */
#define HAVE_THREAD 1
#define HAVE_POSIX_THREADS 1

/* Platform detection */
#if defined(_WIN32) || defined(_WIN64)
  #define SYS_WINDOWS 1
  #define SYS_LINUX 0
  #define SYS_MACOSX 0
  #define HAVE_WIN32THREAD 1
#elif defined(__APPLE__)
  #define SYS_WINDOWS 0
  #define SYS_LINUX 0
  #define SYS_MACOSX 1
#elif defined(__linux__)
  #define SYS_WINDOWS 0
  #define SYS_LINUX 1
  #define SYS_MACOSX 0
#else
  #define SYS_WINDOWS 0
  #define SYS_LINUX 0
  #define SYS_MACOSX 0
#endif

/* Architecture detection */
#if defined(__x86_64__) || defined(_M_X64)
  #define ARCH_X86 1
  #define ARCH_X86_64 1
  #define ARCH_AARCH64 0
  #define HAVE_VECTOREXT 0  /* Disabled for cross-compilation */
#elif defined(__aarch64__) || defined(_M_ARM64)
  #define ARCH_X86 0
  #define ARCH_X86_64 0
  #define ARCH_AARCH64 1
  #define HAVE_VECTOREXT 0  /* Disabled for cross-compilation */
#else
  #define ARCH_X86 0
  #define ARCH_X86_64 0
  #define ARCH_AARCH64 0
  #define HAVE_VECTOREXT 0
#endif

/* SIMD - disabled for portable cross-compiled builds */
#define HAVE_MMX 0
#define HAVE_SSE 0
#define HAVE_SSE2 0
#define HAVE_SSE3 0
#define HAVE_SSSE3 0
#define HAVE_SSE4 0
#define HAVE_SSE42 0
#define HAVE_AVX 0
#define HAVE_AVX2 0
#define HAVE_AVX512 0
#define HAVE_NEON 0
#define HAVE_ARMV6 0
#define HAVE_ARMV6T2 0

/* Assembly - disabled for cross-compilation */
#define HAVE_X86_INLINE_ASM 0
#define HAVE_ARM_INLINE_ASM 0

/* Compiler features */
#if defined(__GNUC__) || defined(__clang__)
  #define HAVE_ALIGNED_STACK 1
  #define HAVE_ATTRIBUTE_VISIBILITY 1
  #define HAVE_ATTRIBUTE_PACKED 1
  #define HAVE_ATTRIBUTE_MAY_ALIAS 1
  #define HAVE_MALLOC_H 1
  #define fseek fseeko
  #define ftell ftello
#endif

/* Standard library features */
#define HAVE_STDINT_H 1
#define HAVE_INTTYPES_H 1
#define HAVE_STDBOOL_H 1
#define HAVE_LOG2F 1
#define HAVE_LRINT 1

/* String functions */
#define HAVE_STRTOK_R 1
#define strtok_r strtok_r

/* Memory functions */
#define HAVE_MEMALIGN 1

#if defined(_WIN32)
  #define HAVE_ALIGNED_ALLOC 0
  #define HAVE__ALIGNED_MALLOC 1
#else
  #define HAVE_ALIGNED_ALLOC 1
  #define HAVE__ALIGNED_MALLOC 0
#endif

#define HAVE_POSIX_MEMALIGN 1

/* I/O features */
#define HAVE_GETOPT_LONG 1

/* Clock functions */
#define HAVE_CLOCK_GETTIME 1

/* External libraries - disabled (we compile x264 standalone) */
#define HAVE_GPL 1
#define HAVE_SWSCALE 0
#define HAVE_LAVF 0
#define HAVE_FFMS 0
#define HAVE_GPAC 0
#define HAVE_LSMASH 0
#define HAVE_AVS 0

/* CLI - disabled (library only) */
#define HAVE_CLI 0

/* Inline keyword */
#if defined(_MSC_VER) && !defined(__clang__)
  #define ALWAYS_INLINE __forceinline
  #define NOINLINE __declspec(noinline)
#elif defined(__GNUC__) || defined(__clang__)
  #define ALWAYS_INLINE __attribute__((always_inline)) inline
  #define NOINLINE __attribute__((noinline))
#else
  #define ALWAYS_INLINE inline
  #define NOINLINE
#endif

/* API visibility - x264.h defines X264_API based on X264_API_EXPORTS.
 * We just set the exports flag here; x264.h will define X264_API. */
#define X264_API_EXPORTS 1

/* Unused parameter suppression */
#define UNUSED __attribute__((unused))

/* Static assert (C11) */
#if defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
  #define STATIC_ASSERT(cond, msg) _Static_assert(cond, msg)
#else
  #define STATIC_ASSERT(cond, msg)
#endif

/* Note: x264_prefetch and DECLARE_ALIGNED are defined in osdep.h */
/* Do not define them here to avoid redefinition errors */

/* Disable interlaced encoding (not used in DICOM) */
#define HAVE_INTERLACED 0

/* Maximum reference frames */
#define X264_REF_MAX 16

/* Log level */
#define X264_LOG_NONE (-1)
#define X264_LOG_ERROR 0
#define X264_LOG_WARNING 1
#define X264_LOG_INFO 2
#define X264_LOG_DEBUG 3

#endif /* X264_CONFIG_H */
