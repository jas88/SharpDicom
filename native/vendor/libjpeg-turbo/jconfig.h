/*
 * jconfig.h - libjpeg-turbo configuration header
 *
 * Generated for SharpDicom cross-platform builds.
 * This file configures libjpeg-turbo for 8-bit and 12-bit JPEG support.
 *
 * IMPORTANT: This header must be included BEFORE jpeglib.h/jmorecfg.h.
 * It sets up configuration that jmorecfg.h will use via #ifndef guards.
 */

#ifndef JCONFIG_H
#define JCONFIG_H

/* Version information */
#define LIBJPEG_TURBO_VERSION "3.0.4"
#define LIBJPEG_TURBO_VERSION_NUMBER 3000004

/* JPEG library version (compatibility with libjpeg 6b API) */
#define JPEG_LIB_VERSION 62

/* Standard type definitions - tells libjpeg we have standard headers */
#define HAVE_STDDEF_H 1
#define HAVE_STDLIB_H 1
#define HAVE_UNSIGNED_CHAR 1
#define HAVE_UNSIGNED_SHORT 1

/* Disable POSIX features that aren't available in cross-compilation.
 * This prevents jinclude.h from using setenv() and other POSIX functions. */
#undef HAVE_LOCALE_H
#undef HAVE_SETENV

/* Memory manager selection */
#define JMEM_NOBS 1  /* Use no backing store (all in memory) */

/* Data precision for 8-bit or 12-bit builds.
 * This is set via compiler flags:
 *   -DWITH_12BIT=1 for 12-bit builds
 *   Default (no flag) for 8-bit builds
 */
#ifndef WITH_12BIT
#define BITS_IN_JSAMPLE 8
#else
#define BITS_IN_JSAMPLE 12
#endif

/* CMYK support (arithmetic coding) */
#define C_ARITH_CODING_SUPPORTED 1
#define D_ARITH_CODING_SUPPORTED 1

/* ICC profile support */
#define MEM_SRCDST_SUPPORTED 1

/* Platform-specific configuration */
#if defined(_WIN32) || defined(_WIN64)
  /* Windows configuration */
  #ifdef _MSC_VER
    #define INLINE __inline
  #else
    #define INLINE __inline__
  #endif
#elif defined(__APPLE__)
  /* macOS configuration */
  #define INLINE __inline__
#else
  /* Linux/Unix configuration */
  #define INLINE __inline__
#endif

/* Suppress unused parameter warnings */
#define UNUSED_PARAM(x) ((void)(x))

#endif /* JCONFIG_H */
