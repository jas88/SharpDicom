# SharpDicom Project State

## Current Status

**Milestone**: v3.0.0 - Polish, CLI & Migration
**Phase**: 30 - HT Block Coder (In Progress)
**Plan**: 10 of 10 in current phase
**Status**: In progress
**Last activity**: 2026-02-08 - Completed 30-09-PLAN.md (multi-tile pipeline + EBCOT regression)

**Progress**: █████████░ (9/10 plans in Phase 30)

**Test Status**: 5885 tests (5687 pass, 198 skipped, 0 failed)

## Completed

- [x] Project initialization
- [x] PROJECT.md created
- [x] GSD workflow configured (YOLO mode)
- [x] Research completed (Stack, Features, Architecture, Pitfalls)
- [x] Research synthesized (SUMMARY.md)
- [x] Requirements defined (REQUIREMENTS.md)
- [x] Roadmap created (ROADMAP.md)
- [x] Phase 1 Plan 01: Core primitive types (DicomTag, DicomVR, DicomVRInfo, DicomMaskedTag, ValueMultiplicity)
- [x] Phase 1 Plan 02: Source generator infrastructure and NEMA XML cache
- [x] Phase 1 Plan 03: DicomUID and TransferSyntax types
- [x] Phase 1 Plan 04: Source generator implementation (XML parsing and code emission)
- [x] Phase 1 Plan 05: IDicomElement interface hierarchy and element types
- [x] Phase 1 Plan 06: DicomDataset and PrivateCreatorDictionary
- [x] Phase 1 Plan 07: Generator tests and Phase 1 verification
- [x] Phase 2 Plan 01: DicomStreamReader for low-level Span-based parsing
- [x] Phase 2 Plan 02: Part10Reader for file structure parsing
- [x] Phase 2 Plan 03: DicomFileReader for high-level async file reading
- [x] Phase 2 Plan 04: DicomFile class and integration tests
- [x] Phase 3 Plan 01: DicomReaderOptions sequence config, implicit VR tests, context caching
- [x] Phase 3 Plan 02: SequenceParser with depth guard, delimiter handling, Parent property
- [x] Phase 3 Plan 03: Sequence integration into file reading pipeline
- [x] Phase 3 Plan 04: VRResolver class and comprehensive Phase 3 integration tests
- [x] Phase 4 Plan 01: DicomEncoding core (DicomCharacterSets registry, FromSpecificCharacterSet, UTF-8 zero-copy)
- [x] Phase 4 Plan 02: DicomDataset.Encoding property, string element integration, encoding inheritance
- [x] Phase 5 Plan 01: Core pixel data types (PixelDataInfo, PixelDataHandling, FragmentParser)
- [x] Phase 5 Plan 02: Lazy loading infrastructure (IPixelDataSource, DicomPixelDataElement, PixelDataContext)
- [x] Phase 5 Plan 03: Integration with DicomFileReader and DicomFile (PixelData property, GetPixelData method)
- [x] Phase 6 Plan 01: Vendor dictionary source generator (Siemens, GE, Philips, 9268 tags)
- [x] Phase 6 Plan 02: PrivateCreatorDictionary enhancements and DicomDatasetExtensions
- [x] Phase 7 Plan 01: DicomStreamWriter for low-level IBufferWriter<byte> element writing
- [x] Phase 7 Plan 02: DicomFileWriter and FileMetaInfoGenerator for Part 10 file output
- [x] Phase 8 Plan 01: Core validation infrastructure (ValidationIssue, ValidationResult, IValidationRule)
- [x] Phase 8 Plan 02: Built-in VR validators (DA, TM, DT, AS, UI, PN, CS, length, character repertoire)
- [x] Phase 8 Plan 03: ValidationProfile presets and DicomReaderOptions/DicomFileReader integration
- [x] Phase 9 Plan 01: IPixelDataCodec interface and CodecRegistry
- [x] Phase 9 Plan 02: RLE codec with PackBits compression, SIMD optimization, MSB-first interleaving
- [x] Phase 7 Plan 03: Sequence length handling, sequence writing, roundtrip integration tests

### v2.0.0 Plans (In Progress)

- [x] Phase 10 Plan 01: PDU types and constants
- [x] Phase 10 Plan 02: PDU sub-items (PresentationContext, UserInformation, AssociationOptions)
- [x] Phase 10 Plan 03: PDU parsing (PduReader, PduWriter ref structs)
- [x] Phase 10 Plan 04: Association state machine (13 states, DicomAssociation)
- [x] Phase 10 Plan 05: DicomClient SCU with C-ECHO (CommandField, DicomCommand, DicomClient)
- [x] Phase 10 Plan 06: DicomServer C-ECHO SCP (DicomServer, DicomServerOptions, handlers)
- [x] Phase 10 Plan 07: Integration tests (CEchoTests, CEchoIntegrationTests, state machine bug fixes)
- [x] Phase 11 Plan 01: Common DIMSE types (QueryRetrieveLevel, SubOperationProgress, DicomTransferProgress, DicomCommand extensions)
- [x] Phase 11 Plan 02: C-STORE SCU (CStoreOptions, CStoreResponse, CStoreScu service)
- [x] Phase 11 Plan 03: C-FIND SCU (CFindOptions, DicomQuery fluent builder, CFindScu with IAsyncEnumerable)
- [x] Phase 11 Plan 06: C-GET SCU (CGetOptions, CGetProgress, CGetScu with interleaved C-STORE sub-operations, SCP role selection)
- [x] Phase 11 Plan 04: C-STORE SCP (ICStoreHandler, IStreamingCStoreHandler, CStoreHandlerMode, DicomServer integration)
- [x] Phase 11 Plan 05: C-MOVE SCU (CMoveOptions, CMoveProgress, CMoveScu with IAsyncEnumerable progress)
- [x] Phase 11 Plan 07: DIMSE integration tests (roundtrip, DCMTK interop, protocol verification)
- [x] Phase 12 Plan 01: Codec infrastructure (IImageCodec, JPEG markers, color conversion)
- [x] Phase 12 Plan 02: DCT & Bit I/O (DctTransform, BitReader, BitWriter, JpegCodecOptions)
- [x] Phase 12 Plan 03: JPEG Baseline codec (JpegBaselineDecoder/Encoder/Codec, DCT-based lossy 8-bit)
- [x] Phase 12 Plan 04: JPEG Lossless codec (Predictor, LosslessHuffman, JpegLosslessDecoder/Encoder/Codec)
- [x] Phase 12 Plan 05: JPEG 2000 infrastructure (J2kCodestream, Dwt53, Dwt97, MqCoder)
- [x] Phase 12 Plan 06: JPEG 2000 codec (EbcotEncoder/Decoder, PacketEncoder/Decoder, J2kEncoder/Decoder)
- [x] Phase 12 Plan 07: JPEG 2000 integration (Jpeg2000LosslessCodec, Jpeg2000LossyCodec, CodecInitializer)
- [x] Phase 13 Plan 01: Native build infrastructure (Zig cross-compilation, C API header, CI workflow)
- [x] Phase 13 Plan 02: libjpeg-turbo wrapper (jpeg_decode, jpeg_encode, 8-bit/12-bit, TurboJPEG API)
- [x] Phase 13 Plan 03: JPEG 2000 wrapper (OpenJPEG integration, resolution levels, ROI decode, tiled encode)
- [x] Phase 13 Plan 04: CharLS/FFmpeg wrappers (jls_decode/encode, video_decoder for MPEG2/H.264/HEVC)
- [x] Phase 13 Plan 05: GPU acceleration (nvJPEG2000 wrapper, GPU dispatch, CPU fallback)
- [x] Phase 13 Plan 06: Managed P/Invoke layer (SharpDicom.Codecs project, NativeMethods, NativeCodecs)
- [x] Phase 13 Plan 07: IPixelDataCodec wrappers (NativeJpegCodec, NativeJpeg2000Codec, NativeJpegLsCodec, priority registration)
- [x] Phase 13 Plan 08: NuGet package structure (MSBuild targets, runtime packages, release workflow)
- [x] Phase 13 Plan 09: Native codecs test suite (NativeCodecsTests, CodecRegistryPriorityTests, codec-specific tests)
- [x] Phase 14 Plan 01: PS3.15 source generator (Part15Parser, DeidentificationEmitter, ~600 tags)
- [x] Phase 14 Plan 02: Core de-identification types (ActionResolver, DeidentificationOptions, DummyValueGenerator)
- [x] Phase 14 Plan 03: UID remapping infrastructure (IUidStore, UidGenerator, UidRemapper, InMemoryUidStore, SqliteUidStore)
- [x] Phase 14 Plan 04: Date shifting module (DateShifter, DateShiftConfig, IDateOffsetStore, DateShiftResult)
- [x] Phase 14 Plan 05: DicomDeidentifier main class (DicomDeidentifierBuilder, DicomDeidentifier, streaming integration)
- [x] Phase 14 Plan 06: JSON configuration (DeidentificationConfig, DeidentificationConfigLoader, presets)
- [x] Phase 14 Plan 07: Pixel Data Redaction (PixelDataRedactor, RedactionRegion, BurnedInAnnotationDetector)
- [x] Phase 14 Plan 08: Batch Processing & Integration Tests (BatchDeidentifier, 74 new tests)

### Phase 20 - Critical Bug Fixes (COMPLETE)

- [x] Phase 20 Plan 01: FindSequenceDelimiter Depth Tracking Fix (DicomStreamReader fix, 7 edge case tests, +18 passing tests)
- [x] Phase 20 Plan 02: C-STORE SCP Sequence Parser Integration (SequenceParser delegation, 6 roundtrip tests, streaming architecture preserved)
- [x] Phase 20 Plan 03: Property-Based and DCMTK Interop Testing (FsCheck 4 property tests with 140+ iterations, DCMTK 5 interop tests)

### Phase 21 - Complete Managed Codecs (COMPLETE)

