using System;
using System.Collections.Generic;
using SharpDicom.Network.Items;

namespace SharpDicom.Network
{
    /// <summary>
    /// Configuration options for DICOM association negotiation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options are used when establishing a DICOM association (connection).
    /// They specify the AE titles, proposed presentation contexts, and timeout values.
    /// </para>
    /// <para>
    /// Use <see cref="AssociationOptionsBuilder"/> for fluent configuration.
    /// </para>
    /// </remarks>
    public sealed class AssociationOptions
    {
        /// <summary>
        /// Gets the Called AE Title (remote/server AE title).
        /// </summary>
        /// <remarks>
        /// The AE title of the remote DICOM application entity being called.
        /// Must be 1-16 ASCII characters with no leading/trailing spaces.
        /// </remarks>
        public string CalledAETitle { get; }

        /// <summary>
        /// Gets the Calling AE Title (local/client AE title).
        /// </summary>
        /// <remarks>
        /// The AE title of the local DICOM application entity making the call.
        /// Must be 1-16 ASCII characters with no leading/trailing spaces.
        /// </remarks>
        public string CallingAETitle { get; }

        /// <summary>
        /// Gets the proposed presentation contexts for the association.
        /// </summary>
        /// <remarks>
        /// At least one presentation context is required. Each presentation context
        /// must have a unique ID.
        /// </remarks>
        public IReadOnlyList<PresentationContext> PresentationContexts { get; }

        /// <summary>
        /// Gets the user information to send during association negotiation.
        /// </summary>
        public UserInformation UserInformation { get; }

        /// <summary>
        /// Gets the ARTIM (Association Request/Reject/Release Timer) timeout.
        /// </summary>
        /// <remarks>
        /// This timer is used during association establishment and release.
        /// Default is 30 seconds per DICOM PS3.8.
        /// </remarks>
        public TimeSpan ArtimTimeout { get; }

