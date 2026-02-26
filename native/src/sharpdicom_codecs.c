/**
 * SharpDicom Native Codecs - Core Implementation
 *
 * Provides version detection, feature detection, SIMD capability detection,
 * GPU dispatch, and thread-local error message handling.
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "sharpdicom_codecs.h"
#include "gpu_wrapper.h"

#include <string.h>
#include <stdio.h>

/*============================================================================
 * musl libc initialization for -nostdlib builds (Linux only)
 *
 * This .so is built with -nostdlib, statically linking musl libc.a. The .so
 * is loaded by .NET on glibc-based Linux. musl and glibc coexist in the same
 * process: glibc owns the C runtime (thread pointer, dynamic linker), while
 * musl provides libc to our statically-linked codec code.
 *
 * musl's startup code (__libc_start_main) never runs, so we must initialize
 * the musl globals that its subsystems need:
 *   - libc.auxv     : auxiliary vector pointer (needed by malloc's RNG)
 *   - libc.page_size: page size (needed by malloc's arena logic)
 *
 * We also stub functions that access musl-internal state incompatible with
 * running inside a glibc process:
 *   - __dso_handle  : normally in crtbeginS.o (which we don't link)
 *   - __cxa_atexit  : no-op (DSO is never dlclose'd, destructors unneeded)
 *   - dl_iterate_phdr: musl's version accesses musl's dynamic linker state
 *
 * These .o files are linked with --whole-archive BEFORE the --start-group
 * containing libc.a, so our definitions take precedence over musl's.
 *============================================================================*/
#if defined(__linux__) && !defined(_WIN32) && !defined(__APPLE__)

#include <sys/syscall.h>

/* Mirror of musl's struct __libc (from src/internal/libc.h, musl 1.2.5).
 * Only the fields we need to initialize are listed; the rest are padding. */
struct musl_libc {
    char can_do_threads;
    char threaded;
    char secure;
    volatile signed char need_locks;
    int threads_minus_1;
    size_t *auxv;               /* offset 8 on 64-bit */
    void *tls_head;
    size_t tls_size, tls_align, tls_cnt;
    size_t page_size;           /* offset 48 on 64-bit */
};
extern struct musl_libc __libc;

/* Raw syscall wrappers — avoid calling any libc during early init */
static long raw_open(const char *path, int flags)
{
#ifdef __x86_64__
    long ret;
    __asm__ volatile("syscall"
        : "=a"(ret)
        : "a"((long)SYS_openat), "D"((long)-100/*AT_FDCWD*/), "S"((long)path),
          "d"((long)flags)
        : "rcx", "r11", "memory");
    return ret;
#elif defined(__aarch64__)
    register long x8 __asm__("x8") = SYS_openat;
    register long x0 __asm__("x0") = -100; /* AT_FDCWD */
    register long x1 __asm__("x1") = (long)path;
    register long x2 __asm__("x2") = flags;
    __asm__ volatile("svc 0" : "+r"(x0) : "r"(x1), "r"(x2), "r"(x8) : "memory");
    return x0;
#endif
}

static long raw_read(int fd, void *buf, size_t count)
{
#ifdef __x86_64__
    long ret;
    __asm__ volatile("syscall"
        : "=a"(ret)
        : "a"((long)SYS_read), "D"((long)fd), "S"((long)buf), "d"((long)count)
        : "rcx", "r11", "memory");
    return ret;
#elif defined(__aarch64__)
    register long x8 __asm__("x8") = SYS_read;
    register long x0 __asm__("x0") = fd;
    register long x1 __asm__("x1") = (long)buf;
    register long x2 __asm__("x2") = count;
    __asm__ volatile("svc 0" : "+r"(x0) : "r"(x1), "r"(x2), "r"(x8) : "memory");
    return x0;
#endif
}

static long raw_close(int fd)
{
#ifdef __x86_64__
    long ret;
    __asm__ volatile("syscall"
        : "=a"(ret)
        : "a"((long)SYS_close), "D"((long)fd)
        : "rcx", "r11", "memory");
    return ret;
#elif defined(__aarch64__)
    register long x8 __asm__("x8") = SYS_close;
    register long x0 __asm__("x0") = fd;
    __asm__ volatile("svc 0" : "+r"(x0) : "r"(x8) : "memory");
    return x0;
#endif
}

