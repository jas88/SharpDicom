/**
 * SharpDicom Native Codecs - 12-bit JPEG Wrapper Implementation
 *
 * Uses libjpeg-turbo 3.0+ native multi-precision API for 12-bit JPEG
 * encoding/decoding. A single libjpeg-turbo library provides both 8-bit
 * and 12-bit support:
 *
 *   - Standard functions (jpeg_create_decompress, jpeg_read_header, etc.)
 *     handle setup, teardown, and all non-scanline operations.
 *   - jpeg12_read_scanlines / jpeg12_write_scanlines handle 12-bit
 *     scanline I/O using J12SAMPLE (short) buffers.
 *   - data_precision is set to 12 to select the 12-bit code path.
 *
 * Architecture:
 *   8-bit path:  Standard libjpeg API with JSAMPLE (unsigned char)
 *   12-bit path: Standard libjpeg API + jpeg12_read/write_scanlines
 *                with J12SAMPLE (short) buffers
 *
 * When SHARPDICOM_WITH_JPEG12 is not defined, all functions compile as
 * stubs that return SHARPDICOM_ERR_UNSUPPORTED.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "jpeg12_wrapper.h"
#include "sharpdicom_codecs.h"

#include <stdio.h>
#include <string.h>
#include <stdlib.h>

/* Forward declaration of error functions from sharpdicom_codecs.c */
extern void set_error(const char* message);
extern void set_error_fmt(const char* fmt, ...);

#ifdef SHARPDICOM_WITH_JPEG12

#include <jpeglib.h>
#include <jerror.h>
#include <setjmp.h>

/*============================================================================
 * Error handling
 *
 * Uses the same setjmp/longjmp pattern as the 8-bit jpeg_wrapper.c.
 * On error, libjpeg calls error_exit which longjmps back to the caller.
 *============================================================================*/

typedef struct {
    struct jpeg_error_mgr pub;     /* Standard libjpeg error manager */
    jmp_buf setjmp_buffer;         /* For return to caller on error */
    char error_msg[JMSG_LENGTH_MAX]; /* Formatted error message */
} jpeg12_error_handler;

/**
 * Custom error exit handler. Formats the error message from libjpeg,
 * stores it in thread-local error state, and longjmps to the caller.
 */
static void jpeg12_error_exit(j_common_ptr cinfo) {
    jpeg12_error_handler* handler = (jpeg12_error_handler*)cinfo->err;
    (*cinfo->err->format_message)(cinfo, handler->error_msg);
    set_error(handler->error_msg);
    longjmp(handler->setjmp_buffer, 1);
}

/*============================================================================
 * 12-bit JPEG Decode Implementation
 *============================================================================*/

