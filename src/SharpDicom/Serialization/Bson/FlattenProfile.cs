using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Defines which sequences should be flattened (dot-notation fields at top level)
/// during BSON serialization.
/// </summary>
/// <remarks>
/// When a sequence tag is in the flatten set and the sequence appears at depth 1
/// (top-level dataset), its items' elements are additionally written as
/// concatenated key fields at the document root, enabling direct MongoDB queries
/// without <c>$elemMatch</c>.
/// </remarks>
public sealed class FlattenProfile
{
    /// <summary>
    /// Gets the name of this profile.
    /// </summary>
    /// <example>"radiology", "pathology"</example>
    public string Name { get; }

    /// <summary>
    /// Gets the set of sequence tags whose items should be flattened.
    /// </summary>
    public HashSet<DicomTag> FlattenTags { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlattenProfile"/> class.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="flattenTags">The initial set of tags to flatten.</param>
    public FlattenProfile(string name, IEnumerable<DicomTag> flattenTags)
    {
        Name = name;
        FlattenTags = new HashSet<DicomTag>(flattenTags);
    }

    /// <summary>
    /// Gets a predefined radiology profile that flattens common radiology sequences.
    /// </summary>
    /// <remarks>
    /// Flattens:
    /// <list type="bullet">
    /// <item><description>Referenced Study Sequence (0008,1110)</description></item>
    /// <item><description>Referenced Series Sequence (0008,1115)</description></item>
    /// <item><description>Request Attributes Sequence (0040,0275)</description></item>
    /// <item><description>Procedure Code Sequence (0008,1032)</description></item>
    /// </list>
    /// </remarks>
    public static FlattenProfile Radiology { get; } = new("radiology", new[]
    {
        new DicomTag(0x0008, 0x1110), // Referenced Study Sequence
        new DicomTag(0x0008, 0x1115), // Referenced Series Sequence
        new DicomTag(0x0040, 0x0275), // Request Attributes Sequence
        new DicomTag(0x0008, 0x1032), // Procedure Code Sequence
    });

    /// <summary>
    /// Creates a new profile with an additional tag to flatten.
    /// </summary>
    /// <param name="tag">The sequence tag to add.</param>
    /// <returns>A new <see cref="FlattenProfile"/> with the added tag.</returns>
    public FlattenProfile WithTag(DicomTag tag)
    {
        var newTags = new HashSet<DicomTag>(FlattenTags) { tag };
        return new FlattenProfile(Name, newTags);
    }
}
