using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dicom
{
    /// <summary>
    /// Compatibility wrapper for DICOM file I/O.
    /// Matches fo-dicom 4.x DicomFile API surface.
    /// </summary>
    public sealed class DicomFile
    {
        private readonly SharpDicom.DicomFile _inner;
        private DicomDataset? _dataset;
        private DicomDataset? _fileMetaInfo;

        private DicomFile(SharpDicom.DicomFile inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// Gets the dataset from this file.
        /// </summary>
        public DicomDataset Dataset
        {
            get
            {
                if (_dataset == null)
                    _dataset = new DicomDataset(_inner.Dataset);
                return _dataset;
            }
        }

        /// <summary>
        /// Gets the File Meta Information from this file.
        /// </summary>
        public DicomDataset FileMetaInfo
        {
            get
            {
                if (_fileMetaInfo == null)
                    _fileMetaInfo = new DicomDataset(_inner.FileMetaInfo);
                return _fileMetaInfo;
            }
        }

        /// <summary>
        /// Opens a DICOM file from disk.
        /// </summary>
        /// <param name="path">The path to the DICOM file.</param>
        /// <returns>The opened DICOM file.</returns>
        public static DicomFile Open(string path)
        {
            var sdFile = SharpDicom.DicomFile.Open(path);
            return new DicomFile(sdFile);
        }

        /// <summary>
        /// Opens a DICOM file asynchronously from disk.
        /// </summary>
        /// <param name="path">The path to the DICOM file.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task containing the opened DICOM file.</returns>
        public static async Task<DicomFile> OpenAsync(string path, CancellationToken ct = default)
        {
            var sdFile = await SharpDicom.DicomFile.OpenAsync(path, ct: ct).ConfigureAwait(false);
            return new DicomFile(sdFile);
        }

        /// <summary>
        /// Saves the DICOM file to disk.
        /// </summary>
        /// <param name="path">The path to save to.</param>
        public void Save(string path)
        {
            _inner.Save(path);
        }

        /// <summary>
        /// Saves the DICOM file asynchronously to disk.
        /// </summary>
        /// <param name="path">The path to save to.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task SaveAsync(string path, CancellationToken ct = default)
        {
            await _inner.SaveAsync(path, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Unwraps to the underlying SharpDicom file.
        /// </summary>
        public SharpDicom.DicomFile Unwrap() => _inner;
    }
}
