using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// SCP handler for Modality Performed Procedure Step (MPPS) N-CREATE and N-SET operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the MPPS SOP Class SCP role per DICOM PS3.4 Annex F.
    /// Handles N-CREATE (create InProgress MPPS) and N-SET (transition to Completed/Discontinued).
    /// </para>
    /// <para>
    /// State machine enforcement:
    /// <list type="bullet">
    ///   <item><description>InProgress -> Completed: allowed</description></item>
    ///   <item><description>InProgress -> Discontinued: allowed</description></item>
    ///   <item><description>Completed -> any: rejected with 0x0106 (InvalidAttributeValue)</description></item>
    ///   <item><description>Discontinued -> any: rejected with 0x0106 (InvalidAttributeValue)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class MppsScpHandler : INCreateHandler, INSetHandler
    {
        private readonly IMppsPersistence _persistence;

        /// <summary>
        /// Initializes a new instance of the <see cref="MppsScpHandler"/> class.
        /// </summary>
        /// <param name="persistence">
        /// The persistence provider to use. If null, defaults to <see cref="InMemoryMppsPersistence"/>.
        /// </param>
        public MppsScpHandler(IMppsPersistence? persistence = null)
        {
            _persistence = persistence ?? new InMemoryMppsPersistence();
        }

        /// <summary>
        /// Handles an N-CREATE request to create a new MPPS instance in the InProgress state.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="attributeList">The attribute list dataset with initial MPPS values.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and the assigned SOP Instance UID.</returns>
        public async ValueTask<NServiceResponse> OnNCreateAsync(
            NCreateRequestContext context,
            DicomDataset? attributeList,
            CancellationToken ct)
        {
            // Verify SOP Class UID is MPPS
            if (context.SOPClassUID != DicomUID.ModalityPerformedProcedureStep)
            {
                return new NServiceResponse(DicomStatus.NoSuchSOPClass);
            }

            // Generate SOP Instance UID if not provided
            var sopInstanceUid = context.SOPInstanceUID.IsEmpty
                ? DicomUID.Generate()
                : context.SOPInstanceUID;

            var dataset = attributeList ?? new DicomDataset();

            // Create the MPPS instance in InProgress state
            var instance = MppsInstance.CreateInProgress(sopInstanceUid, dataset);

            try
            {
                await _persistence.PutAsync(instance, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Instance already exists
                return new NServiceResponse(DicomStatus.DuplicateSOPInstance);
            }

            return new NServiceResponse(DicomStatus.Success, dataset, sopInstanceUid);
        }

        /// <summary>
        /// Handles an N-SET request to update an existing MPPS instance.
        /// Validates state machine transitions per DICOM PS3.4 Annex F.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="modificationList">Dataset containing the attribute values to set.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        public async ValueTask<NServiceResponse> OnNSetAsync(
            NSetRequestContext context,
            DicomDataset? modificationList,
            CancellationToken ct)
        {
            // Verify SOP Class UID is MPPS
            if (context.SOPClassUID != DicomUID.ModalityPerformedProcedureStep)
            {
                return new NServiceResponse(DicomStatus.NoSuchSOPClass);
            }

            // Look up existing instance
            var instance = await _persistence.GetAsync(context.SOPInstanceUID, ct).ConfigureAwait(false);
            if (instance == null)
            {
                return new NServiceResponse(DicomStatus.NoSuchObjectInstance);
            }

            // Validate state machine: only InProgress can be transitioned
            if (instance.Status == MppsStatus.Completed || instance.Status == MppsStatus.Discontinued)
            {
                // Terminal states cannot be changed
                return new NServiceResponse(DicomStatus.InvalidAttributeValue);
            }

            // Validate the modification sets a valid target status
            if (modificationList != null)
            {
                var newStatusString = modificationList.GetString(DicomTag.PerformedProcedureStepStatus);
                if (newStatusString != null)
                {
                    try
                    {
                        var newStatus = MppsInstance.ParseStatus(newStatusString);
                        if (newStatus != MppsStatus.Completed && newStatus != MppsStatus.Discontinued)
                        {
                            // Can only transition to terminal states
                            return new NServiceResponse(DicomStatus.InvalidAttributeValue);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Unrecognized status string
                        return new NServiceResponse(DicomStatus.InvalidAttributeValue);
                    }
                }
            }

            // Apply the modification
            if (modificationList != null)
            {
                await _persistence.UpdateAsync(context.SOPInstanceUID, modificationList, ct).ConfigureAwait(false);
            }

            return new NServiceResponse(DicomStatus.Success);
        }
    }
}
