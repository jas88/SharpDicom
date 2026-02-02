using SharpDicom.Data;

namespace SharpDicom.Deidentification;

/// <summary>
/// Interface for custom de-identification rules that extend or override standard profiles.
/// </summary>
/// <remarks>
/// <para>
/// Custom rules allow fine-grained control over de-identification behavior for specific
/// tags or categories of tags. Rules are evaluated in order, and the first rule that
/// returns a non-null action will be used.
/// </para>
/// <para>
/// Example: Always remove a specific vendor tag:
/// <code>
/// public class RemoveVendorTagRule : IDeidentificationRule
/// {
///     private static readonly DicomTag VendorTag = new(0x7FE1, 0x0010);
///
///     public bool AppliesTo(DicomTag tag) => tag == VendorTag;
///
///     public DeidentificationAction? GetAction(DicomTag tag, DeidentificationProfile profile)
///         => DeidentificationAction.Remove;
///
///     public IDicomElement? Transform(IDicomElement element, DeidentificationContext context)
///         => null; // Use standard action handling
/// }
/// </code>
/// </para>
/// </remarks>
public interface IDeidentificationRule
{
    /// <summary>
    /// Determines whether this rule applies to the given tag.
    /// </summary>
    /// <param name="tag">The DICOM tag to check.</param>
    /// <returns>true if this rule should be consulted for the tag; otherwise, false.</returns>
    /// <remarks>
    /// This method is called first for each element. If it returns false,
    /// <see cref="GetAction"/> and <see cref="Transform"/> will not be called
    /// for this element.
    /// </remarks>
    bool AppliesTo(DicomTag tag);

    /// <summary>
    /// Gets the action to apply for the given tag, or null to use the standard profile action.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="profile">The current de-identification profile being applied.</param>
    /// <returns>
    /// The action to apply, or null to fall back to the standard profile action.
    /// </returns>
    /// <remarks>
    /// Return null to let the standard profile determine the action.
    /// Return a specific action to override the standard profile.
    /// </remarks>
    DeidentificationAction? GetAction(DicomTag tag, DeidentificationProfile profile);

    /// <summary>
    /// Optionally transforms the element value during de-identification.
    /// </summary>
    /// <param name="element">The element to potentially transform.</param>
    /// <param name="context">The de-identification context with UID mappings and date offsets.</param>
    /// <returns>
    /// A transformed element to use instead of standard processing,
    /// the original element unchanged, or null to remove the element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is called after <see cref="GetAction"/> if the action is not Remove.
    /// It allows custom transformation of element values, such as:
    /// </para>
    /// <list type="bullet">
    /// <item>Custom UID generation with specific prefix</item>
    /// <item>Structured cleaning of text fields</item>
    /// <item>Custom date handling</item>
    /// </list>
    /// <para>
    /// Return the original element to keep it unchanged (for Keep action).
    /// Return a new element with modified value.
    /// Return null to remove the element regardless of the action.
    /// </para>
    /// </remarks>
    IDicomElement? Transform(IDicomElement element, DeidentificationContext context);
}