/* Buffer for the auxiliary vector — static so it persists after init */
#define AUXV_BUF_SIZE 128
static unsigned long auxv_buf[AUXV_BUF_SIZE];

/**
 * Initialize musl's internal state before any other constructors run.
 * Priority 101 = earliest user constructor (100 is reserved by the compiler).
 * Reads /proc/self/auxv via raw syscalls to avoid any libc dependency.
 */
__attribute__((constructor(101)))
static void init_musl_libc(void)
{
    int fd = (int)raw_open("/proc/self/auxv", 0 /* O_RDONLY */);
    if (fd < 0) {
        /* Fallback: set page_size to 4096 (correct for x86_64/aarch64) */
        __libc.page_size = 4096;
        return;
    }

    long n = raw_read(fd, auxv_buf, sizeof(auxv_buf));
    raw_close(fd);

    if (n > 0) {
        __libc.auxv = auxv_buf;

        /* Parse AT_PAGESZ from the auxiliary vector.
         * Bound the loop by the number of entries actually read, in case
         * the read filled the entire buffer and the zero sentinel was lost. */
        int nentries = (int)((unsigned long)n / sizeof(unsigned long));
        for (int i = 0; i + 1 < nentries && auxv_buf[i] != 0; i += 2) {
            if (auxv_buf[i] == 6 /* AT_PAGESZ */) {
                __libc.page_size = auxv_buf[i + 1];
                break;
            }
        }
    }

    if (__libc.page_size == 0)
        __libc.page_size = 4096;
}

/* --- ABI stubs --- */

void* __dso_handle __attribute__((visibility("hidden"))) = (void*)&__dso_handle;

int __attribute__((visibility("hidden")))
__cxa_atexit(void (*func)(void *), void *arg, void *dso)
{
    (void)func; (void)arg; (void)dso;
    return 0;
}

/**
 * Stub dl_iterate_phdr — musl's version accesses musl's dynamic linker
 * internals which don't exist in a glibc host process.
 */
struct dl_phdr_info;
int dl_iterate_phdr(int (*callback)(struct dl_phdr_info *, size_t, void *),
                    void *data)
{
    (void)callback; (void)data;
    return 0;
}

#endif

/*============================================================================
 * Platform detection
 *============================================================================*/

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
    #define SHARPDICOM_ARCH_X86 1
#elif defined(__aarch64__) || defined(_M_ARM64)
    #define SHARPDICOM_ARCH_ARM64 1
#endif

/*============================================================================
 * Thread-local error message storage
 *============================================================================*/

/* Thread-local storage: use __declspec(thread) only for actual MSVC */
#if defined(_MSC_VER)
    #define THREAD_LOCAL __declspec(thread)
#else
    #define THREAD_LOCAL __thread
#endif

/** Thread-local error message buffer (256 bytes as per spec) */
static THREAD_LOCAL char tls_error_message[256] = {0};

/**
 * Set the thread-local error message.
 * Used by codec wrappers to report errors.
 * Visible to other compilation units (e.g., jpeg_wrapper.c).
 *
 * @param message Error message to store
 */
void set_error(const char* message) {
    if (message != NULL) {
        size_t len = strlen(message);
        if (len >= sizeof(tls_error_message)) {
            len = sizeof(tls_error_message) - 1;
        }
        memcpy(tls_error_message, message, len);
        tls_error_message[len] = '\0';
    } else {
        tls_error_message[0] = '\0';
    }
}

#include <stdarg.h>

/**
 * Set the thread-local error message with printf-style formatting.
 * Used by codec wrappers to report errors with context.
 * Visible to other compilation units.
 *
 * @param fmt Format string
 * @param ... Format arguments
 */
void set_error_fmt(const char* fmt, ...) {
    if (fmt != NULL) {
        va_list args;
        va_start(args, fmt);
        vsnprintf(tls_error_message, sizeof(tls_error_message), fmt, args);
        va_end(args);
    } else {
        tls_error_message[0] = '\0';
    }
}

