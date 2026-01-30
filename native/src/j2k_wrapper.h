/**
 * SharpDicom JPEG 2000 Wrapper API
 *
 * Wraps OpenJPEG library for JPEG 2000 lossless and lossy codec support.
 * Provides resolution level decoding for thumbnails and ROI decode for large images.
 *
 * Thread Safety: All functions are thread-safe.
 * Error messages are stored in thread-local storage via sharpdicom_last_error().
 */

#ifndef J2K_WRAPPER_H
#define J2K_WRAPPER_H

#include "sharpdicom_codecs.h"

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * JPEG 2000 Format Types
 *============================================================================*/

/** JPEG 2000 codestream/file format */
typedef enum {
    /** J2K raw codestream (no file wrapper) */
    J2K_FORMAT_J2K = 0,
    /** JP2 file format (with file wrapper) */
    J2K_FORMAT_JP2 = 1
} J2kFormat;

/*============================================================================
 * JPEG 2000 Color Space
 *============================================================================*/

/** Color space for input/output pixels */
typedef enum {
    /** Unknown color space */
    J2K_COLORSPACE_UNKNOWN = 0,
    /** Grayscale */
    J2K_COLORSPACE_GRAY = 1,
    /** RGB color */
    J2K_COLORSPACE_RGB = 2,
    /** YCbCr color (4:4:4 or 4:2:2 or 4:2:0) */
    J2K_COLORSPACE_YCC = 3,
    /** sYCC (standard YCC) */
    J2K_COLORSPACE_SYCC = 4
} J2kColorSpace;

/*============================================================================
 * JPEG 2000 Image Information
 *============================================================================*/

/** Image information extracted from codestream header */
typedef struct {
    /** Image width in pixels */
    int32_t width;
    /** Image height in pixels */
    int32_t height;
    /** Number of components (1=grayscale, 3=color, 4=color+alpha) */
    int32_t num_components;
    /** Bits per component (typically 8, 12, or 16) */
    int32_t bits_per_component;
    /** Whether samples are signed */
    int32_t is_signed;
    /** Color space */
    J2kColorSpace color_space;
    /** Number of resolution levels available */
    int32_t num_resolutions;
    /** Number of quality layers */
    int32_t num_quality_layers;
    /** Tile width (0 if single tile) */
    int32_t tile_width;
    /** Tile height (0 if single tile) */
    int32_t tile_height;
    /** Number of tiles in X direction */
    int32_t num_tiles_x;
    /** Number of tiles in Y direction */
    int32_t num_tiles_y;
    /** Detected format (J2K or JP2) */
    J2kFormat format;
} J2kImageInfo;

/*============================================================================
 * JPEG 2000 Encode Parameters
 *============================================================================*/

/** Encoding parameters for JPEG 2000 compression */
typedef struct {
    /** Lossless mode (1=lossless with 5/3 wavelet, 0=lossy with 9/7 wavelet) */
    int32_t lossless;
    /** Compression ratio for lossy mode (e.g., 10 = 10:1 compression, 0 = use quality) */
    float compression_ratio;
    /** Quality for lossy mode (1-100, 100=best, only used if compression_ratio is 0) */
    float quality;
    /** Number of resolution levels (0 = auto based on image size) */
    int32_t num_resolutions;
    /** Number of quality layers (0 = single layer) */
    int32_t num_quality_layers;
    /** Tile width (0 = single tile covering whole image) */
    int32_t tile_width;
    /** Tile height (0 = single tile covering whole image) */
    int32_t tile_height;
    /** Output format (J2K or JP2) */
    J2kFormat format;
    /** Code-block width exponent (4-10, 0 = default 6 = 64 pixels) */
    int32_t cblk_width_exp;
    /** Code-block height exponent (4-10, 0 = default 6 = 64 pixels) */
    int32_t cblk_height_exp;
    /** Use progression order: LRCP=0, RLCP=1, RPCL=2, PCRL=3, CPRL=4 */
    int32_t progression_order;
} J2kEncodeParams;

/*============================================================================
 * JPEG 2000 Decode Options
 *============================================================================*/

/** Decoding options for partial/reduced resolution decode */
typedef struct {
    /** Reduction factor (0=full, 1=half, 2=quarter, etc.) */
    int32_t reduce;
    /** Maximum quality layer to decode (0 = all layers) */
    int32_t max_quality_layers;
} J2kDecodeOptions;

