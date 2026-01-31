using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Resolves compound de-identification actions to concrete actions based on context.
    /// </summary>
    public static class ActionResolver
    {
        /// <summary>
        /// Resolves a de-identification action to a concrete action.
        /// </summary>
        /// <param name="action">The de-identification action from the profile.</param>
        /// <param name="attributeType">The attribute type in the IOD (for conditional actions).</param>
        /// <returns>The resolved concrete action.</returns>
        public static ResolvedAction Resolve(
            DeidentificationAction action,
            DicomAttributeType attributeType = DicomAttributeType.Type3)
        {
            return action switch
            {
                DeidentificationAction.Keep => ResolvedAction.Keep,
                DeidentificationAction.Remove => ResolvedAction.Remove,
                DeidentificationAction.ZeroOrDummy => ResolvedAction.ReplaceWithEmpty,
                DeidentificationAction.Dummy => ResolvedAction.ReplaceWithDummy,
                DeidentificationAction.Clean => ResolvedAction.Clean,
                DeidentificationAction.RemapUid => ResolvedAction.RemapUid,

                // Compound actions resolved based on attribute type
                DeidentificationAction.ZeroOrDummyConditional => ResolveZeroOrDummy(attributeType),
                DeidentificationAction.RemoveOrZeroConditional => ResolveRemoveOrZero(attributeType),
                DeidentificationAction.RemoveOrDummyConditional => ResolveRemoveOrDummy(attributeType),
                DeidentificationAction.RemoveOrZeroOrDummyConditional => ResolveRemoveOrZeroOrDummy(attributeType),
                DeidentificationAction.RemoveOrZeroOrUidConditional => ResolveRemoveOrZeroOrUid(attributeType),

                _ => ResolvedAction.Remove
            };
        }

        /// <summary>
        /// Resolves a de-identification action considering VR and presence of value.
        /// </summary>
        /// <param name="action">The de-identification action from the profile.</param>
        /// <param name="vr">The value representation of the element.</param>
        /// <param name="hasValue">Whether the element currently has a value.</param>
        /// <param name="attributeType">The attribute type in the IOD.</param>
        /// <returns>The resolved concrete action.</returns>
        public static ResolvedAction Resolve(
            DeidentificationAction action,
            DicomVR vr,
            bool hasValue,
            DicomAttributeType attributeType = DicomAttributeType.Type3)
        {
            // First resolve compound actions
            var baseResolution = Resolve(action, attributeType);

            // RemapUid only makes sense for UI VR - for non-UI Type1 attributes,
            // use ReplaceWithDummy instead
            if (baseResolution == ResolvedAction.RemapUid && vr != DicomVR.UI)
            {
                return ResolvedAction.ReplaceWithDummy;
            }

            // If element is already empty and resolved to empty, keep it that way
            if (!hasValue && baseResolution == ResolvedAction.ReplaceWithEmpty)
            {
                return ResolvedAction.Keep;
            }

            return baseResolution;
        }

        /// <summary>
        /// Z/D: Use Z (empty) if Type 2/3, D (dummy) if Type 1.
        /// </summary>
        private static ResolvedAction ResolveZeroOrDummy(DicomAttributeType type)
        {
            return type is DicomAttributeType.Type1 or DicomAttributeType.Type1C
                ? ResolvedAction.ReplaceWithDummy
                : ResolvedAction.ReplaceWithEmpty;
        }

        /// <summary>
        /// X/Z: Remove if Type 3, use Z (empty) if Type 2.
        /// </summary>
        private static ResolvedAction ResolveRemoveOrZero(DicomAttributeType type)
        {
            return type is DicomAttributeType.Type2 or DicomAttributeType.Type2C
                ? ResolvedAction.ReplaceWithEmpty
                : ResolvedAction.Remove;
        }

        /// <summary>
        /// X/D: Remove if Type 2/3, use D (dummy) if Type 1.
        /// </summary>
        private static ResolvedAction ResolveRemoveOrDummy(DicomAttributeType type)
        {
            return type is DicomAttributeType.Type1 or DicomAttributeType.Type1C
                ? ResolvedAction.ReplaceWithDummy
                : ResolvedAction.Remove;
        }

        /// <summary>
        /// X/Z/D: Remove if Type 3, empty if Type 2, dummy if Type 1.
        /// </summary>
        private static ResolvedAction ResolveRemoveOrZeroOrDummy(DicomAttributeType type)
        {
            return type switch
            {
                DicomAttributeType.Type1 or DicomAttributeType.Type1C => ResolvedAction.ReplaceWithDummy,
                DicomAttributeType.Type2 or DicomAttributeType.Type2C => ResolvedAction.ReplaceWithEmpty,
                _ => ResolvedAction.Remove
            };
        }

        /// <summary>
        /// X/Z/U*: Remove if Type 3, empty if Type 2, UID remap if Type 1 with UI VR.
        /// </summary>
        private static ResolvedAction ResolveRemoveOrZeroOrUid(DicomAttributeType type)
        {
            return type switch
            {
                DicomAttributeType.Type1 or DicomAttributeType.Type1C => ResolvedAction.RemapUid,
                DicomAttributeType.Type2 or DicomAttributeType.Type2C => ResolvedAction.ReplaceWithEmpty,
                _ => ResolvedAction.Remove
            };
        }
    }
}