/*============================================================================
 * SIMD detection
 *============================================================================*/

#if SHARPDICOM_ARCH_X86

#if defined(_MSC_VER)
    #include <intrin.h>
    static void cpuid(int info[4], int level) {
        __cpuid(info, level);
    }
    static void cpuidex(int info[4], int level, int count) {
        __cpuidex(info, level, count);
    }
#elif defined(__GNUC__) || defined(__clang__)
    #include <cpuid.h>
    static void cpuid(int info[4], int level) {
        __cpuid(level, info[0], info[1], info[2], info[3]);
    }
    static void cpuidex(int info[4], int level, int count) {
        __cpuid_count(level, count, info[0], info[1], info[2], info[3]);
    }
#endif

/**
 * Read Extended Control Register (XGETBV) to check OS-enabled XSAVE features.
 * Returns 0 if XGETBV is not supported.
 */
static unsigned long long get_xcr0(void) {
#if defined(_MSC_VER)
    return _xgetbv(0);
#elif defined(__GNUC__) || defined(__clang__)
    unsigned int eax, edx;
    __asm__ volatile("xgetbv" : "=a"(eax), "=d"(edx) : "c"(0));
    return ((unsigned long long)edx << 32) | eax;
#else
    return 0;
#endif
}

/**
 * Detect x86_64 SIMD features using CPUID.
 *
 * IMPORTANT: AVX/AVX2/AVX512 detection requires checking both:
 * 1. CPUID feature flags (CPU supports the instructions)
 * 2. XGETBV XCR0 flags (OS has enabled the state save/restore)
 *
 * Without the OS check, AVX code can crash with illegal instruction
 * on VMs or systems where the OS hasn't enabled AVX state.
 */
static int detect_x86_simd(void) {
    int features = SHARPDICOM_SIMD_NONE;
    int info[4] = {0};

    /* Check CPUID support and get max function level */
    cpuid(info, 0);
    int max_level = info[0];

    if (max_level < 1) {
        return features;
    }

    cpuid(info, 1);
    int ecx = info[2];
    int edx = info[3];

    /* EDX flags - SSE2 doesn't need XSAVE check */
    if (edx & (1 << 26)) features |= SHARPDICOM_SIMD_SSE2;

    /* ECX flags - SSE4.x doesn't need XSAVE check */
    if (ecx & (1 << 19)) features |= SHARPDICOM_SIMD_SSE4_1;
    if (ecx & (1 << 20)) features |= SHARPDICOM_SIMD_SSE4_2;

    /* AVX requires OSXSAVE (bit 27) and XGETBV check */
    int cpu_has_avx = (ecx & (1 << 28)) != 0;
    int os_has_xsave = (ecx & (1 << 27)) != 0;

    if (cpu_has_avx && os_has_xsave) {
        unsigned long long xcr0 = get_xcr0();
        /* XCR0 bits 1-2 must be set for AVX (XMM + YMM state) */
        int os_avx_enabled = ((xcr0 & 0x6) == 0x6);

        if (os_avx_enabled) {
            features |= SHARPDICOM_SIMD_AVX;

            /* Check AVX2 and AVX-512 only if AVX is OS-enabled */
            if (max_level >= 7) {
                cpuidex(info, 7, 0);
                int ebx = info[1];

                if (ebx & (1 << 5)) {
                    features |= SHARPDICOM_SIMD_AVX2;
                }

                /* AVX-512 requires XCR0 bits 5-7 (opmask, ZMM_Hi256, Hi16_ZMM) */
                int os_avx512_enabled = ((xcr0 & 0xE0) == 0xE0);
                if ((ebx & (1 << 16)) && os_avx512_enabled) {
                    features |= SHARPDICOM_SIMD_AVX512F;
                }
            }
        }
    }

    return features;
}

#elif SHARPDICOM_ARCH_ARM64

/**
 * Detect ARM64 SIMD features.
 * NEON is always available on AArch64.
 */
static int detect_arm64_simd(void) {
    /* NEON is mandatory on ARM64 */
    return SHARPDICOM_SIMD_NEON;
}

#endif

/**
 * Cached SIMD features (detected once).
 */
static int cached_simd_features = -1;