int jpeg12_decode(
    const uint8_t* input, size_t inputLen,
    uint16_t* output, size_t outputLen,
    int* width, int* height, int* components)
{
    struct jpeg_decompress_struct cinfo;
    jpeg12_error_handler jerr;
    J12SAMPROW row_buf = NULL;
    JDIMENSION row_stride;
    size_t required_size;

    /* Validate arguments */
    if (input == NULL || inputLen == 0) {
        set_error("jpeg12_decode: invalid input");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || outputLen == 0) {
        set_error("jpeg12_decode: invalid output buffer");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (width == NULL || height == NULL || components == NULL) {
        set_error("jpeg12_decode: output parameters cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Set up error handling with setjmp/longjmp */
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = jpeg12_error_exit;

    if (setjmp(jerr.setjmp_buffer)) {
        /* Error occurred - clean up and return */
        jpeg_destroy_decompress(&cinfo);
        if (row_buf != NULL) {
            free(row_buf);
        }
        return SHARPDICOM_ERR_DECODE_FAILED;
    }

    /* Initialize decompression (standard API) */
    jpeg_create_decompress(&cinfo);

    /* Set up memory source */
    jpeg_mem_src(&cinfo, input, (unsigned long)inputLen);

    /* Read JPEG header */
    if (jpeg_read_header(&cinfo, TRUE) != JPEG_HEADER_OK) {
        set_error("jpeg12_decode: failed to read JPEG header");
        jpeg_destroy_decompress(&cinfo);
        return JPEG12_ERR_INVALID_HEADER;
    }

    /* Read image dimensions from the struct */
    *width = (int)cinfo.image_width;
    *height = (int)cinfo.image_height;
    *components = cinfo.num_components;

    /* Check output buffer size (each sample is uint16_t = 2 bytes) */
    required_size = safe_mul4_size(
        (size_t)*width, (size_t)*height, (size_t)*components, sizeof(uint16_t));
    if (required_size == 0 || outputLen < required_size) {
        set_error_fmt("jpeg12_decode: output buffer too small (need %zu, have %zu)",
                      required_size, outputLen);
        jpeg_destroy_decompress(&cinfo);
        return JPEG12_ERR_OUTPUT_TOO_SMALL;
    }

    /* Force 12-bit data precision */
    cinfo.data_precision = 12;

    /* Use accurate IDCT for medical imaging quality */
    cinfo.dct_method = JDCT_ISLOW;

    /* Start decompression */
    jpeg_start_decompress(&cinfo);

    /* Allocate a single-row buffer for scanline reading */
    row_stride = (JDIMENSION)(*width * *components);
    row_buf = (J12SAMPROW)malloc(row_stride * sizeof(J12SAMPLE));
    if (row_buf == NULL) {
        set_error("jpeg12_decode: out of memory for row buffer");
        jpeg_destroy_decompress(&cinfo);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Read scanlines using the 12-bit API and copy to uint16_t output */
    {
        JDIMENSION rows_read = 0;
        uint16_t* out_ptr = output;
        J12SAMPROW row_array[1];
        row_array[0] = row_buf;

        while (rows_read < (JDIMENSION)*height) {
            JDIMENSION count = jpeg12_read_scanlines(&cinfo, row_array, 1);
            if (count == 0) {
                break; /* Should not happen in normal operation */
            }

            /* Copy J12SAMPLE (short) values to uint16_t output */
            for (JDIMENSION i = 0; i < row_stride; i++) {
                *out_ptr++ = (uint16_t)row_buf[i];
            }
            rows_read += count;
        }
    }

    /* Finish decompression */
    jpeg_finish_decompress(&cinfo);

    /* Clean up */
    jpeg_destroy_decompress(&cinfo);
    free(row_buf);

    return SHARPDICOM_OK;
}

/*============================================================================
 * 12-bit JPEG Encode Implementation
 *============================================================================*/

int jpeg12_encode(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, size_t* outputLen,
    int quality)
{
    struct jpeg_compress_struct cinfo;
    jpeg12_error_handler jerr;
    J12SAMPROW row_buf = NULL;
    unsigned char* outbuffer = NULL;
    unsigned long outsize = 0;
    JDIMENSION row_stride;

    /* Validate arguments */
    if (input == NULL) {
        set_error("jpeg12_encode: input cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (width <= 0 || height <= 0) {
        set_error("jpeg12_encode: invalid dimensions");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (components != 1 && components != 3) {
        set_error("jpeg12_encode: components must be 1 (grayscale) or 3 (color)");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (output == NULL || outputLen == NULL) {
        set_error("jpeg12_encode: output parameters cannot be NULL");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }
    if (quality < 1 || quality > 100) {
        set_error("jpeg12_encode: quality must be 1-100");
        return SHARPDICOM_ERR_INVALID_ARGUMENT;
    }

    /* Set up error handling */
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = jpeg12_error_exit;

    if (setjmp(jerr.setjmp_buffer)) {
        /* Error occurred - clean up and return */
        jpeg_destroy_compress(&cinfo);
        if (row_buf != NULL) {
            free(row_buf);
        }
        if (outbuffer != NULL) {
            free(outbuffer);
        }
        return SHARPDICOM_ERR_ENCODE_FAILED;
    }

    /* Initialize compression (standard API) */
    jpeg_create_compress(&cinfo);

    /* Set up memory destination */
    jpeg_mem_dest(&cinfo, &outbuffer, &outsize);

    /* Set image parameters */
    cinfo.image_width = (JDIMENSION)width;
    cinfo.image_height = (JDIMENSION)height;
    cinfo.input_components = components;
    cinfo.in_color_space = (components == 1) ? JCS_GRAYSCALE : JCS_RGB;

    /* Set defaults first, then override data_precision to 12-bit */
    jpeg_set_defaults(&cinfo);
    cinfo.data_precision = 12;

    /* Set quality (force_baseline FALSE because 12-bit is not baseline) */
    jpeg_set_quality(&cinfo, quality, FALSE);

    /* Use accurate DCT for medical imaging */
    cinfo.dct_method = JDCT_ISLOW;

    /* Start compression */
    jpeg_start_compress(&cinfo, TRUE);

    /* Allocate row buffer */
    row_stride = (JDIMENSION)(width * components);
    row_buf = (J12SAMPROW)malloc(row_stride * sizeof(J12SAMPLE));
    if (row_buf == NULL) {
        set_error("jpeg12_encode: out of memory for row buffer");
        jpeg_destroy_compress(&cinfo);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }

    /* Write scanlines using the 12-bit API */
    {
        const uint16_t* in_ptr = input;
        J12SAMPROW row_array[1];
        row_array[0] = row_buf;

        for (int row = 0; row < height; row++) {
            /* Copy uint16_t values to J12SAMPLE (short) buffer */
            for (JDIMENSION i = 0; i < row_stride; i++) {
                row_buf[i] = (J12SAMPLE)(*in_ptr++);
            }
            jpeg12_write_scanlines(&cinfo, row_array, 1);
        }
    }

    /* Finish compression */
    jpeg_finish_compress(&cinfo);

    /* Copy output buffer (libjpeg may have allocated it internally) */
    *output = (uint8_t*)malloc(outsize);
    if (*output == NULL) {
        set_error("jpeg12_encode: out of memory for output copy");
        jpeg_destroy_compress(&cinfo);
        free(row_buf);
        free(outbuffer);
        return SHARPDICOM_ERR_OUT_OF_MEMORY;
    }
    memcpy(*output, outbuffer, outsize);
    *outputLen = (size_t)outsize;

    /* Clean up */
    jpeg_destroy_compress(&cinfo);
    free(row_buf);
    free(outbuffer);

    return SHARPDICOM_OK;
}

/*============================================================================
 * Memory management
 *============================================================================*/

void jpeg12_free(uint8_t* buffer) {
    if (buffer != NULL) {
        free(buffer);
    }
}

/*============================================================================
 * Capability query
 *============================================================================*/

int jpeg12_has_support(void) {
    return 1;
}

#else /* SHARPDICOM_WITH_JPEG12 not defined */

/*============================================================================
 * Stub implementations when 12-bit JPEG is not available
 *============================================================================*/

int jpeg12_decode(
    const uint8_t* input, size_t inputLen,
    uint16_t* output, size_t outputLen,
    int* width, int* height, int* components)
{
    (void)input;
    (void)inputLen;
    (void)output;
    (void)outputLen;
    (void)width;
    (void)height;
    (void)components;
    set_error("12-bit JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

int jpeg12_encode(
    const uint16_t* input, int width, int height, int components,
    uint8_t** output, size_t* outputLen,
    int quality)
{
    (void)input;
    (void)width;
    (void)height;
    (void)components;
    (void)output;
    (void)outputLen;
    (void)quality;
    set_error("12-bit JPEG support not compiled in");
    return SHARPDICOM_ERR_UNSUPPORTED;
}

void jpeg12_free(uint8_t* buffer) {
    (void)buffer;
}

int jpeg12_has_support(void) {
    return 0;
}

#endif /* SHARPDICOM_WITH_JPEG12 */
