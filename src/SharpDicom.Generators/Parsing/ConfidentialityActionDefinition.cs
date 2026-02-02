namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Represents a DICOM confidentiality action definition parsed from PS3.15 Table E.1-1.
    /// </summary>
    internal readonly struct ConfidentialityActionDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfidentialityActionDefinition"/> struct.
        /// </summary>
        public ConfidentialityActionDefinition(
            ushort group,
            ushort element,
            string name,
            string basicAction,
            string retainSafePrivateAction,
            string retainUidsAction,
            string retainDeviceIdentityAction,
            string retainInstitutionIdentityAction,
            string retainPatientCharacteristicsAction,
            string retainLongFullDatesAction,
            string retainLongModifDatesAction,
            string cleanDescAction,
            string cleanStructuredContentAction,
            string cleanGraphAction)
        {
            Group = group;
            Element = element;
            Name = name;
            BasicAction = basicAction;
            RetainSafePrivateAction = retainSafePrivateAction;
            RetainUidsAction = retainUidsAction;
            RetainDeviceIdentityAction = retainDeviceIdentityAction;
            RetainInstitutionIdentityAction = retainInstitutionIdentityAction;
            RetainPatientCharacteristicsAction = retainPatientCharacteristicsAction;
            RetainLongFullDatesAction = retainLongFullDatesAction;
            RetainLongModifDatesAction = retainLongModifDatesAction;
            CleanDescAction = cleanDescAction;
            CleanStructuredContentAction = cleanStructuredContentAction;
            CleanGraphAction = cleanGraphAction;
        }

        /// <summary>Gets the tag group number.</summary>
        public ushort Group { get; }

        /// <summary>Gets the tag element number.</summary>
        public ushort Element { get; }

        /// <summary>Gets the attribute name.</summary>
        public string Name { get; }

        /// <summary>Gets the Basic Profile action code (D, Z, X, K, C, U, or combination like X/Z).</summary>
        public string BasicAction { get; }

        /// <summary>Gets the Retain Safe Private Option action code.</summary>
        public string RetainSafePrivateAction { get; }

        /// <summary>Gets the Retain UIDs Option action code.</summary>
        public string RetainUidsAction { get; }

        /// <summary>Gets the Retain Device Identity Option action code.</summary>
        public string RetainDeviceIdentityAction { get; }

        /// <summary>Gets the Retain Institution Identity Option action code.</summary>
        public string RetainInstitutionIdentityAction { get; }

        /// <summary>Gets the Retain Patient Characteristics Option action code.</summary>
        public string RetainPatientCharacteristicsAction { get; }

        /// <summary>Gets the Retain Longitudinal Full Dates Option action code.</summary>
        public string RetainLongFullDatesAction { get; }

        /// <summary>Gets the Retain Longitudinal Modified Dates Option action code.</summary>
        public string RetainLongModifDatesAction { get; }

        /// <summary>Gets the Clean Descriptions Option action code.</summary>
        public string CleanDescAction { get; }

        /// <summary>Gets the Clean Structured Content Option action code.</summary>
        public string CleanStructuredContentAction { get; }

        /// <summary>Gets the Clean Graphics Option action code.</summary>
        public string CleanGraphAction { get; }
    }
}
