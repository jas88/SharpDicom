/*
 * jconfig.h - libjpeg-turbo configuration header
 *
 * Generated for SharpDicom cross-platform builds.
 * This file configures libjpeg-turbo for 8-bit and 12-bit JPEG support.
 */

#ifndef JCONFIG_H
#define JCONFIG_H

/* Version information */
#define LIBJPEG_TURBO_VERSION "3.0.4"
#define LIBJPEG_TURBO_VERSION_NUMBER 3000004

/* JPEG library version (compatibility with libjpeg 6b API) */
#define JPEG_LIB_VERSION 62

/* Standard type definitions */
#define HAVE_STDDEF_H 1
#define HAVE_STDLIB_H 1
#define HAVE_UNSIGNED_CHAR 1
#define HAVE_UNSIGNED_SHORT 1

/* Memory manager selection */
#define JMEM_NOBS 1  /* Use no backing store (all in memory) */

/* Maximum image dimensions (DICOM images can be large) */
#define JPEG_MAX_DIMENSION 65535L

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

/* RGB pixel ordering (standard RGB, not BGR) */
#define RGB_RED 0
#define RGB_GREEN 1
#define RGB_BLUE 2
#define RGB_PIXELSIZE 3

/* Component order for interleaved sampling */
#define EXT_RGB_RED 0
#define EXT_RGB_GREEN 1
#define EXT_RGB_BLUE 2
#define EXT_RGB_PIXELSIZE 3

#define EXT_RGBX_RED 0
#define EXT_RGBX_GREEN 1
#define EXT_RGBX_BLUE 2
#define EXT_RGBX_PIXELSIZE 4

#define EXT_BGR_RED 2
#define EXT_BGR_GREEN 1
#define EXT_BGR_BLUE 0
#define EXT_BGR_PIXELSIZE 3

#define EXT_BGRX_RED 2
#define EXT_BGRX_GREEN 1
#define EXT_BGRX_BLUE 0
#define EXT_BGRX_PIXELSIZE 4

#define EXT_XBGR_RED 3
#define EXT_XBGR_GREEN 2
#define EXT_XBGR_BLUE 1
#define EXT_XBGR_PIXELSIZE 4

#define EXT_XRGB_RED 1
#define EXT_XRGB_GREEN 2
#define EXT_XRGB_BLUE 3
#define EXT_XRGB_PIXELSIZE 4

/* CMYK support */
#define C_ARITH_CODING_SUPPORTED 1
#define D_ARITH_CODING_SUPPORTED 1

/* Progressive JPEG support (used in DICOM) */
#define C_PROGRESSIVE_SUPPORTED 1
#define D_PROGRESSIVE_SUPPORTED 1

/* Multiscan support (for progressive and non-interleaved) */
#define C_MULTISCAN_FILES_SUPPORTED 1
#define D_MULTISCAN_FILES_SUPPORTED 1

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