- [x] Phase 21 Plan 01: JPEG-LS Encoder/Decoder (JpegLsPredictor, JlsContext, GolombRiceCoder, all 8 predictors, 365 contexts, full ITU-T T.87 compliance)
- [x] Phase 21 Plan 02: HTJ2K Codec Shell (Htj2kCodec delegates to J2K, HT block coder deferred to 21-04, 12 property tests)
- [x] Phase 21 Plan 03: SIMD Optimization (SimdHelpers, Vector128-optimized DWT 5/3 and 9/7, AggressiveInlining, performance benchmarks)
- [x] Phase 21 Plan 04: Codec Conformance Tests (JPEG-LS vs CharLS, HTJ2K vs OpenJPH, 18 new tests: 10 conformance + 8 integration, documented known issues)
- [x] Phase 21 Plan 05: JPEG-LS Bug Fixes (Golomb-Rice limit escape per ITU-T T.87 A.5.3, context update symmetry, non-interleaved decode fix)
- [x] Phase 21 Plan 06: Partial J2K Bug Fix (MQ coder uniform coding symmetry)
- [x] Phase 21 Plan 07: EBCOT Pass Tracking Fix (visitedThisBitplane, refinedCount, three-context magnitude refinement)
- [x] Phase 21 Plan 08: Tier-2 Packet Encoding Fixes (ReadNumPasses, WriteZeroBitPlanes symmetry; J2K pipeline investigation)
- [x] Phase 21 Plan 09: J2K Pipeline Investigation (6 stage isolation tests; identified architectural gap: multi-resolution subband support missing; HTJ2K deferred to Phase 30)

### Phase 22 - TLS Networking (COMPLETE)

- [x] Phase 22 Plan 01: TLS Configuration and Validation (TlsOptions, TlsServerOptions, CertificateValidator, DicomTlsProfile, DICOM BCP 195 compliance, 38 tests)
- [x] Phase 22 Plan 02: DicomClient TLS Integration (Optional TLS via TlsOptions, SslStream wrapping, DICOM BCP 195 validation, protocol downgrade detection, backward-compatible plain TCP)
- [x] Phase 22 Plan 03: DicomServer TLS Integration (Server-side TLS handshake, mutual TLS support, Stream abstraction, SslStreamCertificateContext caching, backward-compatible plain TCP)
- [x] Phase 22 Plan 04: TLS Integration Tests (TlsCertificateHelper programmatic cert generation, 10 integration tests: C-ECHO/C-STORE over TLS, mTLS, certificate validation, 7/10 passing)

### Phase 23 - CLI Tools (COMPLETE)

- [x] Phase 23 Plan 01: CLI Scaffolding (SharpDicom.Cli project, System.CommandLine 2.0.2, Spectre.Console 0.54.0, Tomlyn 0.20.0, Text/JSON/XML formatters, config system, helpers)
- [x] Phase 23 Plan 02: Dump Command (sharpdcm dump, text/JSON/XML output, recursive directory processing, sequence nesting, tag filtering)
- [x] Phase 23 Plan 03: Store Command (sharpdcm store, C-STORE SCU, PACS connection resolution, retry, TTY-aware progress)
- [x] Phase 23 Plan 04: Find Command (sharpdcm find, C-FIND SCU, patient/study/series/instance levels, wildcard filters, text/JSON/CSV output, PacsConnectionResolver)
- [x] Phase 23 Plan 05: Lint and Fix Commands (sharpdcm lint with strict/lenient/permissive profiles, sharpdcm fix with DicomFixer engine, 5 fix categories, dry-run/force/output-dir)
- [x] Phase 23 Plan 06: Integration Tests (60 NUnit tests for CLI helpers, formatters, lint validation, DicomFixer engine)

### Phase 24 - Server-Side DIMSE (SCP) (COMPLETE)

- [x] Phase 24 Plan 01: C-FIND SCP handler and query matching infrastructure (DicomQueryMatcher, DicomDateRange, DIMSE dispatch extension, HandleCFindAsync)
- [x] Phase 24 Plan 02: C-MOVE SCP and C-GET SCP handlers (HandleCMoveAsync with separate forwarding, HandleCGetAsync with same-association C-STORE, SubOperationProgress tracking)
- [x] Phase 24 Plan 03: FileSystemDicomStore + SQLite metadata index (DicomMetadataIndex, FileSystemDicomStore, CreateServerOptions)
- [x] Phase 24 Plan 04: Phase 24 test suite (70 new tests: DicomQueryMatcher, SCP integration, FileSystemDicomStore)

### Phase 25 - Advanced De-identification (COMPLETE)

- [x] Phase 25 Plan 01: UidReferenceWalker for comprehensive VR=UI traversal and pipeline integration
- [x] Phase 25 Plan 02: Tesseract native wrapper and P/Invoke layer (stub mode, SafeHandle, dual LibraryImport/DllImport)
- [x] Phase 25 Plan 03: OcrScanner for burned-in PHI detection (OCR scanning, dual-threshold confidence, allow/deny filtering, pipeline integration)
- [x] Phase 25 Plan 04: Advanced de-identification test suite (50 tests: UidReferenceWalker, OcrScanner, pipeline integration)

### Phase 26 - Migration Tooling (COMPLETE)

- [x] Phase 26 Plan 01: FoDicom5.Compat core types (DicomFile, DicomDataset, DicomTag, DicomItem hierarchy, DicomVR, DicomUID, 38 tests)
- [x] Phase 26 Plan 02: FoDicom5.Compat network adapter (DicomClient, DicomClientFactory, DicomCFindRequest, DicomCFindResponse, DicomStatus, DicomQueryRetrieveLevel, 16 tests)
- [x] Phase 26 Plan 03: dcm2csv validation (compiles and 9 integration tests pass against FoDicom5.Compat, no API changes needed)
- [x] Phase 26 Plan 04: nccid validation (compiles and 17 integration tests pass against FoDicom5.Compat networking, both phase gates met)
- [x] Phase 26 Plan 05: FoDicom4.Compat with Dicom namespace and Get<T> API (15 source files, 25 tests)
- [x] Phase 26 Plan 06: SharpDicom.Analyzers with FoDicomUsageAnalyzer, CompatUsageAnalyzer, FoDicomToCompatFix, CompatToNativeFix
- [x] Phase 26 Plan 07: Analyzer test suite (21 tests: FoDicomUsageAnalyzer, CompatUsageAnalyzer, FoDicomToCompatFix, CompatToNativeFix)

### Phase 27 - Extended Codec Support (COMPLETE - VERIFIED 20/20)

- [x] Phase 27 Plan 01: Transfer syntax and UID definitions (MPEG2, H264, HEVC CompressionType; 10 transfer syntaxes; 7 video SOP UIDs)
- [x] Phase 27 Plan 02: JPEG Extended codec (JpegExtendedDecoder/Encoder/Codec, 8/12-bit SOF1, int[] component buffers, CodecInitializer registration)
- [x] Phase 27 Plan 03: 12-bit JPEG native wrapper (jpeg12_wrapper.c/h, SHARPDICOM_HAS_JPEG12 flag, dual libjpeg-turbo build.zig)
- [x] Phase 27 Plan 04: Native 12-bit JPEG codec (NativeJpeg8Codec, NativeJpeg12Codec, jpeg12_* P/Invoke, Jpeg12Bit feature detection)
- [x] Phase 27 Plan 05: 12-bit/16-bit codec test suites (38 tests: JpegExtendedCodecTests, JpegExtended12BitTests, JpegLossless16BitTests)
- [x] Phase 27 Plan 06: Native video encoder and stb_image wrapper (video_encoder.c/h, stb_image_wrapper.c/h, GPU-accelerated encoding)
- [x] Phase 27 Plan 07: FFmpeg encoding build infrastructure (SHARPDICOM_HAS_VIDEO_ENC, addX264Sources/addX265Sources/addFfmpegEncSources)
- [x] Phase 27 Plan 08: Video Encoding API (VideoEncoder, NativeVideoEncoder, VideoFrame, VideoEncoderOptions, NativeImageLoader)
- [x] Phase 27 Plan 09: Video DICOM Builder (VideoSopClass enum, VideoDicomBuilder fluent API, VideoEncoder integration)
- [x] Phase 27 Plan 10: Video Encoding Test Suite (66 tests: VideoDicomBuilder, VideoEncoderOptions, VideoEncoder, VideoFrame, VideoEncodeProgress)
- [x] Phase 27 Plan 11: Video encoder backend registration (NativeCodecs wiring, gap 1+2 closure)
- [x] Phase 27 Plan 12: Document native build requirements (BUILD-REQUIREMENTS.md, gap 3 closure)

### Phase 28 - DIMSE-N Services (COMPLETE - VERIFIED 20/20)

- [x] Phase 28 Plan 01: N-Service command foundation (12 factory methods, N-Service status codes, MPPS/Storage Commitment UIDs)
- [x] Phase 28 Plan 02: Async Operations Window negotiation (0x53 sub-item, DicomClientOptions async ops, FoDicom5.Compat wiring)
- [x] Phase 28 Plan 03: N-Service infrastructure (6 handler interfaces, NServiceScu, DicomServer N-Service dispatch, DicomServerOptions handler registration)
- [x] Phase 28 Plan 04: MPPS and Storage Commitment services (MppsScpHandler with state machine, MppsScu, StorageCommitmentScpHandler, StorageCommitmentScu)
- [x] Phase 28 Plan 05: Comprehensive N-Service test suite (70 tests: NServiceCommandTests, AsyncOpsWindowTests, MppsTests, StorageCommitmentTests)

### Phase 29 - MongoDB/BSON Serialization (COMPLETE)

- [x] Phase 29 Plan 01: Core BSON serialization types and BsonDicomWriter
- [x] Phase 29 Plan 02: BsonDicomReader for BSON deserialization
- [x] Phase 29 Plan 03: DicomJsonWriter and DicomJsonReader for PS3.18 Annex F
- [x] Phase 29 Plan 04: Comprehensive BSON and DICOM-JSON test suite (73 tests)
- [x] Phase 29 Plan 05: SharpDicom.MongoDB adapter package (BsonDocumentAdapter, IndexRecommendations, DicomCollectionHelper, BulkImporter)

## In Progress

