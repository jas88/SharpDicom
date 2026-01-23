using System;
using System.Collections.Generic;
using System.Text;

namespace SharpDicom.Data;

/// <summary>
/// Extension methods for DicomDataset private tag operations.
/// </summary>
public static class DicomDatasetExtensions
{
    /// <summary>
    /// Strips all private tags (odd group elements) from the dataset.
    /// This includes both private creator elements and private data elements.
    /// Recursively processes sequences.
    /// </summary>
    /// <param name="dataset">The dataset to strip.</param>
    public static void StripPrivateTags(this DicomDataset dataset)
    {
        StripPrivateTags(dataset, creatorFilter: null);
    }

    /// <summary>
    /// Strips private tags from the dataset, optionally keeping specific creators.
    /// Recursively processes sequences.
    /// </summary>
    /// <param name="dataset">The dataset to strip.</param>
    /// <param name="creatorFilter">
    /// Optional filter function. Return true to KEEP the creator's tags, false to remove.
    /// If null, all private tags are removed.
    /// </param>
    public static void StripPrivateTags(
        this DicomDataset dataset,
        Func<string, bool>? creatorFilter)
    {
        // Collect tags to remove
        var tagsToRemove = new List<DicomTag>();

        // First pass: identify creators to keep
        var creatorsToKeep = new HashSet<(ushort Group, byte Slot)>();

        if (creatorFilter != null)
        {
            foreach (var (tag, creator) in dataset.PrivateCreators.GetAll())
            {
                if (creatorFilter(creator))
                {
                    creatorsToKeep.Add((tag.Group, (byte)tag.Element));
                }
            }
        }

        // Collect sequences to process recursively
        var sequences = new List<DicomSequence>();

        // Second pass: identify elements to remove and sequences to process
        foreach (var element in dataset)
        {
            if (element is DicomSequence seq)
            {
                sequences.Add(seq);
            }

            if (!element.Tag.IsPrivate)
                continue;

            if (element.Tag.IsPrivateCreator)
            {
                // Keep creator if its tags are being kept
                var key = (element.Tag.Group, (byte)element.Tag.Element);
                if (!creatorsToKeep.Contains(key))
                {
                    tagsToRemove.Add(element.Tag);
                }
            }
            else
            {
                // Private data element - keep if creator is kept
                var slot = element.Tag.PrivateCreatorSlot;
                var key = (element.Tag.Group, slot);
                if (!creatorsToKeep.Contains(key))
                {
                    tagsToRemove.Add(element.Tag);
                }
            }
        }

        // Remove identified tags and track which creator elements are removed
        var removedCreatorTags = new List<DicomTag>();
        foreach (var tag in tagsToRemove)
        {
            dataset.Remove(tag);
            if (tag.IsPrivateCreator)
            {
                removedCreatorTags.Add(tag);
            }
        }

        // Clean up the PrivateCreatorDictionary
        if (creatorFilter == null)
        {
            dataset.PrivateCreators.Clear();
        }
        else
        {
            // Remove specific creators from the dictionary
            foreach (var creatorTag in removedCreatorTags)
            {
                dataset.PrivateCreators.Remove(creatorTag);
            }
        }

        // Process sequences recursively
        foreach (var sequence in sequences)
        {
            foreach (var item in sequence.Items)
            {
                item.StripPrivateTags(creatorFilter);
            }
        }
    }

