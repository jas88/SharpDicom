/* FFmpeg parser list - generated for SharpDicom builds
 *
 * This file lists the enabled parsers for registration.
 */

/* Parser declarations */
extern const AVCodecParser ff_h264_parser;
extern const AVCodecParser ff_hevc_parser;
extern const AVCodecParser ff_mpeg4video_parser;
extern const AVCodecParser ff_mpegvideo_parser;
extern const AVCodecParser ff_aac_parser;

static const AVCodecParser * const parser_list[] = {
    &ff_h264_parser,
    &ff_hevc_parser,
    &ff_mpeg4video_parser,
    &ff_mpegvideo_parser,
    &ff_aac_parser,
    NULL
};
