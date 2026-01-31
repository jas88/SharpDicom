/**
 * nvJPEG2000 Wrapper Implementation
 *
 * This file provides a C wrapper around NVIDIA's nvJPEG2000 library for
 * GPU-accelerated JPEG 2000 decoding. It manages CUDA contexts, streams,
 * and nvJPEG2000 handles with thread-safe initialization.
 *
 * Build: nvcc -shared -o nvjpeg2k_wrapper.so nvjpeg2k_wrapper.c -lnvjpeg2k -lcudart
 */

#define NVJPEG2K_WRAPPER_EXPORTS
#include "nvjpeg2k_wrapper.h"
#include "../src/sharpdicom_codecs.h"

#include <string.h>
#include <stdio.h>
#include <stdlib.h>

/*============================================================================
 * CUDA and nvJPEG2000 includes
 * These are guarded by HAVE_NVJPEG2K to allow building without CUDA
 *============================================================================*/

#ifdef HAVE_NVJPEG2K
#include <cuda_runtime.h>
#include <nvjpeg2k.h>
#endif

/*============================================================================
 * Thread-local error storage
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
    #define THREAD_LOCAL __declspec(thread)
#else
    #define THREAD_LOCAL __thread
#endif

static THREAD_LOCAL char tls_error[256] = {0};

static void set_error(const char* msg) {
    if (msg) {
        size_t len = strlen(msg);
        if (len >= sizeof(tls_error)) len = sizeof(tls_error) - 1;
        memcpy(tls_error, msg, len);
        tls_error[len] = '\0';
    } else {
        tls_error[0] = '\0';
    }
}

static void set_error_fmt(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vsnprintf(tls_error, sizeof(tls_error), fmt, args);
    va_end(args);
}

/*============================================================================
 * Global state (protected by mutex/critical section)
 *============================================================================*/

#ifdef HAVE_NVJPEG2K

/* Initialization state */
static volatile int g_initialized = 0;
static int g_device_id = -1;

/* CUDA objects */
static cudaStream_t g_stream = NULL;

/* nvJPEG2000 handles */
static nvjpeg2kHandle_t g_handle = NULL;
static nvjpeg2kDecodeState_t g_state = NULL;

/* Device info cache */
static nvj2k_device_info_t g_device_info = {0};

/* Mutex for thread-safe initialization */
#if defined(_WIN32) || defined(_WIN64)
#include <windows.h>
static CRITICAL_SECTION g_init_lock;
static volatile int g_lock_initialized = 0;

static void init_lock(void) {
    if (!g_lock_initialized) {
        InitializeCriticalSection(&g_init_lock);
        g_lock_initialized = 1;
    }
}

static void lock(void) {
    init_lock();
    EnterCriticalSection(&g_init_lock);
}

static void unlock(void) {
    LeaveCriticalSection(&g_init_lock);
}

#else
#include <pthread.h>
static pthread_mutex_t g_init_lock = PTHREAD_MUTEX_INITIALIZER;

static void lock(void) {
    pthread_mutex_lock(&g_init_lock);
}

static void unlock(void) {
    pthread_mutex_unlock(&g_init_lock);
}
#endif

/*============================================================================
 * Helper macros for CUDA/nvJPEG2000 error checking
 *============================================================================*/

#define CUDA_CHECK(call, error_code, cleanup) do { \
    cudaError_t err = (call); \
    if (err != cudaSuccess) { \
        set_error_fmt("CUDA error: %s", cudaGetErrorString(err)); \
        cleanup; \
        return error_code; \
    } \
} while(0)

#define NVJ2K_CHECK(call, error_code, cleanup) do { \
    nvjpeg2kStatus_t status = (call); \
    if (status != NVJPEG2K_STATUS_SUCCESS) { \
        set_error_fmt("nvJPEG2000 error: %d", (int)status); \
        cleanup; \
        return error_code; \
    } \
} while(0)

/*============================================================================
 * Internal helpers
 *============================================================================*/

/**
 * Check if a CUDA device has sufficient compute capability.
 * Requires compute capability 5.0 (Maxwell) or higher.
 */
static int check_compute_capability(int device_id) {
    int major = 0, minor = 0;
    cudaDeviceGetAttribute(&major, cudaDevAttrComputeCapabilityMajor, device_id);
    cudaDeviceGetAttribute(&minor, cudaDevAttrComputeCapabilityMinor, device_id);
    return (major > 5) || (major == 5 && minor >= 0);
}

/**
 * Find a suitable CUDA device.
 * Returns device ID or -1 if none found.
 */
static int find_suitable_device(void) {
    int device_count = 0;
    if (cudaGetDeviceCount(&device_count) != cudaSuccess || device_count == 0) {
        return -1;
    }

    /* Find first device with compute capability >= 5.0 */
    for (int i = 0; i < device_count; i++) {
        if (check_compute_capability(i)) {
            return i;
        }
    }

    return -1;
}

