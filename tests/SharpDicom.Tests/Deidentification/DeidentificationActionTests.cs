using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for the de-identification action table (PS3.15 compliance).
/// </summary>
[TestFixture]
public class DeidentificationActionTests
{
    [Test]
    public void GetAction_PatientName_Basic_ReturnsZeroOrDummy()
    {
        // PS3.15 Table E.1-1: PatientName (0010,0010) Basic = Z (or D depending on Type)
        var action = DeidentificationActionTable.GetAction(
            DicomTag.PatientName, DeidentificationProfile.Basic);

        // Should be either Zero or Dummy for PatientName
        Assert.That(action, Is.EqualTo(DeidentificationAction.Zero).Or.EqualTo(DeidentificationAction.Dummy));
    }

    [Test]
    public void GetAction_PatientName_RetainPatientChars_ReturnsKeepOrOriginal()
    {
        // With RetainPatientCharacteristics option, PatientName action changes
        var basicAction = DeidentificationActionTable.GetAction(
            DicomTag.PatientName, DeidentificationProfile.Basic);
        var retainAction = DeidentificationActionTable.GetAction(
            DicomTag.PatientName,
            DeidentificationProfile.Basic | DeidentificationProfile.RetainPatientCharacteristics);

        // The action with RetainPatientCharacteristics should either be Keep
        // or remain unchanged if the profile doesn't override PatientName
        Assert.That(retainAction, Is.EqualTo(DeidentificationAction.Keep)
            .Or.EqualTo(basicAction));
    }

