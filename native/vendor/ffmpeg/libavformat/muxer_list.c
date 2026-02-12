/* FFmpeg muxer list - generated for SharpDicom builds
 *
 * This file lists the enabled muxers for registration.
 * Only includes muxers needed for DICOM video encoding.
 */

/* Muxer declarations */
extern const FFOutputFormat ff_mpegts_muxer;
extern const FFOutputFormat ff_adts_muxer;
extern const FFOutputFormat ff_wav_muxer;
extern const FFOutputFormat ff_h264_muxer;
extern const FFOutputFormat ff_hevc_muxer;
extern const FFOutputFormat ff_rawvideo_muxer;

static const FFOutputFormat * const muxer_list[] = {
    &ff_mpegts_muxer,
    &ff_adts_muxer,
    &ff_wav_muxer,
    &ff_h264_muxer,
    &ff_hevc_muxer,
    &ff_rawvideo_muxer,
    NULL
};