/**
 * Fill device info structure for the current device.
 */
static void fill_device_info(int device_id, nvj2k_device_info_t* info) {
    memset(info, 0, sizeof(*info));
    info->device_id = device_id;

    cudaDeviceProp prop;
    if (cudaGetDeviceProperties(&prop, device_id) == cudaSuccess) {
        info->compute_major = prop.major;
        info->compute_minor = prop.minor;
        info->total_memory = prop.totalGlobalMem;
        strncpy(info->name, prop.name, sizeof(info->name) - 1);
    }

    size_t free_mem = 0, total_mem = 0;
    if (cudaMemGetInfo(&free_mem, &total_mem) == cudaSuccess) {
        info->free_memory = free_mem;
    }
}

#endif /* HAVE_NVJPEG2K */

/*============================================================================
 * Public API implementation
 *============================================================================*/

NVJ2K_API int nvj2k_available(void) {
#ifdef HAVE_NVJPEG2K
    /* Check if CUDA driver/runtime is available */
    int device_count = 0;
    cudaError_t err = cudaGetDeviceCount(&device_count);

    /* Reset CUDA error state */
    cudaGetLastError();

    if (err != cudaSuccess || device_count == 0) {
        return 0;
    }

    /* Check for suitable device */
    return find_suitable_device() >= 0 ? 1 : 0;
#else
    return 0;
#endif
}

NVJ2K_API int nvj2k_init(int device_id) {
#ifdef HAVE_NVJPEG2K
    lock();

    /* Already initialized? */
    if (g_initialized) {
        unlock();
        set_error("Already initialized. Call nvj2k_shutdown() first.");
        return NVJ2K_ERR_INVALID_ARGUMENT;
    }

    /* Find device */
    int selected_device = device_id;
    if (selected_device < 0) {
        selected_device = find_suitable_device();
        if (selected_device < 0) {
            unlock();
            set_error("No suitable CUDA device found (requires compute 5.0+)");
            return NVJ2K_ERR_NO_DEVICE;
        }
    }

    /* Verify device has required compute capability */
    if (!check_compute_capability(selected_device)) {
        unlock();
        set_error("GPU does not meet minimum compute capability (5.0+)");
        return NVJ2K_ERR_UNSUPPORTED_GPU;
    }

    /* Set device */
    cudaError_t cuda_err = cudaSetDevice(selected_device);
    if (cuda_err != cudaSuccess) {
        unlock();
        set_error_fmt("Failed to set CUDA device: %s", cudaGetErrorString(cuda_err));
        return NVJ2K_ERR_CUDA_ERROR;
    }

    /* Create CUDA stream */
    cuda_err = cudaStreamCreate(&g_stream);
    if (cuda_err != cudaSuccess) {
        unlock();
        set_error_fmt("Failed to create CUDA stream: %s", cudaGetErrorString(cuda_err));
        return NVJ2K_ERR_CUDA_ERROR;
    }

    /* Create nvJPEG2000 handle */
    nvjpeg2kStatus_t nv_status = nvjpeg2kCreate(NVJPEG2K_BACKEND_DEFAULT, NULL, &g_handle);
    if (nv_status != NVJPEG2K_STATUS_SUCCESS) {
        cudaStreamDestroy(g_stream);
        g_stream = NULL;
        unlock();
        set_error_fmt("Failed to create nvJPEG2000 handle: %d", (int)nv_status);
        return NVJ2K_ERR_INTERNAL;
    }

    /* Create decode state */
    nv_status = nvjpeg2kDecodeStateCreate(g_handle, &g_state);
    if (nv_status != NVJPEG2K_STATUS_SUCCESS) {
        nvjpeg2kDestroy(g_handle);
        g_handle = NULL;
        cudaStreamDestroy(g_stream);
        g_stream = NULL;
        unlock();
        set_error_fmt("Failed to create decode state: %d", (int)nv_status);
        return NVJ2K_ERR_INTERNAL;
    }

    /* Cache device info */
    g_device_id = selected_device;
    fill_device_info(selected_device, &g_device_info);

    g_initialized = 1;
    unlock();
    return NVJ2K_OK;

#else
    (void)device_id;
    set_error("nvJPEG2000 support not compiled in");
    return NVJ2K_ERR_UNSUPPORTED_GPU;
#endif
}

