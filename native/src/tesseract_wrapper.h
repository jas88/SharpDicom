/**
 * SharpDicom Native Codecs - Tesseract OCR Wrapper
 *
 * Thin C wrapper around the Tesseract 5.x C API for OCR-based
 * burned-in PHI detection in DICOM pixel data.
 *
 * Thread Safety: TessBaseAPI handles are NOT thread-safe.
 * Each thread must create its own handle via tess_create().
 */

#ifndef TESSERACT_WRAPPER_H
#define TESSERACT_WRAPPER_H

#include "sharpdicom_codecs.h"

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*============================================================================
 * Detection result structure (shared with managed code via P/Invoke)
 *============================================================================*/

/**
 * A single OCR detection result with bounding box, confidence, and text.
 * The text pointer is owned by the caller and must be freed via tess_free_text().
 */
typedef struct {
    int left;       /**< Left edge of bounding box (pixels) */
    int top;        /**< Top edge of bounding box (pixels) */
    int right;      /**< Right edge of bounding box (pixels) */
    int bottom;     /**< Bottom edge of bounding box (pixels) */
    float confidence; /**< Recognition confidence (0.0 - 100.0) */
    char* text;     /**< UTF-8 null-terminated text, must free via tess_free_text */
} TessDetectionResult;

/*============================================================================
 * Tesseract wrapper functions
 *============================================================================*/

/**
 * Creates a new TessBaseAPI instance.
 *
 * @return Opaque handle to TessBaseAPI, or NULL on failure.
 *         Must be freed with tess_delete().
 */
SHARPDICOM_API void* tess_create(void);

/**
 * Destroys a TessBaseAPI instance.
 *
 * @param handle  Handle returned by tess_create() (may be NULL).
 */
SHARPDICOM_API void tess_delete(void* handle);

/**
 * Initializes Tesseract with language data.
 *
 * @param handle    Handle returned by tess_create().
 * @param datapath  Path to tessdata directory (may be NULL for default).
 * @param language  Language code, e.g. "eng" (may be NULL for default).
 *
 * @return 0 on success, -1 on failure.
 */
SHARPDICOM_API int tess_init(void* handle, const char* datapath, const char* language);

/**
 * Sets the image to recognize.
 *
 * @param handle          Handle returned by tess_create().
 * @param data            Raw pixel data.
 * @param width           Image width in pixels.
 * @param height          Image height in pixels.
 * @param bytes_per_pixel Bytes per pixel (1=grayscale, 3=RGB, 4=RGBA).
 * @param bytes_per_line  Bytes per line (stride), or 0 for width * bpp.
 */
SHARPDICOM_API void tess_set_image(
    void* handle,
    const unsigned char* data,
    int width, int height,
    int bytes_per_pixel, int bytes_per_line);

/**
 * Sets the page segmentation mode.
 *
 * @param handle  Handle returned by tess_create().
 * @param mode    Tesseract page segmentation mode (PSM_* values).
 */
SHARPDICOM_API void tess_set_page_seg_mode(void* handle, int mode);

/**
 * Runs OCR recognition on the current image.
 *
 * @param handle  Handle returned by tess_create().
 *
 * @return 0 on success, -1 on failure.
 */
SHARPDICOM_API int tess_recognize(void* handle);

/**
 * Gets word-level detection results after recognition.
 *
 * Each result includes a bounding box, confidence score, and recognized text.
 * The text in each result must be freed by the caller via tess_free_text().
 *
 * @param handle        Handle returned by tess_create().
 * @param results       Output array for detection results.
 * @param max_results   Maximum number of results to fill.
 * @param actual_count  [out] Number of results actually written.
 *
 * @return 0 on success, -1 on failure.
 */
SHARPDICOM_API int tess_get_detections(
    void* handle,
    TessDetectionResult* results,
    int max_results,
    int* actual_count);

/**
 * Frees text returned in TessDetectionResult.
 *
 * @param text  Text pointer from TessDetectionResult (may be NULL).
 */
SHARPDICOM_API void tess_free_text(char* text);

/**
 * Clears recognition results, preserving initialization.
 *
 * @param handle  Handle returned by tess_create() (may be NULL).
 */
SHARPDICOM_API void tess_clear(void* handle);

/**
 * Checks whether Tesseract OCR support is compiled in.
 *
 * @return 1 if Tesseract is available, 0 if compiled as stub.
 */
SHARPDICOM_API int tess_available(void);

#ifdef __cplusplus
}
#endif

#endif /* TESSERACT_WRAPPER_H */
