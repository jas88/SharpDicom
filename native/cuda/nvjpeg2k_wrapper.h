/**
 * nvJPEG2000 Wrapper for SharpDicom
 *
 * Provides GPU-accelerated JPEG 2000 decoding using NVIDIA's nvJPEG2000 library.
 * This wrapper is built separately with nvcc and loaded dynamically.
 *
 * Requirements:
 * - CUDA 11.0 or later
 * - nvJPEG2000 library (part of CUDA Toolkit or separate download)
 * - NVIDIA GPU with compute capability 5.0 or higher (Maxwell+)
 *
 * Thread Safety: All functions are thread-safe.
 */

#ifndef NVJPEG2K_WRAPPER_H
#define NVJPEG2K_WRAPPER_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Platform-specific export macros
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
    #ifdef NVJPEG2K_WRAPPER_EXPORTS
        #define NVJ2K_API __declspec(dllexport)
    #else
        #define NVJ2K_API __declspec(dllimport)
    #endif
#else
    #define NVJ2K_API __attribute__((visibility("default")))
#endif

/*============================================================================
 * Error codes
 *============================================================================*/

/** Success */
#define NVJ2K_OK                        0

/** Error codes (negative values) */
#define NVJ2K_ERR_INVALID_ARGUMENT     -1  /* Invalid parameter passed */
#define NVJ2K_ERR_OUT_OF_MEMORY        -2  /* Memory allocation failed */
#define NVJ2K_ERR_DECODE_FAILED        -3  /* Decoding operation failed */
#define NVJ2K_ERR_ENCODE_FAILED        -4  /* Encoding operation failed */
#define NVJ2K_ERR_NOT_INITIALIZED      -5  /* Library not initialized */
#define NVJ2K_ERR_CUDA_ERROR           -6  /* CUDA runtime error */
#define NVJ2K_ERR_UNSUPPORTED_GPU      -7  /* GPU does not meet requirements */
#define NVJ2K_ERR_NO_DEVICE            -8  /* No CUDA device found */
#define NVJ2K_ERR_INTERNAL             -9  /* Internal library error */

/*============================================================================
 * GPU device information
 *============================================================================*/

/**
 * GPU device information structure.
 */
typedef struct nvj2k_device_info {
    int device_id;           /* CUDA device ID */
    int compute_major;       /* Compute capability major version */
    int compute_minor;       /* Compute capability minor version */
    size_t total_memory;     /* Total GPU memory in bytes */
    size_t free_memory;      /* Available GPU memory in bytes */
    char name[256];          /* Device name */
} nvj2k_device_info_t;

/*============================================================================
 * Decode parameters
 *============================================================================*/

/**
 * Decode parameters for single-frame decoding.
 */
typedef struct nvj2k_decode_params {
    int reduce_factor;       /* Resolution reduction: 0=full, 1=1/2, 2=1/4, etc. */
    int num_components;      /* Expected number of components (0 = auto-detect) */
    int precision;           /* Expected bit depth (0 = auto-detect) */
} nvj2k_decode_params_t;

/**
 * Decode result information.
 */
typedef struct nvj2k_decode_result {
    int width;               /* Decoded image width */
    int height;              /* Decoded image height */
    int num_components;      /* Number of components */
    int precision;           /* Bit depth per component */
    size_t output_size;      /* Size of decoded data in bytes */
} nvj2k_decode_result_t;

/*============================================================================
 * Initialization and cleanup
 *============================================================================*/

/**
 * Initialize the nvJPEG2000 wrapper.
 * Must be called before any other functions.
 *
 * @param device_id CUDA device ID to use (-1 for default/first available)
 * @return NVJ2K_OK on success, error code on failure
 */
NVJ2K_API int nvj2k_init(int device_id);

/**
 * Check if nvJPEG2000 GPU acceleration is available.
 * Can be called before nvj2k_init() to check availability.
 *
 * @return 1 if available, 0 if not available
 */
NVJ2K_API int nvj2k_available(void);

/**
 * Get information about the GPU being used.
 *
 * @param info Pointer to device info structure to fill
 * @return NVJ2K_OK on success, error code on failure
 */
NVJ2K_API int nvj2k_get_device_info(nvj2k_device_info_t* info);

/**
 * Shutdown the nvJPEG2000 wrapper and release all resources.
 * After calling this, nvj2k_init() must be called again before use.
 */
NVJ2K_API void nvj2k_shutdown(void);

/*============================================================================
 * Single-frame decoding
 *============================================================================*/

/**
 * Decode a single JPEG 2000 codestream.
 *
 * @param input      Input compressed data
 * @param input_len  Length of input data in bytes
 * @param output     Output buffer for decoded pixels
 * @param output_len Size of output buffer in bytes
 * @param params     Decode parameters (NULL for defaults)
 * @param result     Output: decode result information
 * @return NVJ2K_OK on success, error code on failure
 */
NVJ2K_API int nvj2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    const nvj2k_decode_params_t* params,
    nvj2k_decode_result_t* result
);

/*============================================================================
 * Batch decoding
 *============================================================================*/

/**
 * Batch decode result for a single frame.
 */
typedef struct nvj2k_batch_result {
    int status;              /* NVJ2K_OK or error code */
    int width;               /* Decoded width */
    int height;              /* Decoded height */
    int num_components;      /* Number of components */
    int precision;           /* Bit depth per component */
    size_t output_size;      /* Actual output size */
} nvj2k_batch_result_t;

/**
 * Decode multiple JPEG 2000 codestreams in batch.
 * More efficient than multiple nvj2k_decode() calls for multi-frame images.
 *
 * @param inputs       Array of input data pointers
 * @param input_lens   Array of input data lengths
 * @param outputs      Array of output buffer pointers
 * @param output_lens  Array of output buffer sizes
 * @param count        Number of frames to decode
 * @param params       Decode parameters (applies to all frames, NULL for defaults)
 * @param results      Array of batch results (must have 'count' elements)
 * @return Number of successfully decoded frames (0 on complete failure)
 */
NVJ2K_API int nvj2k_decode_batch(
    const uint8_t** inputs,
    const size_t* input_lens,
    uint8_t** outputs,
    const size_t* output_lens,
    int count,
    const nvj2k_decode_params_t* params,
    nvj2k_batch_result_t* results
);

/*============================================================================
 * Error handling
 *============================================================================*/

/**
 * Get the last error message for the current thread.
 *
 * @return Error message string, or empty string if no error
 */
NVJ2K_API const char* nvj2k_last_error(void);

/**
 * Clear the last error message for the current thread.
 */
NVJ2K_API void nvj2k_clear_error(void);

#ifdef __cplusplus
}
#endif

#endif /* NVJPEG2K_WRAPPER_H */