NVJ2K_API int nvj2k_get_device_info(nvj2k_device_info_t* info) {
#ifdef HAVE_NVJPEG2K
    if (!info) {
        set_error("info parameter is NULL");
        return NVJ2K_ERR_INVALID_ARGUMENT;
    }

    if (!g_initialized) {
        set_error("Not initialized. Call nvj2k_init() first.");
        return NVJ2K_ERR_NOT_INITIALIZED;
    }

    *info = g_device_info;
    return NVJ2K_OK;
#else
    (void)info;
    set_error("nvJPEG2000 support not compiled in");
    return NVJ2K_ERR_UNSUPPORTED_GPU;
#endif
}

NVJ2K_API void nvj2k_shutdown(void) {
#ifdef HAVE_NVJPEG2K
    lock();

    if (g_state) {
        nvjpeg2kDecodeStateDestroy(g_state);
        g_state = NULL;
    }

    if (g_handle) {
        nvjpeg2kDestroy(g_handle);
        g_handle = NULL;
    }

    if (g_stream) {
        cudaStreamDestroy(g_stream);
        g_stream = NULL;
    }

    g_device_id = -1;
    g_initialized = 0;
    memset(&g_device_info, 0, sizeof(g_device_info));

    unlock();
#endif
}

NVJ2K_API int nvj2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    const nvj2k_decode_params_t* params,
    nvj2k_decode_result_t* result
) {
#ifdef HAVE_NVJPEG2K
    if (!input || input_len == 0) {
        set_error("input is NULL or empty");
        return NVJ2K_ERR_INVALID_ARGUMENT;
    }

    if (!output || output_len == 0) {
        set_error("output is NULL or empty");
        return NVJ2K_ERR_INVALID_ARGUMENT;
    }

    if (!g_initialized) {
        set_error("Not initialized. Call nvj2k_init() first.");
        return NVJ2K_ERR_NOT_INITIALIZED;
    }

    nvjpeg2kStatus_t status;

    /* Create stream for parsing */
    nvjpeg2kStream_t j2k_stream = NULL;
    status = nvjpeg2kStreamCreate(&j2k_stream);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        set_error_fmt("Failed to create J2K stream: %d", (int)status);
        return NVJ2K_ERR_INTERNAL;
    }

    /* Parse the codestream */
    status = nvjpeg2kStreamParse(g_handle, input, input_len, 0, 0, j2k_stream);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Failed to parse J2K codestream: %d", (int)status);
        return NVJ2K_ERR_DECODE_FAILED;
    }

    /* Get image info */
    nvjpeg2kImageInfo_t image_info;
    status = nvjpeg2kStreamGetImageInfo(j2k_stream, &image_info);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Failed to get image info: %d", (int)status);
        return NVJ2K_ERR_DECODE_FAILED;
    }

    /* Apply reduction factor if specified */
    int reduce = (params && params->reduce_factor > 0) ? params->reduce_factor : 0;
    uint32_t decode_width = image_info.image_width >> reduce;
    uint32_t decode_height = image_info.image_height >> reduce;
    if (decode_width == 0) decode_width = 1;
    if (decode_height == 0) decode_height = 1;

    /* Get component info for first component (assume all same) */
    nvjpeg2kImageComponentInfo_t comp_info;
    status = nvjpeg2kStreamGetImageComponentInfo(j2k_stream, &comp_info, 0);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Failed to get component info: %d", (int)status);
        return NVJ2K_ERR_DECODE_FAILED;
    }

    /* Calculate expected output size (with overflow protection) */
    int bytes_per_sample = (comp_info.precision + 7) / 8;
    size_t expected_size = safe_mul4_size((size_t)decode_width, (size_t)decode_height,
                                          (size_t)image_info.num_components, (size_t)bytes_per_sample);

    if (expected_size == 0 || output_len < expected_size) {
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Output buffer too small or dimensions too large: need %zu, got %zu", expected_size, output_len);
        return NVJ2K_ERR_INVALID_ARGUMENT;
    }

    /* Allocate device memory */
    uint8_t* d_output = NULL;
    cudaError_t cuda_err = cudaMalloc(&d_output, expected_size);
    if (cuda_err != cudaSuccess) {
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Failed to allocate GPU memory: %s", cudaGetErrorString(cuda_err));
        return NVJ2K_ERR_OUT_OF_MEMORY;
    }

    /* Set up decode parameters */
    nvjpeg2kDecodeParams_t decode_params;
    status = nvjpeg2kDecodeParamsCreate(&decode_params);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        cudaFree(d_output);
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Failed to create decode params: %d", (int)status);
        return NVJ2K_ERR_INTERNAL;
    }

    /* Configure output image */
    nvjpeg2kImage_t output_image;
    output_image.num_components = image_info.num_components;

    /* For interleaved output, we use a single buffer */
    size_t comp_size = safe_mul3_size((size_t)decode_width, (size_t)decode_height, (size_t)bytes_per_sample);
    size_t pitch = safe_mul_size((size_t)decode_width, (size_t)bytes_per_sample);
    for (uint32_t c = 0; c < image_info.num_components && c < NVJPEG2K_MAX_COMPONENT; c++) {
        output_image.pixel_data[c] = d_output + c * comp_size;
        output_image.pitch_in_bytes[c] = pitch;
        output_image.pixel_type = (bytes_per_sample == 1) ? NVJPEG2K_UINT8 :
                                   (bytes_per_sample == 2) ? NVJPEG2K_UINT16 : NVJPEG2K_UINT8;
    }

    /* Decode */
    status = nvjpeg2kDecode(g_handle, g_state, j2k_stream, decode_params, &output_image, g_stream);
    if (status != NVJPEG2K_STATUS_SUCCESS) {
        nvjpeg2kDecodeParamsDestroy(decode_params);
        cudaFree(d_output);
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Decode failed: %d", (int)status);
        return NVJ2K_ERR_DECODE_FAILED;
    }

    /* Synchronize and copy to host */
    cuda_err = cudaStreamSynchronize(g_stream);
    if (cuda_err != cudaSuccess) {
        nvjpeg2kDecodeParamsDestroy(decode_params);
        cudaFree(d_output);
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("Stream sync failed: %s", cudaGetErrorString(cuda_err));
        return NVJ2K_ERR_CUDA_ERROR;
    }

    cuda_err = cudaMemcpy(output, d_output, expected_size, cudaMemcpyDeviceToHost);
    if (cuda_err != cudaSuccess) {
        nvjpeg2kDecodeParamsDestroy(decode_params);
        cudaFree(d_output);
        nvjpeg2kStreamDestroy(j2k_stream);
        set_error_fmt("GPU->CPU copy failed: %s", cudaGetErrorString(cuda_err));
        return NVJ2K_ERR_CUDA_ERROR;
    }

    /* Fill result */
    if (result) {
        result->width = (int)decode_width;
        result->height = (int)decode_height;
        result->num_components = (int)image_info.num_components;
        result->precision = (int)comp_info.precision;
        result->output_size = expected_size;
    }

    /* Cleanup */
    nvjpeg2kDecodeParamsDestroy(decode_params);
    cudaFree(d_output);
    nvjpeg2kStreamDestroy(j2k_stream);

    return NVJ2K_OK;