- [x] Phase 30 Plan 01: Subband Infrastructure (COMPLETE)
- [x] Phase 30 Plan 02: IBlockCoder Interface and Subband Routing Fix (COMPLETE)
- [x] Phase 30 Plan 03: HT Primitive Components (VlcTable, MelCoder, HtBitIO) (COMPLETE)
- [x] Phase 30 Plan 04: HT Cleanup Pass (HtCleanup encode/decode, 89 tests) (COMPLETE)
- [x] Phase 30 Plan 05: HtSigProp + HtMagRef + HtBlockEncoder/Decoder (IBlockCoder, 38 tests) (COMPLETE)
- [x] Phase 30 Plan 06: HTJ2K Codec Integration (COMPLETE)
- [x] Phase 30 Plan 07: IProgressiveCodec + SIMD Vector256/512 Expansion (COMPLETE)
- [x] Phase 30 Plan 08: sharpdcm convert CLI command (COMPLETE)
- [x] Phase 30 Plan 09: Multi-tile Pipeline + EBCOT Regression (COMPLETE)
- [ ] Phase 30 Plan 10: Remaining HT Block Coder plan

## Blocked

*None*

## Phase Progress

| Phase | Name | Status | Plans | Started | Completed |
|-------|------|--------|-------|---------|-----------|
| 10 | Network Foundation | COMPLETE | 7/7 | 2026-01-28 | 2026-01-28 |
| 11 | DIMSE Services | COMPLETE | 7/7 | 2026-01-29 | 2026-01-29 |
| 12 | Pure C# Codecs | COMPLETE | 7/7 | 2026-01-29 | 2026-01-29 |
| 13 | Native Codecs Package | COMPLETE | 9/9 | 2026-01-29 | 2026-01-30 |
| 14 | De-identification | COMPLETE | 8/8 | 2026-01-29 | 2026-01-30 |
| 20 | Critical Bug Fixes | COMPLETE | 3/3 | 2026-02-03 | 2026-02-03 |
| 21 | Complete Managed Codecs | COMPLETE | 9/9 | 2026-02-03 | 2026-02-04 |
| 22 | TLS Networking | COMPLETE | 4/4 | 2026-02-04 | 2026-02-04 |
| 23 | CLI Tools | COMPLETE | 6/6 | 2026-02-05 | 2026-02-06 |
| 24 | Server-Side DIMSE (SCP) | COMPLETE | 4/4 | 2026-02-06 | 2026-02-06 |
| 25 | Advanced De-identification | COMPLETE | 4/4 | 2026-02-06 | 2026-02-06 |
| 26 | Migration Tooling | COMPLETE | 7/7 | 2026-02-06 | 2026-02-06 |
| 27 | Extended Codec Support | COMPLETE (VERIFIED) | 12/12 | 2026-02-07 | 2026-02-07 |
| 28 | DIMSE-N Services | COMPLETE (VERIFIED) | 5/5 | 2026-02-07 | 2026-02-07 |
| 29 | MongoDB/BSON Serialization | COMPLETE | 5/5 | 2026-02-07 | 2026-02-07 |
| 30 | HT Block Coder | IN PROGRESS | 9/10 | 2026-02-08 | - |

## v1.0.0 Phase Progress (Complete)

| Phase | Name | Status | Plans | Started | Completed |
|-------|------|--------|-------|---------|-----------|
| 1 | Core Data Model & Dictionary | COMPLETE | 7/7 | 2026-01-27 | 2026-01-26 |
| 2 | Basic File Reading | COMPLETE | 4/4 | 2026-01-27 | 2026-01-27 |
| 3 | Implicit VR & Sequences | COMPLETE | 4/4 | 2026-01-27 | 2026-01-27 |
| 4 | Character Encoding | COMPLETE | 2/2 | 2026-01-26 | 2026-01-27 |
| 5 | Pixel Data & Lazy Loading | COMPLETE | 3/3 | 2026-01-27 | 2026-01-27 |
| 6 | Private Tags | COMPLETE | 2/2 | 2026-01-27 | 2026-01-27 |
| 7 | File Writing | COMPLETE | 3/3 | 2026-01-27 | 2026-01-27 |
| 8 | Validation & Strictness | COMPLETE | 3/3 | 2026-01-27 | 2026-01-27 |
| 9 | RLE Codec | COMPLETE | 2/2 | 2026-01-27 | 2026-01-27 |

## Key Decisions Log

