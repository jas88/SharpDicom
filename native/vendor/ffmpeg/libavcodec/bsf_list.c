/* FFmpeg bitstream filter list - generated for SharpDicom builds
 *
 * This file lists the enabled bitstream filters for registration.
 */

/* BSF declarations */
extern const FFBitStreamFilter ff_null_bsf;
extern const FFBitStreamFilter ff_h264_mp4toannexb_bsf;
extern const FFBitStreamFilter ff_hevc_mp4toannexb_bsf;

static const FFBitStreamFilter * const bitstream_filters[] = {
    &ff_null_bsf,
    &ff_h264_mp4toannexb_bsf,
    &ff_hevc_mp4toannexb_bsf,
    NULL
};
