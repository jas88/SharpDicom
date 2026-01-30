using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for generated DeidentificationProfiles functionality.
/// </summary>
[TestFixture]
public class DeidentificationProfilesTests
{
    [Test]
    public void Count_HasExpectedNumberOfEntries()
    {
        // Expect at least 500 entries (spec has ~600+)
        Assert.That(DeidentificationProfiles.Count, Is.GreaterThanOrEqualTo(500),
            "Should have at least 500 de-identification action definitions");

        // Should be less than 1000 (sanity check)
        Assert.That(DeidentificationProfiles.Count, Is.LessThanOrEqualTo(1000),
            "Should have at most 1000 de-identification action definitions");
    }

    [Test]
    public void GetAction_AccessionNumber_ReturnsZeroOrDummy()
    {
        // Accession Number (0008,0050) has Basic Profile action Z
        var tag = new DicomTag(0x0008, 0x0050);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummy),
            "Accession Number should have Z (ZeroOrDummy) action");
    }

    [Test]
    public void GetAction_PatientName_ReturnsZeroOrDummy()
    {
        // Patient's Name (0010,0010) has Basic Profile action Z
        var tag = new DicomTag(0x0010, 0x0010);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummy),
            "Patient's Name should have Z (ZeroOrDummy) action");
    }

    [Test]
    public void GetAction_SOPInstanceUID_ReturnsRemapUid()
    {
        // SOP Instance UID (0008,0018) has Basic Profile action U
        var tag = new DicomTag(0x0008, 0x0018);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.RemapUid),
            "SOP Instance UID should have U (RemapUid) action");
    }

    [Test]
    public void GetAction_StudyInstanceUID_ReturnsRemapUid()
    {
        // Study Instance UID (0020,000D) has Basic Profile action U
        var tag = new DicomTag(0x0020, 0x000D);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.RemapUid),
            "Study Instance UID should have U (RemapUid) action");
    }

    [Test]
    public void GetAction_SeriesInstanceUID_ReturnsRemapUid()
    {
        // Series Instance UID (0020,000E) has Basic Profile action U
        var tag = new DicomTag(0x0020, 0x000E);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.RemapUid),
            "Series Instance UID should have U (RemapUid) action");
    }

    [Test]
    public void GetAction_StudyDescription_ReturnsRemove()
    {
        // Study Description (0008,1030) has Basic Profile action X
        var tag = new DicomTag(0x0008, 0x1030);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove),
            "Study Description should have X (Remove) action");
    }

    [Test]
    public void GetAction_UnknownTag_ReturnsRemove()
    {
        // Unknown tags should default to Remove (X)
        var tag = new DicomTag(0x9999, 0x9999);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove),
            "Unknown tags should default to Remove action");
    }

    [Test]
    public void GetAction_WithRetainUIDs_ReturnsKeep()
    {
        // SOP Instance UID (0008,0018) with RetainUIDs option should return K
        var tag = new DicomTag(0x0008, 0x0018);
        var action = DeidentificationProfiles.GetAction(tag, DeidentificationProfileOption.RetainUIDs);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Keep),
            "SOP Instance UID with RetainUIDs option should return Keep");
    }

    [Test]
    public void GetAction_WithRetainInstitutionIdentity_ReturnsKeep()
    {
        // Institution Name (0008,0080) with RetainInstitutionIdentity option should return K
        var tag = new DicomTag(0x0008, 0x0080);
        var action = DeidentificationProfiles.GetAction(tag, DeidentificationProfileOption.RetainInstitutionIdentity);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Keep),
            "Institution Name with RetainInstitutionIdentity option should return Keep");
    }

    [Test]
    public void GetAction_WithCleanDescriptors_ReturnsClean()
    {
        // Study Description (0008,1030) with CleanDescriptors option should return C
        var tag = new DicomTag(0x0008, 0x1030);
        var action = DeidentificationProfiles.GetAction(tag, DeidentificationProfileOption.CleanDescriptors);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Clean),
            "Study Description with CleanDescriptors option should return Clean");
    }

    [Test]
    public void GetAction_WithRetainLongitudinalFullDates_ReturnsKeep()
    {
        // Study Date (0008,0020) with RetainLongitudinalFullDates option should return K
        var tag = new DicomTag(0x0008, 0x0020);
        var action = DeidentificationProfiles.GetAction(tag, DeidentificationProfileOption.RetainLongitudinalFullDates);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Keep),
            "Study Date with RetainLongitudinalFullDates option should return Keep");
    }

    [Test]
    public void GetAction_WithRetainLongitudinalModifiedDates_ReturnsClean()
    {
        // Study Date (0008,0020) with RetainLongitudinalModifiedDates option should return C
        var tag = new DicomTag(0x0008, 0x0020);
        var action = DeidentificationProfiles.GetAction(tag, DeidentificationProfileOption.RetainLongitudinalModifiedDates);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Clean),
            "Study Date with RetainLongitudinalModifiedDates option should return Clean");
    }

    [Test]
    public void GetAction_StudyDate_ReturnsZeroOrDummy()
    {
        // Study Date (0008,0020) has Basic Profile action Z
        var tag = new DicomTag(0x0008, 0x0020);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummy),
            "Study Date should have Z (ZeroOrDummy) action");
    }

    [Test]
    public void GetAction_AcquisitionDateTime_ReturnsRemoveOrZeroOrDummyConditional()
    {
        // Acquisition DateTime (0008,002A) has Basic Profile action X/Z/D
        var tag = new DicomTag(0x0008, 0x002A);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.RemoveOrZeroOrDummyConditional),
            "Acquisition DateTime should have X/Z/D (RemoveOrZeroOrDummyConditional) action");
    }

    [Test]
    public void GetAction_SeriesDate_ReturnsRemoveOrDummyConditional()
    {
        // Series Date (0008,0021) has Basic Profile action X/D
        var tag = new DicomTag(0x0008, 0x0021);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.RemoveOrDummyConditional),
            "Series Date should have X/D (RemoveOrDummyConditional) action");
    }

    [Test]
    public void GetAction_ContentDate_ReturnsZeroOrDummyConditional()
    {
        // Content Date (0008,0023) has Basic Profile action Z/D
        var tag = new DicomTag(0x0008, 0x0023);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummyConditional),
            "Content Date should have Z/D (ZeroOrDummyConditional) action");
    }

    [Test]
    public void GetAction_ReferringPhysicianName_ReturnsZeroOrDummy()
    {
        // Referring Physician's Name (0008,0090) has Basic Profile action Z
        var tag = new DicomTag(0x0008, 0x0090);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummy),
            "Referring Physician's Name should have Z (ZeroOrDummy) action");
    }

    [Test]
    public void GetAction_PatientID_ReturnsZeroOrDummyConditional()
    {
        // Patient ID (0010,0020) has Basic Profile action Z/D
        var tag = new DicomTag(0x0010, 0x0020);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummyConditional),
            "Patient ID should have Z/D (ZeroOrDummyConditional) action");
    }

    [Test]
    public void GetAction_PatientBirthDate_ReturnsZeroOrDummy()
    {
        // Patient's Birth Date (0010,0030) has Basic Profile action Z
        var tag = new DicomTag(0x0010, 0x0030);
        var action = DeidentificationProfiles.GetAction(tag);

        Assert.That(action, Is.EqualTo(DeidentificationAction.ZeroOrDummy),
            "Patient's Birth Date should have Z (ZeroOrDummy) action");
    }

    [Test]
    public void DeidentificationAction_HasAllExpectedValues()
    {
        // Verify all action codes are present
        var actions = System.Enum.GetValues<DeidentificationAction>();

        Assert.That(actions, Has.Length.EqualTo(11),
            "Should have exactly 11 action codes (D, Z, X, K, C, U, Z/D, X/Z, X/D, X/Z/D, X/Z/U)");

        // Verify specific enum values exist
        Assert.That(System.Enum.IsDefined(DeidentificationAction.Keep));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.Remove));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.ZeroOrDummy));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.Dummy));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.Clean));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.RemapUid));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.ZeroOrDummyConditional));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.RemoveOrZeroConditional));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.RemoveOrDummyConditional));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.RemoveOrZeroOrDummyConditional));
        Assert.That(System.Enum.IsDefined(DeidentificationAction.RemoveOrZeroOrUidConditional));
    }

    [Test]
    public void DeidentificationProfileOption_HasAllExpectedValues()
    {
        // Verify all profile options are present (10 options + None)
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.None));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainSafePrivate));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainUIDs));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainDeviceIdentity));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainInstitutionIdentity));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainPatientCharacteristics));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainLongitudinalFullDates));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.RetainLongitudinalModifiedDates));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.CleanDescriptors));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.CleanStructuredContent));
        Assert.That(System.Enum.IsDefined(DeidentificationProfileOption.CleanGraphics));
    }

    [Test]
    public void DeidentificationProfileOption_IsFlagsEnum()
    {
        // Verify options can be combined
        var combined = DeidentificationProfileOption.RetainUIDs | DeidentificationProfileOption.CleanDescriptors;

        Assert.That((combined & DeidentificationProfileOption.RetainUIDs) != 0);
        Assert.That((combined & DeidentificationProfileOption.CleanDescriptors) != 0);
        Assert.That((combined & DeidentificationProfileOption.RetainSafePrivate) == 0);
    }

    [Test]
    public void TryGetEntry_KnownTag_ReturnsTrue()
    {
        // Patient's Name (0010,0010) should be found
        var tag = new DicomTag(0x0010, 0x0010);
        var found = DeidentificationProfiles.TryGetEntry(tag, out var entry);

        Assert.That(found, Is.True, "Patient's Name should be found in action table");
        Assert.That(entry.BasicProfile, Is.EqualTo(DeidentificationAction.ZeroOrDummy));
    }

    [Test]
    public void TryGetEntry_UnknownTag_ReturnsFalse()
    {
        // Unknown tag should not be found
        var tag = new DicomTag(0x9999, 0x9999);
        var found = DeidentificationProfiles.TryGetEntry(tag, out _);

        Assert.That(found, Is.False, "Unknown tag should not be found in action table");
    }
}
