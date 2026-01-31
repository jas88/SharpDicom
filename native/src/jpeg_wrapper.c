/**
 * SharpDicom Native Codecs - JPEG Wrapper Implementation
 *
 * Uses libjpeg-turbo's TurboJPEG API for high-performance JPEG encoding/decoding.
 * Thread-safe implementation using thread-local handles.
 */

#include "jpeg_wrapper.h"
#include "sharpdicom_codecs.h"

#include <string.h>
#include <stdlib.h>

/* Forward declaration of set_error from sharpdicom_codecs.c */
extern void set_error(const char* message);

#ifdef SHARPDICOM_WITH_JPEG

/*============================================================================
 * TurboJPEG API declarations
 *
 * Minimal subset of turbojpeg.h for building without the full header.
 * When libjpeg-turbo is available, the actual header will be used.
 *============================================================================*/

#ifndef TURBOJPEG_H
#define TURBOJPEG_H

/** TurboJPEG handle type */
typedef void* tjhandle;

/** Pixel formats */
enum TJPF {
    TJPF_RGB = 0,       /**< RGB pixel format */
    TJPF_BGR = 1,       /**< BGR pixel format */
    TJPF_RGBX = 2,      /**< RGBX pixel format */
    TJPF_BGRX = 3,      /**< BGRX pixel format */
    TJPF_XBGR = 4,      /**< XBGR pixel format */
    TJPF_XRGB = 5,      /**< XRGB pixel format */
    TJPF_GRAY = 6,      /**< Grayscale pixel format */
    TJPF_RGBA = 7,      /**< RGBA pixel format */
    TJPF_BGRA = 8,      /**< BGRA pixel format */
    TJPF_ABGR = 9,      /**< ABGR pixel format */
    TJPF_ARGB = 10,     /**< ARGB pixel format */
    TJPF_CMYK = 11,     /**< CMYK pixel format */
    TJPF_UNKNOWN = -1   /**< Unknown pixel format */
};

/** Chroma subsampling */
enum TJSAMP {
    TJSAMP_444 = 0,     /**< 4:4:4 */
    TJSAMP_422 = 1,     /**< 4:2:2 */
    TJSAMP_420 = 2,     /**< 4:2:0 */
    TJSAMP_GRAY = 3,    /**< Grayscale */
    TJSAMP_440 = 4,     /**< 4:4:0 */
    TJSAMP_411 = 5,     /**< 4:1:1 */
    TJSAMP_UNKNOWN = -1 /**< Unknown */
};

/** Colorspace */
enum TJCS {
    TJCS_RGB = 0,       /**< RGB */
    TJCS_YCbCr = 1,     /**< YCbCr */
    TJCS_GRAY = 2,      /**< Grayscale */
    TJCS_CMYK = 3,      /**< CMYK */
    TJCS_YCCK = 4       /**< YCCK */
};

/** Flags */
#define TJFLAG_FASTUPSAMPLE  (1 << 0)  /**< Use fast upsampling */
#define TJFLAG_NOREALLOC     (1 << 10) /**< Don't reallocate buffer */
#define TJFLAG_FASTDCT       (1 << 11) /**< Use fast DCT */
#define TJFLAG_ACCURATEDCT   (1 << 12) /**< Use accurate DCT */

/** API functions - will link against actual libjpeg-turbo */
extern tjhandle tjInitDecompress(void);
extern tjhandle tjInitCompress(void);
extern int tjDestroy(tjhandle handle);
extern int tjDecompressHeader3(tjhandle handle,
    const unsigned char* jpegBuf, unsigned long jpegSize,
    int* width, int* height, int* jpegSubsamp, int* jpegColorspace);
extern int tjDecompress2(tjhandle handle,
    const unsigned char* jpegBuf, unsigned long jpegSize,
    unsigned char* dstBuf, int width, int pitch, int height, int pixelFormat, int flags);
extern int tjCompress2(tjhandle handle,
    const unsigned char* srcBuf, int width, int pitch, int height, int pixelFormat,
    unsigned char** jpegBuf, unsigned long* jpegSize, int jpegSubsamp, int jpegQual, int flags);
