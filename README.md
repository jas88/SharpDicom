# SharpDicom

A pure .NET DICOM toolkit built from scratch with zero external DICOM library dependencies.

[![CI](https://github.com/jas88/SharpDicom/actions/workflows/ci.yml/badge.svg)](https://github.com/jas88/SharpDicom/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SharpDicom.svg)](https://www.nuget.org/packages/SharpDicom/)
[![codecov](https://codecov.io/gh/jas88/SharpDicom/branch/main/graph/badge.svg)](https://codecov.io/gh/jas88/SharpDicom)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

## Features

- **Pure .NET** - No fo-dicom or other DICOM library dependencies
- **High Performance** - Span-based parsing, minimal allocations, SIMD optimizations
- **Modern .NET** - Async/await, IAsyncEnumerable, nullable reference types
- **Multi-target** - netstandard2.0, net8.0, net9.0, net10.0
- **Streaming** - Process large files without loading entirely into memory
- **Comprehensive** - Full DICOM Part 10 file support

## Installation

```bash
dotnet add package SharpDicom
```

## Quick Start

```csharp
using SharpDicom;
using SharpDicom.Data;

// Read a DICOM file
var file = await DicomFile.OpenAsync("image.dcm");

// Access metadata
var patientName = file.Dataset.GetString(DicomTag.PatientName);
var modality = file.Dataset.GetString(DicomTag.Modality);

// Work with sequences
var sequence = file.Dataset.GetSequence(DicomTag.ReferencedStudySequence);
foreach (var item in sequence.Items)
{
    var uid = item.GetString(DicomTag.ReferencedSOPInstanceUID);
}

// Write a DICOM file
var dataset = new DicomDataset();
dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, "Doe^John"));
dataset.Add(new DicomStringElement(DicomTag.Modality, DicomVR.CS, "CT"));

var newFile = new DicomFile(dataset);
await newFile.SaveAsync("output.dcm");
```

## Capabilities

| Feature | Status |
|---------|--------|
| File reading (Part 10) | Complete |
| File writing | Complete |
| Explicit/Implicit VR | Complete |
| Little/Big Endian | Complete |
| Sequences (nested) | Complete |
| Pixel data (native) | Complete |
| Pixel data (lazy loading) | Complete |
| RLE codec | Complete |
| Private tags | Complete |
| Character encoding | Complete |
| Validation profiles | Complete |

## Transfer Syntaxes

Supported for reading and writing:
- Implicit VR Little Endian
- Explicit VR Little Endian
- Explicit VR Big Endian (retired)
- RLE Lossless

## Configuration

```csharp
// Strict parsing - reject invalid files
var strict = await DicomFile.OpenAsync("file.dcm", DicomReaderOptions.Strict);

// Lenient parsing - recover from common errors
var lenient = await DicomFile.OpenAsync("file.dcm", DicomReaderOptions.Lenient);

// Custom options
var options = new DicomReaderOptions
{
    PixelDataHandling = PixelDataHandling.LazyLoad,
    MaxSequenceDepth = 10,
    ValidationProfile = ValidationProfile.Strict
};
```

## Building

```bash
dotnet build
dotnet test
```

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

For alternate licensing terms (commercial/proprietary use), contact the author.
