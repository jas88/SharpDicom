/**
 * SharpDicom Native Codecs - Tesseract OCR Wrapper Implementation
 *
 * Thin C wrapper around the Tesseract 5.x C API (capi.h).
 * When SHARPDICOM_WITH_TESSERACT is not defined, all functions
 * compile as stubs that report "Tesseract not available".
 */

#define SHARPDICOM_CODECS_EXPORTS
#include "tesseract_wrapper.h"
#include "sharpdicom_codecs.h"

#include <string.h>
#include <stdlib.h>

/* Forward declaration of set_error from sharpdicom_codecs.c */
extern void set_error(const char* message);

#ifdef SHARPDICOM_WITH_TESSERACT

/*============================================================================
 * Tesseract C API declarations (from tesseract/capi.h)
 *============================================================================*/

#include <tesseract/capi.h>

/*============================================================================
 * Full implementation (Tesseract available)
 *============================================================================*/

SHARPDICOM_API void* tess_create(void) {
    TessBaseAPI* api = TessBaseAPICreate();
    return (void*)api;
}

SHARPDICOM_API void tess_delete(void* handle) {
    if (handle != NULL) {
        TessBaseAPIDelete((TessBaseAPI*)handle);
    }
}

SHARPDICOM_API int tess_init(void* handle, const char* datapath, const char* language) {
    if (handle == NULL) {
        set_error("tess_init: NULL handle");
        return -1;
    }

    int result = TessBaseAPIInit3((TessBaseAPI*)handle, datapath, language);
    if (result != 0) {
        set_error("tess_init: Tesseract initialization failed");
        return -1;
    }
    return 0;
}

SHARPDICOM_API void tess_set_image(
    void* handle,
    const unsigned char* data,
    int width, int height,
    int bytes_per_pixel, int bytes_per_line)
{
    if (handle == NULL || data == NULL) {
        return;
    }
    TessBaseAPISetImage((TessBaseAPI*)handle, data, width, height,
                        bytes_per_pixel, bytes_per_line);
}

SHARPDICOM_API void tess_set_page_seg_mode(void* handle, int mode) {
    if (handle == NULL) {
        return;
    }
    TessBaseAPISetPageSegMode((TessBaseAPI*)handle, (TessPageSegMode)mode);
}

SHARPDICOM_API int tess_recognize(void* handle) {
    if (handle == NULL) {
        set_error("tess_recognize: NULL handle");
        return -1;
    }

    int result = TessBaseAPIRecognize((TessBaseAPI*)handle, NULL);
    if (result != 0) {
        set_error("tess_recognize: recognition failed");
        return -1;
    }
    return 0;
}

SHARPDICOM_API int tess_get_detections(
    void* handle,
    TessDetectionResult* results,
    int max_results,
    int* actual_count)
{
    if (handle == NULL || results == NULL || actual_count == NULL) {
        set_error("tess_get_detections: NULL argument");
        if (actual_count != NULL) *actual_count = 0;
        return -1;
    }
    if (max_results <= 0) {
        *actual_count = 0;
        return 0;
    }

    TessResultIterator* ri = TessBaseAPIGetIterator((TessBaseAPI*)handle);
    if (ri == NULL) {
        *actual_count = 0;
        return 0; /* No results is not an error */
    }

    int count = 0;
    TessPageIterator* pi = TessResultIteratorGetPageIterator(ri);

    do {
        if (count >= max_results) {
            break;
        }

        /* Get text for this word */
        char* word = TessResultIteratorGetUTF8Text(ri, RIL_WORD);
        if (word == NULL) {
            continue;
        }

        /* Get bounding box */
        int left, top, right, bottom;
        if (!TessPageIteratorBoundingBox(pi, RIL_WORD, &left, &top, &right, &bottom)) {
            TessDeleteText(word);
            continue;
        }

        /* Get confidence */
        float confidence = TessResultIteratorConfidence(ri, RIL_WORD);

        results[count].left = left;
        results[count].top = top;
        results[count].right = right;
        results[count].bottom = bottom;
        results[count].confidence = confidence;
        results[count].text = word; /* Caller must free via tess_free_text */

        count++;
    } while (TessPageIteratorNext(pi, RIL_WORD));

    TessResultIteratorDelete(ri);

    *actual_count = count;
    return 0;
}

SHARPDICOM_API void tess_free_text(char* text) {
    if (text != NULL) {
        TessDeleteText(text);
    }
}

SHARPDICOM_API void tess_clear(void* handle) {
    if (handle != NULL) {
        TessBaseAPIClear((TessBaseAPI*)handle);
    }
}

SHARPDICOM_API int tess_available(void) {
    return 1;
}

#else /* SHARPDICOM_WITH_TESSERACT not defined */

/*============================================================================
 * Stub implementations when Tesseract is not available
 *============================================================================*/

SHARPDICOM_API void* tess_create(void) {
    return NULL;
}

SHARPDICOM_API void tess_delete(void* handle) {
    (void)handle;
}

SHARPDICOM_API int tess_init(void* handle, const char* datapath, const char* language) {
    (void)handle;
    (void)datapath;
    (void)language;
    set_error("Tesseract OCR support not available (not compiled in)");
    return -1;
}

SHARPDICOM_API void tess_set_image(
    void* handle,
    const unsigned char* data,
    int width, int height,
    int bytes_per_pixel, int bytes_per_line)
{
    (void)handle;
    (void)data;
    (void)width;
    (void)height;
    (void)bytes_per_pixel;
    (void)bytes_per_line;
}

SHARPDICOM_API void tess_set_page_seg_mode(void* handle, int mode) {
    (void)handle;
    (void)mode;
}

SHARPDICOM_API int tess_recognize(void* handle) {
    (void)handle;
    set_error("Tesseract OCR support not available (not compiled in)");
    return -1;
}

SHARPDICOM_API int tess_get_detections(
    void* handle,
    TessDetectionResult* results,
    int max_results,
    int* actual_count)
{
    (void)handle;
    (void)results;
    (void)max_results;
    if (actual_count != NULL) {
        *actual_count = 0;
    }
    set_error("Tesseract OCR support not available (not compiled in)");
    return -1;
}

SHARPDICOM_API void tess_free_text(char* text) {
    (void)text;
}

SHARPDICOM_API void tess_clear(void* handle) {
    (void)handle;
}

SHARPDICOM_API int tess_available(void) {
    return 0;
}

#endif /* SHARPDICOM_WITH_TESSERACT */