extern unsigned char* tjAlloc(int bytes);
extern void tjFree(unsigned char* buffer);
extern const char* tjGetErrorStr2(tjhandle handle);
extern int tjPixelSize[];
extern unsigned long tjBufSize(int width, int height, int jpegSubsamp);

#endif /* TURBOJPEG_H */

/*============================================================================
 * Thread-local handles
 *============================================================================*/

/* Thread-local storage: use __declspec(thread) only for actual MSVC */
#if defined(_MSC_VER)
    #define THREAD_LOCAL __declspec(thread)
#else
    #define THREAD_LOCAL __thread
#endif

/** Thread-local decompression handle */
static THREAD_LOCAL tjhandle tls_decompress_handle = NULL;

/** Thread-local compression handle */
static THREAD_LOCAL tjhandle tls_compress_handle = NULL;

/** Get or create decompression handle for this thread */
static tjhandle get_decompress_handle(void) {
    if (tls_decompress_handle == NULL) {
        tls_decompress_handle = tjInitDecompress();
    }
    return tls_decompress_handle;
}

/** Get or create compression handle for this thread */
static tjhandle get_compress_handle(void) {
    if (tls_compress_handle == NULL) {
        tls_compress_handle = tjInitCompress();
    }
    return tls_compress_handle;
}

/*============================================================================
 * Internal helper functions
 *============================================================================*/

/** Map JpegSubsampling to TurboJPEG TJSAMP */
static int map_subsamp_to_tj(int subsamp) {
    switch (subsamp) {
        case JPEG_SAMP_444:  return TJSAMP_444;
        case JPEG_SAMP_422:  return TJSAMP_422;
        case JPEG_SAMP_420:  return TJSAMP_420;
        case JPEG_SAMP_GRAY: return TJSAMP_GRAY;
        case JPEG_SAMP_440:  return TJSAMP_440;
        case JPEG_SAMP_411:  return TJSAMP_411;
        default:             return TJSAMP_444;
    }
}

/** Map TurboJPEG TJSAMP to JpegSubsampling */
static int map_tj_to_subsamp(int tjsamp) {
    switch (tjsamp) {
        case TJSAMP_444:  return JPEG_SAMP_444;
        case TJSAMP_422:  return JPEG_SAMP_422;
        case TJSAMP_420:  return JPEG_SAMP_420;
        case TJSAMP_GRAY: return JPEG_SAMP_GRAY;
        case TJSAMP_440:  return JPEG_SAMP_440;
        case TJSAMP_411:  return JPEG_SAMP_411;
        default:          return JPEG_SAMP_444;
    }
}

/** Get pixel format for requested colorspace and components
 * Marked unused for stub builds where this function isn't called. */
#if defined(__GNUC__) || defined(__clang__)
__attribute__((unused))
#endif
static int get_pixel_format(int colorspace, int components) {
    if (components == 1 || colorspace == JPEG_CS_GRAY) {
        return TJPF_GRAY;
    }
    switch (colorspace) {
        case JPEG_CS_RGB:
        case JPEG_CS_UNKNOWN:
        default:
            return TJPF_RGB;
    }
}

/*============================================================================
 * 8-bit JPEG functions
 *============================================================================*/