static int get_simd_features(void) {
    if (cached_simd_features < 0) {
#if SHARPDICOM_ARCH_X86
        cached_simd_features = detect_x86_simd();
#elif SHARPDICOM_ARCH_ARM64
        cached_simd_features = detect_arm64_simd();
#else
        cached_simd_features = SHARPDICOM_SIMD_NONE;
#endif
    }
    return cached_simd_features;
}

/*============================================================================
 * Public API implementation
 *============================================================================*/

SHARPDICOM_API int sharpdicom_version(void) {
    return SHARPDICOM_NATIVE_VERSION;
}

SHARPDICOM_API int sharpdicom_features(void) {
    int features = 0;

    /* Set JPEG flag when libjpeg-turbo is linked */
#ifdef SHARPDICOM_WITH_JPEG
    features |= SHARPDICOM_HAS_JPEG;
#endif

    /* Set J2K flag when OpenJPEG is linked */
#ifdef SHARPDICOM_WITH_J2K
    features |= SHARPDICOM_HAS_J2K;
#endif

    /* Set JLS flag when CharLS is linked */
#ifdef SHARPDICOM_WITH_JLS
    features |= SHARPDICOM_HAS_JLS;
#endif

    /* Set Video flag when FFmpeg is linked */
#ifdef SHARPDICOM_WITH_MPEG
    features |= SHARPDICOM_HAS_VIDEO;
#endif

    /* Check GPU availability at runtime */
    if (gpu_available()) {
        features |= SHARPDICOM_HAS_GPU;
    }

    /* Set Tesseract flag when Tesseract OCR is linked */
#ifdef SHARPDICOM_WITH_TESSERACT
    features |= SHARPDICOM_HAS_TESSERACT;
#endif

    /* Set JPEG12 flag when 12-bit libjpeg-turbo is linked */
#ifdef SHARPDICOM_WITH_JPEG12
    features |= SHARPDICOM_HAS_JPEG12;
#endif

    /* Set Video Encoding flag when FFmpeg encoding is linked */
#ifdef SHARPDICOM_WITH_FFMPEG_ENC
    features |= SHARPDICOM_HAS_VIDEO_ENC;
#endif

    /* Set stb_image flag when stb_image is linked */
#ifdef SHARPDICOM_WITH_STB_IMAGE
    features |= SHARPDICOM_HAS_STB_IMAGE;
#endif

    return features;
}

SHARPDICOM_API int sharpdicom_simd_features(void) {
    return get_simd_features();
}

SHARPDICOM_API const char* sharpdicom_last_error(void) {
    return tls_error_message;
}

SHARPDICOM_API void sharpdicom_clear_error(void) {
    tls_error_message[0] = '\0';
}

/*============================================================================
 * GPU dispatch exports
 *
 * These re-export the gpu_wrapper functions for the managed code.
 *============================================================================*/

SHARPDICOM_API int sharpdicom_gpu_available(void) {
    return gpu_available();
}

SHARPDICOM_API int sharpdicom_gpu_type(void) {
    return (int)gpu_get_type();
}

SHARPDICOM_API int sharpdicom_gpu_j2k_decode(
    const uint8_t* input,
    size_t input_len,
    uint8_t* output,
    size_t output_len,
    int* width,
    int* height,
    int* components
) {
    gpu_decode_result_t result;
    int status = gpu_j2k_decode(input, input_len, output, output_len, &result);

    if (status == GPU_OK) {
        if (width) *width = result.width;
        if (height) *height = result.height;
        if (components) *components = result.num_components;
    }

    return status;
}

/*============================================================================
 * Codec functions
 *
 * JPEG wrapper: implemented in jpeg_wrapper.c (13-02)
 * J2K wrapper: implemented in j2k_wrapper.c (13-03)
 * JLS wrapper: implemented in jls_wrapper.c (13-04)
 * Video wrapper: implemented in video_wrapper.c (13-04)
 *============================================================================*/

/* Include wrapper headers - these are compiled as separate translation units */
#include "jls_wrapper.h"
#include "video_wrapper.h"
#include "tesseract_wrapper.h"
#include "jpeg12_wrapper.h"