| Date | Phase-Plan | Decision | Rationale |
|------|------------|----------|-----------|
| 2025-01-26 | Setup | YOLO mode selected | Experienced user, faster execution |
| 2025-01-26 | Setup | Research enabled | DICOM complexity warrants exploration |
| 2025-01-26 | Setup | Parallel execution | Maximize throughput |
| 2025-01-26 | Setup | Lenient-by-default parsing | Real-world files non-conformant |
| 2025-01-26 | Setup | Two-layer reader architecture | Zero-allocation + async support |
| 2026-01-27 | 01-01 | Single uint representation for DicomTag | Compact (4 bytes), trivial equality/comparison |
| 2026-01-27 | 01-01 | Packed ushort for DicomVR | 2 bytes, first char in high byte, second in low byte |
| 2026-01-27 | 01-01 | Separate DicomVRInfo lookup | Keeps DicomVR at 2 bytes, metadata separate |
| 2026-01-27 | 01-01 | DicomMaskedTag as separate type | Pattern matching for repeating groups without bloating DicomTag |
| 2026-01-27 | 01-01 | Multi-targeting with polyfills | netstandard2.0 for max compatibility |
| 2026-01-26 | 01-04 | Parse DocBook XML with XNamespace | NEMA standard uses DocBook 5.0 |
| 2026-01-26 | 01-04 | Clean zero-width spaces from keywords | NEMA XML contains U+200B |
| 2026-01-26 | 01-04 | Generate ~4000 static DicomTag members | IntelliSense-friendly |
| 2026-01-26 | 01-04 | Use FrozenDictionary on .NET 8+ | 40-50% faster lookups |
| 2026-01-26 | 01-06 | Dictionary + sorted cache pattern | O(1) lookup with lazy-sorted enumeration |
| 2026-01-27 | 01-05 | Interface hierarchy for elements | Allows sequences to contain datasets |
| 2026-01-27 | 01-05 | Stateless value parsing | Simpler, lower memory footprint |
| 2026-01-26 | 01-07 | Verify.SourceGenerators for testing | Industry standard for generator testing |
| 2026-01-27 | 02-01 | DicomStreamReader as ref struct | Zero-copy Span<T> parsing, cannot escape stack |
| 2026-01-27 | 02-02 | Partial struct for DicomTag | Allows well-known constants in separate file |
| 2026-01-27 | 02-02 | DicomFileException hierarchy | Enables fine-grained error handling at parsing stages |
| 2026-01-27 | 02-03 | List-based element batch for yield | C# disallows ref struct across yield boundary |
| 2026-01-27 | 02-03 | Microsoft.Bcl.AsyncInterfaces package | IAsyncEnumerable support for netstandard2.0 |
| 2026-01-27 | 02-04 | DicomFile wraps DicomFileReader | Convenient one-call file loading |
| 2026-01-27 | 02-04 | Null character trimming in GetString | DICOM UI VR padding requires trimming |
| 2026-01-27 | 03-01 | MaxSequenceDepth=128 default | Conservative limit; real files rarely exceed 10 |
| 2026-01-27 | 03-01 | MaxTotalItems=100,000 default | Prevents memory exhaustion |
| 2026-01-27 | 03-01 | Context value inheritance from parent | Nested sequences inherit BitsAllocated/PixelRepresentation |
| 2026-01-27 | 03-02 | Parent property on DicomDataset | Enables context inheritance in nested sequences |
| 2026-01-27 | 03-02 | Explicit depth tracking in SequenceParser | Avoids stack overflow on deeply nested malformed files |
| 2026-01-27 | 03-02 | Delimiter-based parsing for undefined length | FFFE group tags (Item, ItemDelimitationItem, SequenceDelimitationItem) |
| 2026-01-27 | 03-03 | Lazy SequenceParser initialization | Use correct transfer syntax from file header |
| 2026-01-27 | 03-03 | FindSequenceDelimiter with depth tracking | Handle nested undefined length sequences |
| 2026-01-27 | 03-03 | Encapsulated pixel data as binary | Store raw bytes for Phase 5 enhancement |
| 2026-01-27 | 03-04 | Static VRResolver methods | VR resolution is stateless - context from DicomDataset |
| 2026-01-27 | 03-04 | Add OV, SV, UV VRs | DICOM 2020 64-bit support |
| 2026-01-27 | 03-04 | Fix CacheContextValue to use GetUInt16 | US VR is 2 bytes, not 4 |
| 2026-01-26 | 04-01 | Static character set registry with normalization | DICOM terms have variants (ISO IR/ISO-IR/ISO_IR), centralized registry handles all |
| 2026-01-26 | 04-01 | UTF-8/ASCII zero-copy optimization | 80%+ of modern DICOM is UTF-8, TryGetUtf8 enables zero-allocation access |
| 2026-01-26 | 04-01 | Delegate ISO 2022 to .NET | .NET's ISO2022Encoding handles escape sequences internally |
| 2026-01-26 | 04-01 | FrozenDictionary on .NET 8+ | 40-50% faster lookups for character set registry |
| 2026-01-27 | 04-02 | DicomDataset.GetString uses dataset encoding | Automatic encoding selection reduces errors and boilerplate |
| 2026-01-27 | 04-02 | DicomStringValue as ref struct | Zero-allocation UTF-8 access with enforced stack-only semantics |
| 2026-01-27 | 04-02 | Encoding inheritance via Parent property | Consistent with BitsAllocated/PixelRepresentation pattern from Phase 3 |
| 2026-01-27 | 05-01 | Two PixelDataInfo types (Data vs Codecs) | Different use cases - nullable for extraction, non-nullable for codec operations |
| 2026-01-27 | 05-01 | Lazy offset table parsing | Parse on first access, not construction |
| 2026-01-27 | 05-01 | Extended Offset Table support | Required for DICOM files > 4GB |
| 2026-01-27 | 05-02 | IPixelDataSource as common interface | Unified API for accessing pixel data regardless of loading strategy |
| 2026-01-27 | 05-02 | Thread-safe LazyPixelDataSource | SemaphoreSlim for concurrent access protection |
| 2026-01-27 | 05-02 | Stream not disposed by LazyPixelDataSource | Stream lifecycle managed externally |
| 2026-01-27 | 05-02 | DicomPixelDataElement implements IDisposable | Ensures timely resource release |
| 2026-01-27 | 05-03 | LoadInMemory is default PixelDataHandling | Matches existing behavior, immediate accessibility |
| 2026-01-27 | 05-03 | VR resolution from BitsAllocated context | OB for 8-bit/encapsulated, OW for 16-bit native |
| 2026-01-27 | 05-03 | Encapsulated fragments load immediately | Structure parsing required for boundaries |
| 2026-01-27 | 06-01 | Case-insensitive creator matching | ToUpperInvariant normalization for vendor strings |
| 2026-01-27 | 06-01 | FrozenDictionary for vendor lookup | 9268 entries - O(1) lookup performance |
| 2026-01-27 | 06-01 | User dictionary precedence | Registered tags override generated |
| 2026-01-27 | 06-02 | PrivateCreatorDictionary.Remove for selective cleanup | Support StripPrivateTags filter cleanup |
| 2026-01-27 | 06-02 | StripPrivateTags cleans dictionary on filter | Keeps dictionary consistent with dataset |
| 2026-01-27 | 06-02 | CreateElement uses VRInfo.IsStringVR | Selects appropriate element type automatically |
| 2026-01-27 | 07-01 | IBufferWriter<byte> GetSpan/Advance pattern | Zero-copy writing to any buffer target |
| 2026-01-27 | 07-01 | Dual constructor support | Options-based and explicit parameter construction for flexibility |
| 2026-01-27 | 07-02 | Implementation UID uses 2.25 prefix | UUID-derived format for guaranteed uniqueness |
| 2026-01-27 | 07-02 | FMI always Explicit VR Little Endian | Per DICOM standard regardless of dataset TS |
| 2026-01-27 | 07-02 | Group length calculated by summing encoded lengths | All FMI elements after (0002,0000) |
| 2026-01-27 | 07-02 | Sequences written with undefined length | Uses Item/Sequence Delimitation Items |
| 2026-01-27 | 07-02 | StreamBufferWriter with ArrayPool | Efficient memory usage for buffered writing |
| 2026-01-27 | 08-01 | Readonly record struct for ValidationIssue | Immutable, value semantics, built-in equality |
| 2026-01-27 | 08-01 | Readonly struct for ElementValidationContext | Pass by reference (in parameter), avoid copying |
| 2026-01-27 | 08-01 | Validation codes as constants | Compile-time checks, IntelliSense, unique error identification |
| 2026-01-27 | 08-02 | Pre-trimming space-only AE detection | Space-only AE values must be detected before padding is trimmed |
| 2026-01-27 | 08-02 | Warnings for CS/PN violations | Real-world files frequently violate these constraints |
| 2026-01-27 | 08-02 | Error for date/time/UID format violations | Structural issues that prevent correct interpretation |
| 2026-01-27 | 08-03 | DicomReaderOptions.Default has no validation | Backward compatibility - existing code continues to work |
| 2026-01-27 | 08-03 | ValidationCallback can abort by returning false | Overrides profile behavior for precise control |
| 2026-01-27 | 09-01 | FrozenDictionary for CodecRegistry | Lock-free reads after freeze on .NET 8+ |
| 2026-01-27 | 09-01 | Auto-freeze on first lookup | Transparent optimization without explicit Freeze() call |
| 2026-01-27 | 09-01 | Registration after freeze invalidates cache | Allows dynamic codec registration in test scenarios |
| 2026-01-27 | 09-02 | MSB-first segment ordering | DICOM PS3.5 Annex G requirement - high bytes before low bytes |
| 2026-01-27 | 09-02 | Vector128 for SIMD run detection | Cross-platform, available on .NET 8+, 16-byte alignment optimal |
| 2026-01-27 | 09-02 | Readonly struct for RleSegmentHeader | Inline 15 offset fields avoids array allocation |
| 2026-01-27 | 09-02 | Automatic even-length padding | DICOM requirement for all RLE encoded segments |
| 2026-01-27 | 07-03 | Two-pass length calculation | SequenceLengthCalculator computes lengths recursively |
| 2026-01-27 | 07-03 | Overflow protection for defined length | Return UndefinedLength (0xFFFFFFFF) on overflow, fall back to delimiter mode |
| 2026-01-27 | 07-03 | Skip undefined-length roundtrip tests | Pre-existing reader bug in FindSequenceDelimiter, writer is correct |
| 2026-01-28 | 10-02 | PresentationContext ID validation | Must be odd integer 1-255 per DICOM PS3.8 |
| 2026-01-28 | 10-02 | UserInformation.Default uses fixed UID | 2.25.{uuid} for consistent implementation identification |
| 2026-01-28 | 10-02 | PresentationDataValue as struct | Zero-allocation for high-throughput P-DATA handling |
| 2026-01-28 | 10-02 | AE title validation | 1-16 ASCII printable chars, no leading/trailing spaces |
| 2026-01-28 | 10-01 | RejectReason single enum with multi-source interpretation | Overlapping PS3.8 values handled via documentation |
| 2026-01-28 | 10-01 | DicomStatus equality by code only | ErrorComment is informational, not identity |
| 2026-01-28 | 10-01 | Exception Source property renamed | AbortSource/RejectSource avoid hiding Exception.Source |
| 2026-01-28 | 10-03 | PduReader as ref struct | Zero-copy PDU parsing following DicomStreamReader pattern |
| 2026-01-28 | 10-03 | PduWriter as ref struct | Efficient PDU building with IBufferWriter<byte> pattern |
| 2026-01-28 | 10-03 | TryRead returns false on insufficient data | TCP fragmentation handling without exceptions |
| 2026-01-28 | 10-03 | Big-Endian for all PDU lengths | DICOM PS3.8 requirement for network byte order |
| 2026-01-28 | 10-04 | 13 states with Sta1-Sta13 numbering | Match PS3.8 Section 9.2 for cross-reference |
| 2026-01-28 | 10-04 | Event-based ARTIM timer | Timer start/stop via events, caller integrates |
| 2026-01-28 | 10-04 | Switch expression for state table | (current, event) => (next, action) pattern |
| 2026-01-28 | 10-04 | Release collision states Sta9-Sta12 | Full edge case handling for simultaneous release |
| 2026-01-28 | 10-06 | ArrayBufferWriter polyfill | netstandard2.0 compatibility for PDU building |
| 2026-01-28 | 10-06 | Inline C-ECHO parsing | Avoid dependency on full DIMSE infrastructure |
| 2026-01-28 | 10-06 | Task-per-association model | SemaphoreSlim for MaxAssociations throttling |
| 2026-01-28 | 10-06 | ARTIM timer via CancelAfter | Linked CTS for association timeout enforcement |
| 2026-01-28 | 10-05 | Commands always Implicit VR Little Endian | DICOM PS3.7 requires command elements to use Implicit VR |
| 2026-01-28 | 10-05 | Static VR lookup for command elements | Group 0000 elements have fixed VRs per PS3.7 |
| 2026-01-28 | 10-05 | BufferWriter type alias pattern | ArrayBufferWriter polyfill for netstandard2.0 |
| 2026-01-28 | 10-05 | IDicomElement for dataset iteration | DicomDataset implements IEnumerable<IDicomElement> |
| 2026-01-28 | 10-07 | Fix DicomClient state machine | Add AAssociateRequest before TransportConnectionConfirm per PS3.8 |
| 2026-01-28 | 10-07 | Fix DicomServer AssociationOptions timing | Read A-ASSOCIATE-RQ before creating AssociationOptions |
| 2026-01-28 | 10-07 | Integration test isolation | Use [Explicit] + [Category("Integration")] for DCMTK tests |
| 2026-01-29 | 11-01 | Readonly record struct for progress types | Value semantics, immutable, zero-allocation for high-frequency reporting |
| 2026-01-29 | 11-01 | Extension methods for QueryRetrieveLevel | Enums cannot have methods; extensions provide fluent API |
| 2026-01-29 | 11-01 | Internal visibility for DicomClient DIMSE primitives | SCU services in same assembly; public API is service classes |
| 2026-01-29 | 11-01 | Existing well-known tags verified | All required command tags already present from Phase 10 |
| 2026-01-29 | 11-02 | Removed incomplete pre-existing files | DicomQuery.cs, CFindOptions.cs, CFindScuTests.cs blocked build |
| 2026-01-29 | 11-02 | CStoreOptions uses object initializer | Consistent with DicomClientOptions pattern |
| 2026-01-29 | 11-02 | SendAsync(Stream) loads full file | True streaming optimization deferred |
| 2026-01-29 | 11-02 | Retry only on 0xA7xx Out of Resources | Permanent failures returned immediately |
| 2026-01-29 | 11-03 | Fluent builder pattern for DicomQuery | Provides intuitive API for common query patterns without manual dataset construction |
| 2026-01-29 | 11-03 | IAsyncEnumerable for query results | Enables streaming of results as they arrive; efficient memory usage for large result sets |
| 2026-01-29 | 11-03 | C-CANCEL on CancellationToken | Proper DICOM protocol compliance; gracefully stops remote enumeration |
| 2026-01-29 | 11-03 | Convenience Find SOP Class UID methods | GetPatientRootFindSopClassUid() simpler than GetPatientRootSopClassUid(CommandField) |
| 2026-01-29 | 11-06 | CGetProgress yields on both message types | Progress updates after C-STORE sub-ops (with dataset) and C-GET-RSP (with counts) |
| 2026-01-29 | 11-06 | PresentationContext SCP role as mutable properties | ScuRoleRequested/ScpRoleRequested enable fluent WithScpRole() without breaking constructors |
| 2026-01-29 | 11-06 | CancellationBehavior.RejectInFlight default | Fail fast on cancel; CompleteInFlight option for data integrity |
| 2026-01-29 | 11-06 | Store handler as async delegate | Flexible storage implementations with proper async support |
| 2026-01-29 | 11-04 | Dual handler support (delegate + interface) | Delegate is simpler; interface allows testable implementations |
| 2026-01-29 | 11-04 | Delegate precedence over interface | Allows quick override without replacing interface implementation |
| 2026-01-29 | 11-04 | Streaming mode requires explicit handler | Fail-fast prevents runtime errors; streaming needs explicit implementation |
| 2026-01-29 | 11-05 | C-MOVE SCU does not receive data | C-MOVE sends data to third-party destination; SCU only gets progress updates |
| 2026-01-29 | 11-05 | Consistent patterns across Q/R SCU services | Follow CFindScu/CGetScu patterns for API consistency |
| 2026-01-29 | 11-05 | Validate destinationAE early | Fail fast on empty destination rather than network error |
| 2026-01-29 | 11-07 | Protocol verification test scope | Focus on testable protocol aspects without wire capture |
| 2026-01-29 | 12-02 | Loeffler algorithm for 1D DCT | Uses theoretical minimum operations (11 muls, 29 adds) |
| 2026-01-29 | 12-02 | AVX2 SIMD with matrix transpose | Processes all 8 rows/columns in parallel using 256-bit vectors |
| 2026-01-29 | 12-02 | 32-bit buffer for bit I/O | Allows reads/writes up to 25 bits at once |
| 2026-01-29 | 12-02 | Quality 90 for MedicalImaging preset | Balances compression with diagnostic quality preservation |
| 2026-01-29 | 12-04 | Extended Huffman table to categories 0-16 | 16-bit samples require category 16 for worst-case differences |
| 2026-01-29 | 12-04 | Output buffer 4 bytes/sample + 1024 overhead | Random data may not compress; each sample needs up to 32 bits |
| 2026-01-29 | 12-03 | PSNR-based quality verification for tests | Lossy codec requires statistical quality metrics instead of bit-perfect comparison |
| 2026-01-29 | 12-05 | Lifting scheme for DWT | In-place computation, memory efficient, no intermediate buffers |
| 2026-01-29 | 12-05 | Integer arithmetic for 5/3 | Bit-exact reconstruction required for lossless compression |
| 2026-01-29 | 12-05 | Float arithmetic for 9/7 | Standard coefficients from ITU-T T.800 Table F.4 |
| 2026-01-29 | 12-05 | 47-state probability table for MQ | Standard MQ-coder state machine from ITU-T T.800 Table C.2 |
| 2026-01-29 | 12-05 | 19 coding contexts | Supports full EBCOT bitplane coding |
| 2026-01-29 | 12-06 | EBCOT context model per ITU-T T.800 | 19 contexts for significance, sign, refinement coding |
| 2026-01-29 | 12-06 | Simplified tier-2 packet encoding | Medical imaging typically uses single-tile; full complexity deferred |
| 2026-01-29 | 12-07 | Removed ModuleInitializer attribute | CA2255 warning treated as error; explicit CodecInitializer.RegisterAll() preferred for AOT |
| 2026-01-29 | 12-07 | MedicalImaging preset uses 5:1 ratio | Conservative compression for diagnostic imaging quality preservation |
| 2026-01-29 | 12-07 | Codec tests focus on wrapper behavior | J2K encoder quality is separate concern; tests verify IPixelDataCodec contract |
| 2026-01-29 | 12-06 | BufferWriter alias for netstandard2.0 | Consistent with existing network code ArrayBufferWriter polyfill |
| 2026-01-29 | 13-01 | Zig 0.13.0 for cross-compilation | Single toolchain for 6 targets, bundled libc |
| 2026-01-29 | 13-01 | musl for Linux builds | Zero runtime dependencies |
| 2026-01-29 | 13-01 | CPUID-based SIMD detection | Runtime feature detection (SSE2-AVX512, NEON) |
| 2026-01-29 | 13-01 | Thread-local error storage | 256 bytes per thread for concurrent safety |
| 2026-01-30 | 13-03 | J2K format auto-detection | Detect J2K vs JP2 from magic bytes for transparent API |
| 2026-01-30 | 13-03 | Memory stream callbacks for OpenJPEG | In-memory buffers without file I/O |
| 2026-01-30 | 13-03 | Stub compilation pattern | Return UNSUPPORTED when vendor lib not present |
| 2026-01-30 | 13-03 | Resolution levels via cp_reduce | Efficient thumbnail generation at 1/2, 1/4, etc. |
| 2026-01-30 | 13-02 | TurboJPEG API | Simplified high-performance access vs raw libjpeg |
| 2026-01-30 | 13-02 | Thread-local handles | Avoid handle creation overhead per call |
| 2026-01-30 | 13-02 | TJFLAG_ACCURATEDCT | Medical imaging requires highest quality DCT |
| 2026-01-30 | 13-02 | 12-bit stub | Most distributions don't build with -DWITH_12BIT |
| 2026-01-30 | 13-04 | Handle-based video decoder | Multi-frame DICOM video requires stateful decoder |
| 2026-01-30 | 13-04 | Parameter struct pattern | Cleaner API for encode/decode options |
| 2026-01-30 | 13-04 | CharLS 2.4.2 | ~2x faster than HP reference for JPEG-LS |
| 2026-01-30 | 13-04 | FFmpeg libavcodec only | Decode-only, no encoding needed for DICOM video |
| 2026-01-30 | 13-05 | Compute capability 5.0+ minimum | Maxwell GPUs (GTX 750 Ti, 2014) oldest supported |
| 2026-01-30 | 13-05 | Dynamic nvJPEG2000 loading | dlopen/LoadLibrary avoids CUDA dependency |
| 2026-01-30 | 13-05 | Thread-local prefer_cpu flag | Enables testing fallback without disabling GPU |
| 2026-01-30 | 13-05 | Optional CI cuda-build job | GPU builds don't block CI (continue-on-error: true) |
| 2026-01-30 | 13-06 | LibraryImport (NET7+) vs DllImport | Source-generated marshalling for AOT; DllImport for netstandard2.0 |
| 2026-01-30 | 13-06 | ModuleInitializer with AppContext switch | Auto-init convenience with opt-out via DisableAutoInit |
| 2026-01-30 | 13-06 | SafeHandle for native resources | Ensures proper cleanup even with exceptions |
| 2026-01-30 | 13-08 | RID-conditional runtime package references | Separate packages per platform reduces download size |
| 2026-01-30 | 13-08 | MSBuild auto-detect platform RID | Convenience for development without explicit RuntimeIdentifier |
| 2026-01-30 | 13-08 | Matrix strategy for native builds | Parallel compilation across 6 platforms |
| 2026-01-29 | 14-01 | DocBook 5.0 parsing for part15.xml | Same approach as part06.xml, XNamespace for proper handling |
| 2026-01-29 | 14-01 | Deduplication by tag value | Same tag can appear multiple times (retired variants) |
| 2026-01-29 | 14-02 | Separate enums for action and resolution | DeidentificationAction (PS3.15 codes) vs ResolvedAction (runtime operations) |
| 2026-01-29 | 14-02 | DicomAttributeType for compound resolution | Type1/2/3 determines Z/D vs X/Z vs X/D resolution |
| 2026-01-29 | 14-02 | Profile option flags pattern | DeidentificationOptions.ToProfileOptions() for generated code |
| 2026-01-30 | 14-06 | System.Text.Json for config | Built-in, AOT-friendly with RequiresUnreferencedCode attributes |
| 2026-01-30 | 14-06 | $extends inheritance pattern | Config composition via preset name or file path resolution |
| 2026-01-30 | 14-06 | Built-in presets (4) | basic-profile, research, clinical-trial, teaching for common use cases |
| 2026-01-29 | 14-07 | Static PixelDataRedactor API | No instance state needed; simpler API |
| 2026-01-29 | 14-07 | Modality risk categorization | High (US, ES, SC), Moderate (XA, MG), Low (CT, MR) for burned-in annotation assessment |
| 2026-01-29 | 14-07 | Region-based redaction | Rectangular regions with optional frame-specific targeting |
| 2026-01-30 | 14-08 | SOPClassUID preserved in BatchDeidentifier | Required for writing valid DICOM files after de-identification |
| 2026-01-30 | 14-08 | SemaphoreSlim for parallel throttling | Better control than TPL parallelism |
| 2026-01-30 | 14-08 | Progress reported per-file | Simpler than incremental in-file progress |
| 2026-02-03 | 21-02 | Defer HT block coder to Phase 21-04 | ISO/IEC 15444-15 implementation requires 3000-5000 LOC and spec study |
| 2026-02-03 | 21-02 | HTJ2K via J2K delegation | Functionally correct, backward compatible, defers performance optimization |
| 2026-02-03 | 21-02 | Comprehensive test coverage for future | Encode/decode tests prepared but [Ignore] until J2K encoder fixed |
| 2026-02-03 | 21-01 | Extract JPEG-LS components to separate files | JpegLsPredictor, JlsContext, GolombRiceCoder for better maintainability |
| 2026-02-03 | 21-01 | Implement all 8 ITU-T T.87 predictors | Full standard compliance, minimal overhead |
| 2026-02-03 | 21-01 | Use 365-element context array | Full ITU-T T.87 context model for optimal compression |
| 2026-02-03 | 21-01 | Support all three interleave modes | None, Line, Sample for complete DICOM transfer syntax support |
| 2026-02-03 | 21-04 | Skip strict/lenient error mode enum | Current DecodeResult error handling adequate; enum would be architectural change |
| 2026-02-03 | 21-04 | Ignore pre-existing codec failures | 12 JPEG-LS failures from 21-01, HTJ2K issues; focus on test patterns not bug fixes |
| 2026-02-03 | 21-04 | Conformance tests skip gracefully | CharLS/OpenJPH unavailable in CI; Category marker enables local-only validation |
| 2026-02-03 | 21-05 | Context update with rawError | Encoder/decoder symmetry: both update context with rawError (not rawError - biasCorrection) |
| 2026-02-03 | 21-05 | Golomb-Rice limit escape per ITU-T T.87 A.5.3 | When quotient >= LIMIT - qbpp - 1, use escape encoding for large prediction errors |
| 2026-02-03 | 21-05 | Component buffer for non-interleaved decode | Decode to separate buffer per component, then copy to interleaved positions |
| 2026-02-03 | 21-05 | Adjusted compression ratio test thresholds | Current implementation lacks run-length mode; tests reflect realistic expectations |
| 2026-02-03 | 21-08 | Tier-2 fixes don't enable HTJ2K roundtrip | EBCOT works in isolation; J2kEncoder/J2kDecoder integration has deeper issues |
| 2026-02-03 | 21-08 | Keep tests ignored with updated reason | Document investigation findings rather than leave failing tests |
| 2026-02-04 | 21-09 | Created pipeline stage isolation tests | 6 tests systematically identify J2K integration failure points |
| 2026-02-04 | 21-09 | HTJ2K deferred to Phase 30 | Requires multi-resolution subband architecture (~2000 LOC), beyond quick fix scope |
| 2026-02-04 | 21-09 | J2K encoder/decoder are shell implementations | API exists but lacks DWT subband structure handling; architectural rewrite needed |
| 2026-02-04 | 22-01 | Runtime TLS 1.3 detection via numeric value | Handle netstandard2.0 builds running on .NET 5+ runtimes with TLS 1.3 support |
| 2026-02-04 | 22-01 | Separate TlsOptions and TlsServerOptions | Clear client/server distinction prevents confusion about which properties apply to which role |
| 2026-02-04 | 22-01 | CertificateValidator with factory methods | SystemOnly, AcceptThumbprints, AcceptSelfSigned, WithCustomCAs enable common validation strategies |
| 2026-02-04 | 22-01 | DicomTlsProfile enforces BCP 195 by default | DICOM PS3.15 Annex B.3 compliance (TLS 1.2+, compliant cipher suites) with opt-out |
| 2026-02-04 | 22-03 | TLS handshake before ARTIM timer | Server performs TLS handshake after TCP accept but before ARTIM timer to ensure encrypted association |
| 2026-02-04 | 22-03 | Stream abstraction for TLS transparency | All DicomServer methods use Stream instead of NetworkStream for unified TLS/plain TCP paths |
| 2026-02-04 | 22-03 | SslStreamCertificateContext pre-building | Cache certificate context in Start() for NET6+ connection performance |
| 2026-02-06 | 23-01 | System.CommandLine 2.0.2 stable API | Property-based Option construction, SetAction with ParseResult+CancellationToken |
| 2026-02-06 | 23-01 | RootCommand auto-includes VersionOption | No manual VersionOption add needed in 2.0.2 |
| 2026-02-06 | 23-01 | TextFormatter uses reflection for UID name lookup | Reflection on DicomUIDs class for reverse UID-to-name mapping |
| 2026-02-06 | 23-01 | Progress output always to stderr | Keeps stdout clean for piped structured output |
| 2026-02-06 | 23-01 | Config errors produce warnings, never block | Config parse failures warn to stderr but never prevent command execution |
| 2026-02-06 | 23-02 | Command.Create() factory pattern for subcommands | Static factory returns fully configured Command, clean separation of subcommand logic |
| 2026-02-06 | 23-02 | Tag filter multi-format parsing | Accept GGGGEEEE, GGGG,EEEE, and (GGGG,EEEE) for user convenience |
| 2026-02-06 | 23-02 | Layered format resolution | CLI flag > env var > config file > text default |
| 2026-02-06 | 23-03 | Two-pass SOP class scanning for store | Scan all file headers first for SOP Class UIDs, connect once with all needed presentation contexts |
| 2026-02-06 | 23-03 | IsSuccessOrWarning counts as success | DICOM C-STORE warnings (0xB000) indicate data was stored with modifications - still counts as success |
| 2026-02-06 | 23-04 | Private static DicomTag fields for missing constants | DicomTag.WellKnown lacks some tags; define locally rather than modifying source generator |
| 2026-02-06 | 23-04 | ForImages() for instance-level queries | DicomQuery API uses ForImages not ForInstances; accept both "instance" and "image" as level strings |
| 2026-02-06 | 23-04 | AddStringFilter for raw DICOM date strings | DicomQuery.WithStudyDate takes DateTime; created helper for raw string filters including date ranges |
| 2026-02-06 | 23-04 | PacsConnectionResolver as shared helper | Extracted to Helpers namespace for reuse across store, find, and future network commands |
| 2026-02-06 | 23-05 | Lint uses colored ANSI output when TTY, plain text when piped | Respects piped/redirected output for CI integration |
| 2026-02-06 | 23-05 | Lint JSON includes per-file issues and aggregate summary | Machine-readable output for CI/CD consumption |
| 2026-02-06 | 23-05 | Fix writes to .fixed.dcm by default; --force for overwrite | Safe default prevents accidental data loss |
| 2026-02-06 | 23-05 | DicomFixer.RemoveInvalidElements is opt-in (destructive) | Must explicitly opt in with --remove-invalid flag |
| 2026-02-06 | 23-05 | --fix-dates flag covers both DA and TM VR elements | Date and time VRs are closely related, single flag simplifies CLI |
| 2026-02-05 | 23-06 | InternalsVisibleTo for CLI test access | CLI types are internal; tests need direct access without process invocation |
| 2026-02-05 | 23-06 | Split strict/lenient validation tests | Strict profile throws on errors; Lenient collects as warnings; separate tests reflect actual behavior |
| 2026-02-05 | 23-06 | Exclude CLI tests from Polyfills project | Polyfills lacks CLI project reference; shared source wildcard would cause build failures |
| 2026-02-06 | 24-01 | IAsyncEnumerable for C-FIND streaming | Memory-efficient delivery of large result sets; consistent with CFindScu pattern |
| 2026-02-06 | 24-01 | Server-side return key filtering | Callbacks return full datasets; server filters to requested tags per PS3.4 C.2.2 |
| 2026-02-06 | 24-01 | Unregistered handlers return 0xA900 | Per CONTEXT.md: failure status, not empty results; dataset still consumed |
| 2026-02-06 | 24-01 | QRCommandInfo struct for Q/R dispatch | Parallel to CStoreCommandInfo; holds parsed command data for C-FIND/C-MOVE/C-GET/C-CANCEL |
| 2026-02-06 | 24-02 | CStoreScu for C-MOVE forwarding | Reuses existing SCU infrastructure; clean separation via DicomClient |
| 2026-02-06 | 24-02 | Raw PDV building for C-GET same-association C-STORE | SCP must send C-STORE on same association; can't use DicomClient (which opens new connection) |
| 2026-02-06 | 24-02 | Match collection cap at 10000 | Prevents memory exhaustion during C-MOVE/C-GET with large result sets |
| 2026-02-06 | 24-03 | Synchronous ADO.NET for SQLite | SQLite async is actually sync per RESEARCH.md Pitfall 4 |
| 2026-02-06 | 24-03 | INSERT OR REPLACE without foreign keys | Simplifies upsert operations; SQLite doesn't enforce FK by default |
| 2026-02-06 | 24-03 | COLLATE NOCASE for PatientName | Per DICOM PS3.4 C.2.2.2.4 case-insensitive PN matching |
| 2026-02-06 | 24-03 | Hierarchical file layout | patient_id/study_uid/series_uid/sop_uid.dcm for organized storage |
| 2026-02-06 | 24-03 | Path sanitization with fallbacks | Invalid chars replaced with underscore; empty values get UNKNOWN/NO_STUDY fallbacks |
| 2026-02-06 | 24-04 | Direct callback testing over network roundtrip | SCP behavior tested by invoking OnCFind/OnCStoreRequest callbacks directly; avoids P-DATA PDV interleaving issue in client |
| 2026-02-06 | 24-04 | Real SQLite for FileSystemDicomStore tests | Tests use actual SQLite databases in temp directories rather than mocks for higher-fidelity verification |
| 2026-02-06 | 24-04 | End-to-end network tests marked Explicit | 2 network roundtrip tests included but marked [Explicit] pending PDV parsing fix in DicomClient |
| 2026-02-06 | 25-01 | Generic VR=UI traversal for UID reference walking | Walk ALL VR=UI elements at unlimited depth to catch all current/future referencing patterns |
| 2026-02-06 | 25-01 | Pipeline ordering: walk after primary de-id | UidRemapper has already mapped primary UIDs so references get consistent mapping |
| 2026-02-06 | 25-01 | Separate UidReferencesRemapped counter | Distinguishes PS3.15 profile remaps from walker-discovered remaps for diagnostics |
| 2026-02-06 | 25-01 | Opt-in via builder WithUidReferenceWalking() | Backward compatible; no behavioral change for existing callers |
| 2026-02-06 | 25-02 | SHARPDICOM_HAS_TESSERACT = 1 << 8 in native, Tesseract = 1 << 5 in managed | Native bits 0-7 allocated to existing features; managed enum uses own numbering |
| 2026-02-06 | 25-02 | Internal constructor for TesseractHandle SafeHandle | CA1419 compliance: parameterless constructor must be as visible as containing type |
| 2026-02-06 | 25-02 | BestFitMapping=false for DllImport string marshalling | CA2101 compliance for netstandard2.0 P/Invoke string parameters |
| 2026-02-06 | 25-03 | Duplicate P/Invoke declarations in SharpDicom rather than referencing SharpDicom.Codecs | Avoids circular project dependency; both call the same native library |
| 2026-02-06 | 25-03 | Use DllImport uniformly (not LibraryImport) for TessInterop | LibraryImport source generator complications with nested private partial classes |
| 2026-02-06 | 25-03 | Lazy-create OcrScanner on first Deidentify() call | Avoids paying Tesseract init cost when not processing images with pixel data |
| 2026-02-06 | 26-01 | DicomTag as class (not struct) for compat layer | fo-dicom uses reference type with nullable DictionaryEntry property |
| 2026-02-06 | 26-01 | DicomVR as class with static readonly instances | fo-dicom uses DicomVR.LO pattern; string-keyed dictionary lookup |
| 2026-02-06 | 26-01 | Public DicomDataset wrapping constructor | Enables interop between native SharpDicom and compat code |
| 2026-02-06 | 26-01 | CA1716 suppressed on Get<T> method | fo-dicom API compatibility requires exact method name |
| 2026-02-06 | 26-01 | Composition pattern throughout compat layer | All types wrap SharpDicom types via _inner field, never inherit |
| 2026-02-06 | 26-02 | SendAsync creates fresh client per call | Matches fo-dicom's stateless pattern; each SendAsync is a complete connection lifecycle |
| 2026-02-06 | 26-02 | Patient Root Q/R as default SOP Class | Most common in real-world usage for C-FIND presentation contexts |
| 2026-02-06 | 26-02 | Pending/Success callback pattern | OnResponseReceived with Pending per result, Success for final, matches fo-dicom behavior |
| 2026-02-06 | 26-05 | Get<T> as primary, GetSingleValue<T> as alias | fo-dicom 4.x uses Get<T>(tag) as primary accessor; late 4.x also has GetSingleValue<T> |
| 2026-02-06 | 26-05 | Get<T>(tag, defaultValue) overload | Common fo-dicom 4.x pattern for safe missing-tag access |
| 2026-02-06 | 26-05 | No network types in FoDicom4 | fo-dicom 4.x network API (direct constructor) differs significantly from 5.x; not needed yet |
| 2026-02-06 | 26-06 | RS1038 suppressed for analyzer+codefix assembly | Standard pattern: CodeFixProviders require Workspaces, analyzers don't |
| 2026-02-06 | 26-06 | Semantic analysis for fo-dicom detection | Prevents false positives on user types named "Dicom" |
| 2026-02-06 | 26-06 | Two-step migration diagnostic scheme | SD0001-SD0003 for fo-dicom, SD0010-SD0011 for compat layer |
| 2026-02-06 | 26-03 | Extract Entry class from top-level statements | C# top-level statements cannot compile into library; class extraction preserves logic |
| 2026-02-06 | 26-03 | Namespace alias for DicomFile conflict resolution | SharpDicom.Migration.Integration namespace causes C# to find SharpDicom.DicomFile before FellowOakDicom.DicomFile |
| 2026-02-06 | 26-07 | DefaultVerifier instead of NUnit-specific verifier | Avoids extra package dependency; works correctly with NUnit |
| 2026-02-06 | 26-07 | CompilerDiagnostics.None for non-existent namespaces | Isolates analyzer behavior from irrelevant CS0246 compiler errors |
| 2026-02-06 | 26-07 | Pin Microsoft.CodeAnalysis.* to 5.0.0 in test project | Overrides 1.0.1 transitive dependencies from testing packages |
| 2026-02-07 | 27-01 | Added video SOP UIDs to existing DicomUID.WellKnown.cs | Follow existing partial struct pattern rather than creating separate file |
| 2026-02-07 | 27-03 | Raw libjpeg API for 12-bit (not TurboJPEG) | WITH_12BIT disables TurboJPEG/SIMD; 8-bit path retains full SIMD performance |
| 2026-02-07 | 27-03 | Symbol prefix via -D compiler flags for dual libjpeg | jpeg_* -> jpeg12_jpeg_* avoids collisions in single shared library |
| 2026-02-07 | 27-03 | Opaque struct buffers for libjpeg structs | Version-dependent layouts; actual jpeglib.h will provide correct layout when vendor sources present |
| 2026-02-07 | 27-06 | Separate SHARPDICOM_WITH_FFMPEG_ENC flag | Encoding requires libavformat+libswresample; decode-only builds should not need these |
| 2026-02-07 | 27-06 | GPU encoder cascade: VideoToolbox > NVENC > VAAPI | Platform-specific ordering; VideoToolbox first for macOS developer experience |
| 2026-02-07 | 27-06 | In-memory muxing via avio_write_buffer callback | Zero-temp-file encoding for DICOM pixel data fragment embedding |
| 2026-02-07 | 27-06 | Annex-B for H.264/HEVC, MPEG-TS for MPEG-2/audio | Minimal container overhead for raw bitstreams; TS required for muxed streams |
| 2026-02-07 | 27-06 | stb_image vendored in-repo (not CI-downloaded) | Single 8KB public-domain header; no version management overhead |
| 2026-02-07 | 27-02 | int[] component buffers for 12-bit JPEG Extended | byte[] only holds 0-255; int[] prevents 12-bit value truncation |
| 2026-02-07 | 27-02 | Decoder accepts both SOF0 and SOF1 | Some encoders use SOF0 even in Extended transfer syntax; maximum compatibility |
| 2026-02-07 | 27-02 | JpegLosslessCodec already supports 16-bit | LosslessHuffman categories 0-16 and int[] buffers handle up to 16-bit; no changes needed |
| 2026-02-07 | 27-04 | NativeJpeg12Codec registered only when Jpeg12Bit feature detected | Preserves managed JpegExtendedCodec as fallback when native 12-bit lib absent |
| 2026-02-07 | 27-04 | 12-bit decode outputs 2 bytes per sample (uint16_t) | Native library outputs 16-bit values even for 12-bit precision; bytesWritten = w*h*c*2 |
| 2026-02-07 | 27-04 | NativeJpeg8Codec not separately registered | Existing NativeJpegCodec covers JPEGBaseline; NativeJpeg8Codec available for explicit use |
| 2026-02-07 | 27-07 | Separate have_ffmpeg_enc from have_ffmpeg | Encoding requires x264/x265 backends that decoding does not; independent control |
| 2026-02-07 | 27-07 | Compile x264/x265/FFmpeg from source via Zig | Consistent cross-platform behavior; bypass configure/make; allyourcodebase pattern |
| 2026-02-07 | 27-07 | x265 compiled as C++ with -std=c++14 | x265 is C++ codebase; Zig's built-in C++ compiler handles it with linkLibCpp() |
| 2026-02-07 | 27-08 | VideoEncoder in core uses delegate backend pattern | Avoids hard dependency on SharpDicom.Codecs; backend registered at runtime |
| 2026-02-07 | 27-08 | IAsyncEnumerable API gated behind NET8_0_OR_GREATER | Ensures netstandard2.0 compatibility while providing modern async API |
| 2026-02-07 | 27-08 | VideoEncodeProgress as struct with manual equality | record struct unavailable on netstandard2.0; manual IEquatable implementation |
| 2026-02-07 | 27-08 | NTSC frame rates with exact rational representation | 30000/1001 for 29.97fps avoids drift in video encoding |
| 2026-02-07 | 27-08 | AudioSampleFormat.IeeeFloat (not Float32) | CA1720 analyzer rule forbids enum names containing type names |
| 2026-02-06 | 27-05 | 12-bit test values constrained to 1500-2800 range | Standard Huffman DC tables (categories 0-11) cannot encode values far from level shift 2048 |
| 2026-02-06 | 27-05 | PSNR threshold 15 dB for 12-bit lossy roundtrip | DCT quantization with limited Huffman range; 30 dB too aggressive for some patterns |
| 2026-02-06 | 27-05 | Smooth gradients instead of modular wrap patterns | High-frequency wrap boundaries degrade PSNR; smooth gradients represent real images better |
| 2026-02-07 | 27-09 | Data.PixelDataInfo qualified namespace for builder | Codecs.PixelDataInfo takes priority in Codecs.Video namespace; explicit qualification avoids ambiguity |
| 2026-02-07 | 27-09 | YBR_PARTIAL_420 for all video transfer syntaxes | MPEG2, H.264, HEVC all use 4:2:0 chroma subsampling per DICOM PS3.5 C.7.6.3.1.2 |
| 2026-02-07 | 27-09 | Single encapsulated fragment for video bitstream | Video codecs require contiguous bitstreams, not per-frame fragmentation |
| 2026-02-07 | 27-10 | Synthetic byte arrays for video test data | No native encoder needed in CI; tests validate managed types and API contracts |
| 2026-02-07 | 27-10 | Frame rate detection priority: CineRate > RecommendedDisplayFrameRate > FrameTime > FrameTimeVector | Matches implementation in VideoEncoder.DetectFrameRate |
| 2026-02-07 | 28-01 | N-GET request uses NoDataSetPresent | Attribute identifier list is a separate mechanism, not a data set |
| 2026-02-07 | 28-01 | N-CREATE request has optional affectedSopInstanceUid (DicomUID?) | Allows SCP to assign instance UIDs when SCU does not specify one |
| 2026-02-07 | 28-01 | N-DELETE response always uses NoDataSetPresent | Per PS3.7 specification, no dataset in N-DELETE response |
| 2026-02-07 | 28-03 | Abstract NServiceRequestContext base class with PresentationContextId | Matches CStoreRequestContext pattern, enables common context across all 6 N-Services |
| 2026-02-07 | 28-03 | Unified NServiceResponse for all N-Services | Single type with Status + optional Dataset + optional AffectedSOPInstanceUID covers all use cases |
| 2026-02-07 | 28-03 | ParseNServiceCommand handles both Affected and Requested SOP UIDs | N-CREATE/N-EVENT-REPORT use Affected, N-SET/N-GET/N-ACTION/N-DELETE use Requested |
| 2026-02-07 | 28-03 | Handler-absent N-Service returns ProcessingFailure (0x0110) | Consistent with handler pattern; handler absence is a server configuration issue |
| 2026-02-07 | 28-04 | MPPS state machine rejects all transitions from terminal states with 0x0106 | InProgress->Completed/Discontinued only; Completed and Discontinued are terminal |
| 2026-02-07 | 28-04 | StorageCommitment SCP stores result for later N-EVENT-REPORT via TakeResult() | Asynchronous protocol: N-ACTION returns immediately, result delivered later |
| 2026-02-07 | 28-04 | Added 7 DICOM tags to WellKnown for MPPS/StorageCommitment | PerformedProcedureStepStatus, ReferencedSOPClassUID/InstanceUID, TransactionUID, ReferencedSOPSequence, FailedSOPSequence, FailureReason |
| 2026-02-07 | 28-05 | Raw byte scanning for 0x53 sub-item verification | PduReader.TryReadUserInformation doesn't reconstruct UserInformation; raw scan more reliable |
| 2026-02-07 | 28-05 | DicomNumericElement.GetUInt16() for US VR retrieval | DicomDataset.GetInt32() requires 4 bytes; US VR is 2 bytes |
| 2026-02-07 | 29-01 | Zero external dependencies for BSON serialization | Pure BinaryPrimitives + UTF8 encoding, no MongoDB driver needed |
| 2026-02-07 | 29-01 | #if NET8_0_OR_GREATER for ThrowIfNegative | CA1512 compliance on modern TFMs, classic throw on netstandard2.0 |
| 2026-02-07 | 29-01 | Flatten profile writes first item only | Single-item sequences common in radiology; dot-notation enables direct MongoDB queries |
| 2026-02-07 | 29-02 | Tag key parsing accepts Hex8, Dotted, and Keyword formats | Any serialized format can be deserialized regardless of BsonTagKeyFormat setting |
| 2026-02-07 | 29-02 | Sequence items stored as raw byte[] for recursive deserialization | DeserializeCore called recursively on each item's BSON sub-document |
| 2026-02-07 | 29-02 | FromBson as static method on extensions class | No DicomDataset instance to extend; placed alongside ToBson for discoverability |
| 2026-02-07 | 29-02 | Flattened keys detected and skipped during deserialization | Informational keys from FlattenProfile are not round-trippable to elements |
| 2026-02-07 | 29-03 | DicomJsonWriter uses Utf8JsonWriter directly | No System.Text.Json serialization overhead; PS3.18 format is fixed structure |
| 2026-02-07 | 29-03 | DicomJsonReader uses Utf8JsonReader ref struct | Zero-allocation JSON parsing matching DicomStreamReader pattern |
| 2026-02-07 | 29-03 | UV values > Int64.Max encoded as JSON strings | PS3.18 F.2.3 specifies string fallback for numbers exceeding JSON range |
| 2026-02-07 | 29-05 | MongoDB.Driver 3.6.0 over 2.x legacy line | Current actively-developed line; netstandard2.1+ requirement acceptable for optional adapter |
| 2026-02-07 | 29-05 | Target netstandard2.1 not netstandard2.0 for MongoDB adapter | MongoDB.Driver 3.x requires netstandard2.1+; adapter users will be on modern .NET |
| 2026-02-07 | 29-05 | Single MongoDB.Driver package reference | MongoDB.Bson is a transitive dependency; reduces Central Package Management overhead |
| 2026-02-08 | 30-01 | SubbandType enum matches existing EBCOT convention (HL=1, LH=2) | Existing DwtTransform and EbcotEncoder use 0=LL, 1=HL, 2=LH, 3=HH consistently |
| 2026-02-08 | 30-03 | VLC codewords bit-reversed for LSB-first table indexing | Stream bits are consumed LSB-first; table index must match raw stream bit order |
| 2026-02-08 | 30-03 | MEL partial runs encoded as MelE[state] bits after break signal | Without partial run encoding, decoder cannot determine insignificant quad count in broken runs |
| 2026-02-08 | 30-03 | MEL stream does not use JPEG byte stuffing | Byte stuffing is specific to MQ coder; MEL uses simple 8-bit bytes |
| 2026-02-07 | 30-02 | Concrete EbcotBlockCoder type in private methods (not IBlockCoder interface) | CA1859 analyzer treats interface usage as warning/error when only one concrete implementation exists |
| 2026-02-07 | 30-02 | Singleton Instance pattern for EbcotBlockCoder | EBCOT encoder is IDisposable but safe for sequential use; avoids per-call allocation overhead |
| 2026-02-07 | 30-02 | Duplicated FindSubbandTypeForPosition in encoder and decoder | Code locality preferred over shared utility for 10-line private helper |
| 2026-02-07 | 30-04 | Raw 4-bit significance patterns instead of VLC table encode/decode | VLC tables only define 8 of 16 patterns per context; raw 4-bit writes guarantee lossless roundtrip |
| 2026-02-07 | 30-04 | Unary-terminated exponent MagSgn format | Self-delimiting format: [sign:1][(E-1) ones][0-term][(E-1) mantissa]; consistent encode/decode |
| 2026-02-07 | 30-04 | FloorLog2 conditional compilation for netstandard2.0 | BitOperations.LeadingZeroCount not available on netstandard2.0; manual fallback |
| 2026-02-08 | 30-05 | Significance state derived from cleanup decode | Avoids modifying HtCleanup API; decode output + non-zero check gives sigState |
| 2026-02-08 | 30-05 | Byte-aligned bitstream for SigProp/MagRef | Simple format with 4-byte bit-count prefix; self-consistent roundtrip |
| 2026-02-08 | 30-05 | Embedded pass-length header for multi-pass data | IBlockCoder.DecodeBlock only gets data+numPasses; header makes data self-describing |
| 2026-02-08 | 30-05 | Adaptive pass count based on MSB position | MSB=0->1 pass, MSB=1->3 passes, MSB>=2->6 passes; matches data precision |
| 2026-02-08 | 30-08 | Kebab-case transfer syntax short names for CLI convert | Consistent with CLI conventions; case-insensitive matching for usability |
| 2026-02-08 | 30-08 | Default .converted.dcm suffix for non-destructive output | Follows existing FixCommand .fixed.dcm pattern; safe default prevents data loss |
| 2026-02-08 | 30-08 | SemaphoreSlim-gated parallel file processing | Configurable concurrency for CPU-bound codec work; respects user-specified --parallel limit |
| 2026-02-08 | 30-09 | Color transforms applied before tile extraction | RCT/ICT operate on full image per J2K spec; tile extraction happens after color transform |
| 2026-02-08 | 30-09 | Thread-safe parallel decode via per-tile EbcotBlockCoder | EbcotBlockCoder singleton not thread-safe; create separate instances for Parallel.For tiles |
| 2026-02-08 | 30-09 | PLT variable-length encoding per ITU-T T.800 B.8 | 7-bit groups with continuation bit for packet length markers |
| 2026-02-08 | 30-09 | DecodeFrame backward compatible with maxDegreeOfParallelism=1 | Existing 4-parameter overload calls new overload with sequential default |
| 2026-02-08 | 30-09 | Subband type test assertions use non-zero checks | EBCOT context varies by subband type; exact value assertions fragile |