int jpeg_decode_header(
    const uint8_t* input, int inputLen,
    int* width, int* height, int* components, int* subsampling)
{
    tjhandle handle;
    int tjSubsamp, tjColorspace;

    /* Validate arguments */
    if (input == NULL || inputLen <= 0) {
        set_error("jpeg_decode_header: invalid input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (width == NULL || height == NULL || components == NULL) {
        set_error("jpeg_decode_header: output parameters cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Get or create handle */
    handle = get_decompress_handle();
    if (handle == NULL) {
        set_error("jpeg_decode_header: failed to initialize decompressor");
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Read header */
    if (tjDecompressHeader3(handle, input, (unsigned long)inputLen,
                            width, height, &tjSubsamp, &tjColorspace) != 0) {
        const char* err = tjGetErrorStr2(handle);
        set_error(err ? err : "jpeg_decode_header: failed to read JPEG header");
        return JPEG_ERR_INVALID_HEADER;
    }

    /* Determine components from colorspace */
    switch (tjColorspace) {
        case TJCS_GRAY:
            *components = 1;
            break;
        case TJCS_RGB:
        case TJCS_YCbCr:
            *components = 3;
            break;
        case TJCS_CMYK:
        case TJCS_YCCK:
            *components = 4;
            break;
        default:
            *components = 3;
            break;
    }

    /* Map subsampling */
    if (subsampling != NULL) {
        *subsampling = map_tj_to_subsamp(tjSubsamp);
    }

    return SHARPDICOM_OK;
}

int jpeg_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components,
    int colorspace)
{
    tjhandle handle;
    int w, h, comps, subsamp;
    int pixelFormat;
    size_t requiredSize;
    int flags;

    /* Validate input arguments */
    if (input == NULL || inputLen <= 0) {
        set_error("jpeg_decode: invalid input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || outputLen <= 0) {
        set_error("jpeg_decode: invalid output buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Read header first to get dimensions */
    if (jpeg_decode_header(input, inputLen, &w, &h, &comps, &subsamp) != SHARPDICOM_OK) {
        return JPEG_ERR_INVALID_HEADER;  /* Error already set */
    }

    /* Get handle */
    handle = get_decompress_handle();
    if (handle == NULL) {
        set_error("jpeg_decode: failed to initialize decompressor");
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Determine output pixel format and components */
    if (colorspace == JPEG_CS_GRAY) {
        pixelFormat = TJPF_GRAY;
        comps = 1;
    } else if (colorspace == JPEG_CS_RGB || colorspace == JPEG_CS_UNKNOWN) {
        pixelFormat = TJPF_RGB;
        if (comps == 1) {
            /* Keep grayscale as-is */
            pixelFormat = TJPF_GRAY;
        } else {
            comps = 3;
        }
    } else {
        pixelFormat = TJPF_RGB;
        comps = 3;
    }

    /* Check output buffer size (with overflow protection) */
    requiredSize = safe_mul3_size((size_t)w, (size_t)h, (size_t)comps);
    if (requiredSize == 0 || (size_t)outputLen < requiredSize) {
        set_error("jpeg_decode: output buffer too small or dimensions too large");
        return JPEG_ERR_OUTPUT_TOO_SMALL;
    }

    /* Decode with accurate DCT for medical imaging quality */
    flags = TJFLAG_ACCURATEDCT;

    if (tjDecompress2(handle, input, (unsigned long)inputLen,
                      output, w, 0, h, pixelFormat, flags) != 0) {
        const char* err = tjGetErrorStr2(handle);
        set_error(err ? err : "jpeg_decode: decompression failed");
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Return dimensions */
    if (width != NULL) *width = w;
    if (height != NULL) *height = h;
    if (components != NULL) *components = comps;

    return SHARPDICOM_OK;
}

int jpeg_encode(
    const uint8_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality, int subsamp)
{
    tjhandle handle;
    int pixelFormat;
    int tjSubsamp;
    unsigned char* jpegBuf = NULL;
    unsigned long jpegSize = 0;
    int flags;

    /* Validate arguments */
    if (input == NULL) {
        set_error("jpeg_encode: input cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (width <= 0 || height <= 0) {
        set_error("jpeg_encode: invalid dimensions");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (components != 1 && components != 3) {
        set_error("jpeg_encode: components must be 1 (grayscale) or 3 (RGB)");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || outputLen == NULL) {
        set_error("jpeg_encode: output parameters cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (quality < 1 || quality > 100) {
        set_error("jpeg_encode: quality must be 1-100");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Get handle */
    handle = get_compress_handle();
    if (handle == NULL) {
        set_error("jpeg_encode: failed to initialize compressor");
        return SHARPDICOM_ERR_INTERNAL;
    }

    /* Determine pixel format */
    pixelFormat = (components == 1) ? TJPF_GRAY : TJPF_RGB;

    /* Map subsampling */
    if (components == 1) {
        tjSubsamp = TJSAMP_GRAY;
    } else {
        tjSubsamp = map_subsamp_to_tj(subsamp);
    }

    /* Use accurate DCT for medical imaging */
    flags = TJFLAG_ACCURATEDCT;

    /* Compress - TurboJPEG allocates the buffer */
    if (tjCompress2(handle, input, width, 0, height, pixelFormat,
                    &jpegBuf, &jpegSize, tjSubsamp, quality, flags) != 0) {
        const char* err = tjGetErrorStr2(handle);
        set_error(err ? err : "jpeg_encode: compression failed");
        if (jpegBuf != NULL) {
            tjFree(jpegBuf);
        }
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    *output = jpegBuf;
    *outputLen = (int)jpegSize;

    return SHARPDICOM_OK;
}

void jpeg_free(uint8_t* buffer) {
    if (buffer != NULL) {
        tjFree(buffer);
    }
}

/*============================================================================
 * 12-bit JPEG functions
 *
 * Note: 12-bit support requires libjpeg-turbo built with -DWITH_12BIT=1.
 * Most standard builds do not include this.
 * These functions provide a runtime check and graceful fallback.
 *============================================================================*/

/** Check for 12-bit support (compile-time flag) */
#ifdef WITH_12BIT
#define JPEG_12BIT_AVAILABLE 1
#else
#define JPEG_12BIT_AVAILABLE 0
#endif

int jpeg_has_12bit_support(void) {
    return JPEG_12BIT_AVAILABLE;
}

int jpeg_decode_12bit(
    const uint8_t* input, int inputLen,
    uint16_t* output, int outputLen,
    int* width, int* height, int* components)
{
#if JPEG_12BIT_AVAILABLE
    /* 12-bit decoding implementation would go here
     * Using tj12Decompress2 API from libjpeg-turbo 12-bit build */

    /* For now, return unsupported until we have a 12-bit build */
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;

    set_error("jpeg_decode_12bit: 12-bit JPEG requires special libjpeg-turbo build");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
#else
    /* 12-bit not compiled in */
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;

    set_error("jpeg_decode_12bit: 12-bit JPEG support not available (library built without -DWITH_12BIT)");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
#endif
}

int jpeg_encode_12bit(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality)
{
#if JPEG_12BIT_AVAILABLE
    /* 12-bit encoding implementation would go here
     * Using tj12Compress2 API from libjpeg-turbo 12-bit build */

    /* For now, return unsupported until we have a 12-bit build */
    (void)input;
    (void)width;
    (void)height;
    (void)components;
    (void)output;
    (void)outputLen;
    (void)quality;

    set_error("jpeg_encode_12bit: 12-bit JPEG requires special libjpeg-turbo build");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
#else
    /* 12-bit not compiled in */
    (void)input;
    (void)width;
    (void)height;
    (void)components;
    (void)output;
    (void)outputLen;
    (void)quality;

    set_error("jpeg_encode_12bit: 12-bit JPEG support not available (library built without -DWITH_12BIT)");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
#endif
}

#else /* SHARPDICOM_WITH_JPEG not defined */

/*============================================================================
 * Stub implementations when libjpeg-turbo is not available
 *============================================================================*/

int jpeg_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components,
    int colorspace)
{
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;
    (void)colorspace;
    set_error("JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

int jpeg_decode_header(
    const uint8_t* input, int inputLen,
    int* width, int* height, int* components, int* subsampling)
{
    (void)input;
    (void)inputLen;
    (void)width;
    (void)height;
    (void)components;
    (void)subsampling;
    set_error("JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

int jpeg_encode(
    const uint8_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality, int subsamp)
{
    (void)input;
    (void)width;
    (void)height;
    (void)components;
    (void)output;
    (void)outputLen;
    (void)quality;
    (void)subsamp;
    set_error("JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

void jpeg_free(uint8_t* buffer)
{
    (void)buffer;
}

int jpeg_decode_12bit(
    const uint8_t* input, int inputLen,
    uint16_t* output, int outputLen,
    int* width, int* height, int* components)
{
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;
    set_error("JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

int jpeg_encode_12bit(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality)
{
    (void)input;
    (void)width;
    (void)height;
    (void)components;
    (void)output;
    (void)outputLen;
    (void)quality;
    set_error("JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

int jpeg_has_12bit_support(void)
{
    return 0;
}

#endif /* SHARPDICOM_WITH_JPEG */
