using System;
using System.Net;
using System.Threading.Tasks;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network
{
    /// <summary>
    /// Configuration options for DicomServer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control how the DICOM server listens for connections,
    /// handles associations, and processes DIMSE requests.
    /// </para>
    /// <para>
    /// Use <see cref="Func{T, TResult}"/> delegates for event handlers instead of
    /// traditional .NET events. This allows for async processing and return values.
    /// </para>
    /// </remarks>
    public sealed class DicomServerOptions
    {
        /// <summary>
        /// Gets the port to listen on.
        /// </summary>
        /// <remarks>
        /// Default is 104, the standard DICOM port. Ports below 1024 typically
        /// require elevated privileges on most operating systems.
        /// </remarks>
        public int Port { get; init; } = 104;

        /// <summary>
        /// Gets the IP address to bind to.
        /// </summary>
        /// <remarks>
        /// Default is <see cref="IPAddress.Any"/> which listens on all network interfaces.
        /// Use <see cref="IPAddress.Loopback"/> for localhost-only access.
        /// </remarks>
        public IPAddress BindAddress { get; init; } = IPAddress.Any;

        /// <summary>
        /// Gets the AE title for this server.
        /// </summary>
        /// <remarks>
        /// Must be 1-16 ASCII printable characters with no leading or trailing spaces.
        /// </remarks>
        public string AETitle { get; init; } = null!;

        /// <summary>
        /// Gets the maximum number of concurrent associations.
        /// </summary>
        /// <remarks>
        /// Default is 100. Incoming connections beyond this limit will wait
        /// until an active association completes.
        /// </remarks>
        public int MaxAssociations { get; init; } = 100;

        /// <summary>
        /// Gets the ARTIM timeout for waiting for A-ASSOCIATE-RQ after connection.
        /// </summary>
        /// <remarks>
        /// Default is 30 seconds per DICOM PS3.8. If no A-ASSOCIATE-RQ is received
        /// within this time, the connection is closed.
        /// </remarks>
        public TimeSpan ArtimTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the graceful shutdown timeout.
        /// </summary>
        /// <remarks>
        /// When stopping the server, this is the maximum time to wait for active
        /// associations to complete before aborting them. Default is 30 seconds.
        /// </remarks>
        public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the maximum PDU length to advertise in A-ASSOCIATE-AC.
        /// </summary>
        /// <remarks>
        /// Default is 16384 bytes (16 KB). Larger values may improve throughput
        /// for bulk data transfers but use more memory per connection.
        /// </remarks>
        public uint MaxPduLength { get; init; } = PduConstants.DefaultMaxPduLength;

        /// <summary>
        /// Gets the handler called when an A-ASSOCIATE-RQ is received.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This handler decides whether to accept or reject incoming associations.
        /// Return <see cref="AssociationRequestResult.Accepted"/> with the accepted
        /// presentation contexts, or <see cref="AssociationRequestResult.Rejected"/>
        /// with rejection details.
        /// </para>
        /// <para>
        /// If null, all associations are accepted with the requested presentation contexts
        /// using the first proposed transfer syntax for each.
        /// </para>
        /// </remarks>
        public Func<AssociationRequestContext, ValueTask<AssociationRequestResult>>? OnAssociationRequest { get; init; }

        /// <summary>
        /// Gets the handler called when a C-ECHO-RQ is received.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This handler processes C-ECHO verification requests. Return the status
        /// to include in the C-ECHO-RSP.
        /// </para>
        /// <para>
        /// If null, C-ECHO returns <see cref="DicomStatus.Success"/> automatically.
        /// </para>
        /// </remarks>
        public Func<CEchoRequestContext, ValueTask<DicomStatus>>? OnCEcho { get; init; }

        // Future handlers for Phase 11:
        // public Func<CStoreRequestContext, ValueTask<DicomStatus>>? OnCStore { get; init; }
        // public Func<CFindRequestContext, IAsyncEnumerable<DicomDataset>>? OnCFind { get; init; }

        /// <summary>
        /// Validates the options and throws if invalid.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <see cref="Port"/> is not in the range 1-65535 or
        /// <see cref="MaxAssociations"/> is less than 1.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <see cref="AETitle"/> is invalid.
        /// </exception>
        public void Validate()
        {
            if (Port < 1 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be in the range 1-65535.");

            if (string.IsNullOrWhiteSpace(AETitle))
                throw new ArgumentException("AETitle must not be null or whitespace.", nameof(AETitle));

            if (AETitle.Length > PduConstants.MaxAETitleLength)
                throw new ArgumentException($"AETitle must be 1-{PduConstants.MaxAETitleLength} characters.", nameof(AETitle));

            if (AETitle.Length > 0 && (AETitle[0] == ' ' || AETitle[AETitle.Length - 1] == ' '))
                throw new ArgumentException("AETitle must not have leading or trailing spaces.", nameof(AETitle));

            if (MaxAssociations < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxAssociations), MaxAssociations, "MaxAssociations must be at least 1.");

            if (ArtimTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ArtimTimeout), ArtimTimeout, "ArtimTimeout must be positive.");

            if (ShutdownTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout), ShutdownTimeout, "ShutdownTimeout must be positive.");

            if (MaxPduLength < PduConstants.MinMaxPduLength)
                throw new ArgumentOutOfRangeException(nameof(MaxPduLength), MaxPduLength, $"MaxPduLength must be at least {PduConstants.MinMaxPduLength}.");
        }
    }
}
