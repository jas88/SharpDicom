using System;

namespace SharpDicom.Storage
{
    /// <summary>
    /// Configuration options for <see cref="FileSystemDicomStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At minimum, <see cref="RootDirectory"/> must be set. All other properties have sensible defaults.
    /// </para>
    /// <para>
    /// The SQLite database path defaults to <c>{RootDirectory}/index.db</c> if not explicitly set.
    /// </para>
    /// </remarks>
    public sealed class FileSystemDicomStoreOptions
    {
        /// <summary>
        /// Gets or sets the root directory for DICOM file storage.
        /// </summary>
        /// <remarks>
        /// Files are stored in a hierarchical layout:
        /// <c>{RootDirectory}/{PatientID}/{StudyInstanceUID}/{SeriesInstanceUID}/{SOPInstanceUID}.dcm</c>
        /// </remarks>
        public string RootDirectory { get; init; } = null!;

        /// <summary>
        /// Gets or sets the path to the SQLite metadata database.
        /// </summary>
        /// <remarks>
        /// If null, defaults to <c>{RootDirectory}/index.db</c>.
        /// </remarks>
        public string? DatabasePath { get; init; }

        /// <summary>
        /// Gets or sets the AE title for the DICOM server.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="FileSystemDicomStore.CreateServerOptions"/> when creating
        /// a <see cref="SharpDicom.Network.DicomServerOptions"/> instance.
        /// </remarks>
        public string AETitle { get; init; } = "SHARPDICOM";

        /// <summary>
        /// Gets or sets the listen port for the DICOM server.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="FileSystemDicomStore.CreateServerOptions"/> when creating
        /// a <see cref="SharpDicom.Network.DicomServerOptions"/> instance.
        /// Default is 11112, a common non-privileged DICOM port.
        /// </remarks>
        public int Port { get; init; } = 11112;

        /// <summary>
        /// Gets the effective database path, using the default if not explicitly set.
        /// </summary>
        internal string EffectiveDatabasePath =>
            DatabasePath ?? System.IO.Path.Combine(RootDirectory, "index.db");

        /// <summary>
        /// Validates the options and throws if invalid.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when <see cref="RootDirectory"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="Port"/> is out of range.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(RootDirectory))
                throw new ArgumentException("RootDirectory must be set.", nameof(RootDirectory));

            if (Port < 1 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be in the range 1-65535.");

            if (string.IsNullOrWhiteSpace(AETitle))
                throw new ArgumentException("AETitle must not be null or whitespace.", nameof(AETitle));
        }
    }
}