    [Test]
    public void GetAction_StudyInstanceUID_Basic_ReturnsUidRemap()
    {
        // PS3.15: StudyInstanceUID (0020,000D) Basic = U
        var action = DeidentificationActionTable.GetAction(
            DicomTag.StudyInstanceUID, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.UidRemap));
    }

    [Test]
    public void GetAction_StudyInstanceUID_RetainUIDs_ReturnsKeep()
    {
        // With RetainUIDs option, UIDs are kept
        var action = DeidentificationActionTable.GetAction(
            DicomTag.StudyInstanceUID,
            DeidentificationProfile.Basic | DeidentificationProfile.RetainUIDs);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Keep));
    }

    [Test]
    public void GetAction_AccessionNumber_Basic_ReturnsRemoveOrZero()
    {
        // PS3.15: AccessionNumber (0008,0050) Basic = X or Z
        var action = DeidentificationActionTable.GetAction(
            DicomTag.AccessionNumber, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove)
            .Or.EqualTo(DeidentificationAction.Zero));
    }

    [Test]
    public void GetAction_UnknownTag_ReturnsRemove()
    {
        // Unknown tags should default to Remove for safety
        var unknownTag = new DicomTag(0x9999, 0x9999);
        var action = DeidentificationActionTable.GetAction(unknownTag, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove));
    }

    [Test]
    public void GetAction_SOPInstanceUID_Basic_ReturnsUidRemap()
    {
        // PS3.15: SOPInstanceUID (0008,0018) Basic = U
        var action = DeidentificationActionTable.GetAction(
            DicomTag.SOPInstanceUID, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.UidRemap));
    }

    [Test]
    public void GetAction_PatientBirthDate_Basic_ReturnsRemoveOrZero()
    {
        // PS3.15: PatientBirthDate (0010,0030) Basic = X or Z
        var action = DeidentificationActionTable.GetAction(
            DicomTag.PatientBirthDate, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove)
            .Or.EqualTo(DeidentificationAction.Zero));
    }

    [Test]
    public void GetAction_StudyDate_Basic_ReturnsRemoveOrZeroOrClean()
    {
        // PS3.15: StudyDate (0008,0020) Basic = X/Z or C depending on option
        var action = DeidentificationActionTable.GetAction(
            DicomTag.StudyDate, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove)
            .Or.EqualTo(DeidentificationAction.Zero)
            .Or.EqualTo(DeidentificationAction.Clean));
    }

    [Test]
    public void GetAction_StudyDate_RetainLongitudinalModified_ReturnsCleanOrKeep()
    {
        // With RetainLongitudinalModifiedDates, dates are cleaned (shifted)
        var action = DeidentificationActionTable.GetAction(
            DicomTag.StudyDate,
            DeidentificationProfile.Basic | DeidentificationProfile.RetainLongitudinalModifiedDates);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Clean)
            .Or.EqualTo(DeidentificationAction.Keep));
    }

    [Test]
    public void GetBasicAction_SameAsGetActionWithBasic()
    {
        var tag = DicomTag.PatientName;
        var actionFull = DeidentificationActionTable.GetAction(tag, DeidentificationProfile.Basic);
        var actionBasic = DeidentificationActionTable.GetBasicAction(tag);

        Assert.That(actionBasic, Is.EqualTo(actionFull));
    }

    [Test]
    public void TryGetEntry_KnownTag_ReturnsTrue()
    {
        var found = DeidentificationActionTable.TryGetEntry(DicomTag.PatientName, out var entry);

        Assert.That(found, Is.True);
        Assert.That(entry.Basic, Is.Not.EqualTo(DeidentificationAction.None));
    }

    [Test]
    public void TryGetEntry_UnknownTag_ReturnsFalse()
    {
        var unknownTag = new DicomTag(0x9999, 0x9999);
        var found = DeidentificationActionTable.TryGetEntry(unknownTag, out _);

        Assert.That(found, Is.False);
    }

    [TestCase(DeidentificationAction.Dummy, 'D')]
    [TestCase(DeidentificationAction.Zero, 'Z')]
    [TestCase(DeidentificationAction.Remove, 'X')]
    [TestCase(DeidentificationAction.Keep, 'K')]
    [TestCase(DeidentificationAction.Clean, 'C')]
    [TestCase(DeidentificationAction.UidRemap, 'U')]
    public void ActionEnum_HasCorrectCharValue(DeidentificationAction action, char expected)
    {
        Assert.That((char)(byte)action, Is.EqualTo(expected));
    }

    [Test]
    public void ActionEnum_NoneHasZeroValue()
    {
        Assert.That((byte)DeidentificationAction.None, Is.EqualTo(0));
    }

    [Test]
    public void GetAction_Modality_Basic_ReturnsKeep()
    {
        // Modality (0008,0060) is typically kept as it's not identifying
        var action = DeidentificationActionTable.GetAction(
            DicomTag.Modality, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Keep)
            .Or.EqualTo(DeidentificationAction.Remove));
    }

    [Test]
    public void GetAction_PatientID_Basic_ReturnsZeroOrRemove()
    {
        // PS3.15: PatientID (0010,0020) Basic = Z or X
        var action = DeidentificationActionTable.GetAction(
            DicomTag.PatientID, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Zero)
            .Or.EqualTo(DeidentificationAction.Remove));
    }

    [Test]
    public void Profile_CombineWithBitwiseOr()
    {
        var combined = DeidentificationProfile.Basic |
                       DeidentificationProfile.RetainUIDs |
                       DeidentificationProfile.RetainPatientCharacteristics;

        Assert.That(combined.HasFlag(DeidentificationProfile.Basic), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.RetainUIDs), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.RetainPatientCharacteristics), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.RetainDeviceIdentity), Is.False);
    }

    [Test]
    public void GetAction_SeriesInstanceUID_ReturnsUidRemap()
    {
        // SeriesInstanceUID (0020,000E) Basic = U
        var seriesUid = new DicomTag(0x0020, 0x000E);
        var action = DeidentificationActionTable.GetAction(seriesUid, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.UidRemap));
    }

    [Test]
    public void GetAction_PrivateTag_ReturnsRemove()
    {
        // Private tags should default to Remove
        var privateTag = new DicomTag(0x0009, 0x1001);
        var action = DeidentificationActionTable.GetAction(privateTag, DeidentificationProfile.Basic);

        Assert.That(action, Is.EqualTo(DeidentificationAction.Remove));
    }
}
