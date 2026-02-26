/**
 * SharpDicom Native Codecs - JPEG Wrapper Implementation
 *
 * Uses libjpeg-turbo's standard libjpeg API for JPEG encoding/decoding.
 * Thread-safe: no global state, all state is on the stack per call.
 */

#include "jpeg_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <setjmp.h>

/* Forward declaration of set_error from sharpdicom_codecs.c */
extern void set_error(const char* message);

#ifdef SHARPDICOM_WITH_JPEG

#include <jpeglib.h>
#include <jerror.h>

/*============================================================================
 * Error handling
 *
 * libjpeg uses setjmp/longjmp for error handling. We wrap this to integrate
 * with our error reporting system.
 *============================================================================*/

typedef struct {
    struct jpeg_error_mgr pub;
    jmp_buf setjmp_buffer;
    char error_msg[JMSG_LENGTH_MAX];
} sharpdicom_error_mgr;

static void error_exit_handler(j_common_ptr cinfo) {
    sharpdicom_error_mgr* err = (sharpdicom_error_mgr*)cinfo->err;
    (*cinfo->err->format_message)(cinfo, err->error_msg);
    longjmp(err->setjmp_buffer, 1);
}

/*============================================================================
 * Memory-based data source (for decompression from buffer)
 *============================================================================*/

/* jpeg_mem_src is available in libjpeg-turbo and libjpeg 9+ */

/*============================================================================
 * Memory-based data destination (for compression to buffer)
 *============================================================================*/

/* jpeg_mem_dest is available in libjpeg-turbo and libjpeg 9+ */

/*============================================================================
 * Internal helper functions
 *============================================================================*/

/** Map JpegSubsampling to J_COLOR_SPACE and sampling factors */
static void set_subsamp_factors(struct jpeg_compress_struct* cinfo, int subsamp) {
    switch (subsamp) {
        case JPEG_SAMP_444:
            cinfo->comp_info[0].h_samp_factor = 1;
            cinfo->comp_info[0].v_samp_factor = 1;
            break;
        case JPEG_SAMP_422:
            cinfo->comp_info[0].h_samp_factor = 2;
            cinfo->comp_info[0].v_samp_factor = 1;
            break;
        case JPEG_SAMP_420:
            cinfo->comp_info[0].h_samp_factor = 2;
            cinfo->comp_info[0].v_samp_factor = 2;
            break;
        case JPEG_SAMP_440:
            cinfo->comp_info[0].h_samp_factor = 1;
            cinfo->comp_info[0].v_samp_factor = 2;
            break;
        case JPEG_SAMP_411:
            cinfo->comp_info[0].h_samp_factor = 4;
            cinfo->comp_info[0].v_samp_factor = 1;
            break;
        case JPEG_SAMP_GRAY:
        default:
            /* Grayscale or default: 1x1 */
            cinfo->comp_info[0].h_samp_factor = 1;
            cinfo->comp_info[0].v_samp_factor = 1;
            break;
    }
    /* Chroma components always 1x1 */
    if (cinfo->num_components > 1) {
        cinfo->comp_info[1].h_samp_factor = 1;
        cinfo->comp_info[1].v_samp_factor = 1;
        cinfo->comp_info[2].h_samp_factor = 1;
        cinfo->comp_info[2].v_samp_factor = 1;
    }
}

/** Map libjpeg subsampling to JpegSubsampling enum */
static int get_subsamp_from_jpeg(struct jpeg_decompress_struct* cinfo) {
    if (cinfo->num_components == 1) {
        return JPEG_SAMP_GRAY;
    }
    int h = cinfo->comp_info[0].h_samp_factor;
    int v = cinfo->comp_info[0].v_samp_factor;
    if (h == 1 && v == 1) return JPEG_SAMP_444;
    if (h == 2 && v == 1) return JPEG_SAMP_422;
    if (h == 2 && v == 2) return JPEG_SAMP_420;
    if (h == 1 && v == 2) return JPEG_SAMP_440;
    if (h == 4 && v == 1) return JPEG_SAMP_411;
    return JPEG_SAMP_444; /* default */
}

/*============================================================================
 * 8-bit JPEG functions
 *============================================================================*/

