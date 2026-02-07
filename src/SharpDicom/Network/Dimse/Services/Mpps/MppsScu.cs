using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// SCU convenience wrapper for Modality Performed Procedure Step (MPPS) operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps <see cref="NServiceScu"/> to provide typed methods for the MPPS workflow:
    /// <list type="bullet">
    ///   <item><description><see cref="CreateAsync"/>: Create a new MPPS instance (N-CREATE)</description></item>
    ///   <item><description><see cref="SetCompletedAsync"/>: Transition to Completed (N-SET)</description></item>
    ///   <item><description><see cref="SetDiscontinuedAsync"/>: Transition to Discontinued (N-SET)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class MppsScu
    {
        private readonly NServiceScu _scu;

        /// <summary>
        /// Initializes a new instance of the <see cref="MppsScu"/> class.
        /// </summary>
        /// <param name="scu">The N-Service SCU to use for DIMSE operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when scu is null.</exception>
        public MppsScu(NServiceScu scu)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(scu);
#else
            if (scu == null)
                throw new ArgumentNullException(nameof(scu));
#endif
            _scu = scu;
        }

        /// <summary>
        /// Creates a new MPPS instance via N-CREATE with InProgress status.
        /// </summary>
        /// <param name="attributes">The attribute list for the new MPPS instance.</param>
        /// <param name="sopInstanceUid">Optional SOP Instance UID. If null, the SCP assigns one.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and the assigned SOP Instance UID.</returns>
        public ValueTask<NServiceResponse> CreateAsync(
            DicomDataset attributes,
            DicomUID? sopInstanceUid = null,
            CancellationToken ct = default)
        {
            return _scu.NCreateAsync(
                DicomUID.ModalityPerformedProcedureStep,
                attributes,
                sopInstanceUid,
                ct);
        }

        /// <summary>
        /// Transitions an MPPS instance to the Completed state via N-SET.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID of the MPPS to complete.</param>
        /// <param name="additionalAttributes">Optional additional attributes to set alongside the status change.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        public ValueTask<NServiceResponse> SetCompletedAsync(
            DicomUID sopInstanceUid,
            DicomDataset? additionalAttributes = null,
            CancellationToken ct = default)
        {
            var mod = additionalAttributes ?? new DicomDataset();
            SetStatusInDataset(mod, MppsInstance.StatusCompleted);

            return _scu.NSetAsync(
                DicomUID.ModalityPerformedProcedureStep,
                sopInstanceUid,
                mod,
                ct);
        }

        /// <summary>
        /// Transitions an MPPS instance to the Discontinued state via N-SET.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID of the MPPS to discontinue.</param>
        /// <param name="additionalAttributes">Optional additional attributes to set alongside the status change.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        public ValueTask<NServiceResponse> SetDiscontinuedAsync(
            DicomUID sopInstanceUid,
            DicomDataset? additionalAttributes = null,
            CancellationToken ct = default)
        {
            var mod = additionalAttributes ?? new DicomDataset();
            SetStatusInDataset(mod, MppsInstance.StatusDiscontinued);

            return _scu.NSetAsync(
                DicomUID.ModalityPerformedProcedureStep,
                sopInstanceUid,
                mod,
                ct);
        }

        private static void SetStatusInDataset(DicomDataset dataset, string statusValue)
        {
            var bytes = Encoding.ASCII.GetBytes(statusValue);
            // CS VR is padded with spaces to even length
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = (byte)' ';
                bytes = padded;
            }
            dataset.AddOrUpdate(new DicomStringElement(DicomTag.PerformedProcedureStepStatus, DicomVR.CS, bytes));
        }
    }
}
