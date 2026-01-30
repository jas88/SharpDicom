using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Network.Items
{
    /// <summary>
    /// Represents a presentation context for DICOM association negotiation.
    /// </summary>
    /// <remarks>
    /// Presentation contexts associate an abstract syntax (SOP Class) with one or more
    /// transfer syntaxes. During association negotiation, the SCU proposes presentation
    /// contexts and the SCP accepts or rejects each one.
    /// </remarks>
    public sealed class PresentationContext
    {
        private readonly IReadOnlyList<TransferSyntax> _transferSyntaxes;

        /// <summary>
        /// Gets the presentation context identifier.
        /// </summary>
        /// <remarks>
        /// Must be an odd integer in the range 1-255 per DICOM PS3.8.
        /// </remarks>
        public byte Id { get; }

        /// <summary>
        /// Gets the abstract syntax (SOP Class UID) for this presentation context.
        /// </summary>
        public DicomUID AbstractSyntax { get; }

        /// <summary>
        /// Gets the list of transfer syntaxes.
        /// </summary>
        /// <remarks>
        /// For requests (A-ASSOCIATE-RQ), this contains all proposed transfer syntaxes.
        /// For responses (A-ASSOCIATE-AC), this contains only the accepted transfer syntax
        /// if the context was accepted.
        /// </remarks>
        public IReadOnlyList<TransferSyntax> TransferSyntaxes => _transferSyntaxes;

        /// <summary>
        /// Gets the negotiation result, or null if this is a request (not yet negotiated).
        /// </summary>
        public PresentationContextResult? Result { get; }

        /// <summary>
        /// Gets the accepted transfer syntax, or null if the context was rejected or not yet negotiated.
        /// </summary>
        public TransferSyntax? AcceptedTransferSyntax { get; }

        /// <summary>
        /// Gets or sets whether SCU role is proposed/accepted for this context.
        /// </summary>
        /// <remarks>
        /// Default is true (SCU). Set to true to request SCU role during negotiation.
        /// </remarks>
        public bool ScuRoleRequested { get; set; } = true;

        /// <summary>
        /// Gets or sets whether SCP role is proposed/accepted for this context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Default is false. Set to true to request SCP role during negotiation.
        /// </para>
        /// <para>
        /// Required for C-GET to receive C-STORE sub-operations. When using C-GET,
        /// the SCU must accept the SCP role for Storage SOP Classes so the remote
        /// SCP can send C-STORE sub-operations back on the same association.
        /// </para>
        /// </remarks>
        public bool ScpRoleRequested { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="PresentationContext"/> for a request.
        /// </summary>
        /// <param name="id">The presentation context ID (must be odd, 1-255).</param>
        /// <param name="abstractSyntax">The abstract syntax (SOP Class UID).</param>
        /// <param name="transferSyntaxes">The proposed transfer syntaxes (at least one required).</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid presentation context ID.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="abstractSyntax"/> is empty or no transfer syntaxes are provided.
        /// </exception>
        public PresentationContext(byte id, DicomUID abstractSyntax, params TransferSyntax[] transferSyntaxes)
        {
            if (!IsValidId(id))
                throw new ArgumentOutOfRangeException(nameof(id), id, "Presentation context ID must be an odd integer between 1 and 255.");

            if (abstractSyntax.IsEmpty)
                throw new ArgumentException("Abstract syntax cannot be empty.", nameof(abstractSyntax));

            if (transferSyntaxes == null || transferSyntaxes.Length == 0)
                throw new ArgumentException("At least one transfer syntax must be provided.", nameof(transferSyntaxes));

            Id = id;
            AbstractSyntax = abstractSyntax;
            _transferSyntaxes = transferSyntaxes;
            Result = null;
            AcceptedTransferSyntax = null;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="PresentationContext"/> with a negotiation result.
        /// </summary>
        private PresentationContext(
            byte id,
            DicomUID abstractSyntax,
            IReadOnlyList<TransferSyntax> transferSyntaxes,
            PresentationContextResult result,
            TransferSyntax? acceptedTransferSyntax)
        {
            Id = id;
            AbstractSyntax = abstractSyntax;
            _transferSyntaxes = transferSyntaxes;
            Result = result;
            AcceptedTransferSyntax = acceptedTransferSyntax;
        }

        /// <summary>
        /// Creates an accepted presentation context.
        /// </summary>
        /// <param name="id">The presentation context ID.</param>
        /// <param name="abstractSyntax">The abstract syntax.</param>
        /// <param name="accepted">The accepted transfer syntax.</param>
        /// <returns>A presentation context with <see cref="Result"/> set to <see cref="PresentationContextResult.Acceptance"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid presentation context ID.
        /// </exception>
        public static PresentationContext CreateAccepted(byte id, DicomUID abstractSyntax, TransferSyntax accepted)
        {
            if (!IsValidId(id))
                throw new ArgumentOutOfRangeException(nameof(id), id, "Presentation context ID must be an odd integer between 1 and 255.");

            return new PresentationContext(
                id,
                abstractSyntax,
                new[] { accepted },
                PresentationContextResult.Acceptance,
                accepted);
        }

        /// <summary>
        /// Creates a rejected presentation context.
        /// </summary>
        /// <param name="id">The presentation context ID.</param>
        /// <param name="abstractSyntax">The abstract syntax.</param>
        /// <param name="reason">The rejection reason.</param>
        /// <returns>A presentation context with <see cref="Result"/> set to the specified rejection reason.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="id"/> is not a valid presentation context ID.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="reason"/> is <see cref="PresentationContextResult.Acceptance"/>.
        /// </exception>
        public static PresentationContext CreateRejected(byte id, DicomUID abstractSyntax, PresentationContextResult reason)
        {
            if (!IsValidId(id))
                throw new ArgumentOutOfRangeException(nameof(id), id, "Presentation context ID must be an odd integer between 1 and 255.");

            if (reason == PresentationContextResult.Acceptance)
                throw new ArgumentException("Rejection reason cannot be Acceptance.", nameof(reason));

            return new PresentationContext(
                id,
                abstractSyntax,
                Array.Empty<TransferSyntax>(),
                reason,
                null);
        }

        /// <summary>
        /// Determines whether the specified ID is a valid presentation context identifier.
        /// </summary>
        /// <param name="id">The ID to validate.</param>
        /// <returns>true if the ID is odd and in the range 1-255; otherwise, false.</returns>
        public static bool IsValidId(byte id) => (id & 1) == 1;

        /// <summary>
        /// Configures this presentation context to accept SCP role.
        /// </summary>
        /// <returns>This instance for fluent chaining.</returns>
        /// <remarks>
        /// <para>
        /// Call this for Storage SOP Classes when using C-GET to enable
        /// receiving C-STORE sub-operations on the same association.
        /// </para>
        /// <para>
        /// Per DICOM PS3.8 Section 9.3.1, the SCP/SCU Role Selection Sub-Item
        /// allows the association requestor to propose role reversal. For C-GET,
        /// the SCU needs to act as SCP for Storage operations.
        /// </para>
        /// </remarks>
        public PresentationContext WithScpRole()
        {
            ScpRoleRequested = true;
            return this;
        }

        /// <summary>
        /// Configures this presentation context to accept both SCU and SCP roles.
        /// </summary>
        /// <returns>This instance for fluent chaining.</returns>
        /// <remarks>
        /// Useful when the same presentation context needs to support both
        /// sending and receiving operations (e.g., C-STORE as SCU for sending
        /// and SCP for receiving C-GET sub-operations).
        /// </remarks>
        public PresentationContext WithBothRoles()
        {
            ScuRoleRequested = true;
            ScpRoleRequested = true;
            return this;
        }
    }
}
