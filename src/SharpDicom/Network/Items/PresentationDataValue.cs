using System;

namespace SharpDicom.Network.Items
{
    /// <summary>
    /// Represents a Presentation Data Value (PDV) extracted from a P-DATA-TF PDU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each P-DATA-TF PDU can contain multiple PDVs. A PDV contains a fragment of
    /// a DIMSE message (command or data) associated with a specific presentation context.
    /// </para>
    /// <para>
    /// The message control header byte indicates whether this is command or data,
    /// and whether this is the last fragment of the message.
    /// </para>
    /// </remarks>
    public readonly struct PresentationDataValue
    {
        /// <summary>
        /// Gets the presentation context ID that this PDV is associated with.
        /// </summary>
        /// <remarks>
        /// This identifies which negotiated presentation context (and thus which
        /// abstract syntax and transfer syntax) applies to this data.
        /// </remarks>
        public byte PresentationContextId { get; }

        /// <summary>
        /// Gets a value indicating whether this PDV contains command data (true) or dataset data (false).
        /// </summary>
        /// <remarks>
        /// Command data contains the DIMSE command fields (Command Set).
        /// Dataset data contains the actual DICOM dataset (Data Set).
        /// </remarks>
        public bool IsCommand { get; }

        /// <summary>
        /// Gets a value indicating whether this is the last fragment of the message.
        /// </summary>
        /// <remarks>
        /// When true, this PDV contains the final (or only) fragment of the command or data.
        /// The receiver should assemble all fragments and process the complete message.
        /// </remarks>
        public bool IsLastFragment { get; }

        /// <summary>
        /// Gets the payload data for this PDV.
        /// </summary>
        /// <remarks>
        /// This is a fragment of the DIMSE message encoded according to the
        /// transfer syntax of the associated presentation context.
        /// </remarks>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="PresentationDataValue"/>.
        /// </summary>
        /// <param name="presentationContextId">The presentation context ID.</param>
        /// <param name="isCommand">true if this is command data; false if dataset data.</param>
        /// <param name="isLastFragment">true if this is the last fragment of the message.</param>
        /// <param name="data">The PDV payload data.</param>
        public PresentationDataValue(
            byte presentationContextId,
            bool isCommand,
            bool isLastFragment,
            ReadOnlyMemory<byte> data)
        {
            PresentationContextId = presentationContextId;
            IsCommand = isCommand;
            IsLastFragment = isLastFragment;
            Data = data;
        }

        /// <summary>
        /// Creates a PDV from a raw message control header byte and payload.
        /// </summary>
        /// <param name="presentationContextId">The presentation context ID.</param>
        /// <param name="messageControlHeader">The message control header byte from the PDU.</param>
        /// <param name="data">The PDV payload data.</param>
        /// <returns>A <see cref="PresentationDataValue"/> with properties decoded from the header.</returns>
        /// <remarks>
        /// The message control header byte has the following format:
        /// - Bit 0: 0 = data, 1 = command
        /// - Bit 1: 0 = not last, 1 = last fragment
        /// </remarks>
        public static PresentationDataValue FromHeader(
            byte presentationContextId,
            byte messageControlHeader,
            ReadOnlyMemory<byte> data)
        {
            bool isCommand = (messageControlHeader & 0x01) != 0;
            bool isLastFragment = (messageControlHeader & 0x02) != 0;

            return new PresentationDataValue(presentationContextId, isCommand, isLastFragment, data);
        }

        /// <summary>
        /// Gets the message control header byte that would represent this PDV.
        /// </summary>
        /// <returns>The message control header byte.</returns>
        public byte ToMessageControlHeader()
        {
            byte header = 0;
            if (IsCommand)
                header |= 0x01;
            if (IsLastFragment)
                header |= 0x02;
            return header;
        }
    }
}
