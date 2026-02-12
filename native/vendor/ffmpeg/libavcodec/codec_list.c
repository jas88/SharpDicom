/* FFmpeg codec list - generated for SharpDicom builds
 *
 * This file lists the enabled codecs for registration.
 */

/* Decoder declarations */
extern const FFCodec ff_mpeg1video_decoder;
extern const FFCodec ff_mpeg2video_decoder;
extern const FFCodec ff_h264_decoder;
extern const FFCodec ff_hevc_decoder;
extern const FFCodec ff_mpeg4_decoder;
extern const FFCodec ff_aac_decoder;
extern const FFCodec ff_pcm_s16le_decoder;
extern const FFCodec ff_pcm_s16be_decoder;

/* Encoder declarations */
extern const FFCodec ff_mpeg2video_encoder;
extern const FFCodec ff_libx264_encoder;
#ifdef SHARPDICOM_HAS_X265
extern const FFCodec ff_libx265_encoder;
#endif
extern const FFCodec ff_aac_encoder;
extern const FFCodec ff_pcm_s16le_encoder;

static const FFCodec * const codec_list[] = {
    /* Decoders */
    &ff_mpeg1video_decoder,
    &ff_mpeg2video_decoder,
    &ff_h264_decoder,
    &ff_hevc_decoder,
    &ff_mpeg4_decoder,
    &ff_aac_decoder,
    &ff_pcm_s16le_decoder,
    &ff_pcm_s16be_decoder,
    /* Encoders */
    &ff_mpeg2video_encoder,
    &ff_libx264_encoder,
#ifdef SHARPDICOM_HAS_X265
    &ff_libx265_encoder,
#endif
    &ff_aac_encoder,
    &ff_pcm_s16le_encoder,
    NULL
};
