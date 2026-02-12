/*
 * FFmpeg config.h - Minimal configuration for SharpDicom
 *
 * This header enables only the codecs and features needed for DICOM:
 * - Video: MPEG-2, H.264 (via libx264), HEVC (via libx265)
 * - Audio: AAC, PCM
 * - Muxers: MPEG-TS, raw H.264/HEVC, WAV, ADTS
 *
 * Generated for cross-platform Zig builds without configure script.
 */

#ifndef FFMPEG_CONFIG_H
#define FFMPEG_CONFIG_H

/* FFmpeg version */
#define FFMPEG_VERSION "7.1"
#define LIBAVUTIL_VERSION_MAJOR 59
#define LIBAVCODEC_VERSION_MAJOR 61
#define LIBAVFORMAT_VERSION_MAJOR 61
#define LIBSWSCALE_VERSION_MAJOR 8
#define LIBSWRESAMPLE_VERSION_MAJOR 5

/* Build configuration */
#define FFMPEG_CONFIGURATION "--enable-static --disable-programs"
#define CONFIG_STATIC 1
#define CONFIG_SHARED 0

/* Architecture detection */
#if defined(__x86_64__) || defined(_M_X64)
  #define ARCH_X86 1
  #define ARCH_X86_64 1
  #define HAVE_FAST_64BIT 1
  #define HAVE_FAST_UNALIGNED 1
#elif defined(__aarch64__) || defined(_M_ARM64)
  #define ARCH_AARCH64 1
  #define HAVE_FAST_64BIT 1
  #define HAVE_FAST_UNALIGNED 1
#else
  #define ARCH_X86 0
  #define ARCH_X86_64 0
  #define ARCH_AARCH64 0
#endif

/* Platform detection */
#if defined(_WIN32) || defined(_WIN64)
  #define CONFIG_WIN32 1
  #define HAVE_WINDOWS_H 1
  #define HAVE_DIRECT_H 1
#else
  #define CONFIG_WIN32 0
  #define HAVE_UNISTD_H 1
  #define HAVE_SYS_TIME_H 1
  #define HAVE_SYS_RESOURCE_H 1
#endif

#if defined(__APPLE__)
  #define CONFIG_DARWIN 1
#else
  #define CONFIG_DARWIN 0
#endif

#if defined(__linux__)
  #define CONFIG_LINUX 1
#else
  #define CONFIG_LINUX 0
#endif

/* Compiler features */
#define HAVE_ATTRIBUTE_MAY_ALIAS 1
#define HAVE_ATTRIBUTE_PACKED 1
#define HAVE_PRAGMA_DEPRECATED 1

/* Compiler attributes - let libavutil/attributes.h define these */
#define HAVE_INLINE_ASM 0  /* Disable for cross-compilation */

/* Threading */
#define HAVE_PTHREADS 1
#define HAVE_THREADS 1
#define CONFIG_THREAD_SANITIZER 0

/* Standard library features */
#define HAVE_STDINT_H 1
#define HAVE_INTTYPES_H 1
#define HAVE_STDBOOL_H 1
#define HAVE_MATH_H 1
#define HAVE_FLOAT_H 1
#define HAVE_LIMITS_H 1
#define HAVE_STDLIB_H 1
#define HAVE_STRING_H 1
#define HAVE_MALLOC_H 1
#define HAVE_MEMORY_H 1

/* Memory functions */
#define HAVE_ALIGNED_MALLOC 1
#define HAVE_MMAP 1
#define HAVE_MEMALIGN 1
#define HAVE_POSIX_MEMALIGN 1

/* Math functions - tell FFmpeg we have standard C99 math functions
 * This prevents FFmpeg's libm.h from providing conflicting fallbacks */
#define HAVE_LRINT 1
#define HAVE_LRINTF 1
#define HAVE_LLRINT 1
#define HAVE_LLRINTF 1
#define HAVE_LOG2 1
#define HAVE_LOG2F 1
#define HAVE_CBRT 1
#define HAVE_CBRTF 1
#define HAVE_COPYSIGN 1
#define HAVE_TRUNC 1
#define HAVE_TRUNCF 1
#define HAVE_RINT 1
#define HAVE_ROUND 1
#define HAVE_ROUNDF 1
#define HAVE_ISNAN 1
#define HAVE_ISINF 1
#define HAVE_ISFINITE 1
#define HAVE_HYPOT 1
#define HAVE_ERF 1

