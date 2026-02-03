# SharpDicom Roadmap

## Milestones

- **v1.0.0 Core DICOM Toolkit** — Phases 1-9 (shipped 2026-01-28) — see [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)
- **v2.0.0 Network, Codecs & De-identification** — Phases 10-14 (shipped 2026-02-02) — see [milestones/v2.0-ROADMAP.md](milestones/v2.0-ROADMAP.md)
- **v3.0.0 Advanced Features** — Phases 15+ (planned)

---

## Future Work (v3.0.0+)

### Phase 18: Complete Managed Codec Implementations

**Goal**: Complete pure C# JPEG-LS and HTJ2K codecs (infrastructure added in v2.0)

**Must-haves**:
- [ ] Complete JPEG-LS encoder/decoder (ITU-T T.87)
- [ ] Complete HTJ2K encoder/decoder
- [ ] Roundtrip tests passing for all bit depths

### Phase 19: TLS Support

**Goal**: Secure DICOM networking

**Must-haves**:
- [ ] TLS 1.2/1.3 support for DicomClient/DicomServer
- [ ] Certificate validation options

### Phase 20: CLI Tools

**Goal**: Command-line utilities (dcmdump equivalent)

---

*Last updated: 2026-02-02 (v2.0.0 milestone archived)*
