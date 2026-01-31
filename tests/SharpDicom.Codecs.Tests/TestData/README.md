# Test Data for SharpDicom.Codecs.Tests

This directory contains test files for native codec testing.

## Required Test Files

The following test files are needed for codec-specific tests. These files should be obtained
from DICOM test datasets or generated using reference implementations.

### JPEG Baseline (SOI=0xFFD8, EOI=0xFFD9)

- `sample_jpeg_8bit.jpg` - 8-bit grayscale JPEG for baseline testing
- `sample_jpeg_rgb.jpg` - 24-bit RGB JPEG for color testing

### JPEG 2000 (SOC=0xFF4F, EOC=0xFFD9)

- `sample_j2k_lossless.j2k` - JPEG 2000 lossless codestream
- `sample_j2k_lossy.j2k` - JPEG 2000 lossy codestream
- `sample_j2k_16bit.j2k` - 16-bit grayscale J2K

### JPEG-LS (SOI=0xFFD8, SOF-LS=0xFFF7)

- `sample_jpegls_lossless.jls` - JPEG-LS lossless
- `sample_jpegls_nearlossless.jls` - JPEG-LS near-lossless

## Test File Sources

Test files can be obtained from:

1. **DICOM Sample Files**: https://www.dicomlibrary.com/
2. **fo-dicom Test Data**: https://github.com/fo-dicom/fo-dicom/tree/development/Tests/fo-dicom.Tests/Data
3. **ITK Test Data**: https://data.kitware.com/
4. **GDCM Test Data**: https://sourceforge.net/projects/gdcm/

## File Naming Convention

Files should follow this naming pattern:
`{format}_{bit-depth}_{color-model}_{variant}.{ext}`

Examples:
- `jpeg_8bit_gray_baseline.jpg`
- `j2k_12bit_gray_lossless.j2k`
- `jpegls_16bit_gray_nearlossless.jls`

## Notes

- Tests will be skipped if required test files are not present
- Tests will be skipped if native codecs are not available
- GPU-specific tests require CUDA/Metal-capable hardware