/*============================================================================
 * JPEG 2000 API Functions
 *============================================================================*/

/**
 * Get information about a JPEG 2000 codestream without decoding.
 * Reads only the header to extract image metadata.
 *
 * @param input         Pointer to compressed J2K/JP2 data
 * @param input_len     Length of compressed data in bytes
 * @param info          Output: Image information structure
 * @return              SHARPDICOM_OK on success, error code on failure
 */
SHARPDICOM_API int j2k_get_info(
    const uint8_t* input,
    size_t input_len,
    J2kImageInfo* info
);

/**
 * Decode a JPEG 2000 codestream to raw pixels.
 * Supports resolution level reduction for thumbnail generation.
 *
 * @param input         Pointer to compressed J2K/JP2 data
 * @param input_len     Length of compressed data in bytes
 * @param output        Pointer to output buffer for decoded pixels
 * @param output_len    Size of output buffer in bytes
 * @param options       Decode options (can be NULL for defaults)
 * @param out_width     Output: Actual decoded width (may differ if reduce > 0)
 * @param out_height    Output: Actual decoded height (may differ if reduce > 0)
 * @param out_components Output: Number of components
 * @return              SHARPDICOM_OK on success, error code on failure
 */
SHARPDICOM_API int j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
);

/**
 * Decode a region of a JPEG 2000 codestream.
 * Supports ROI (Region of Interest) decode for efficient partial extraction.
 *
 * @param input         Pointer to compressed J2K/JP2 data
 * @param input_len     Length of compressed data in bytes
 * @param output        Pointer to output buffer for decoded pixels
 * @param output_len    Size of output buffer in bytes
 * @param x0            Left coordinate of region (in full resolution space)
 * @param y0            Top coordinate of region (in full resolution space)
 * @param x1            Right coordinate of region (exclusive)
 * @param y1            Bottom coordinate of region (exclusive)
 * @param options       Decode options (can be NULL for defaults)
 * @param out_width     Output: Actual decoded region width
 * @param out_height    Output: Actual decoded region height
 * @param out_components Output: Number of components
 * @return              SHARPDICOM_OK on success, error code on failure
 */
SHARPDICOM_API int j2k_decode_region(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int32_t x0,
    int32_t y0,
    int32_t x1,
    int32_t y1,
    const J2kDecodeOptions* options,
    int32_t* out_width,
    int32_t* out_height,
    int32_t* out_components
);

/**
 * Encode raw pixels to JPEG 2000 format.
 * Supports both lossless (5/3 wavelet) and lossy (9/7 wavelet) compression.
 *
 * @param input         Pointer to raw pixel data (component-interleaved)
 * @param input_len     Length of input data in bytes
 * @param width         Image width in pixels
 * @param height        Image height in pixels
 * @param num_components Number of components (1, 3, or 4)
 * @param bits_per_component Bits per component (8, 12, or 16)
 * @param is_signed     Whether samples are signed
 * @param params        Encoding parameters (can be NULL for lossless defaults)
 * @param output        Pointer to output buffer for compressed data
 * @param output_len    Size of output buffer in bytes
 * @param out_size      Output: Actual size of compressed data
 * @return              SHARPDICOM_OK on success, error code on failure
 */
SHARPDICOM_API int j2k_encode(
    const uint8_t* input,
    size_t input_len,
    int32_t width,
    int32_t height,
    int32_t num_components,
    int32_t bits_per_component,
    int32_t is_signed,
    const J2kEncodeParams* params,
    uint8_t* output,
    size_t output_len,
    size_t* out_size
);

/**
 * Free memory allocated by j2k_* functions.
 * Currently not used as all decoding writes to caller-provided buffers,
 * but reserved for future streaming/handle-based API.
 *
 * @param ptr           Pointer to memory to free
 */
SHARPDICOM_API void j2k_free(void* ptr);

/**
 * Get the OpenJPEG library version string.
 *
 * @return              Version string (e.g., "2.5.3"), or NULL if not available
 */
SHARPDICOM_API const char* j2k_version(void);

#ifdef __cplusplus
}
#endif

#endif /* J2K_WRAPPER_H */
