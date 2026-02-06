using System;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Walks a DICOM dataset recursively, remapping all VR=UI elements at unlimited depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="UidRemapper.RemapDataset"/> which only remaps tags designated
    /// by PS3.15 profiles with the RemapUid action, this walker performs a generic VR=UI
    /// traversal that catches ALL UID references including those in nested sequences.
    /// </para>
    /// <para>
    /// This is essential for de-identification of complex DICOM objects such as RT Plans,
    /// Presentation States, Structured Reports, and Key Object Selection documents where
    /// cross-referenced UIDs must remain consistent after remapping.
    /// </para>
    /// <para>
    /// Multi-valued UIDs (backslash-separated, VM &gt; 1) are split and each component
    /// is remapped independently. Standard DICOM UIDs (Transfer Syntax, SOP Class, etc.)
    /// are never remapped.
    /// </para>
    /// </remarks>
    public sealed class UidReferenceWalker
    {
        private readonly UidRemapper _remapper;

        /// <summary>
        /// Creates a new UID reference walker.
        /// </summary>
        /// <param name="remapper">
        /// The UID remapper to use for consistent UID mapping. The walker does not own
        /// the remapper and will not dispose it.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="remapper"/> is null.</exception>
        public UidReferenceWalker(UidRemapper remapper)
        {
            _remapper = remapper ?? throw new ArgumentNullException(nameof(remapper));
        }

        /// <summary>
        /// Remaps all VR=UI elements in the dataset and all nested sequences.
        /// </summary>
        /// <param name="dataset">The dataset to process.</param>
        /// <param name="context">Optional context for consistent UID mapping (e.g., patient ID).</param>
        /// <returns>A <see cref="UidRemapResult"/> containing traversal statistics.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataset"/> is null.</exception>
        public UidRemapResult RemapAllReferences(DicomDataset dataset, string? context = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
#endif

            var result = new UidRemapResult();
            WalkDataset(dataset, context, result);
            return result;
        }

        private void WalkDataset(DicomDataset dataset, string? context, UidRemapResult result)
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

                // Handle sequences recursively
                if (element is DicomSequence seq)
                {
                    foreach (var item in seq.Items)
                    {
                        result.SequenceItemsTraversed++;
                        WalkDataset(item, context, result);
                    }
                    continue;
                }

                // Handle VR=UI elements
                if (element.VR == DicomVR.UI && element is DicomStringElement stringElement)
                {
                    RemapUidElement(dataset, tag, stringElement, context, result);
                }
            }
        }

        private void RemapUidElement(
            DicomDataset dataset,
            DicomTag tag,
            DicomStringElement stringElement,
            string? context,
            UidRemapResult result)
        {
            var originalValue = stringElement.GetString(DicomEncoding.Default);
            if (string.IsNullOrWhiteSpace(originalValue))
            {
                return;
            }

            var trimmedValue = originalValue!.Trim();

            // Check if multi-valued (backslash-separated)
#if NETSTANDARD2_0
            if (trimmedValue.IndexOf('\\') >= 0)
#else
            if (trimmedValue.Contains('\\'))
#endif
            {
                RemapMultiValuedUid(dataset, tag, trimmedValue, context, result);
            }
            else
            {
                RemapSingleUid(dataset, tag, trimmedValue, context, result);
            }
        }

        private void RemapSingleUid(
            DicomDataset dataset,
            DicomTag tag,
            string uid,
            string? context,
            UidRemapResult result)
        {
            // Skip standard DICOM UIDs
            if (_remapper.IsStandardUid(uid))
            {
                return;
            }

            var newUid = _remapper.Remap(uid, context);
            if (newUid != uid)
            {
                var bytes = Encoding.ASCII.GetBytes(newUid);
                dataset.Add(new DicomStringElement(tag, DicomVR.UI, bytes));
                result.UidsRemapped++;
                result.RemappedTags.Add(tag);
            }
        }

        private void RemapMultiValuedUid(
            DicomDataset dataset,
            DicomTag tag,
            string multiValuedUid,
            string? context,
            UidRemapResult result)
        {
            var components = multiValuedUid.Split('\\');
            var anyChanged = false;
            var componentsRemapped = 0;

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i].Trim();
                if (string.IsNullOrEmpty(component))
                {
                    continue;
                }

                // Skip standard DICOM UIDs
                if (_remapper.IsStandardUid(component))
                {
                    components[i] = component;
                    continue;
                }

                var newComponent = _remapper.Remap(component, context);
                if (newComponent != component)
                {
                    components[i] = newComponent;
                    anyChanged = true;
                    componentsRemapped++;
                }
                else
                {
                    components[i] = component;
                }
            }

            if (anyChanged)
            {
                var newValue = string.Join("\\", components);
                var bytes = Encoding.ASCII.GetBytes(newValue);
                dataset.Add(new DicomStringElement(tag, DicomVR.UI, bytes));
                result.UidsRemapped += componentsRemapped;
                result.RemappedTags.Add(tag);
            }
        }
    }
}
