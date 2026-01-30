using System;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network
{
    public sealed class DicomClientOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string CalledAE { get; set; } = string.Empty;
        public string CallingAE { get; set; } = string.Empty;
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan AssociationTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan DimseTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public uint MaxPduLength { get; set; } = PduConstants.DefaultMaxPduLength;

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
        }
    }
}
