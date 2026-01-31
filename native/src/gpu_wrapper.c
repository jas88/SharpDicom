/**
 * GPU Dispatch Wrapper Implementation
 *
 * Provides GPU acceleration by dynamically loading the nvJPEG2000 wrapper
 * at runtime. Falls back to CPU implementations when GPU is unavailable.
 */

#include "gpu_wrapper.h"
#include "sharpdicom_codecs.h"

#include <string.h>
#include <stdio.h>
#include <stdlib.h>

/*============================================================================
 * Dynamic library loading
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
    typedef HMODULE lib_handle_t;
    #define LIB_INVALID NULL
    #define load_library(name) LoadLibraryA(name)
    #define close_library(h) FreeLibrary(h)
    #define get_symbol(h, name) GetProcAddress(h, name)
    #define LIB_SUFFIX ".dll"
#else
    #include <dlfcn.h>
    typedef void* lib_handle_t;
    #define LIB_INVALID NULL
    #define load_library(name) dlopen(name, RTLD_NOW | RTLD_LOCAL)
    #define close_library(h) dlclose(h)
    #define get_symbol(h, name) dlsym(h, name)
    #if defined(__APPLE__)
        #define LIB_SUFFIX ".dylib"
    #else
        #define LIB_SUFFIX ".so"
    #endif
#endif

/*============================================================================
 * Thread-local error storage
 *============================================================================*/

/* Thread-local storage: use __declspec(thread) only for actual MSVC */
#if defined(_MSC_VER)
    #define THREAD_LOCAL __declspec(thread)
#else
    #define THREAD_LOCAL __thread
#endif

static THREAD_LOCAL char tls_error[256] = {0};
static THREAD_LOCAL int tls_prefer_cpu = 0;

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

/*============================================================================
 * nvJPEG2000 wrapper function pointers
 *============================================================================*/

/* Types matching nvjpeg2k_wrapper.h */
typedef struct {
    int device_id;
    int compute_major;
    int compute_minor;
    size_t total_memory;
    size_t free_memory;
    char name[256];
} nvj2k_device_info_t;

typedef struct {
    int reduce_factor;
    int num_components;
    int precision;
} nvj2k_decode_params_t;

typedef struct {
    int width;
    int height;
    int num_components;
    int precision;
    size_t output_size;
} nvj2k_decode_result_t;

typedef struct {
    int status;
    int width;
    int height;
    int num_components;
    int precision;
    size_t output_size;
} nvj2k_batch_result_t;

/* Function pointer types */
typedef int (*pfn_nvj2k_available)(void);
typedef int (*pfn_nvj2k_init)(int device_id);
typedef int (*pfn_nvj2k_get_device_info)(nvj2k_device_info_t* info);
typedef void (*pfn_nvj2k_shutdown)(void);
typedef int (*pfn_nvj2k_decode)(
    const uint8_t* input, size_t input_len,
    uint8_t* output, size_t output_len,
    const nvj2k_decode_params_t* params,
    nvj2k_decode_result_t* result
);
typedef int (*pfn_nvj2k_decode_batch)(
    const uint8_t** inputs, const size_t* input_lens,
    uint8_t** outputs, const size_t* output_lens,
    int count,
    const nvj2k_decode_params_t* params,
    nvj2k_batch_result_t* results
);
typedef const char* (*pfn_nvj2k_last_error)(void);
typedef void (*pfn_nvj2k_clear_error)(void);

/* Global state */
static lib_handle_t g_nvj2k_lib = LIB_INVALID;
static volatile int g_nvj2k_loaded = 0;
static volatile int g_nvj2k_available = 0;
static nvj2k_device_info_t g_device_info = {0};

/* Function pointers */
static pfn_nvj2k_available fn_nvj2k_available = NULL;
static pfn_nvj2k_init fn_nvj2k_init = NULL;
static pfn_nvj2k_get_device_info fn_nvj2k_get_device_info = NULL;
static pfn_nvj2k_shutdown fn_nvj2k_shutdown = NULL;
static pfn_nvj2k_decode fn_nvj2k_decode = NULL;
static pfn_nvj2k_decode_batch fn_nvj2k_decode_batch = NULL;
static pfn_nvj2k_last_error fn_nvj2k_last_error = NULL;
static pfn_nvj2k_clear_error fn_nvj2k_clear_error = NULL;

/*============================================================================
 * Lock for thread-safe initialization
 *============================================================================*/

#if defined(_WIN32) || defined(_WIN64)
static CRITICAL_SECTION g_lock;
static volatile LONG g_lock_init = 0;

static void init_lock(void) {
    /* Thread-safe one-time initialization using InterlockedCompareExchange.
     * State machine: 0 = uninitialized, 1 = initializing, 2 = initialized */
    if (g_lock_init == 2) return; /* Fast path: already initialized */

    LONG state = InterlockedCompareExchange(&g_lock_init, 1, 0);
    if (state == 0) {
        /* We won the race - initialize the critical section */
        InitializeCriticalSection(&g_lock);
        InterlockedExchange(&g_lock_init, 2);
    } else if (state == 1) {
        /* Another thread is initializing - spin until complete */
        while (g_lock_init != 2) {
            SwitchToThread(); /* Yield to other threads */
        }
    }
    /* state == 2 means already initialized, nothing to do */
}

