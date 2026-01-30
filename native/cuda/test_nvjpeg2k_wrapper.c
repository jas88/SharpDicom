/**
 * Test for nvJPEG2000 Wrapper
 *
 * This test verifies the wrapper's initialization and availability checks.
 */

#include "nvjpeg2k_wrapper.h"
#include <stdio.h>
#include <string.h>

int main(void) {
    int tests_passed = 0;
    int tests_failed = 0;

    printf("nvJPEG2000 Wrapper Tests\n");
    printf("========================\n\n");

    /* Test 1: Check availability (should work even without GPU) */
    printf("Test 1: nvj2k_available()... ");
    int available = nvj2k_available();
    printf("%s (available=%d)\n", available ? "GPU found" : "No GPU", available);
    tests_passed++; /* This test always passes - it just reports status */

    /* Test 2: last_error should return empty string initially */
    printf("Test 2: nvj2k_last_error() initial state... ");
    const char* err = nvj2k_last_error();
    if (err && err[0] == '\0') {
        printf("PASSED\n");
        tests_passed++;
    } else {
        printf("FAILED (expected empty string)\n");
        tests_failed++;
    }

    /* Test 3: clear_error should work */
    printf("Test 3: nvj2k_clear_error()... ");
    nvj2k_clear_error();
    err = nvj2k_last_error();
    if (err && err[0] == '\0') {
        printf("PASSED\n");
        tests_passed++;
    } else {
        printf("FAILED\n");
        tests_failed++;
    }

    /* Test 4: get_device_info without init should fail */
    printf("Test 4: nvj2k_get_device_info() without init... ");
    nvj2k_device_info_t info;
    int result = nvj2k_get_device_info(&info);
    if (result == NVJ2K_ERR_NOT_INITIALIZED || result == NVJ2K_ERR_UNSUPPORTED_GPU) {
        printf("PASSED (expected error: %d)\n", result);
        tests_passed++;
    } else {
        printf("FAILED (unexpected result: %d)\n", result);
        tests_failed++;
    }

    /* Test 5: decode without init should fail */
    printf("Test 5: nvj2k_decode() without init... ");
    uint8_t dummy_input[] = {0xFF, 0x4F, 0xFF, 0x51}; /* J2K SOC marker */
    uint8_t dummy_output[1024];
    nvj2k_decode_result_t decode_result;
    result = nvj2k_decode(
        dummy_input, sizeof(dummy_input),
        dummy_output, sizeof(dummy_output),
        NULL, &decode_result
    );
    if (result == NVJ2K_ERR_NOT_INITIALIZED || result == NVJ2K_ERR_UNSUPPORTED_GPU) {
        printf("PASSED (expected error: %d)\n", result);
        tests_passed++;
    } else {
        printf("FAILED (unexpected result: %d)\n", result);
        tests_failed++;
    }

    /* Test 6: batch decode without init should fail */
    printf("Test 6: nvj2k_decode_batch() without init... ");
    const uint8_t* inputs[] = { dummy_input };
    size_t input_lens[] = { sizeof(dummy_input) };
    uint8_t* outputs[] = { dummy_output };
    size_t output_lens[] = { sizeof(dummy_output) };
    nvj2k_batch_result_t batch_results[1];
    int success_count = nvj2k_decode_batch(
        inputs, input_lens,
        outputs, output_lens,
        1, NULL, batch_results
    );
    if (success_count == 0 &&
        (batch_results[0].status == NVJ2K_ERR_NOT_INITIALIZED ||
         batch_results[0].status == NVJ2K_ERR_UNSUPPORTED_GPU)) {
        printf("PASSED\n");
        tests_passed++;
    } else {
        printf("FAILED (success_count=%d, status=%d)\n", success_count, batch_results[0].status);
        tests_failed++;
    }

    /* Test 7: NULL parameter handling */
    printf("Test 7: NULL parameter handling... ");
    result = nvj2k_decode(NULL, 0, dummy_output, sizeof(dummy_output), NULL, NULL);
    if (result != NVJ2K_OK) {
        printf("PASSED (rejected NULL input)\n");
        tests_passed++;
    } else {
        printf("FAILED (accepted NULL input)\n");
        tests_failed++;
    }

    /* If GPU is available, test initialization */
    if (available) {
        printf("\nGPU Availability Tests\n");
        printf("-----------------------\n");

        /* Test 8: Initialize */
        printf("Test 8: nvj2k_init(-1)... ");
        result = nvj2k_init(-1);
        if (result == NVJ2K_OK) {
            printf("PASSED\n");
            tests_passed++;

            /* Test 9: Get device info after init */
            printf("Test 9: nvj2k_get_device_info() after init... ");
            result = nvj2k_get_device_info(&info);
            if (result == NVJ2K_OK) {
                printf("PASSED\n");
                printf("  Device: %s\n", info.name);
                printf("  Compute: %d.%d\n", info.compute_major, info.compute_minor);
                printf("  Memory: %.2f GB total, %.2f GB free\n",
                       info.total_memory / (1024.0 * 1024.0 * 1024.0),
                       info.free_memory / (1024.0 * 1024.0 * 1024.0));
                tests_passed++;
            } else {
                printf("FAILED (error: %d)\n", result);
                tests_failed++;
            }

            /* Test 10: Shutdown */
            printf("Test 10: nvj2k_shutdown()... ");
            nvj2k_shutdown();
            printf("PASSED\n");
            tests_passed++;

            /* Test 11: After shutdown, should be NOT_INITIALIZED */
            printf("Test 11: After shutdown state... ");
            result = nvj2k_get_device_info(&info);
            if (result == NVJ2K_ERR_NOT_INITIALIZED) {
                printf("PASSED\n");
                tests_passed++;
            } else {
                printf("FAILED (expected NOT_INITIALIZED, got %d)\n", result);
                tests_failed++;
            }
        } else {
            printf("FAILED (error: %d - %s)\n", result, nvj2k_last_error());
            tests_failed++;
        }
    }

    /* Summary */
    printf("\n========================\n");
    printf("Results: %d passed, %d failed\n", tests_passed, tests_failed);

    return tests_failed > 0 ? 1 : 0;
}