/* Time functions */
#define HAVE_GMTIME_R 1
#define HAVE_LOCALTIME_R 1

/* ============================================================
 * Enabled codecs (DICOM-relevant subset)
 * ============================================================ */

/* Video decoders */
#define CONFIG_MPEG2VIDEO_DECODER 1
#define CONFIG_H264_DECODER 1
#define CONFIG_HEVC_DECODER 1
#define CONFIG_MPEG4_DECODER 1

/* Video encoders */
#define CONFIG_MPEG2VIDEO_ENCODER 1
#define CONFIG_LIBX264_ENCODER 1
#define CONFIG_LIBX265_ENCODER 1

/* Audio decoders */
#define CONFIG_AAC_DECODER 1
#define CONFIG_PCM_S16LE_DECODER 1
#define CONFIG_PCM_S16BE_DECODER 1

/* Audio encoders */
#define CONFIG_AAC_ENCODER 1
#define CONFIG_PCM_S16LE_ENCODER 1

/* Parsers */
#define CONFIG_H264_PARSER 1
#define CONFIG_HEVC_PARSER 1
#define CONFIG_MPEG4VIDEO_PARSER 1
#define CONFIG_MPEGVIDEO_PARSER 1
#define CONFIG_AAC_PARSER 1

/* Muxers */
#define CONFIG_MPEGTS_MUXER 1
#define CONFIG_H264_MUXER 1
#define CONFIG_HEVC_MUXER 1
#define CONFIG_WAV_MUXER 1
#define CONFIG_ADTS_MUXER 1
#define CONFIG_RAWVIDEO_MUXER 1

/* Demuxers */
#define CONFIG_MPEGTS_DEMUXER 1
#define CONFIG_H264_DEMUXER 1
#define CONFIG_HEVC_DEMUXER 1
#define CONFIG_WAV_DEMUXER 1
#define CONFIG_AAC_DEMUXER 1

/* Protocols (minimal for in-memory) */
#define CONFIG_FILE_PROTOCOL 0  /* We use memory I/O */
#define CONFIG_PIPE_PROTOCOL 0
#define CONFIG_CONCAT_PROTOCOL 0

/* Filters (disabled - not needed for DICOM) */
#define CONFIG_AVFILTER 0

/* ============================================================
 * Library features
 * ============================================================ */

/* libavutil */
#define CONFIG_AVUTIL 1
#define CONFIG_PIXELUTILS 1
#define CONFIG_NETWORK 0
#define CONFIG_FFPLAY 0
#define CONFIG_FFPROBE 0
#define CONFIG_FFMPEG 0

/* libavcodec */
#define CONFIG_AVCODEC 1
#define CONFIG_ENCODERS 1
#define CONFIG_DECODERS 1
#define CONFIG_HWACCELS 0  /* No hardware acceleration in cross-compiled builds */
#define CONFIG_BSFS 1

/* libavformat */
#define CONFIG_AVFORMAT 1
#define CONFIG_MUXERS 1
#define CONFIG_DEMUXERS 1
#define CONFIG_PROTOCOLS 0

/* libswscale */
#define CONFIG_SWSCALE 1
#define CONFIG_SWSCALE_ALPHA 1

/* libswresample */
#define CONFIG_SWRESAMPLE 1

/* External library support */
#define CONFIG_LIBX264 1
#define CONFIG_LIBX265 1
#define CONFIG_GPL 1

/* ============================================================
 * Disabled features
 * ============================================================ */