        /// <summary>
        /// Gets the DIMSE message response timeout.
        /// </summary>
        /// <remarks>
        /// The maximum time to wait for a DIMSE response after sending a request.
        /// Default is 60 seconds.
        /// </remarks>
        public TimeSpan DimseTimeout { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="AssociationOptions"/>.
        /// </summary>
        /// <param name="calledAETitle">The called (remote) AE title.</param>
        /// <param name="callingAETitle">The calling (local) AE title.</param>
        /// <param name="presentationContexts">The proposed presentation contexts.</param>
        /// <param name="userInformation">The user information (null for default).</param>
        /// <param name="artimTimeout">The ARTIM timeout (null for 30 seconds).</param>
        /// <param name="dimseTimeout">The DIMSE timeout (null for 60 seconds).</param>
        /// <exception cref="ArgumentException">
        /// Thrown when AE titles are invalid, no presentation contexts are provided,
        /// or presentation context IDs are not unique.
        /// </exception>
        public AssociationOptions(
            string calledAETitle,
            string callingAETitle,
            IReadOnlyList<PresentationContext> presentationContexts,
            UserInformation? userInformation = null,
            TimeSpan? artimTimeout = null,
            TimeSpan? dimseTimeout = null)
        {
            ValidateAETitle(calledAETitle, nameof(calledAETitle));
            ValidateAETitle(callingAETitle, nameof(callingAETitle));
            ValidatePresentationContexts(presentationContexts);

            CalledAETitle = calledAETitle;
            CallingAETitle = callingAETitle;
            PresentationContexts = presentationContexts;
            UserInformation = userInformation ?? Items.UserInformation.Default;
            ArtimTimeout = artimTimeout ?? TimeSpan.FromSeconds(30);
            DimseTimeout = dimseTimeout ?? TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Creates a new builder for fluent construction of <see cref="AssociationOptions"/>.
        /// </summary>
        /// <returns>A new <see cref="AssociationOptionsBuilder"/>.</returns>
        public static AssociationOptionsBuilder CreateBuilder() => new();

        private static void ValidateAETitle(string aeTitle, string paramName)
        {
            if (string.IsNullOrEmpty(aeTitle))
                throw new ArgumentException("AE Title cannot be null or empty.", paramName);

            if (aeTitle.Length > PduConstants.MaxAETitleLength)
                throw new ArgumentException($"AE Title cannot exceed {PduConstants.MaxAETitleLength} characters.", paramName);

            // Check for leading/trailing spaces (spaces are allowed in the middle, padded later)
            if (aeTitle.Length > 0 && (aeTitle[0] == ' ' || aeTitle[aeTitle.Length - 1] == ' '))
                throw new ArgumentException("AE Title cannot have leading or trailing spaces.", paramName);

            // Check for ASCII printable characters only (0x20-0x7E, excluding control characters)
            for (int i = 0; i < aeTitle.Length; i++)
            {
                char c = aeTitle[i];
                if (c < 0x20 || c > 0x7E)
                    throw new ArgumentException("AE Title must contain only ASCII printable characters.", paramName);
            }
        }

        private static void ValidatePresentationContexts(IReadOnlyList<PresentationContext> contexts)
        {
            if (contexts == null || contexts.Count == 0)
                throw new ArgumentException("At least one presentation context is required.", nameof(contexts));

            // Check for unique IDs
            var seenIds = new HashSet<byte>();
            for (int i = 0; i < contexts.Count; i++)
            {
                var ctx = contexts[i];
                if (ctx == null)
                    throw new ArgumentException($"Presentation context at index {i} is null.", nameof(contexts));

                if (!seenIds.Add(ctx.Id))
                    throw new ArgumentException($"Duplicate presentation context ID: {ctx.Id}.", nameof(contexts));
            }
        }
    }

    /// <summary>
    /// Builder for fluent construction of <see cref="AssociationOptions"/>.
    /// </summary>
    public sealed class AssociationOptionsBuilder
    {
        private string? _calledAETitle;
        private string? _callingAETitle;
        private readonly List<PresentationContext> _presentationContexts = new();
        private UserInformation? _userInformation;
        private TimeSpan? _artimTimeout;
        private TimeSpan? _dimseTimeout;

        /// <summary>
        /// Sets the called (remote) AE title.
        /// </summary>
        /// <param name="aeTitle">The AE title.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder WithCalledAE(string aeTitle)
        {
            _calledAETitle = aeTitle;
            return this;
        }

        /// <summary>
        /// Sets the calling (local) AE title.
        /// </summary>
        /// <param name="aeTitle">The AE title.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder WithCallingAE(string aeTitle)
        {
            _callingAETitle = aeTitle;
            return this;
        }

        /// <summary>
        /// Adds a presentation context to the list.
        /// </summary>
        /// <param name="context">The presentation context to add.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder AddPresentationContext(PresentationContext context)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(context);
#else
            if (context == null)
                throw new ArgumentNullException(nameof(context));
#endif
            _presentationContexts.Add(context);
            return this;
        }

        /// <summary>
        /// Sets the user information.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder WithUserInformation(UserInformation userInformation)
        {
            _userInformation = userInformation;
            return this;
        }

        /// <summary>
        /// Sets the ARTIM (association) timeout.
        /// </summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder WithArtimTimeout(TimeSpan timeout)
        {
            _artimTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Sets the DIMSE message timeout.
        /// </summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>This builder for chaining.</returns>
        public AssociationOptionsBuilder WithDimseTimeout(TimeSpan timeout)
        {
            _dimseTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Builds the <see cref="AssociationOptions"/> from the configured values.
        /// </summary>
        /// <returns>A new <see cref="AssociationOptions"/> instance.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when required properties (CalledAE, CallingAE) are not set.
        /// </exception>
        public AssociationOptions Build()
        {
            if (string.IsNullOrEmpty(_calledAETitle))
                throw new InvalidOperationException("Called AE Title must be set.");

            if (string.IsNullOrEmpty(_callingAETitle))
                throw new InvalidOperationException("Calling AE Title must be set.");

            return new AssociationOptions(
                _calledAETitle!,
                _callingAETitle!,
                _presentationContexts,
                _userInformation,
                _artimTimeout,
                _dimseTimeout);
        }
    }
}
