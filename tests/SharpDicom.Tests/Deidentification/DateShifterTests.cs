using NUnit.Framework;
using SharpDicom.Deidentification;
using System;

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
}
