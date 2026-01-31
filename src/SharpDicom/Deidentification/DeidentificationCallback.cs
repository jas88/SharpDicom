using System;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Provides callbacks for streaming de-identification during DICOM file parsing.
    /// </summary>
    /// <remarks>
    /// This class enables on-the-fly de-identification without loading the entire dataset
    /// into memory. It integrates with the DicomFileReader's element callback system.
    ///
    /// Example usage with DicomFileReader:
    /// <code>
    /// var deidentifier = new DicomDeidentifier(options);
    /// var callback = new DeidentificationCallback(deidentifier);
    ///
    /// // Process elements during streaming read
    /// var options = new DicomReaderOptions
    /// {
    ///     ElementCallback = callback.ProcessElement
    /// };
    /// </code>
    /// </remarks>
    public sealed class DeidentificationCallback : IDisposable
    {
        private readonly DeidentificationOptions _options;
        private readonly UidRemapper _uidRemapper;
        private readonly bool _ownsUidRemapper;
        private readonly DateShifter? _dateShifter;
        private readonly string? _patientId;
        private readonly DeidentificationProfileOption _profileOptions;
        private readonly HashSet<string> _safePrivateCreators;
        private bool _disposed;

        /// <summary>
        /// Creates a callback for streaming de-identification with default basic profile.
        /// </summary>
        /// <param name="patientId">Optional patient ID for consistent UID mapping.</param>
        public DeidentificationCallback(string? patientId = null)
        {
            _patientId = patientId;
            _options = DeidentificationOptions.BasicProfile;
            _uidRemapper = new UidRemapper();
            _ownsUidRemapper = true;
            _dateShifter = null;
            _profileOptions = _options.ToProfileOptions();
            _safePrivateCreators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a callback for streaming de-identification with specific options.
        /// </summary>
        /// <param name="options">De-identification options.</param>
        /// <param name="uidRemapper">UID remapper for consistent mapping.</param>
        /// <param name="dateShifter">Optional date shifter.</param>
        /// <param name="patientId">Optional patient ID for consistent UID mapping.</param>
        public DeidentificationCallback(
            DeidentificationOptions options,
            UidRemapper uidRemapper,
            DateShifter? dateShifter = null,
            string? patientId = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _uidRemapper = uidRemapper ?? throw new ArgumentNullException(nameof(uidRemapper));
            _ownsUidRemapper = false;
            _dateShifter = dateShifter;
            _patientId = patientId;
            _profileOptions = _options.ToProfileOptions();
            _safePrivateCreators = _options.SafePrivateCreators != null
                ? new HashSet<string>(_options.SafePrivateCreators, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Processes an element during streaming read and returns the de-identified result.
        /// </summary>
        /// <param name="element">The element to process.</param>
        /// <returns>The processed element, null if element should be removed, or original if unchanged.</returns>
        public ElementCallbackResult ProcessElement(IDicomElement element)
        {
            if (element == null)
            {
                return ElementCallbackResult.Keep();
            }

            var tag = element.Tag;

            // Handle private tags
            if (tag.IsPrivate)
            {
                return ProcessPrivateTag(element);
            }

            // Handle sequences - we'll keep them but contents should be processed recursively
            if (element is DicomSequence)
            {
                return ElementCallbackResult.Keep();
            }

            // Check for tag-specific override
            if (_options.Overrides?.TryGetValue(tag, out var overrideAction) == true)
            {
                return ApplyAction(element, overrideAction);
            }

            // Get action from PS3.15 profile
            var action = DeidentificationProfiles.GetAction(tag, _profileOptions);
            return ApplyAction(element, action);
        }

        private ElementCallbackResult ProcessPrivateTag(IDicomElement element)
        {
            // Check if private creator is in safe list
            if (_options.RetainSafePrivate)
            {
                // Would need dataset context to check creator - for streaming we need to be conservative
                // Keep if RetainSafePrivate is enabled and no safe list is specified
                if (_safePrivateCreators.Count == 0)
                {
                    return ElementCallbackResult.Keep();
                }
            }

            // Apply default private tag action
            if (_options.DefaultPrivateTagAction == PrivateTagAction.Remove)
            {
                return ElementCallbackResult.Remove();
            }

            return ElementCallbackResult.Keep();
        }

        private ElementCallbackResult ApplyAction(IDicomElement element, DeidentificationAction action)
        {
            var resolved = ActionResolver.Resolve(action);

            switch (resolved)
            {
                case ResolvedAction.Keep:
                    return ElementCallbackResult.Keep();

                case ResolvedAction.Remove:
                    return ElementCallbackResult.Remove();

                case ResolvedAction.ReplaceWithEmpty:
                    if (element.VR.IsStringVR)
                    {
                        var emptyElement = new DicomStringElement(element.Tag, element.VR, Array.Empty<byte>());
                        return ElementCallbackResult.Replace(emptyElement);
                    }
                    return ElementCallbackResult.Keep();

                case ResolvedAction.ReplaceWithDummy:
                    if (element.VR.IsStringVR)
                    {
                        var dummyStr = DummyValueGenerator.GetDummyString(element.VR);
                        if (dummyStr != null)
                        {
                            var bytes = Encoding.ASCII.GetBytes(dummyStr);
                            var dummyElement = new DicomStringElement(element.Tag, element.VR, bytes);
                            return ElementCallbackResult.Replace(dummyElement);
                        }
                    }
                    return ElementCallbackResult.Keep();

                case ResolvedAction.Clean:
                    // Clean = Replace with safe value of similar meaning
                    if (element.VR.IsStringVR)
                    {
                        var dummyStr = DummyValueGenerator.GetDummyString(element.VR);
                        if (dummyStr != null)
                        {
                            var bytes = Encoding.ASCII.GetBytes(dummyStr);
                            var cleanElement = new DicomStringElement(element.Tag, element.VR, bytes);
                            return ElementCallbackResult.Replace(cleanElement);
                        }
                    }
                    return ElementCallbackResult.Keep();

                case ResolvedAction.RemapUid:
                    if (element.VR == DicomVR.UI && element is DicomStringElement strElem)
                    {
                        var originalUid = strElem.GetString(DicomEncoding.Default);
                        if (!string.IsNullOrWhiteSpace(originalUid))
                        {
                            var trimmedUid = originalUid!.Trim();
                            var newUid = _uidRemapper.Remap(trimmedUid, _patientId);
                            var bytes = Encoding.ASCII.GetBytes(newUid);
                            var remappedElement = new DicomStringElement(element.Tag, DicomVR.UI, bytes);
                            return ElementCallbackResult.Replace(remappedElement);
                        }
                    }
                    return ElementCallbackResult.Keep();

                default:
                    return ElementCallbackResult.Keep();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_ownsUidRemapper)
                {
                    _uidRemapper.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Result of processing an element through a callback.
    /// </summary>
    public readonly struct ElementCallbackResult
    {
        /// <summary>
        /// Gets the action to take for this element.
        /// </summary>
        public ElementCallbackAction Action { get; }

        /// <summary>
        /// Gets the replacement element when Action is Replace.
        /// </summary>
        public IDicomElement? ReplacementElement { get; }

        private ElementCallbackResult(ElementCallbackAction action, IDicomElement? replacementElement = null)
        {
            Action = action;
            ReplacementElement = replacementElement;
        }

        /// <summary>
        /// Keep the element unchanged.
        /// </summary>
        public static ElementCallbackResult Keep() => new(ElementCallbackAction.Keep);

        /// <summary>
        /// Remove the element from the output.
        /// </summary>
        public static ElementCallbackResult Remove() => new(ElementCallbackAction.Remove);

        /// <summary>
        /// Replace the element with a different one.
        /// </summary>
        /// <param name="element">The replacement element.</param>
        public static ElementCallbackResult Replace(IDicomElement element) =>
            new(ElementCallbackAction.Replace, element);
    }

    /// <summary>
    /// Actions that can be taken on an element during streaming.
    /// </summary>
    public enum ElementCallbackAction
    {
        /// <summary>Keep the element unchanged.</summary>
        Keep,

        /// <summary>Remove the element from the output.</summary>
        Remove,

        /// <summary>Replace the element with a modified version.</summary>
        Replace
    }
}
