using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Base context for all N-Service requests, containing common DIMSE command fields.
    /// </summary>
    /// <remarks>
    /// All N-Service operations share the same base fields: calling/called AE titles,
    /// SOP Class UID, SOP Instance UID, and Message ID. Concrete subclasses add
    /// service-specific fields such as Action Type ID or Event Type ID.
    /// </remarks>
    public abstract class NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NServiceRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="sopInstanceUid">The SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        protected NServiceRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId)
        {
            CallingAE = callingAE;
            CalledAE = calledAE;
            SOPClassUID = sopClassUid;
            SOPInstanceUID = sopInstanceUid;
            MessageID = messageId;
            PresentationContextId = presentationContextId;
        }

        /// <summary>Gets the calling AE title.</summary>
        public string CallingAE { get; }

        /// <summary>Gets the called AE title.</summary>
        public string CalledAE { get; }

        /// <summary>Gets the SOP Class UID.</summary>
        public DicomUID SOPClassUID { get; }

        /// <summary>Gets the SOP Instance UID.</summary>
        public DicomUID SOPInstanceUID { get; }

        /// <summary>Gets the DIMSE message ID.</summary>
        public ushort MessageID { get; }

        /// <summary>Gets the presentation context ID.</summary>
        public byte PresentationContextId { get; }
    }

    /// <summary>
    /// Context for N-CREATE requests.
    /// </summary>
    /// <remarks>
    /// N-CREATE uses Affected SOP Class UID. The SOP Instance UID may be empty
    /// if the SCU requests the SCP to assign one.
    /// See DICOM PS3.7 Section 10.1.5.
    /// </remarks>
    public sealed class NCreateRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NCreateRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Affected SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Affected SOP Instance UID (may be empty if SCP assigns).</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        public NCreateRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
        }
    }

    /// <summary>
    /// Context for N-SET requests.
    /// </summary>
    /// <remarks>
    /// N-SET uses Requested SOP Class/Instance UIDs (not Affected).
    /// See DICOM PS3.7 Section 10.1.3.
    /// </remarks>
    public sealed class NSetRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NSetRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        public NSetRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
        }
    }

    /// <summary>
    /// Context for N-GET requests.
    /// </summary>
    /// <remarks>
    /// N-GET uses Requested SOP Class/Instance UIDs. Optionally includes an
    /// attribute identifier list specifying which attributes to retrieve.
    /// See DICOM PS3.7 Section 10.1.2.
    /// </remarks>
    public sealed class NGetRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NGetRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        /// <param name="attributeIdentifierList">Optional list of tags to retrieve. Null means all attributes.</param>
        public NGetRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId,
            DicomTag[]? attributeIdentifierList = null)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
            AttributeIdentifierList = attributeIdentifierList;
        }

        /// <summary>
        /// Gets the optional attribute identifier list specifying which attributes to retrieve.
        /// </summary>
        /// <remarks>
        /// When null, all attributes of the SOP instance should be returned.
        /// When specified, only the listed attributes should be returned.
        /// </remarks>
        public DicomTag[]? AttributeIdentifierList { get; }
    }

    /// <summary>
    /// Context for N-DELETE requests.
    /// </summary>
    /// <remarks>
    /// N-DELETE uses Requested SOP Class/Instance UIDs.
    /// No dataset accompanies the request.
    /// See DICOM PS3.7 Section 10.1.6.
    /// </remarks>
    public sealed class NDeleteRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NDeleteRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        public NDeleteRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
        }
    }

    /// <summary>
    /// Context for N-ACTION requests.
    /// </summary>
    /// <remarks>
    /// N-ACTION uses Requested SOP Class/Instance UIDs and includes an Action Type ID
    /// that identifies the specific action to perform.
    /// See DICOM PS3.7 Section 10.1.4.
    /// </remarks>
    public sealed class NActionRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NActionRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        /// <param name="actionTypeId">The Action Type ID identifying the action to perform.</param>
        public NActionRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId,
            ushort actionTypeId)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
            ActionTypeID = actionTypeId;
        }

        /// <summary>Gets the Action Type ID identifying the action to perform.</summary>
        public ushort ActionTypeID { get; }
    }

    /// <summary>
    /// Context for N-EVENT-REPORT requests.
    /// </summary>
    /// <remarks>
    /// N-EVENT-REPORT uses Affected SOP Class/Instance UIDs and includes an Event Type ID
    /// that identifies the type of event being reported.
    /// See DICOM PS3.7 Section 10.1.1.
    /// </remarks>
    public sealed class NEventReportRequestContext : NServiceRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NEventReportRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The Affected SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Affected SOP Instance UID.</param>
        /// <param name="messageId">The DIMSE message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        /// <param name="eventTypeId">The Event Type ID identifying the type of event.</param>
        public NEventReportRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId,
            ushort eventTypeId)
            : base(callingAE, calledAE, sopClassUid, sopInstanceUid, messageId, presentationContextId)
        {
            EventTypeID = eventTypeId;
        }

        /// <summary>Gets the Event Type ID identifying the type of event.</summary>
        public ushort EventTypeID { get; }
    }
}
