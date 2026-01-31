namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Represents a de-identification action definition from PS3.15 Table E.1-1.
    /// </summary>
    internal readonly struct DeidentificationActionDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeidentificationActionDefinition"/> struct.
        /// </summary>
        public DeidentificationActionDefinition(
            ushort group,
            ushort element,
            string attributeName,
            bool isRetired,
            bool inStandardIOD,
            string basicProfileAction,
            string retainSafePrivateAction,
            string retainUidsAction,
            string retainDeviceIdentityAction,
            string retainInstitutionIdentityAction,
            string retainPatientCharsAction,
            string retainLongFullDatesAction,
            string retainLongModifiedDatesAction,
            string cleanDescriptorsAction,
            string cleanStructuredContentAction,
            string cleanGraphicsAction)
        {
            Group = group;
            Element = element;
            AttributeName = attributeName;
            IsRetired = isRetired;
            InStandardIOD = inStandardIOD;
            BasicProfileAction = basicProfileAction;
            RetainSafePrivateAction = retainSafePrivateAction;
            RetainUidsAction = retainUidsAction;
            RetainDeviceIdentityAction = retainDeviceIdentityAction;
            RetainInstitutionIdentityAction = retainInstitutionIdentityAction;
            RetainPatientCharsAction = retainPatientCharsAction;
            RetainLongFullDatesAction = retainLongFullDatesAction;
            RetainLongModifiedDatesAction = retainLongModifiedDatesAction;
            CleanDescriptorsAction = cleanDescriptorsAction;
            CleanStructuredContentAction = cleanStructuredContentAction;
            CleanGraphicsAction = cleanGraphicsAction;
        }

        /// <summary>Gets the tag group number.</summary>
        public ushort Group { get; }

        /// <summary>Gets the tag element number.</summary>
        public ushort Element { get; }

        /// <summary>Gets the attribute name.</summary>
        public string AttributeName { get; }

        /// <summary>Gets whether the attribute is retired.</summary>
        public bool IsRetired { get; }

        /// <summary>Gets whether the attribute is in standard composite IOD.</summary>
        public bool InStandardIOD { get; }

        /// <summary>Gets the Basic Profile action.</summary>
        public string BasicProfileAction { get; }

        /// <summary>Gets the Retain Safe Private Option action.</summary>
        public string RetainSafePrivateAction { get; }

        /// <summary>Gets the Retain UIDs Option action.</summary>
        public string RetainUidsAction { get; }

        /// <summary>Gets the Retain Device Identity Option action.</summary>
        public string RetainDeviceIdentityAction { get; }

        /// <summary>Gets the Retain Institution Identity Option action.</summary>
        public string RetainInstitutionIdentityAction { get; }

        /// <summary>Gets the Retain Patient Characteristics Option action.</summary>
        public string RetainPatientCharsAction { get; }

        /// <summary>Gets the Retain Longitudinal Full Dates Option action.</summary>
        public string RetainLongFullDatesAction { get; }

        /// <summary>Gets the Retain Longitudinal Modified Dates Option action.</summary>
        public string RetainLongModifiedDatesAction { get; }

        /// <summary>Gets the Clean Descriptors Option action.</summary>
        public string CleanDescriptorsAction { get; }

        /// <summary>Gets the Clean Structured Content Option action.</summary>
        public string CleanStructuredContentAction { get; }

        /// <summary>Gets the Clean Graphics Option action.</summary>
        public string CleanGraphicsAction { get; }
    }
}
