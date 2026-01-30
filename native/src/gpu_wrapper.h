/**
 * GPU Dispatch Wrapper for SharpDicom
 *
 * Provides a unified interface for GPU-accelerated codec operations.
 * Dynamically loads the nvJPEG2000 wrapper at runtime when available,
 * falling back to CPU implementations when GPU is not available.
 *
 * Thread Safety: All functions are thread-safe.
 */

#ifndef GPU_WRAPPER_H
#define GPU_WRAPPER_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * GPU type enumeration
 *============================================================================*/

/**
 * Type of GPU acceleration available.
 */
typedef enum gpu_type {
    GPU_NONE = 0,       /* No GPU acceleration available */
    GPU_NVIDIA = 1,     /* NVIDIA GPU (nvJPEG2000) */
    GPU_OPENCL = 2      /* OpenCL-capable GPU (future) */
} gpu_type_t;

/*============================================================================
 * Error codes (mirrors sharpdicom_codecs.h)
 *============================================================================*/

#define GPU_OK                         0
#define GPU_ERR_INVALID_ARGUMENT      -1
#define GPU_ERR_OUT_OF_MEMORY         -2
#define GPU_ERR_DECODE_FAILED         -3
#define GPU_ERR_ENCODE_FAILED         -4
#define GPU_ERR_NOT_AVAILABLE         -5
#define GPU_ERR_LOAD_FAILED           -6
#define GPU_ERR_INTERNAL              -7

/*============================================================================
 * Decode result structures
 *============================================================================*/

/**
 * Decode result information.
 */
typedef struct gpu_decode_result {
    int width;               /* Decoded image width */
    int height;              /* Decoded image height */
    int num_components;      /* Number of components */
    int precision;           /* Bit depth per component */
    size_t output_size;      /* Size of decoded data in bytes */
} gpu_decode_result_t;

/**
 * Batch decode result for a single frame.
 */
typedef struct gpu_batch_result {
    int status;              /* GPU_OK or error code */
    int width;               /* Decoded width */
    int height;              /* Decoded height */
    int num_components;      /* Number of components */
    int precision;           /* Bit depth per component */
    size_t output_size;      /* Actual output size */
} gpu_batch_result_t;

/*============================================================================
 * GPU availability functions
 *============================================================================*/

/**
 * Check if any GPU acceleration is available.
 *
 * @return 1 if GPU is available, 0 if not
 */
int gpu_available(void);

/**
 * Get the type of GPU acceleration available.
 *
 * @return GPU_NONE, GPU_NVIDIA, or GPU_OPENCL
 */
gpu_type_t gpu_get_type(void);

/**
 * Get the name of the GPU device being used.
 *
 * @param buffer   Buffer to store the device name
 * @param buf_size Size of the buffer
 * @return GPU_OK on success, error code on failure
 */
int gpu_get_device_name(char* buffer, size_t buf_size);

/**
 * Get GPU memory information.
 *
 * @param total_memory Pointer to store total memory (bytes)
 * @param free_memory  Pointer to store free memory (bytes)
 * @return GPU_OK on success, error code on failure
 */
int gpu_get_memory_info(size_t* total_memory, size_t* free_memory);

/*============================================================================
 * GPU preference control
 *============================================================================*/

/**
 * Set thread-local preference to use CPU instead of GPU.
 * Useful for testing or when GPU decode fails.
 *
 * @param prefer_cpu 1 to prefer CPU, 0 to use GPU when available
 */
void gpu_prefer_cpu(int prefer_cpu);

/**
 * Check if current thread prefers CPU.
 *
 * @return 1 if CPU is preferred, 0 if GPU is used when available
 */
int gpu_prefers_cpu(void);

/*============================================================================
 * JPEG 2000 decode functions
 *============================================================================*/

/**
 * Decode a single JPEG 2000 codestream using GPU if available.
 * Falls back to CPU if GPU is not available or prefer_cpu is set.
 *
 * @param input      Input compressed data
 * @param input_len  Length of input data in bytes
 * @param output     Output buffer for decoded pixels
 * @param output_len Size of output buffer in bytes
 * @param result     Output: decode result information
 * @return GPU_OK on success, error code on failure
 */
int gpu_j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    gpu_decode_result_t* result
);

/**
 * Decode multiple JPEG 2000 codestreams in batch.
 * More efficient than multiple gpu_j2k_decode() calls on GPU.
 *
 * @param inputs       Array of input data pointers
 * @param input_lens   Array of input data lengths
 * @param outputs      Array of output buffer pointers
 * @param output_lens  Array of output buffer sizes
 * @param count        Number of frames to decode
 * @param results      Array of batch results (must have 'count' elements)
 * @return Number of successfully decoded frames
 */
int gpu_j2k_decode_batch(
    const uint8_t** inputs,
    const size_t* input_lens,
    uint8_t** outputs,
    const size_t* output_lens,
    int count,
    gpu_batch_result_t* results
);

/*============================================================================
 * Error handling
 *============================================================================*/

/**
 * Get the last GPU error message for the current thread.
 *
 * @return Error message string, or empty string if no error
 */
const char* gpu_last_error(void);

/**
 * Clear the last GPU error message for the current thread.
 */
void gpu_clear_error(void);

#ifdef __cplusplus
}
#endif

#endif /* GPU_WRAPPER_H */
