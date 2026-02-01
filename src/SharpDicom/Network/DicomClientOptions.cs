using System;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network
{
    /// <summary>
    /// Configuration options for <see cref="DicomClient"/>.
    /// </summary>
    public sealed class DicomClientOptions
    {
        /// <summary>
        /// Gets or sets the remote host name or IP address.
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the remote port number.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the Called AE Title (remote SCP).
        /// </summary>
        public string CalledAE { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Calling AE Title (this client).
        /// </summary>
        public string CallingAE { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the connection timeout.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the association negotiation timeout.
        /// </summary>
        public TimeSpan AssociationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the DIMSE message timeout.
        /// </summary>
        public TimeSpan DimseTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets or sets the maximum PDU length to negotiate.
        /// </summary>
        public uint MaxPduLength { get; set; } = PduConstants.DefaultMaxPduLength;

        /// <summary>
        /// Gets or sets the threshold at which the pipe pauses reading from socket (default: 64KB).
        /// </summary>
        /// <remarks>
        /// When buffered data exceeds this threshold, socket reading pauses
        /// until the buffer drains below <see cref="ResumeWriterThreshold"/>.
        /// This enables TCP flow control for slow consumers.
        /// </remarks>
        public int PauseWriterThreshold { get; set; } = 65536;

        /// <summary>
        /// Gets or sets the threshold at which the pipe resumes reading from socket (default: 32KB).
        /// </summary>
        /// <remarks>
        /// Provides hysteresis to prevent thrashing between pause/resume states.
        /// Should be less than <see cref="PauseWriterThreshold"/>.
        /// </remarks>
        public int ResumeWriterThreshold { get; set; } = 32768;

        /// <summary>
        /// Validates the options.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when options are invalid.</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host))
                throw new ArgumentException("Host is required.", nameof(Host));
            if (Port < 1 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");
            if (string.IsNullOrWhiteSpace(CalledAE))
                throw new ArgumentException("CalledAE is required.", nameof(CalledAE));
            if (CalledAE.Length > 16)
                throw new ArgumentException("CalledAE cannot exceed 16 characters.", nameof(CalledAE));
            if (string.IsNullOrWhiteSpace(CallingAE))
                throw new ArgumentException("CallingAE is required.", nameof(CallingAE));
            if (CallingAE.Length > 16)
                throw new ArgumentException("CallingAE cannot exceed 16 characters.", nameof(CallingAE));
            if (ConnectionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ConnectionTimeout), "ConnectionTimeout must be positive.");
            if (AssociationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(AssociationTimeout), "AssociationTimeout must be positive.");
            if (DimseTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(DimseTimeout), "DimseTimeout must be positive.");
            if (MaxPduLength < 4096)
                throw new ArgumentOutOfRangeException(nameof(MaxPduLength), "MaxPduLength must be at least 4096 bytes.");
            if (PauseWriterThreshold < 4096)
                throw new ArgumentOutOfRangeException(nameof(PauseWriterThreshold),
                    "PauseWriterThreshold must be at least 4096 bytes.");
            if (ResumeWriterThreshold < 1024 || ResumeWriterThreshold >= PauseWriterThreshold)
                throw new ArgumentOutOfRangeException(nameof(ResumeWriterThreshold),
                    "ResumeWriterThreshold must be between 1024 and PauseWriterThreshold.");
        }
    }
}