int jpeg_decode_header(
    const uint8_t* input, int inputLen,
    int* width, int* height, int* components, int* subsampling)
{
    struct jpeg_decompress_struct cinfo;
    sharpdicom_error_mgr jerr;

    /* Validate arguments */
    if (input == NULL || inputLen <= 0) {
        set_error("jpeg_decode_header: invalid input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (width == NULL || height == NULL || components == NULL) {
        set_error("jpeg_decode_header: output parameters cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Set up error handling */
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = error_exit_handler;
    if (setjmp(jerr.setjmp_buffer)) {
        set_error(jerr.error_msg);
        jpeg_destroy_decompress(&cinfo);
        return JPEG_ERR_INVALID_HEADER;
    }

    /* Create decompressor and read header */
    jpeg_create_decompress(&cinfo);
    jpeg_mem_src(&cinfo, input, (unsigned long)inputLen);

    if (jpeg_read_header(&cinfo, TRUE) != JPEG_HEADER_OK) {
        set_error("jpeg_decode_header: failed to read JPEG header");
        jpeg_destroy_decompress(&cinfo);
        return JPEG_ERR_INVALID_HEADER;
    }

    *width = (int)cinfo.image_width;
    *height = (int)cinfo.image_height;
    *components = cinfo.num_components;

    if (subsampling != NULL) {
        *subsampling = get_subsamp_from_jpeg(&cinfo);
    }

    jpeg_destroy_decompress(&cinfo);
    return SHARPDICOM_OK;
}

int jpeg_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components,
    int colorspace)
{
    struct jpeg_decompress_struct cinfo;
    sharpdicom_error_mgr jerr;
    JSAMPROW row_pointer[1];
    int row_stride;
    size_t requiredSize;

    /* Validate input arguments */
    if (input == NULL || inputLen <= 0) {
        set_error("jpeg_decode: invalid input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || outputLen <= 0) {
        set_error("jpeg_decode: invalid output buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Set up error handling */
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = error_exit_handler;
    if (setjmp(jerr.setjmp_buffer)) {
        set_error(jerr.error_msg);
        jpeg_destroy_decompress(&cinfo);
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Create decompressor */
    jpeg_create_decompress(&cinfo);
    jpeg_mem_src(&cinfo, input, (unsigned long)inputLen);

    if (jpeg_read_header(&cinfo, TRUE) != JPEG_HEADER_OK) {
        set_error("jpeg_decode: failed to read JPEG header");
        jpeg_destroy_decompress(&cinfo);
        return JPEG_ERR_INVALID_HEADER;
    }

    /* Set output colorspace */
    if (colorspace == JPEG_CS_GRAY) {
        cinfo.out_color_space = JCS_GRAYSCALE;
    } else if (cinfo.jpeg_color_space == JCS_GRAYSCALE) {
        cinfo.out_color_space = JCS_GRAYSCALE;
    } else {
        cinfo.out_color_space = JCS_RGB;
    }

    /* Use accurate IDCT for medical imaging quality */
    cinfo.dct_method = JDCT_ISLOW;

    /* Start decompression */
    jpeg_start_decompress(&cinfo);

    int out_components = cinfo.output_components;
    int w = (int)cinfo.output_width;
    int h = (int)cinfo.output_height;
    row_stride = w * out_components;

    /* Check output buffer size */
    requiredSize = safe_mul3_size((size_t)w, (size_t)h, (size_t)out_components);
    if (requiredSize == 0 || (size_t)outputLen < requiredSize) {
        set_error("jpeg_decode: output buffer too small or dimensions too large");
        jpeg_destroy_decompress(&cinfo);
        return JPEG_ERR_OUTPUT_TOO_SMALL;
    }

    /* Read scanlines */
    while (cinfo.output_scanline < cinfo.output_height) {
        row_pointer[0] = output + ((size_t)cinfo.output_scanline * (size_t)row_stride);
        jpeg_read_scanlines(&cinfo, row_pointer, 1);
    }

    jpeg_finish_decompress(&cinfo);
    jpeg_destroy_decompress(&cinfo);

    /* Return dimensions */
    if (width != NULL) *width = w;
    if (height != NULL) *height = h;
    if (components != NULL) *components = out_components;

    return SHARPDICOM_OK;
}

int jpeg_encode(
    const uint8_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality, int subsamp)
{
    struct jpeg_compress_struct cinfo;
    sharpdicom_error_mgr jerr;
    unsigned char* outbuf = NULL;
    unsigned long outsize = 0;
    JSAMPROW row_pointer[1];
    int row_stride;

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

    /* Set up error handling */
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = error_exit_handler;
    if (setjmp(jerr.setjmp_buffer)) {
        set_error(jerr.error_msg);
        jpeg_destroy_compress(&cinfo);
        if (outbuf != NULL) free(outbuf);
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    /* Create compressor */
    jpeg_create_compress(&cinfo);

    /* Use memory destination */
    jpeg_mem_dest(&cinfo, &outbuf, &outsize);

    /* Set parameters */
    cinfo.image_width = (JDIMENSION)width;
    cinfo.image_height = (JDIMENSION)height;
    cinfo.input_components = components;
    cinfo.in_color_space = (components == 1) ? JCS_GRAYSCALE : JCS_RGB;

    jpeg_set_defaults(&cinfo);
    jpeg_set_quality(&cinfo, quality, TRUE);

    /* Use accurate DCT for medical imaging */
    cinfo.dct_method = JDCT_ISLOW;

    /* Set subsampling */
    if (components > 1) {
        set_subsamp_factors(&cinfo, subsamp);
    }

    /* Compress */
    jpeg_start_compress(&cinfo, TRUE);

    row_stride = width * components;
    while (cinfo.next_scanline < cinfo.image_height) {
        row_pointer[0] = (JSAMPROW)(input + ((size_t)cinfo.next_scanline * (size_t)row_stride));
        jpeg_write_scanlines(&cinfo, row_pointer, 1);
    }

    jpeg_finish_compress(&cinfo);
    jpeg_destroy_compress(&cinfo);

    *output = outbuf;
    *outputLen = (int)outsize;

    return SHARPDICOM_OK;
}

void jpeg_free(uint8_t* buffer) {
    if (buffer != NULL) {
        free(buffer);
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
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;
    set_error("jpeg_decode_12bit: use jpeg12_decode for 12-bit JPEG");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
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
    set_error("jpeg_encode_12bit: use jpeg12_encode for 12-bit JPEG");
    return JPEG_ERR_12BIT_NOT_SUPPORTED;
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