static void lock(void) {
    init_lock();
    EnterCriticalSection(&g_lock);
}

static void unlock(void) {
    LeaveCriticalSection(&g_lock);
}
#else
#include <pthread.h>
static pthread_mutex_t g_lock = PTHREAD_MUTEX_INITIALIZER;

static void lock(void) {
    pthread_mutex_lock(&g_lock);
}

static void unlock(void) {
    pthread_mutex_unlock(&g_lock);
}
#endif

/*============================================================================
 * Library loading
 *============================================================================*/

/**
 * Try to load nvJPEG2000 wrapper library.
 * Searches in several locations.
 */
static int try_load_nvj2k(void) {
    const char* lib_names[] = {
        "nvjpeg2k_wrapper" LIB_SUFFIX,          /* Same directory */
        "./nvjpeg2k_wrapper" LIB_SUFFIX,        /* Explicit current dir */
#if defined(__linux__)
        "/usr/local/lib/nvjpeg2k_wrapper.so",
        "/usr/lib/nvjpeg2k_wrapper.so",
#elif defined(__APPLE__)
        "/usr/local/lib/nvjpeg2k_wrapper.dylib",
#endif
        NULL
    };

    for (int i = 0; lib_names[i] != NULL; i++) {
        g_nvj2k_lib = load_library(lib_names[i]);
        if (g_nvj2k_lib != LIB_INVALID) {
            return 1;
        }
    }

    return 0;
}

/**
 * Load function pointers from the library.
 */
static int load_nvj2k_functions(void) {
    #define LOAD_FUNC(name) \
        fn_##name = (pfn_##name)get_symbol(g_nvj2k_lib, #name); \
        if (!fn_##name) return 0;

    LOAD_FUNC(nvj2k_available)
    LOAD_FUNC(nvj2k_init)
    LOAD_FUNC(nvj2k_get_device_info)
    LOAD_FUNC(nvj2k_shutdown)
    LOAD_FUNC(nvj2k_decode)
    LOAD_FUNC(nvj2k_decode_batch)
    LOAD_FUNC(nvj2k_last_error)
    LOAD_FUNC(nvj2k_clear_error)

    #undef LOAD_FUNC

    return 1;
}

/**
 * Initialize nvJPEG2000 wrapper.
 * Thread-safe, lazy initialization.
 */
static void ensure_nvj2k_loaded(void) {
    if (g_nvj2k_loaded) return;

    lock();

    /* Double-check after lock */
    if (g_nvj2k_loaded) {
        unlock();
        return;
    }

    g_nvj2k_loaded = 1; /* Mark as loaded (even if failed) to prevent retry */
    g_nvj2k_available = 0;

    /* Try to load library */
    if (!try_load_nvj2k()) {
        unlock();
        return;
    }

    /* Load functions */
    if (!load_nvj2k_functions()) {
        close_library(g_nvj2k_lib);
        g_nvj2k_lib = LIB_INVALID;
        unlock();
        return;
    }

    /* Check if GPU is actually available */
    if (!fn_nvj2k_available()) {
        close_library(g_nvj2k_lib);
        g_nvj2k_lib = LIB_INVALID;
        unlock();
        return;
    }

    /* Initialize nvJPEG2000 */
    if (fn_nvj2k_init(-1) != 0) {
        close_library(g_nvj2k_lib);
        g_nvj2k_lib = LIB_INVALID;
        unlock();
        return;
    }

    /* Get device info */
    fn_nvj2k_get_device_info(&g_device_info);

    g_nvj2k_available = 1;
    unlock();
}

/*============================================================================
 * Forward declarations for CPU fallback (declared in j2k_wrapper.h)
 *============================================================================*/

/* CPU JPEG 2000 decode - will be implemented in j2k_wrapper.c */
extern int j2k_decode(
    const uint8_t* input, size_t input_len,
    uint8_t* output, size_t output_len,
    int* width, int* height, int* components
);

/*============================================================================
 * Public API implementation
 *============================================================================*/

int gpu_available(void) {
    ensure_nvj2k_loaded();
    return g_nvj2k_available ? 1 : 0;
}

gpu_type_t gpu_get_type(void) {
    ensure_nvj2k_loaded();

    if (g_nvj2k_available) {
        return GPU_NVIDIA;
    }

    /* Future: Check for OpenCL */

    return GPU_NONE;
}

int gpu_get_device_name(char* buffer, size_t buf_size) {
    if (!buffer || buf_size == 0) {
        set_error("Invalid buffer");
        return GPU_ERR_INVALID_ARGUMENT;
    }

    ensure_nvj2k_loaded();

    if (!g_nvj2k_available) {
        set_error("No GPU available");
        return GPU_ERR_NOT_AVAILABLE;
    }

    size_t len = strlen(g_device_info.name);
    if (len >= buf_size) len = buf_size - 1;
    memcpy(buffer, g_device_info.name, len);
    buffer[len] = '\0';

    return GPU_OK;
}

int gpu_get_memory_info(size_t* total_memory, size_t* free_memory) {
    ensure_nvj2k_loaded();

    if (!g_nvj2k_available) {
        set_error("No GPU available");
        return GPU_ERR_NOT_AVAILABLE;
    }

    /* Refresh device info */
    if (fn_nvj2k_get_device_info) {
        fn_nvj2k_get_device_info(&g_device_info);
    }

    if (total_memory) *total_memory = g_device_info.total_memory;
    if (free_memory) *free_memory = g_device_info.free_memory;

    return GPU_OK;
}

void gpu_prefer_cpu(int prefer_cpu) {
    tls_prefer_cpu = prefer_cpu ? 1 : 0;
}

int gpu_prefers_cpu(void) {
    return tls_prefer_cpu;
}

int gpu_j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    gpu_decode_result_t* result
) {
    if (!input || input_len == 0) {
        set_error("Input is NULL or empty");
        return GPU_ERR_INVALID_ARGUMENT;
    }

    if (!output || output_len == 0) {
        set_error("Output is NULL or empty");
        return GPU_ERR_INVALID_ARGUMENT;
    }

    ensure_nvj2k_loaded();

    /* Use GPU if available and not preferring CPU */
    if (g_nvj2k_available && !tls_prefer_cpu && fn_nvj2k_decode) {
        nvj2k_decode_result_t nvj2k_result;
        int status = fn_nvj2k_decode(
            input, input_len,
            output, output_len,
            NULL, /* Use defaults */
            &nvj2k_result
        );

        if (status == 0) {
            if (result) {
                result->width = nvj2k_result.width;
                result->height = nvj2k_result.height;
                result->num_components = nvj2k_result.num_components;
                result->precision = nvj2k_result.precision;
                result->output_size = nvj2k_result.output_size;
            }
            return GPU_OK;
        }

        /* GPU decode failed - copy error and fall back to CPU */
        if (fn_nvj2k_last_error) {
            set_error(fn_nvj2k_last_error());
        }
    }

    /* CPU fallback - use OpenJPEG j2k_decode */
    int width = 0, height = 0, components = 0;
    int status = j2k_decode(input, input_len, output, output_len,
                            &width, &height, &components);

    if (status == 0) {
        if (result) {
            result->width = width;
            result->height = height;
            result->num_components = components;
            result->precision = 8; /* Default, may need refinement */
            result->output_size = safe_mul3_size((size_t)width, (size_t)height, (size_t)components);
        }
        return GPU_OK;
    }

    set_error(sharpdicom_last_error());
    return GPU_ERR_DECODE_FAILED;
}