    /// <summary>
    /// Adds a private element to the dataset, automatically allocating a creator slot.
    /// </summary>
    /// <param name="dataset">The dataset to add to.</param>
    /// <param name="group">The group number (must be odd).</param>
    /// <param name="creator">The private creator identifier.</param>
    /// <param name="elementOffset">The element offset within the creator's block (0x00-0xFF).</param>
    /// <param name="vr">The value representation.</param>
    /// <param name="value">The value bytes.</param>
    /// <returns>The tag of the added element.</returns>
    /// <exception cref="ArgumentException">Group is not odd.</exception>
    public static DicomTag AddPrivateElement(
        this DicomDataset dataset,
        ushort group,
        string creator,
        byte elementOffset,
        DicomVR vr,
        ReadOnlyMemory<byte> value)
    {
        if ((group & 1) == 0)
            throw new ArgumentException("Private tags require odd group number", nameof(group));

        // Allocate or reuse slot
        var creatorTag = dataset.PrivateCreators.AllocateSlot(group, creator);
        var slot = (byte)creatorTag.Element;

        // Build full element number
        var fullElement = (ushort)((slot << 8) | elementOffset);
        var dataTag = new DicomTag(group, fullElement);

        // Add creator element if not already present
        if (dataset[creatorTag] == null)
        {
            // Creator is LO VR, padded with space to even length
            var creatorBytes = Encoding.ASCII.GetBytes(creator);
            if (creatorBytes.Length % 2 != 0)
            {
                var padded = new byte[creatorBytes.Length + 1];
                creatorBytes.CopyTo(padded, 0);
                padded[padded.Length - 1] = (byte)' ';
                creatorBytes = padded;
            }
            dataset.Add(new DicomStringElement(creatorTag, DicomVR.LO, creatorBytes));
        }

        // Add or update data element based on VR type
        var element = CreateElement(dataTag, vr, value);
        dataset.AddOrUpdate(element);

        return dataTag;
    }

    /// <summary>
    /// Adds a private string element to the dataset.
    /// </summary>
    /// <param name="dataset">The dataset to add to.</param>
    /// <param name="group">The group number (must be odd).</param>
    /// <param name="creator">The private creator identifier.</param>
    /// <param name="elementOffset">The element offset within the creator's block (0x00-0xFF).</param>
    /// <param name="vr">The value representation.</param>
    /// <param name="value">The string value.</param>
    /// <returns>The tag of the added element.</returns>
    public static DicomTag AddPrivateString(
        this DicomDataset dataset,
        ushort group,
        string creator,
        byte elementOffset,
        DicomVR vr,
        string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length % 2 != 0)
        {
            var padded = new byte[bytes.Length + 1];
            bytes.CopyTo(padded, 0);
            padded[padded.Length - 1] = DicomVRInfo.GetInfo(vr).PaddingByte;
            bytes = padded;
        }
        return AddPrivateElement(dataset, group, creator, elementOffset, vr, bytes);
    }

    /// <summary>
    /// Checks if the dataset contains any orphan private elements
    /// (private data elements without corresponding creator).
    /// </summary>
    /// <param name="dataset">The dataset to check.</param>
    /// <returns>List of orphan element tags.</returns>
    public static IReadOnlyList<DicomTag> FindOrphanPrivateElements(this DicomDataset dataset)
    {
        var orphans = new List<DicomTag>();

        foreach (var element in dataset)
        {
            if (element.Tag.IsPrivate &&
                !element.Tag.IsPrivateCreator &&
                element.Tag.Element > 0x00FF)
            {
                if (!dataset.PrivateCreators.ValidateHasCreator(element.Tag))
                {
                    orphans.Add(element.Tag);
                }
            }
        }

        return orphans;
    }

    /// <summary>
    /// Creates an appropriate element type based on VR.
    /// </summary>
    private static IDicomElement CreateElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
    {
        var vrInfo = DicomVRInfo.GetInfo(vr);

        // Use DicomStringElement for string VRs
        if (vrInfo.IsStringVR)
        {
            return new DicomStringElement(tag, vr, value);
        }

        // Use DicomNumericElement for numeric VRs
        if (vr == DicomVR.US || vr == DicomVR.SS || vr == DicomVR.UL || vr == DicomVR.SL ||
            vr == DicomVR.FL || vr == DicomVR.FD || vr == DicomVR.UV || vr == DicomVR.SV)
        {
            return new DicomNumericElement(tag, vr, value);
        }

        // Use DicomBinaryElement for everything else
        return new DicomBinaryElement(tag, vr, value);
    }
}
