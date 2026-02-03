using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler interface for Modality Worklist C-FIND SCP operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to provide Modality Worklist query functionality
    /// in a DICOM server (SCP). The server will call <see cref="OnMwlQueryAsync"/>
    /// when it receives a C-FIND request for the Modality Worklist SOP Class.
    /// </para>
    /// <para>
    /// Example implementation:
    /// <code>
    /// public class MyMwlHandler : IMwlQueryHandler
    /// {
    ///     public async IAsyncEnumerable&lt;DicomDataset&gt; OnMwlQueryAsync(
    ///         MwlQueryContext context,
    ///         DicomDataset identifier,
    ///         CancellationToken ct)
    ///     {
    ///         foreach (var scheduledProcedure in await GetScheduledProcedures(identifier))
    ///         {
    ///             yield return scheduledProcedure;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public interface IMwlQueryHandler
    {
        /// <summary>
        /// Called when an MWL C-FIND request is received.
        /// </summary>
        /// <param name="context">The query context containing association information.</param>
        /// <param name="identifier">The query identifier with match keys and return keys.</param>
        /// <param name="cancellationToken">Cancellation token (triggered if C-CANCEL received or connection lost).</param>
        /// <returns>
        /// An async enumerable of matching worklist items. Each yielded dataset
        /// will be sent as a C-FIND-RSP with Pending status.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The identifier dataset contains:
        /// - Match keys: Elements with values to use as search criteria
        /// - Return keys: Empty elements indicating which attributes the SCU wants returned
        /// </para>
        /// <para>
        /// For each yielded dataset, the server will:
        /// 1. Send a C-FIND-RSP with status Pending (0xFF00 or 0xFF01)
        /// 2. Include the dataset as the response identifier
        /// </para>
        /// <para>
        /// After enumeration completes, the server sends a final C-FIND-RSP with status Success (0x0000).
        /// If an exception is thrown, the server sends a status indicating failure.
        /// </para>
        /// </remarks>
        IAsyncEnumerable<DicomDataset> OnMwlQueryAsync(
            MwlQueryContext context,
            DicomDataset identifier,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Context information for an MWL query request.
    /// </summary>
    /// <remarks>
    /// Provides access to association-level information and the original request command.
    /// </remarks>
    public sealed class MwlQueryContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MwlQueryContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="messageId">The message ID from the request.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        public MwlQueryContext(
            string callingAE,
            string calledAE,
            ushort messageId,
            byte presentationContextId)
        {
            CallingAE = callingAE;
            CalledAE = calledAE;
            MessageID = messageId;
            PresentationContextId = presentationContextId;
        }

        /// <summary>
        /// Gets the calling AE title (the SCU that sent the query).
        /// </summary>
        public string CallingAE { get; }

        /// <summary>
        /// Gets the called AE title (this SCP).
        /// </summary>
        public string CalledAE { get; }

        /// <summary>
        /// Gets the message ID from the C-FIND request.
        /// </summary>
        public ushort MessageID { get; }

        /// <summary>
        /// Gets the presentation context ID for the MWL operation.
        /// </summary>
        public byte PresentationContextId { get; }
    }

    /// <summary>
    /// Helper class for building MWL response datasets.
    /// </summary>
    /// <remarks>
    /// Provides convenience methods for creating properly formatted MWL response datasets
    /// with the Scheduled Procedure Step Sequence structure.
    /// </remarks>
    public static class MwlResponseBuilder
    {
        /// <summary>
        /// Creates an MWL response dataset from a WorklistItem.
        /// </summary>
        /// <param name="item">The worklist item containing the data.</param>
        /// <returns>A dataset suitable for return in an MWL C-FIND response.</returns>
        /// <remarks>
        /// This returns the underlying dataset from the WorklistItem, which already
        /// contains the proper Scheduled Procedure Step Sequence structure.
        /// </remarks>
        public static DicomDataset FromWorklistItem(WorklistItem item)
        {
#if NET6_0_OR_GREATER
            System.ArgumentNullException.ThrowIfNull(item);
#else
            if (item == null)
                throw new System.ArgumentNullException(nameof(item));
#endif
            return item.Dataset;
        }

        /// <summary>
        /// Creates a basic MWL response dataset with required fields.
        /// </summary>
        /// <param name="patientName">Patient name.</param>
        /// <param name="patientId">Patient ID.</param>
        /// <param name="accessionNumber">Accession number.</param>
        /// <param name="studyInstanceUid">Pre-assigned Study Instance UID.</param>
        /// <param name="spsBuilder">Builder for the Scheduled Procedure Step.</param>
        /// <returns>A new dataset with the specified values and SPS sequence.</returns>
        public static DicomDataset Create(
            string? patientName,
            string? patientId,
            string? accessionNumber,
            string? studyInstanceUid,
            System.Action<MwlSpsResponseBuilder>? spsBuilder = null)
        {
            var dataset = new DicomDataset();

            // Patient attributes
            if (patientName != null)
                AddStringElement(dataset, DicomTag.PatientName, patientName, DicomVR.PN);
            else
                dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, System.Array.Empty<byte>()));

            if (patientId != null)
                AddStringElement(dataset, DicomTag.PatientID, patientId, DicomVR.LO);
            else
                dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO, System.Array.Empty<byte>()));

            // Study attributes
            if (accessionNumber != null)
                AddStringElement(dataset, DicomTag.AccessionNumber, accessionNumber, DicomVR.SH);
            else
                dataset.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH, System.Array.Empty<byte>()));

            if (studyInstanceUid != null)
                AddStringElement(dataset, DicomTag.StudyInstanceUID, studyInstanceUid, DicomVR.UI);
            else
                dataset.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, System.Array.Empty<byte>()));

            // Build SPS sequence
            var spsDataset = new DicomDataset();
            if (spsBuilder != null)
            {
                var builder = new MwlSpsResponseBuilder(spsDataset);
                spsBuilder(builder);
            }

            dataset.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, spsDataset));

            return dataset;
        }

        private static void AddStringElement(DicomDataset dataset, DicomTag tag, string value, DicomVR vr)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                System.Array.Copy(bytes, padded, bytes.Length);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)'\0' : (byte)' ';
                bytes = padded;
            }
            dataset.Add(new DicomStringElement(tag, vr, bytes));
        }
    }

    /// <summary>
    /// Builder for SPS sequence items in MWL responses.
    /// </summary>
    public sealed class MwlSpsResponseBuilder
    {
        private readonly DicomDataset _dataset;

        internal MwlSpsResponseBuilder(DicomDataset dataset)
        {
            _dataset = dataset;
        }

        /// <summary>
        /// Sets the modality.
        /// </summary>
        public MwlSpsResponseBuilder Modality(string value)
        {
            AddString(DicomTag.Modality, value, DicomVR.CS);
            return this;
        }

        /// <summary>
        /// Sets the scheduled station AE title.
        /// </summary>
        public MwlSpsResponseBuilder ScheduledStationAETitle(string value)
        {
            AddString(DicomTag.ScheduledStationAETitle, value, DicomVR.AE);
            return this;
        }

        /// <summary>
        /// Sets the scheduled procedure step start date.
        /// </summary>
        public MwlSpsResponseBuilder ScheduledStartDate(System.DateTime date)
        {
            AddString(DicomTag.ScheduledProcedureStepStartDate,
                date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Sets the scheduled procedure step start time.
        /// </summary>
        public MwlSpsResponseBuilder ScheduledStartTime(System.TimeSpan time)
        {
            var timeStr = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:D2}{1:D2}{2:D2}",
                time.Hours, time.Minutes, time.Seconds);
            AddString(DicomTag.ScheduledProcedureStepStartTime, timeStr, DicomVR.TM);
            return this;
        }

        /// <summary>
        /// Sets the scheduled procedure step ID.
        /// </summary>
        public MwlSpsResponseBuilder ScheduledProcedureStepId(string value)
        {
            AddString(DicomTag.ScheduledProcedureStepID, value, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Sets the scheduled procedure step description.
        /// </summary>
        public MwlSpsResponseBuilder ScheduledProcedureStepDescription(string value)
        {
            AddString(DicomTag.ScheduledProcedureStepDescription, value, DicomVR.LO);
            return this;
        }

        private void AddString(DicomTag tag, string value, DicomVR vr)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                System.Array.Copy(bytes, padded, bytes.Length);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)'\0' : (byte)' ';
                bytes = padded;
            }
            _dataset.Add(new DicomStringElement(tag, vr, bytes));
        }
    }
}