int gpu_j2k_decode_batch(
    const uint8_t** inputs,
    const size_t* input_lens,
    uint8_t** outputs,
    const size_t* output_lens,
    int count,
    gpu_batch_result_t* results
) {
    if (!inputs || !input_lens || !outputs || !output_lens || !results) {
        set_error("NULL parameter");
        return 0;
    }

    if (count <= 0) {
        set_error("Count must be positive");
        return 0;
    }

    ensure_nvj2k_loaded();

    /* Use GPU batch decode if available */
    if (g_nvj2k_available && !tls_prefer_cpu && fn_nvj2k_decode_batch) {
        nvj2k_batch_result_t* nvj2k_results = (nvj2k_batch_result_t*)
            malloc(count * sizeof(nvj2k_batch_result_t));

        if (!nvj2k_results) {
            set_error("Memory allocation failed");
            for (int i = 0; i < count; i++) {
                results[i].status = GPU_ERR_OUT_OF_MEMORY;
            }
            return 0;
        }

        int success = fn_nvj2k_decode_batch(
            inputs, input_lens,
            outputs, output_lens,
            count, NULL, nvj2k_results
        );

        /* Copy results */
        for (int i = 0; i < count; i++) {
            results[i].status = nvj2k_results[i].status;
            results[i].width = nvj2k_results[i].width;
            results[i].height = nvj2k_results[i].height;
            results[i].num_components = nvj2k_results[i].num_components;
            results[i].precision = nvj2k_results[i].precision;
            results[i].output_size = nvj2k_results[i].output_size;
        }

        free(nvj2k_results);

        if (success > 0) {
            return success;
        }

        /* All failed on GPU, fall through to CPU */
    }

    /* CPU fallback - decode one by one */
    int success_count = 0;

    for (int i = 0; i < count; i++) {
        gpu_decode_result_t single_result;
        int status = gpu_j2k_decode(
            inputs[i], input_lens[i],
            outputs[i], output_lens[i],
            &single_result
        );

        results[i].status = status;
        if (status == GPU_OK) {
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
}

const char* gpu_last_error(void) {
    return tls_error;
}

void gpu_clear_error(void) {
    tls_error[0] = '\0';
}
