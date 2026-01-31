using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;
using System;
using System.Text;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class DateShifterTests
{
    [Test]
    public void ShiftDate_FixedOffset_ShiftsCorrectly()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDate("20240115", null);

        // 2024-01-15 - 100 days = 2023-10-07
        Assert.That(shifted, Is.EqualTo("20231007"));
    }

    [Test]
    public void ShiftDate_NoneStrategy_ReturnsOriginal()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.None
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDate("20240115", null);

        Assert.That(shifted, Is.EqualTo("20240115"));
    }

    [Test]
    public void ShiftDate_EmptyString_ReturnsEmpty()
    {
        var shifter = new DateShifter(DateShiftConfig.Default);

        var shifted = shifter.ShiftDate("", null);

        Assert.That(shifted, Is.Empty);
    }

    [Test]
    public void ShiftDate_InvalidFormat_ReturnsOriginal()
    {
        var shifter = new DateShifter(DateShiftConfig.Default);

        var shifted = shifter.ShiftDate("invalid", null);

        Assert.That(shifted, Is.EqualTo("invalid"));
    }

    [Test]
    public void ShiftDate_ShortString_ReturnsOriginal()
    {
        var shifter = new DateShifter(DateShiftConfig.Default);

        var shifted = shifter.ShiftDate("2024", null);

        Assert.That(shifted, Is.EqualTo("2024"));
    }

    [Test]
    public void ShiftDateTime_FixedOffset_ShiftsDatePart()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDateTime("20240115120000.000000", null);

        Assert.That(shifted, Does.StartWith("20231007"));
        Assert.That(shifted, Does.EndWith("120000.000000"));
    }

    [Test]
    public void ShiftTime_ReturnsUnchanged()
    {
        var shifter = new DateShifter(DateShiftConfig.Default);

        var shifted = shifter.ShiftTime("120000.000000", null);

        Assert.That(shifted, Is.EqualTo("120000.000000"));
    }

    [Test]
    public void GetOffset_SamePatient_ReturnsSameOffset()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            RandomSeed = 12345,
            MinOffsetDays = -365,
            MaxOffsetDays = -30
        };
        var shifter = new DateShifter(config);

        var offset1 = shifter.GetOffset("PATIENT001");
        var offset2 = shifter.GetOffset("PATIENT001");

        Assert.That(offset2, Is.EqualTo(offset1));
    }

    [Test]
    public void GetOffset_DifferentPatients_ReturnsDifferentOffsets()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            RandomSeed = 12345,
            MinOffsetDays = -365,
            MaxOffsetDays = -30
        };
        var shifter = new DateShifter(config);

        var offset1 = shifter.GetOffset("PATIENT001");
        var offset2 = shifter.GetOffset("PATIENT002");

        // With random offsets, different patients should get different offsets
        // (extremely unlikely to be the same with large range)
        Assert.That(offset2, Is.Not.EqualTo(offset1));
    }

    [Test]
    public void GetOffset_RandomStrategy_WithinRange()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            RandomSeed = 12345,
            MinOffsetDays = -365,
            MaxOffsetDays = -30
        };
        var shifter = new DateShifter(config);

        for (int i = 0; i < 10; i++)
        {
            var offset = shifter.GetOffset($"PATIENT{i:D3}");
            Assert.That(offset.TotalDays, Is.GreaterThanOrEqualTo(-365));
            Assert.That(offset.TotalDays, Is.LessThanOrEqualTo(-30));
        }
    }

    [Test]
    public void ShiftDate_PreservesRelativeOrder()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var date1 = shifter.ShiftDate("20240101", "P1");
        var date2 = shifter.ShiftDate("20240201", "P1");
        var date3 = shifter.ShiftDate("20240301", "P1");

        Assert.That(string.Compare(date1, date2, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(date2, date3, StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void DateShiftConfig_Default_HasNegativeOffset()
    {
        var config = DateShiftConfig.Default;

        Assert.That(config.Strategy, Is.EqualTo(DateShiftStrategy.Fixed));
        Assert.That(config.FixedOffset.TotalDays, Is.LessThan(0));
    }

    [Test]
    public void DateShiftConfig_None_HasZeroOffset()
    {
        var shifter = new DateShifter(DateShiftConfig.None);

        var offset = shifter.GetOffset("TEST");

        Assert.That(offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ShiftDate_RemoveStrategy_ReplacesWith19000101()
    {
        var config = new DateShiftConfig { Strategy = DateShiftStrategy.Remove };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDate("19800520", null);

        Assert.That(shifted, Is.EqualTo("19000101"));
    }

    [Test]
    public void ShiftDateTime_RemoveStrategy_ReturnsFixedDummy()
    {
        var config = new DateShiftConfig { Strategy = DateShiftStrategy.Remove };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDateTime("20240115120000.000000+0500", null);

        Assert.That(shifted, Is.EqualTo("19000101000000.000000"));
    }

    [Test]
    public void ShiftTime_RemoveStrategy_ReturnsDummy()
    {
        var config = new DateShiftConfig { Strategy = DateShiftStrategy.Remove };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftTime("120000.000000", null);

        Assert.That(shifted, Is.EqualTo("000000.000000"));
    }

    [Test]
    public void ShiftDateTime_PreservesTimezone()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-10)
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDateTime("20240115120000.000000+0500", null);

        Assert.That(shifted, Does.EndWith("+0500"));
    }

    [Test]
    public void ShiftDateTime_PreservesNegativeTimezone()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-10)
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDateTime("20240115120000.000000-0800", null);

        Assert.That(shifted, Does.EndWith("-0800"));
    }

    [Test]
    public void ShiftDateTime_RemoveTimeStrategy_RemovesTimeKeepsDate()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RemoveTime,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var shifted = shifter.ShiftDateTime("20240115120000.000000+0500", null);

        // Date should be shifted by 100 days, time removed (zeroed)
        Assert.That(shifted, Does.StartWith("20231007"));
        Assert.That(shifted, Does.Contain("000000.000000"));
        Assert.That(shifted, Does.EndWith("+0500")); // Timezone preserved
    }

    // Well-known DICOM tags for testing
    private static readonly DicomTag SeriesDate = new(0x0008, 0x0021);
    private static readonly DicomTag PatientBirthDate = new(0x0010, 0x0030);
    private static readonly DicomTag ReferencedStudySequence = new(0x0008, 0x1110);

    [Test]
    public void ShiftDates_Dataset_ShiftsAllDateElements()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240115")));
        dataset.Add(new DicomStringElement(SeriesDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240115")));
        dataset.Add(new DicomStringElement(PatientBirthDate, DicomVR.DA, Encoding.ASCII.GetBytes("19800520")));

        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var result = shifter.ShiftDatesWithResult(dataset);

        Assert.That(result.DatesShifted, Is.EqualTo(3));
        Assert.That(dataset.GetString(DicomTag.StudyDate), Is.EqualTo("20231007"));
    }

    [Test]
    public void ShiftDates_Dataset_HandlesSequences()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240115")));

        var item = new DicomDataset();
        item.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20230101")));
        var seq = new DicomSequence(ReferencedStudySequence, item);
        dataset.Add(seq);

        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var result = shifter.ShiftDatesWithResult(dataset);

        Assert.That(result.DatesShifted, Is.EqualTo(2));
    }

    [Test]
    public void ShiftDates_PreservesTemporalOrder_InDataset()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO, Encoding.ASCII.GetBytes("PATIENT001")));
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240101")));
        dataset.Add(new DicomStringElement(SeriesDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240102")));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0022), DicomVR.DA, Encoding.ASCII.GetBytes("20240103"))); // AcquisitionDate

        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-50)
        };
        var shifter = new DateShifter(config);
        shifter.ShiftDates(dataset);

        var studyDate = dataset.GetString(DicomTag.StudyDate);
        var seriesDate = dataset.GetString(SeriesDate);
        var acqDate = dataset.GetString(new DicomTag(0x0008, 0x0022));

        // Temporal order should be preserved
        Assert.That(string.Compare(studyDate!, seriesDate!, StringComparison.Ordinal), Is.LessThan(0));
        Assert.That(string.Compare(seriesDate!, acqDate!, StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public void ShiftDatesWithResult_ReturnsAppliedOffset()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240115")));

        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);

        var result = shifter.ShiftDatesWithResult(dataset);

        Assert.That(result.AppliedOffset, Is.EqualTo(TimeSpan.FromDays(-100)));
    }

    [Test]
    public void ShiftDates_RandomPerPatient_SamePatientSameOffset()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            RandomSeed = 12345,
            MinOffsetDays = -365,
            MaxOffsetDays = 365
        };
        var offsetStore = new InMemoryDateOffsetStore(12345);
        var shifter = new DateShifter(config, offsetStore);

        var dataset1 = new DicomDataset();
        dataset1.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO, Encoding.ASCII.GetBytes("PATIENT001")));
        dataset1.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240115")));

        var dataset2 = new DicomDataset();
        dataset2.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO, Encoding.ASCII.GetBytes("PATIENT001")));
        dataset2.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA, Encoding.ASCII.GetBytes("20240215")));

        var result1 = shifter.ShiftDatesWithResult(dataset1);
        var result2 = shifter.ShiftDatesWithResult(dataset2);

        // Same patient should get same offset
        Assert.That(result1.AppliedOffset, Is.EqualTo(result2.AppliedOffset));
    }

    [Test]
    public void DateShiftConfig_Research_HasRandomStrategy()
    {
        var config = DateShiftConfig.Research;

        Assert.That(config.Strategy, Is.EqualTo(DateShiftStrategy.RandomPerPatient));
        Assert.That(config.MinOffsetDays, Is.EqualTo(-365));
        Assert.That(config.MaxOffsetDays, Is.EqualTo(365));
    }

    [Test]
    public void DateShiftConfig_ClinicalTrial_HasRemoveTimeStrategy()
    {
        var config = DateShiftConfig.ClinicalTrial;

        Assert.That(config.Strategy, Is.EqualTo(DateShiftStrategy.RemoveTime));
    }

    [Test]
    public void InMemoryDateOffsetStore_GetOrCreateOffset_ReturnsSameForSamePatient()
    {
        var store = new InMemoryDateOffsetStore(42);

        var offset1 = store.GetOrCreateOffset("PATIENT001", TimeSpan.FromDays(-365), TimeSpan.FromDays(365), null);
        var offset2 = store.GetOrCreateOffset("PATIENT001", TimeSpan.FromDays(-365), TimeSpan.FromDays(365), null);

        Assert.That(offset2, Is.EqualTo(offset1));
    }

    [Test]
    public void InMemoryDateOffsetStore_TryGetOffset_ReturnsFalseForUnknown()
    {
        var store = new InMemoryDateOffsetStore();

        var found = store.TryGetOffset("UNKNOWN", out var offset);

        Assert.That(found, Is.False);
        Assert.That(offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void InMemoryDateOffsetStore_TryGetOffset_ReturnsTrueAfterCreate()
    {
        var store = new InMemoryDateOffsetStore(42);
        var created = store.GetOrCreateOffset("PATIENT001", TimeSpan.FromDays(-100), TimeSpan.FromDays(100), null);

        var found = store.TryGetOffset("PATIENT001", out var offset);

        Assert.That(found, Is.True);
        Assert.That(offset, Is.EqualTo(created));
    }

    [Test]
    public void ShiftDates_Dataset_ShiftsDateTimeElements()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x002A), DicomVR.DT, Encoding.ASCII.GetBytes("20240115120000.000000"))); // AcquisitionDateTime

        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-10)
        };
        var shifter = new DateShifter(config);

        var result = shifter.ShiftDatesWithResult(dataset);

        Assert.That(result.DateTimesShifted, Is.EqualTo(1));
        Assert.That(dataset.GetString(new DicomTag(0x0008, 0x002A)), Does.StartWith("20240105"));
    }
}