#else
    (void)input;
    (void)input_len;
    (void)output;
    (void)output_len;
    (void)params;
    (void)result;
    set_error("nvJPEG2000 support not compiled in");
    return NVJ2K_ERR_UNSUPPORTED_GPU;
#endif
}

NVJ2K_API int nvj2k_decode_batch(
    const uint8_t** inputs,
    const size_t* input_lens,
    uint8_t** outputs,
    const size_t* output_lens,
    int count,
    const nvj2k_decode_params_t* params,
    nvj2k_batch_result_t* results
) {
#ifdef HAVE_NVJPEG2K
    if (!inputs || !input_lens || !outputs || !output_lens || !results) {
        set_error("NULL parameter passed to batch decode");
        return 0;
    }

    if (count <= 0) {
        set_error("count must be positive");
        return 0;
    }

    if (!g_initialized) {
        set_error("Not initialized. Call nvj2k_init() first.");
        /* Mark all as failed */
        for (int i = 0; i < count; i++) {
            results[i].status = NVJ2K_ERR_NOT_INITIALIZED;
        }
        return 0;
    }

    int success_count = 0;

    /* Decode each frame
     * Note: nvJPEG2000 batch API could be used here for even better
     * performance, but for simplicity we decode sequentially with
     * the same CUDA stream for GPU parallelism.
     */
    for (int i = 0; i < count; i++) {
        nvj2k_decode_result_t single_result;
        int status = nvj2k_decode(
            inputs[i],
            input_lens[i],
            outputs[i],
            output_lens[i],
            params,
            &single_result
        );

        results[i].status = status;
        if (status == NVJ2K_OK) {
            results[i].width = single_result.width;
            results[i].height = single_result.height;
            results[i].num_components = single_result.num_components;
            results[i].precision = single_result.precision;
            results[i].output_size = single_result.output_size;
            success_count++;
        } else {
            results[i].width = 0;
            results[i].height = 0;
            results[i].num_components = 0;
            results[i].precision = 0;
            results[i].output_size = 0;
        }
    }

    return success_count;

#else
    (void)inputs;
    (void)input_lens;
    (void)outputs;
    (void)output_lens;
    (void)count;
    (void)params;
    (void)results;
    set_error("nvJPEG2000 support not compiled in");
    return 0;
#endif
}

NVJ2K_API const char* nvj2k_last_error(void) {
    return tls_error;
}

NVJ2K_API void nvj2k_clear_error(void) {
    tls_error[0] = '\0';
}
