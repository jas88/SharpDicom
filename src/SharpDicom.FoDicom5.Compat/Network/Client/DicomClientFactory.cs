namespace FellowOakDicom.Network.Client
{
    /// <summary>
    /// Factory for creating IDicomClient instances matching fo-dicom 5.x DicomClientFactory.
    /// </summary>
    public static class DicomClientFactory
    {
        /// <summary>
        /// Creates a new IDicomClient configured for the specified remote AE.
        /// </summary>
        /// <param name="host">The remote host.</param>
        /// <param name="port">The remote port.</param>
        /// <param name="useTls">Whether to use TLS.</param>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <returns>A new IDicomClient instance.</returns>
        public static IDicomClient Create(string host, int port, bool useTls, string callingAE, string calledAE)
        {
            return new DicomClient(host, port, useTls, callingAE, calledAE);
        }
    }
}
