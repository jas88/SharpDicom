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

        // De-identification marker tags (PS3.15 Annex E.1.1)
        private static readonly DicomTag TagPatientIdentityRemoved = new(0x0012, 0x0062);
        private static readonly DicomTag TagDeidentificationMethod = new(0x0012, 0x0063);
        private static readonly DicomTag TagDeidentificationMethodCodeSequence = new(0x0012, 0x0064);
        private static readonly DicomTag TagLongitudinalTemporalInformationModified = new(0x0028, 0x0303);

        // Code Sequence item tags
        private static readonly DicomTag TagCodeValue = new(0x0008, 0x0100);
        private static readonly DicomTag TagCodingSchemeDesignator = new(0x0008, 0x0102);
        private static readonly DicomTag TagCodeMeaning = new(0x0008, 0x0104);

        private void AddDeidentificationMarkers(DicomDataset dataset)
        {
            // Add Patient Identity Removed marker (0012,0062)
            var yesBytes = Encoding.ASCII.GetBytes("YES");
            dataset.Add(new DicomStringElement(TagPatientIdentityRemoved, DicomVR.CS, yesBytes));

            // Add De-identification Method text (0012,0063)
            var methodText = BuildMethodText();
            var methodBytes = Encoding.ASCII.GetBytes(methodText);
            dataset.Add(new DicomStringElement(TagDeidentificationMethod, DicomVR.LO, methodBytes));

            // Add De-identification Method Code Sequence (0012,0064)
            var codeSequence = BuildMethodCodeSequence();
            dataset.Add(codeSequence);

            // Add Longitudinal Temporal Information Modified (0028,0303)
            var temporalStatus = GetLongitudinalTemporalStatus();
            if (temporalStatus != null)
            {
                var temporalBytes = Encoding.ASCII.GetBytes(temporalStatus);
                dataset.Add(new DicomStringElement(TagLongitudinalTemporalInformationModified, DicomVR.CS, temporalBytes));
            }
        }

        private string BuildMethodText()
        {
            var parts = new List<string> { "DICOM PS3.15 Basic Application Level Confidentiality Profile" };

            if (_options.RetainSafePrivate)
                parts.Add("Retain Safe Private Option");
            if (_options.RetainUIDs)
                parts.Add("Retain UIDs Option");
            if (_options.RetainDeviceIdentity)
                parts.Add("Retain Device Identity Option");
            if (_options.RetainInstitutionIdentity)
                parts.Add("Retain Institution Identity Option");
            if (_options.RetainPatientCharacteristics)
                parts.Add("Retain Patient Characteristics Option");
            if (_options.RetainLongitudinalFullDates)
                parts.Add("Retain Longitudinal Full Dates Option");
            if (_options.RetainLongitudinalModifiedDates)
                parts.Add("Retain Longitudinal Temporal Information with Modified Dates Option");
            if (_options.CleanDescriptors)
                parts.Add("Clean Descriptors Option");
            if (_options.CleanStructuredContent)
                parts.Add("Clean Structured Content Option");
            if (_options.CleanGraphics)
                parts.Add("Clean Graphics Option");

            return string.Join("\\", parts);  // DICOM value separator
        }

        private DicomSequence BuildMethodCodeSequence()
        {
            var items = new List<DicomDataset>();

            // Basic Profile code (CID 7050, DCM 113100)
            items.Add(CreateCodeItem("113100", "DCM", "Basic Application Confidentiality Profile"));

            // Add codes for active options (CID 7050)
            if (_options.RetainSafePrivate)
                items.Add(CreateCodeItem("113101", "DCM", "Retain Safe Private Option"));
            if (_options.RetainUIDs)
                items.Add(CreateCodeItem("113110", "DCM", "Retain UIDs Option"));
            if (_options.RetainDeviceIdentity)
                items.Add(CreateCodeItem("113109", "DCM", "Retain Device Identity Option"));
            if (_options.RetainInstitutionIdentity)
                items.Add(CreateCodeItem("113112", "DCM", "Retain Institution Identity Option"));
            if (_options.RetainPatientCharacteristics)
                items.Add(CreateCodeItem("113108", "DCM", "Retain Patient Characteristics Option"));
            if (_options.RetainLongitudinalFullDates)
                items.Add(CreateCodeItem("113106", "DCM", "Retain Longitudinal Temporal Information with Full Dates Option"));
            if (_options.RetainLongitudinalModifiedDates)
                items.Add(CreateCodeItem("113107", "DCM", "Retain Longitudinal Temporal Information with Modified Dates Option"));
            if (_options.CleanDescriptors)
                items.Add(CreateCodeItem("113105", "DCM", "Clean Descriptors Option"));
            if (_options.CleanStructuredContent)
                items.Add(CreateCodeItem("113104", "DCM", "Clean Structured Content Option"));
            if (_options.CleanGraphics)
                items.Add(CreateCodeItem("113103", "DCM", "Clean Graphics Option"));

            return new DicomSequence(TagDeidentificationMethodCodeSequence, items);
        }

        private static DicomDataset CreateCodeItem(string codeValue, string codingScheme, string codeMeaning)
        {
            var item = new DicomDataset();
            item.Add(new DicomStringElement(TagCodeValue, DicomVR.SH, Encoding.ASCII.GetBytes(codeValue)));
            item.Add(new DicomStringElement(TagCodingSchemeDesignator, DicomVR.SH, Encoding.ASCII.GetBytes(codingScheme)));
            item.Add(new DicomStringElement(TagCodeMeaning, DicomVR.LO, Encoding.ASCII.GetBytes(codeMeaning)));
            return item;
        }

        private string? GetLongitudinalTemporalStatus()
        {
            // Per PS3.15 Table CID 7050
            if (_options.RetainLongitudinalFullDates)
            {
                // Full dates retained - no modification
                return "UNMODIFIED";
            }
            if (_options.RetainLongitudinalModifiedDates || _dateShifter != null)
            {
                // Dates shifted but relationships preserved
                return "MODIFIED";
            }
            // Dates removed entirely
            return "REMOVED";
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
