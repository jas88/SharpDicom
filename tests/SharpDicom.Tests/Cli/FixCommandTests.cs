using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Cli.Diagnostics;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class FixCommandTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpdcm_fix_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    #region DicomFixer.Fix - UID Fixes

    [Test]
    public void Fix_InvalidUid_ReplacesWithValidUid()
    {
        var dataset = new DicomDataset();
        // Leading zero in component makes this invalid
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.04.5"));

        var options = new FixOptions { FixInvalidUids = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Has.Count.GreaterThanOrEqualTo(1));
        var uidAction = actions.First(a => a.Tag == DicomTag.StudyInstanceUID);
        Assert.That(uidAction.Description, Does.Contain("Invalid UID"));
        Assert.That(uidAction.OldValue, Is.EqualTo("1.2.3.04.5"));
        Assert.That(uidAction.NewValue, Is.Not.Null);
        // Verify new UID is valid
        Assert.That(UidGenerator.IsValidUid(uidAction.NewValue!), Is.True);
    }

    [Test]
    public void Fix_ValidUid_NotModified()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));

        var options = new FixOptions { FixInvalidUids = true };
        var actions = DicomFixer.Fix(dataset, options);

        var uidActions = actions.Where(a => a.Tag == DicomTag.StudyInstanceUID).ToList();
        Assert.That(uidActions, Is.Empty);
    }

    [Test]
    public void Fix_EmptyUid_NotModified()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, ""));

        var options = new FixOptions { FixInvalidUids = true };
        var actions = DicomFixer.Fix(dataset, options);

        // Empty is valid for Type 2 elements
        var uidActions = actions.Where(a => a.Tag == DicomTag.StudyInstanceUID).ToList();
        Assert.That(uidActions, Is.Empty);
    }

    #endregion

    #region DicomFixer.Fix - Date Fixes

    [Test]
    public void Fix_InvalidDate_WithDots_Reformatted()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "2024.01.15"));

        var options = new FixOptions { FixInvalidDates = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Tag, Is.EqualTo(DicomTag.StudyDate));
        Assert.That(actions[0].NewValue, Is.EqualTo("20240115"));
        Assert.That(actions[0].OldValue, Is.EqualTo("2024.01.15"));
    }

    [Test]
    public void Fix_InvalidDate_WithDashes_Reformatted()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "2024-01-15"));

        var options = new FixOptions { FixInvalidDates = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].NewValue, Is.EqualTo("20240115"));
    }

    [Test]
    public void Fix_ValidDate_NotModified()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));

        var options = new FixOptions { FixInvalidDates = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Is.Empty);
    }

    #endregion

    #region DicomFixer.Fix - Time Fixes

    [Test]
    public void Fix_InvalidTime_WithColons_Reformatted()
    {
        var dataset = new DicomDataset();
        // StudyTime tag = (0008,0030)
        var studyTime = new DicomTag(0x0008, 0x0030);
        dataset.Add(CreateStringElement(studyTime, DicomVR.TM, "14:30:45"));

        var options = new FixOptions { FixInvalidTimes = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].NewValue, Is.EqualTo("143045"));
    }

    #endregion

    #region DicomFixer.Fix - Character Encoding

    [Test]
    public void Fix_MissingCharacterSet_WithNonAscii_AddsIsoIr100()
    {
        var dataset = new DicomDataset();
        // Non-ASCII bytes in patient name (e.g., accented character)
        var nameBytes = new byte[] { 0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72 }; // "Mueller" with umlaut
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, nameBytes));

        var options = new FixOptions { FixCharacterEncoding = true };
        var actions = DicomFixer.Fix(dataset, options);

        var encodingAction = actions.FirstOrDefault(a => a.Tag == DicomTag.SpecificCharacterSet);
        Assert.That(encodingAction.NewValue, Is.EqualTo("ISO_IR 100"));
        Assert.That(encodingAction.OldValue, Is.Null);
        Assert.That(dataset.Contains(DicomTag.SpecificCharacterSet), Is.True);
    }

    [Test]
    public void Fix_CharacterSetPresent_NotModified()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.SpecificCharacterSet, DicomVR.CS, "ISO_IR 192"));
        var nameBytes = new byte[] { 0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72 };
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, nameBytes));

        var options = new FixOptions { FixCharacterEncoding = true };
        var actions = DicomFixer.Fix(dataset, options);

        var encodingActions = actions.Where(a => a.Tag == DicomTag.SpecificCharacterSet).ToList();
        Assert.That(encodingActions, Is.Empty);
    }

    [Test]
    public void Fix_AsciiOnly_NoCharacterSetAdded()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));

        var options = new FixOptions { FixCharacterEncoding = true };
        var actions = DicomFixer.Fix(dataset, options);

        var encodingActions = actions.Where(a => a.Tag == DicomTag.SpecificCharacterSet).ToList();
        Assert.That(encodingActions, Is.Empty);
    }

    #endregion

    #region DicomFixer.Fix - Remove Invalid Elements

    [Test]
    public void Fix_RemoveInvalidElements_RemovesControlChars()
    {
        var dataset = new DicomDataset();
        // Element with invalid control characters (not ESC, CR, LF, or TAB)
        var invalidBytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x01 }; // "Hello" + SOH
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, invalidBytes));

        var options = new FixOptions { RemoveInvalidElements = true };
        var actions = DicomFixer.Fix(dataset, options);

        var removeAction = actions.FirstOrDefault(a => a.Tag == DicomTag.PatientName);
        Assert.That(removeAction.Description, Does.Contain("Removed"));
        Assert.That(dataset.Contains(DicomTag.PatientName), Is.False);
    }

    [Test]
    public void Fix_RemoveInvalidElements_KeepsValidElements()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));

        var options = new FixOptions { RemoveInvalidElements = true };
        var actions = DicomFixer.Fix(dataset, options);

        // No elements should be removed
        Assert.That(actions.Where(a => a.Description.Contains("Removed")), Is.Empty);
        Assert.That(dataset.Contains(DicomTag.PatientName), Is.True);
        Assert.That(dataset.Contains(DicomTag.StudyDate), Is.True);
    }

    [Test]
    public void Fix_RemoveDisabled_DoesNotRemove()
    {
        var dataset = new DicomDataset();
        var invalidBytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x01 };
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, invalidBytes));

        // RemoveInvalidElements defaults to false (opt-in)
        var options = new FixOptions();
        var actions = DicomFixer.Fix(dataset, options);

        var removeActions = actions.Where(a => a.Description.Contains("Removed")).ToList();
        Assert.That(removeActions, Is.Empty);
        Assert.That(dataset.Contains(DicomTag.PatientName), Is.True);
    }

    #endregion

    #region DicomFixer.Fix - Combined Options

    [Test]
    public void Fix_AllOptions_MultipleFixes()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.04.5"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "2024-01-15"));
        var nameBytes = new byte[] { 0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72 };
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN, nameBytes));

        var options = new FixOptions
        {
            FixInvalidUids = true,
            FixInvalidDates = true,
            FixCharacterEncoding = true,
        };
        var actions = DicomFixer.Fix(dataset, options);

        // Should have at least 3 fixes: UID, date, and character set
        Assert.That(actions, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(actions.Any(a => a.Tag == DicomTag.StudyInstanceUID), Is.True);
        Assert.That(actions.Any(a => a.Tag == DicomTag.StudyDate), Is.True);
        Assert.That(actions.Any(a => a.Tag == DicomTag.SpecificCharacterSet), Is.True);
    }

    [Test]
    public void Fix_DryRun_DatasetModifiedButNotSaved()
    {
        // DicomFixer.Fix modifies the dataset in-place; dry-run is about
        // not writing the file, which is handled by the command layer.
        // Here we verify that Fix returns actions that describe what was done.
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "2024.01.15"));

        var options = new FixOptions { FixInvalidDates = true };
        var actions = DicomFixer.Fix(dataset, options);

        Assert.That(actions, Has.Count.GreaterThan(0));
        Assert.That(actions[0].OldValue, Is.EqualTo("2024.01.15"));
        Assert.That(actions[0].NewValue, Is.EqualTo("20240115"));
    }

    #endregion

    #region FixAction Record

    [Test]
    public void FixAction_HasCorrectProperties()
    {
        var action = new FixAction(
            DicomTag.StudyDate,
            "Date reformatted",
            "2024.01.15",
            "20240115");

        Assert.That(action.Tag, Is.EqualTo(DicomTag.StudyDate));
        Assert.That(action.Description, Is.EqualTo("Date reformatted"));
        Assert.That(action.OldValue, Is.EqualTo("2024.01.15"));
        Assert.That(action.NewValue, Is.EqualTo("20240115"));
    }

    [Test]
    public void FixAction_NullValues_ForAddAndRemove()
    {
        var addAction = new FixAction(
            DicomTag.SpecificCharacterSet,
            "Added missing character set",
            null,
            "ISO_IR 100");

        Assert.That(addAction.OldValue, Is.Null);
        Assert.That(addAction.NewValue, Is.EqualTo("ISO_IR 100"));

        var removeAction = new FixAction(
            DicomTag.PatientName,
            "Removed invalid element",
            "invalid\x01data",
            null);

        Assert.That(removeAction.OldValue, Is.Not.Null);
        Assert.That(removeAction.NewValue, Is.Null);
    }

    #endregion
}
