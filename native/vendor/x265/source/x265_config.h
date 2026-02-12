/*
 * x265_config.h - x265 build configuration for SharpDicom
 *
 * This header configures x265 for cross-platform builds via Zig.
 * Generated to replace the normal CMake-generated config.
 */

#ifndef X265_CONFIG_H
#define X265_CONFIG_H

/* x265 version */
#define X265_VERSION "3.6"
#define X265_BUILD 210

/* Bit depth (8-bit only for DICOM compatibility) */
#define X265_DEPTH 8
#define HIGH_BIT_DEPTH 0

/* Namespace for linking */
#define X265_NS x265

/* Export C API for FFmpeg integration */
#define EXPORT_C_API 1

/* GPL license */
#define X265_GPL 1

/* Architecture detection */
#if defined(__x86_64__) || defined(_M_X64)
  #define X265_ARCH_X86 1
  #define X86_64 1
  #define X265_ARCH_ARM 0
#elif defined(__aarch64__) || defined(_M_ARM64)
  #define X265_ARCH_X86 0
  #define X86_64 0
  #define X265_ARCH_ARM 1
#else
  #define X265_ARCH_X86 0
  #define X86_64 0
  #define X265_ARCH_ARM 0
#endif

/* SIMD - disabled for portable cross-compiled builds */
#define HAVE_MMX 0
#define HAVE_SSE2 0
#define HAVE_SSE3 0
#define HAVE_SSSE3 0
#define HAVE_SSE4 0
#define HAVE_AVX 0
#define HAVE_AVX2 0
#define HAVE_AVX512 0
#define HAVE_NEON 0
#define ENABLE_ASSEMBLY 0

/* Platform detection */
#if defined(_WIN32) || defined(_WIN64)
  #define _WIN32_WINNT 0x0601
  #define WINVER 0x0601
  #define HAVE_STRTOK_R 0
  /* Use custom strtok_r implementation on Windows */
#else
  #define HAVE_STRTOK_R 1
#endif

#if defined(__APPLE__)
  #define MACOS 1
#endif

#if defined(__linux__)
  #define LINUX 1
  #define HAVE_LIBNUMA 0  /* Disabled - not needed for encoding */
#endif

/* Threading */
#define HAVE_THREAD 1
#define ENABLE_PIC 1

#if defined(_WIN32)
  #define HAVE_WIN32_THREADS 1
#else
  #define HAVE_POSIX_THREADS 1
#endif

/* Compiler features */
#if defined(__GNUC__) || defined(__clang__)
  #define HAVE_ALIGNED_STACK 1
  #define HAVE_LOG2 1
  /* HAVE_STRTOK_R already defined above based on platform */
  #if !defined(_WIN32) && !defined(_WIN64)
    #define HAVE_CLOCK_GETTIME 1
  #else
    #define HAVE_CLOCK_GETTIME 0
  #endif

  #define ALIGN_VAR_8(T, var) T var __attribute__((aligned(8)))
  #define ALIGN_VAR_16(T, var) T var __attribute__((aligned(16)))
  #define ALIGN_VAR_32(T, var) T var __attribute__((aligned(32)))
  #define ALIGN_VAR_64(T, var) T var __attribute__((aligned(64)))

  #define CDECL
  #define fseeko fseeko
  #define ftello ftello

  #define ALWAYS_INLINE __attribute__((always_inline)) inline
  #define NOINLINE __attribute__((noinline))

#elif defined(_MSC_VER)
  #define HAVE_ALIGNED_STACK 0
  #define HAVE_LOG2 1
  /* HAVE_STRTOK_R already defined above as 0 for Windows */
  #define HAVE_CLOCK_GETTIME 0

  #define ALIGN_VAR_8(T, var) __declspec(align(8)) T var
  #define ALIGN_VAR_16(T, var) __declspec(align(16)) T var
  #define ALIGN_VAR_32(T, var) __declspec(align(32)) T var
  #define ALIGN_VAR_64(T, var) __declspec(align(64)) T var

  #define CDECL __cdecl
  #define fseeko _fseeki64
  #define ftello _ftelli64

  #define ALWAYS_INLINE __forceinline
  #define NOINLINE __declspec(noinline)
#endif

/* Standard headers */
#define HAVE_STDINT_H 1
#define HAVE_INTTYPES_H 1
#define HAVE_STDBOOL_H 1

/* Include time.h for timespec (needed by C++ threading headers) */
#include <time.h>

/* Memory functions */
#if defined(_WIN32)
  #define HAVE_ALIGNED_MALLOC 1
#else
  #define HAVE_POSIX_MEMALIGN 1
#endif

/* Integer sizes - must be preprocessor constants (not sizeof()) */
#ifndef SIZEOF_INT
  #define SIZEOF_INT 4
#endif
#ifndef SIZEOF_LONG
  #if defined(__x86_64__) || defined(_M_X64) || defined(__aarch64__) || defined(_M_ARM64)
    #if defined(_WIN32) || defined(_WIN64)
      #define SIZEOF_LONG 4  /* Windows LLP64: long is 32-bit */
    #else
      #define SIZEOF_LONG 8  /* Unix LP64: long is 64-bit */
    #endif
  #else
    #define SIZEOF_LONG 4
  #endif
#endif

/* Visual Studio specific */
#if defined(_MSC_VER)
  #pragma warning(disable: 4244)  /* conversion from int to int16_t */
  #pragma warning(disable: 4267)  /* conversion from size_t to int */
  #pragma warning(disable: 4996)  /* deprecated POSIX names */
#endif

/* Endianness */
#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
  #define WORDS_BIGENDIAN 1
  #define X265_BIG_ENDIAN 1
#else
  #define WORDS_BIGENDIAN 0
  #define X265_BIG_ENDIAN 0
#endif

/* Logging defaults */
#define X265_LOG_NONE (-1)
#define X265_LOG_ERROR 0
#define X265_LOG_WARNING 1
#define X265_LOG_INFO 2
#define X265_LOG_DEBUG 3
#define X265_LOG_FULL 4

/* CLI support - disabled (library only) */
#define ENABLE_CLI 0

/* Analysis features (enabled for better compression) */
#define ENABLE_HDR10_PLUS 0
#define ENABLE_SVT_HEVC 0
#define ENABLE_VTUNE 0

/* Rate control - X265_RC_METHODS is a typedef in x265.h, not a macro */
/* Do not define X265_RC_METHODS here - it will conflict with the enum typedef */

/* SEI (Supplemental Enhancement Information) */
#define ENABLE_ALPHA 0

/* Internal frame buffer limits */
#define X265_MAX_FRAME_THREADS 16

/* Disable some optional features for smaller binary */
#define ENABLE_SHARED 0
#define STATIC_LINK_CRT 1

/* Profile/level detection */
#define X265_MAIN 1
#define X265_MAIN10 0
#define X265_MAIN12 0

#endif /* X265_CONFIG_H */
