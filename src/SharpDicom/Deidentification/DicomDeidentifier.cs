using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Main de-identification engine that applies PS3.15 profiles to DICOM datasets.
    /// </summary>
    public sealed class DicomDeidentifier : IDisposable
    {
        private readonly DeidentificationOptions _options;
        private readonly UidRemapper _uidRemapper;
        private readonly DateShifter? _dateShifter;
        private readonly HashSet<string> _safePrivateCreators;
        private readonly DeidentificationProfileOption _profileOptions;
        private readonly bool _ownsUidRemapper;

        /// <summary>
        /// Creates a de-identifier with the specified options.
        /// </summary>
        /// <param name="options">De-identification options.</param>
        /// <param name="uidRemapper">Optional UID remapper for consistent UID mapping.</param>
        /// <param name="dateShifter">Optional date shifter for date modification.</param>
        public DicomDeidentifier(
            DeidentificationOptions? options = null,
            UidRemapper? uidRemapper = null,
            DateShifter? dateShifter = null)
        {
            _options = options ?? DeidentificationOptions.BasicProfile;
            _uidRemapper = uidRemapper ?? new UidRemapper();
            _ownsUidRemapper = uidRemapper == null;
            _dateShifter = dateShifter;
            _profileOptions = _options.ToProfileOptions();

            _safePrivateCreators = _options.SafePrivateCreators != null
                ? new HashSet<string>(_options.SafePrivateCreators, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// De-identifies a DICOM dataset in place.
        /// </summary>
        /// <param name="dataset">The dataset to de-identify.</param>
        /// <returns>Result containing statistics and any warnings.</returns>
        public DeidentificationResult Deidentify(DicomDataset dataset)
        {
            var result = new DeidentificationResult();

            try
            {
                // Get patient ID for consistent shifting/mapping
                var patientId = dataset.GetString(DicomTag.PatientID);

                // Process all elements
                ProcessDataset(dataset, patientId, result);

                // Apply date shifting if configured
                if (_dateShifter != null)
                {
                    result.Summary.DatesShifted = _dateShifter.ShiftDates(dataset, patientId);
                }

                // Add de-identification markers
                AddDeidentificationMarkers(dataset);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"De-identification failed: {ex.Message}");
            }

            return result;
        }

        private void ProcessDataset(DicomDataset dataset, string? patientId, DeidentificationResult result)
        {
            // Collect tags to process (avoid modifying during enumeration)
            var tagsToProcess = new List<DicomTag>();
            foreach (var element in dataset)
            {
                tagsToProcess.Add(element.Tag);
            }

            foreach (var tag in tagsToProcess)
            {
                var element = dataset[tag];
                if (element == null) continue;

                ProcessElement(dataset, element, patientId, result);
            }
        }

        private void ProcessElement(
            DicomDataset dataset,
            IDicomElement element,
            string? patientId,
            DeidentificationResult result)
        {
            var tag = element.Tag;

            // Handle private tags
            if (tag.IsPrivate)
            {
                ProcessPrivateTag(dataset, element, result);
                return;
            }

            // Handle sequences recursively
            if (element is DicomSequence seq)
            {
                foreach (var item in seq.Items)
                {
                    ProcessDataset(item, patientId, result);
                    result.Summary.SequenceItemsProcessed++;
                }
                return;
            }

            // Check for tag-specific override
            if (_options.Overrides?.TryGetValue(tag, out var overrideAction) == true)
            {
                ApplyAction(dataset, element, overrideAction, patientId, result);
                return;
            }

            // Get action from PS3.15 profile
            var action = DeidentificationProfiles.GetAction(tag, _profileOptions);
            ApplyAction(dataset, element, action, patientId, result);
        }

        private void ProcessPrivateTag(
            DicomDataset dataset,
            IDicomElement element,
            DeidentificationResult result)
        {
            result.Summary.PrivateTagsProcessed++;

            // Check if private creator is in safe list
            if (_options.RetainSafePrivate && element.Tag.PrivateCreatorSlot > 0)
            {
                var creator = dataset.PrivateCreators.GetCreator(element.Tag);
                if (creator != null && _safePrivateCreators.Contains(creator))
                {
                    return; // Keep safe private tag
                }
            }

            // Apply default private tag action
            if (_options.DefaultPrivateTagAction == PrivateTagAction.Remove)
            {
                dataset.Remove(element.Tag);
                result.Summary.AttributesRemoved++;
            }
        }

        private void ApplyAction(
            DicomDataset dataset,
            IDicomElement element,
            DeidentificationAction action,
            string? patientId,
            DeidentificationResult result)
        {
            var resolved = ActionResolver.Resolve(action);
            var tag = element.Tag;

            switch (resolved)
            {
                case ResolvedAction.Keep:
                    // Do nothing
                    break;

                case ResolvedAction.Remove:
                    dataset.Remove(tag);
                    result.Summary.AttributesRemoved++;
                    break;

                case ResolvedAction.ReplaceWithEmpty:
                    ReplaceWithEmpty(dataset, element);
                    result.Summary.AttributesEmptied++;
                    break;

                case ResolvedAction.ReplaceWithDummy:
                    ReplaceWithDummy(dataset, element);
                    result.Summary.AttributesReplaced++;
                    break;

                case ResolvedAction.Clean:
                    CleanElement(dataset, element);
                    result.Summary.AttributesReplaced++;
                    break;

                case ResolvedAction.RemapUid:
                    if (element.VR == DicomVR.UI && element is DicomStringElement strElem)
                    {
                        var originalUid = strElem.GetString(DicomEncoding.Default);
                        if (!string.IsNullOrWhiteSpace(originalUid))
                        {
                            var trimmedUid = originalUid!.Trim();
                            var newUid = _uidRemapper.Remap(trimmedUid, patientId);
                            var bytes = Encoding.ASCII.GetBytes(newUid);
                            dataset.Add(new DicomStringElement(tag, DicomVR.UI, bytes));
                            result.Summary.UidsRemapped++;
                            result.UidRemappings.Add(new UidRemapInfo(tag, trimmedUid, newUid));
                        }
                    }
                    break;
            }
        }

        private static void ReplaceWithEmpty(DicomDataset dataset, IDicomElement element)
        {
            if (element.VR.IsStringVR)
            {
                dataset.Add(new DicomStringElement(element.Tag, element.VR, Array.Empty<byte>()));
            }
        }

        private static void ReplaceWithDummy(DicomDataset dataset, IDicomElement element)
        {
            if (element.VR.IsStringVR)
            {
                var dummyStr = DummyValueGenerator.GetDummyString(element.VR);
                if (dummyStr != null)
                {
                    var bytes = Encoding.ASCII.GetBytes(dummyStr);
                    dataset.Add(new DicomStringElement(element.Tag, element.VR, bytes));
                }
            }
        }

        private static void CleanElement(DicomDataset dataset, IDicomElement element)
        {
            // Clean action: Replace with safe value of similar meaning
            // For most attributes, this means replacing with a generic value
            ReplaceWithDummy(dataset, element);
        }

        // De-identification marker tags
        private static readonly DicomTag TagPatientIdentityRemoved = new(0x0012, 0x0062);
        private static readonly DicomTag TagDeidentificationMethod = new(0x0012, 0x0063);

        private static void AddDeidentificationMarkers(DicomDataset dataset)
        {
            // Add Patient Identity Removed marker
            var yesBytes = Encoding.ASCII.GetBytes("YES");
            dataset.Add(new DicomStringElement(TagPatientIdentityRemoved, DicomVR.CS, yesBytes));

            // Add De-identification Method
            var methodBytes = Encoding.ASCII.GetBytes("DICOM PS3.15 Basic Application Level Confidentiality Profile");
            dataset.Add(new DicomStringElement(TagDeidentificationMethod, DicomVR.LO, methodBytes));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsUidRemapper)
            {
                _uidRemapper.Dispose();
            }
        }
    }
}