#define CONFIG_AVDEVICE 0
#define CONFIG_POSTPROC 0
#define CONFIG_AVRESAMPLE 0
#define CONFIG_BZLIB 0
#define CONFIG_ZLIB 0
#define CONFIG_LZMA 0
#define CONFIG_LIBVPX 0
#define CONFIG_LIBOPUS 0
#define CONFIG_LIBVORBIS 0
#define CONFIG_LIBMP3LAME 0
#define CONFIG_LIBWEBP 0
#define CONFIG_OPENSSL 0
#define CONFIG_GNUTLS 0
#define CONFIG_DOC 0
#define CONFIG_PROGRAMS 0
#define CONFIG_EXAMPLES 0

/* SIMD - disabled for portable cross-compiled builds */
#define CONFIG_MMX 0
#define CONFIG_MMX2 0
#define CONFIG_SSE 0
#define CONFIG_SSE2 0
#define CONFIG_SSE3 0
#define CONFIG_SSSE3 0
#define CONFIG_SSE4 0
#define CONFIG_SSE42 0
#define CONFIG_AVX 0
#define CONFIG_AVX2 0
#define CONFIG_AVX512 0
#define CONFIG_NEON 0
#define CONFIG_ARMV5TE 0
#define CONFIG_ARMV6 0
#define CONFIG_ARMV6T2 0
#define CONFIG_VFP 0
#define CONFIG_VFPV3 0
#define CONFIG_ARMV8 0

/* Hardware acceleration (disabled for cross-compilation) */
#define CONFIG_CUDA 0
#define CONFIG_CUVID 0
#define CONFIG_NVENC 0
#define CONFIG_NVDEC 0
#define CONFIG_VAAPI 0
#define CONFIG_VDPAU 0
#define CONFIG_VIDEOTOOLBOX 0
#define CONFIG_D3D11VA 0
#define CONFIG_DXVA2 0
#define CONFIG_AMF 0
#define CONFIG_QSV 0
#define CONFIG_OPENCL 0

/* ============================================================
 * Size types - must be preprocessor constants, guarded to avoid
 * conflicts with x265_config.h which also defines these
 * ============================================================ */

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

#ifndef SIZEOF_INT
  #define SIZEOF_INT 4
#endif

#ifndef SIZEOF_SHORT
  #define SIZEOF_SHORT 2
#endif

/* Endianness */
#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
  #define WORDS_BIGENDIAN 1
  #define HAVE_BIGENDIAN 1
#else
  #define WORDS_BIGENDIAN 0
  #define HAVE_BIGENDIAN 0
#endif

/* Pointer size */
#if defined(__LP64__) || defined(_WIN64) || defined(__x86_64__) || defined(__aarch64__)
  #define SIZEOF_POINTER 8
#else
  #define SIZEOF_POINTER 4
#endif

/* ============================================================
 * Additional required macros for FFmpeg compilation
 * ============================================================ */

/* Path handling */
#if defined(_WIN32) || defined(_WIN64)
  #define HAVE_DOS_PATHS 1
#else
  #define HAVE_DOS_PATHS 0
#endif

/* NULL_IF_CONFIG_SMALL - returns description or NULL based on config */
#ifndef NULL_IF_CONFIG_SMALL
  #define NULL_IF_CONFIG_SMALL(x) (x)
#endif

/* Swscale filter size */
#ifndef SWS_MAX_FILTER_SIZE
  #define SWS_MAX_FILTER_SIZE 256
#endif

/* Architecture macros that FFmpeg expects */
#ifndef HAVE_ALTIVEC
  #define HAVE_ALTIVEC 0
#endif
#ifndef HAVE_MMX
  #define HAVE_MMX 0
#endif
#ifndef HAVE_LASX
  #define HAVE_LASX 0
#endif
#ifndef HAVE_LSX
  #define HAVE_LSX 0
#endif

/* Math functions - indicate we have standard C99 math */
#define HAVE_ERF 1
#define HAVE_HYPOT 1
#define HAVE_ISFINITE 1

/* Time functions */
#define HAVE_GMTIME_R 1
#define HAVE_LOCALTIME_R 1

/* System features */
#define HAVE_TERMIOS_H 1
#define HAVE_SYS_MMAN_H 1
#define HAVE_SYS_PARAM_H 1

#endif /* FFMPEG_CONFIG_H */
