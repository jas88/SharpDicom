/*
 * avconfig.h - FFmpeg libavutil configuration
 *
 * Generated for SharpDicom cross-platform builds.
 * This header provides compile-time configuration for libavutil.
 */

#ifndef AVUTIL_AVCONFIG_H
#define AVUTIL_AVCONFIG_H

/* Byte order detection */
#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
  #define AV_HAVE_BIGENDIAN 1
#else
  #define AV_HAVE_BIGENDIAN 0
#endif

/* Fast unaligned memory access - enabled for x86 and ARM */
#if defined(__x86_64__) || defined(_M_X64) || defined(__aarch64__) || defined(_M_ARM64)
  #define AV_HAVE_FAST_UNALIGNED 1
#else
  #define AV_HAVE_FAST_UNALIGNED 0
#endif

#endif /* AVUTIL_AVCONFIG_H */
