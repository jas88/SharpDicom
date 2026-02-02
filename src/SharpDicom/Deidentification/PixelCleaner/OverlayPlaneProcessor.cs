using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// Processes DICOM overlay planes (group 60xx) for PHI removal.
/// Overlay planes can contain text annotations separate from pixel data.
/// </summary>
public static class OverlayPlaneProcessor
{
    // Overlay groups: 6000, 6002, 6004, ... 601E (16 possible)
    private const ushort OverlayGroupBase = 0x6000;
    private const ushort OverlayGroupMax = 0x601E;

    // Overlay tags relative to group
    private const ushort OverlayRows = 0x0010;
    private const ushort OverlayColumns = 0x0011;
    private const ushort OverlayType = 0x0040;
    private const ushort OverlayDescription = 0x0022;
    private const ushort OverlayLabel = 0x1500;
    private const ushort OverlayData = 0x3000;

    /// <summary>
    /// Gets all overlay plane groups present in the dataset.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to check.</param>
    /// <returns>Enumerable of overlay group numbers (6000, 6002, etc).</returns>
    public static IEnumerable<ushort> GetOverlayGroups(DicomDataset dataset)
    {
        for (ushort group = OverlayGroupBase; group <= OverlayGroupMax; group += 2)
        {
            var dataTag = new DicomTag(group, OverlayData);
            if (dataset.Contains(dataTag))
            {
                yield return group;
            }
        }
    }

    /// <summary>
    /// Checks if any overlay planes exist in the dataset.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to check.</param>
    /// <returns>True if any overlay planes are present.</returns>
    public static bool HasOverlayPlanes(DicomDataset dataset)
    {
        for (ushort group = OverlayGroupBase; group <= OverlayGroupMax; group += 2)
        {
            if (dataset.Contains(new DicomTag(group, OverlayData)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes all overlay planes from the dataset.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to modify.</param>
    public static void RemoveAllOverlays(DicomDataset dataset)
    {
        var tagsToRemove = new List<DicomTag>();

        foreach (var element in dataset)
        {
            var group = element.Tag.Group;
            if (group >= OverlayGroupBase && group <= OverlayGroupMax && (group & 1) == 0)
            {
                tagsToRemove.Add(element.Tag);
            }
        }

        foreach (var tag in tagsToRemove)
        {
            dataset.Remove(tag);
        }
    }

    /// <summary>
    /// Removes a specific overlay plane from the dataset.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to modify.</param>
    /// <param name="group">The overlay group (6000, 6002, etc).</param>
    public static void RemoveOverlay(DicomDataset dataset, ushort group)
    {
        if (group < OverlayGroupBase || group > OverlayGroupMax || (group & 1) != 0)
            return;

        var tagsToRemove = new List<DicomTag>();

        foreach (var element in dataset)
        {
            if (element.Tag.Group == group)
            {
                tagsToRemove.Add(element.Tag);
            }
        }

        foreach (var tag in tagsToRemove)
        {
            dataset.Remove(tag);
        }
    }

    /// <summary>
    /// Clears overlay data (sets to zeros) while preserving metadata.
    /// Use when you need to preserve overlay structure but remove content.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to modify.</param>
    public static void ClearOverlayData(DicomDataset dataset)
    {
        for (ushort group = OverlayGroupBase; group <= OverlayGroupMax; group += 2)
        {
            var dataTag = new DicomTag(group, OverlayData);
            var element = dataset[dataTag];

            if (element != null)
            {
                // Create zero-filled overlay data of same size
                var length = element.Length;
                var zeros = new byte[length];
                dataset.Add(new DicomBinaryElement(dataTag, element.VR, zeros));
            }
        }
    }

    /// <summary>
    /// Gets overlay plane metadata.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to query.</param>
    /// <param name="group">The overlay group (6000, 6002, etc).</param>
    /// <returns>Overlay plane info, or null if the overlay doesn't exist.</returns>
    public static OverlayPlaneInfo? GetOverlayInfo(DicomDataset dataset, ushort group)
    {
        var rowsTag = new DicomTag(group, OverlayRows);
        var colsTag = new DicomTag(group, OverlayColumns);

        if (!dataset.Contains(rowsTag) || !dataset.Contains(colsTag))
            return null;

        var rows = GetUShort(dataset, rowsTag);
        var cols = GetUShort(dataset, colsTag);
        var type = GetString(dataset, new DicomTag(group, OverlayType));
        var desc = GetString(dataset, new DicomTag(group, OverlayDescription));
        var label = GetString(dataset, new DicomTag(group, OverlayLabel));

        return new OverlayPlaneInfo(group, rows, cols, type, desc, label);
    }

    private static ushort GetUShort(DicomDataset ds, DicomTag tag)
    {
        if (ds[tag] is DicomNumericElement ne)
        {
            var value = ne.GetUInt16();
            return value ?? 0;
        }
        return 0;
    }

    private static string? GetString(DicomDataset ds, DicomTag tag)
    {
        if (ds[tag] is DicomStringElement se)
            return se.GetString();
        return null;
    }
}

/// <summary>
/// Overlay plane metadata.
/// </summary>
/// <param name="Group">Overlay group (6000, 6002, etc).</param>
/// <param name="Rows">Number of rows in the overlay.</param>
/// <param name="Columns">Number of columns in the overlay.</param>
/// <param name="Type">Overlay type (G for graphic, R for ROI).</param>
/// <param name="Description">Optional description text.</param>
/// <param name="Label">Optional label text.</param>
public readonly record struct OverlayPlaneInfo(
    ushort Group,
    ushort Rows,
    ushort Columns,
    string? Type,
    string? Description,
    string? Label);