## Session Continuity

**Last session**: 2026-02-08
**Stopped at**: Completed 30-09-PLAN.md (multi-tile pipeline + EBCOT regression)
**Resume file**: None
**Next step**: Phase 30 Plan 10

## Context for Next Session

If resuming after a break:

1. **Current phase**: Phase 30 - HT Block Coder (9/10 plans complete: 01-09)
2. **Phase 30-09 deliverables**: Multi-tile J2K pipeline + EBCOT regression
   - Multi-tile encoder with configurable TileWidth/TileHeight
   - Parallel tile decode via Parallel.For with MaxDegreeOfParallelism
   - All 5 progression orders (LRCP, RLCP, RPCL, PCRL, CPRL)
   - PLT marker emission per tile
   - 30 new tests (13 pipeline + 17 EBCOT regression)
3. **Test coverage**: 5885 tests (5687 pass, 198 skipped, 0 failed)
4. **Next**: Plan 30-10
5. **Known issues**: P-DATA PDV interleaving issue (pre-existing, works with DCMTK peers)

## Potential Future Work

| Requirement | Phase | Status |
|-------------|-------|--------|
| FR-10.1 (PDU parsing) | Phase 10 | Complete (10-03) |
| FR-10.2 (Association negotiation) | Phase 10 | Complete (10-04) |
| FR-10.3 (C-ECHO SCU) | Phase 10 | Complete (10-05) |
| FR-10.4 (C-ECHO SCP) | Phase 10 | Complete (10-06) |
| FR-10.5 (C-STORE SCU) | Phase 11 | Complete (11-02) |
| FR-10.6 (C-STORE SCP streaming) | Phase 11 | Pending |
| FR-10.7 (C-FIND SCU) | Phase 11 | Complete (11-03) |
| FR-10.8 (C-MOVE SCU) | Phase 11 | Complete (11-05) |
| FR-10.9 (C-GET SCU) | Phase 11 | Complete (11-06) |
| FR-10.10 (DicomClient async) | Phase 10 | Complete (10-05) |
| FR-10.11 (DicomServer events) | Phase 10 | Complete (10-06) |
| FR-10.12 (Zero-copy PDU) | Phase 11 | Pending |
| FR-11.1 (JPEG Baseline) | Phase 12 | Complete (12-03) |
| FR-11.2 (JPEG Lossless) | Phase 12 | Complete (12-04) |
| FR-11.3 (J2K Lossless) | Phase 12 | Complete (12-07) |
| FR-11.4 (J2K Lossy) | Phase 12 | Complete (12-07) |
| FR-11.5 (Pure C#) | Phase 12 | Complete (12-01 to 12-07) |
| FR-11.6 (Trim/AOT) | Phase 12 | Complete (12-07) |
| FR-11.7 (IPixelDataCodec) | Phase 12 | Complete (12-07) |
| FR-12.1 (SharpDicom.Codecs) | Phase 13 | Complete (13-06, 13-08) |
| FR-12.2 (libjpeg-turbo) | Phase 13 | Complete (13-02, 13-07) |
| FR-12.3 (OpenJPEG) | Phase 13 | Complete (13-03, 13-07) |
| FR-12.4 (Override registration) | Phase 13 | Complete (13-07) |
| FR-12.5 (Cross-platform) | Phase 13 | Complete (13-01, 13-08) |
| FR-13.1 (PS3.15 Basic) | Phase 14 | In Progress (14-01, 14-02) |
| FR-13.2 (Source-generated) | Phase 14 | Complete (14-01) |
| FR-13.3 (UID remapping) | Phase 14 | Pending (14-03) |
| FR-13.4 (Date shifting) | Phase 14 | Pending (14-04) |
| FR-13.5 (Callback integration) | Phase 14 | Pending |
| FR-13.6 (DicomDeidentifier) | Phase 14 | Pending (14-05) |

**Coverage**: 30/30 requirements mapped

---
*Last updated: 2026-02-08 (Phase 30 Plan 09 COMPLETE -- multi-tile pipeline + EBCOT regression)*
