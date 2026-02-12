/* FFmpeg demuxer list - generated for SharpDicom builds
 *
 * This file lists the enabled demuxers for registration.
 * Only includes demuxers needed for DICOM video decoding.
 */

/* Demuxer declarations */
extern const FFInputFormat ff_mpegts_demuxer;
extern const FFInputFormat ff_aac_demuxer;
extern const FFInputFormat ff_wav_demuxer;
extern const FFInputFormat ff_h264_demuxer;
extern const FFInputFormat ff_hevc_demuxer;

static const FFInputFormat * const demuxer_list[] = {
    &ff_mpegts_demuxer,
    &ff_aac_demuxer,
    &ff_wav_demuxer,
    &ff_h264_demuxer,
    &ff_hevc_demuxer,
    NULL
};
