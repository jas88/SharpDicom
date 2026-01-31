# Test Data

Test files should be placed in this directory. Use NEMA WG-04 conformance test files for comprehensive codec testing.

## Required Files

| File | Description | Transfer Syntax UID |
|------|-------------|---------------------|
| jpeg_baseline_8bit.dcm | JPEG Baseline 8-bit test file | 1.2.840.10008.1.2.4.50 |
| jpeg_lossless_14sv1.dcm | JPEG Lossless Process 14 SV1 | 1.2.840.10008.1.2.4.70 |
| j2k_lossless.dcm | JPEG 2000 Lossless | 1.2.840.10008.1.2.4.90 |
| j2k_lossy.dcm | JPEG 2000 Lossy | 1.2.840.10008.1.2.4.91 |
| jpegls_lossless.dcm | JPEG-LS Lossless | 1.2.840.10008.1.2.4.80 |
| jpegls_nearlossless.dcm | JPEG-LS Near-Lossless | 1.2.840.10008.1.2.4.81 |

## Obtaining Test Files

### From NEMA WG-04

Download conformance test files from:
- https://www.dclunie.com/images/compressed/

### Using DCMTK

Create test files from uncompressed sources using DCMTK tools:

```bash
# JPEG Baseline
dcmcjpeg +eb input.dcm jpeg_baseline_8bit.dcm

# JPEG Lossless (Process 14, Selection Value 1)
dcmcjpeg +e1 input.dcm jpeg_lossless_14sv1.dcm

# JPEG 2000 Lossless
dcmcjp2k +e1 input.dcm j2k_lossless.dcm

# JPEG 2000 Lossy
dcmcjp2k +ew input.dcm j2k_lossy.dcm

# JPEG-LS Lossless
dcmcjpls input.dcm jpegls_lossless.dcm

# JPEG-LS Near-Lossless (NEAR=2)
dcmcjpls +n2 input.dcm jpegls_nearlossless.dcm
```

### Using fo-dicom

```csharp
var file = DicomFile.Open("uncompressed.dcm");
file.ChangeTransferSyntax(DicomTransferSyntax.JPEGBaseline);
file.Save("jpeg_baseline_8bit.dcm");
```

## Test Data Expectations

### Grayscale Test Images

- Dimensions: 512x512 or 256x256
- Bits Allocated: 8 or 16
- Bits Stored: 8, 12, or 16
- Photometric Interpretation: MONOCHROME1 or MONOCHROME2

### Color Test Images

- Dimensions: 256x256 or 512x512
- Samples Per Pixel: 3
- Photometric Interpretation: RGB or YBR_FULL_422

## Notes

- Test files should NOT be committed to the repository if they exceed 1MB
- For CI, consider using small synthetic test images
- Integration tests with large files should be marked as [Category("LargeFile")]
