using System;
using System.Collections.Generic;
using System.Linq;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Data;

/// <summary>
/// Dictionary for tracking private tag creators.
/// Private tags use odd group numbers with creator identification:
/// (gggg,00xx) - Private Creator element (LO VR)
/// (gggg,xxyy) - Private data elements (xx = creator slot)
/// </summary>
public sealed class PrivateCreatorDictionary
{
    private readonly Dictionary<uint, string> _creators = new();

    /// <summary>
    /// Register a private creator element value during parsing.
    /// </summary>
    /// <param name="creatorTag">The private creator tag (odd group, element 0x00xx)</param>
    /// <param name="creator">The creator identifier string</param>
    /// <exception cref="ArgumentException">Tag is not a private creator tag.</exception>
    public void Register(DicomTag creatorTag, string creator)
    {
        if (!creatorTag.IsPrivateCreator)
            throw new ArgumentException("Not a private creator tag", nameof(creatorTag));

        var key = ((uint)creatorTag.Group << 16) | creatorTag.Element;
        _creators[key] = creator;
    }

    /// <summary>
    /// Look up the creator for a private data element.
    /// </summary>
    /// <param name="tag">The private data element tag</param>
    /// <returns>The creator string, or null if not registered</returns>
    public string? GetCreator(DicomTag tag)
    {
        if (!tag.IsPrivate || tag.IsPrivateCreator)
            return null;

        var creatorKey = tag.PrivateCreatorKey;
        if (creatorKey == 0)
            return null;

        return _creators.TryGetValue(creatorKey, out var creator) ? creator : null;
    }

    /// <summary>
    /// Check if a creator is registered for the given private data element.
    /// </summary>
    public bool HasCreator(DicomTag tag)
        => GetCreator(tag) != null;

    /// <summary>
    /// Gets the slot for an existing creator in a group, if registered.
    /// </summary>
    /// <param name="group">The group number (must be odd).</param>
    /// <param name="creator">The creator string to find.</param>
    /// <returns>The slot byte (0x10-0xFF), or null if not found.</returns>
    public byte? GetSlotForCreator(ushort group, string creator)
    {
        if ((group & 1) == 0)
            throw new ArgumentException("Group must be odd for private tags", nameof(group));

        foreach (var kvp in _creators)
        {
            var keyGroup = (ushort)(kvp.Key >> 16);
            var keySlot = (byte)kvp.Key;
            if (keyGroup == group && string.Equals(kvp.Value, creator, StringComparison.Ordinal))
                return keySlot;
        }
        return null;
    }

    /// <summary>
    /// Allocates a slot for a private creator in the given group.
    /// If the creator already has a slot in this group, returns the existing slot.
    /// Otherwise allocates the next available slot (starting at 0x10).
    /// </summary>
    /// <param name="group">The group number (must be odd).</param>
    /// <param name="creator">The creator identifier string.</param>
    /// <returns>The private creator tag for the allocated slot.</returns>
    /// <exception cref="ArgumentException">Group is not odd.</exception>
    /// <exception cref="DicomDataException">No available slots (all 0x10-0xFF used).</exception>
    public DicomTag AllocateSlot(ushort group, string creator)
    {
        if ((group & 1) == 0)
            throw new ArgumentException("Group must be odd for private tags", nameof(group));

        if (string.IsNullOrEmpty(creator))
            throw new ArgumentException("Creator cannot be null or empty", nameof(creator));

        // Check if creator already has a slot
        var existingSlot = GetSlotForCreator(group, creator);
        if (existingSlot.HasValue)
            return new DicomTag(group, existingSlot.Value);

        // Find used slots in this group
        var usedSlots = new HashSet<byte>();
        foreach (var key in _creators.Keys)
        {
            var keyGroup = (ushort)(key >> 16);
            if (keyGroup == group)
            {
                usedSlots.Add((byte)key);
            }
        }

        // Find first unused slot (0x10-0xFF)
        for (int slot = 0x10; slot <= 0xFF; slot++)
        {
            if (!usedSlots.Contains((byte)slot))
            {
                var newTag = new DicomTag(group, (ushort)slot);
                Register(newTag, creator);
                return newTag;
            }
        }

        throw new DicomDataException($"No available private slots in group {group:X4}");
    }

    /// <summary>
    /// Compacts the slots in a group to remove gaps.
    /// Returns a mapping from old slot to new slot for updating data elements.
    /// </summary>
    /// <param name="group">The group number (must be odd).</param>
    /// <returns>Dictionary mapping old slot to new slot.</returns>
    public Dictionary<byte, byte> Compact(ushort group)
    {
        if ((group & 1) == 0)
            throw new ArgumentException("Group must be odd for private tags", nameof(group));

        var mapping = new Dictionary<byte, byte>();

        // Collect all creators in this group, ordered by current slot
        var groupCreators = _creators
            .Where(kvp => (ushort)(kvp.Key >> 16) == group)
            .OrderBy(kvp => (byte)kvp.Key)
            .ToList();

        if (groupCreators.Count == 0)
            return mapping;

        // Remove old entries
        foreach (var kvp in groupCreators)
        {
            _creators.Remove(kvp.Key);
        }

        // Re-add with compacted slots starting at 0x10
        byte newSlot = 0x10;
        foreach (var kvp in groupCreators)
        {
            var oldSlot = (byte)kvp.Key;
            var newKey = ((uint)group << 16) | newSlot;
            _creators[newKey] = kvp.Value;
            mapping[oldSlot] = newSlot;
            newSlot++;
        }

        return mapping;
    }

    /// <summary>
    /// Validates that a private data element has a corresponding creator registered.
    /// </summary>
    /// <param name="tag">The private data element tag to validate.</param>
    /// <returns>True if valid (has creator), false if orphan.</returns>
    public bool ValidateHasCreator(DicomTag tag)
    {
        if (!tag.IsPrivate || tag.IsPrivateCreator || tag.Element <= 0x00FF)
            return true; // Not a private data element, or is a creator itself

        return HasCreator(tag);
    }

    /// <summary>
    /// Remove a specific creator registration.
    /// </summary>
    /// <param name="creatorTag">The private creator tag to remove.</param>
    /// <returns>True if removed, false if not found.</returns>
    public bool Remove(DicomTag creatorTag)
    {
        if (!creatorTag.IsPrivateCreator)
            return false;

        var key = ((uint)creatorTag.Group << 16) | creatorTag.Element;
        return _creators.Remove(key);
    }

    /// <summary>
    /// Clear all registered creators.
    /// </summary>
    public void Clear() => _creators.Clear();

    /// <summary>
    /// Get the number of registered creators.
    /// </summary>
    public int Count => _creators.Count;

    /// <summary>
    /// Get all registered creators.
    /// </summary>
    public IEnumerable<(DicomTag Tag, string Creator)> GetAll()
        => _creators.Select(kvp => (new DicomTag(kvp.Key), kvp.Value));

    /// <summary>
    /// Get all creators for a specific group.
    /// </summary>
    /// <param name="group">The group number.</param>
    public IEnumerable<(byte Slot, string Creator)> GetCreatorsInGroup(ushort group)
    {
        foreach (var kvp in _creators)
        {
            var keyGroup = (ushort)(kvp.Key >> 16);
            if (keyGroup == group)
                yield return ((byte)kvp.Key, kvp.Value);
        }
    }
}
