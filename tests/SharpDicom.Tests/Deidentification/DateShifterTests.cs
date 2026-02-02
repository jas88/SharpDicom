using System;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for DateShifter VR-aware date/time manipulation.
/// </summary>
[TestFixture]
public class DateShifterTests
{
    // Define well-known tags that may not be in DicomTag.WellKnown
    private static readonly DicomTag StudyTimeTag = new(0x0008, 0x0030);
    private static readonly DicomTag AcquisitionDateTimeTag = new(0x0008, 0x002A);
    [Test]
    public void Shift_DateElement_AppliesOffset()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.InstanceOf<DicomStringElement>());
        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240214")); // Jan 15 + 30 = Feb 14
    }

    [Test]
    public void Shift_DateElement_NegativeOffset()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115");
        var offset = TimeSpan.FromDays(-30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20231216")); // Jan 15 - 30 = Dec 16 prev year
    }

    [Test]
    public void Shift_DateElement_YearBoundary()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20231231");
        var offset = TimeSpan.FromDays(1);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240101")); // Dec 31 + 1 = Jan 1 next year
    }

    [Test]
    public void Shift_DateElement_LeapYear()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240228");
        var offset = TimeSpan.FromDays(1);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240229")); // 2024 is leap year
    }

    [Test]
    public void Shift_TimeElement_ZeroTime_ReturnsZeros()
    {
        var element = CreateStringElement(StudyTimeTag, DicomVR.TM, "143022");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: true);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("000000"));
    }

    [Test]
    public void Shift_TimeElement_NoZeroTime_ReturnsOriginal()
    {
        var element = CreateStringElement(StudyTimeTag, DicomVR.TM, "143022");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.SameAs(element)); // No change when not zeroing time
    }

    [Test]
    public void Shift_DateTimeElement_ZeroTime_KeepsDateZerosTime()
    {
        var element = CreateStringElement(AcquisitionDateTimeTag, DicomVR.DT, "20240115143022");
        var offset = TimeSpan.FromDays(10);

        var shifted = DateShifter.Shift(element, offset, zeroTime: true);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240125000000")); // Date shifted, time zeroed
    }

    [Test]
    public void Shift_DateTimeElement_NoZeroTime_PreservesTime()
    {
        var element = CreateStringElement(AcquisitionDateTimeTag, DicomVR.DT, "20240115143022");
        var offset = TimeSpan.FromDays(10);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240125143022")); // Date shifted, time preserved
    }

    [Test]
    public void Shift_InvalidDate_ReturnsOriginal()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "invalid");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.SameAs(element));
    }

    [Test]
    public void Shift_ShortDate_ReturnsOriginal()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "2024");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.SameAs(element));
    }

    [Test]
    public void Shift_EmptyDate_ReturnsOriginal()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.SameAs(element));
    }

    [Test]
    public void Shift_NonDateVR_ReturnsOriginal()
    {
        var element = CreateStringElement(DicomTag.PatientID, DicomVR.LO, "12345678");
        var offset = TimeSpan.FromDays(30);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        Assert.That(shifted, Is.SameAs(element));
    }

#if NET6_0_OR_GREATER
    [Test]
    public void CalculateAge_ValidDates_ReturnsCorrectAge()
    {
        var birthDate = new DateOnly(1980, 6, 15);
        var studyDate = new DateOnly(2024, 1, 15);

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.EqualTo("043Y"));
    }

    [Test]
    public void CalculateAge_BeforeBirthday_ReturnsOneLessYear()
    {
        var birthDate = new DateOnly(1980, 6, 15);
        var studyDate = new DateOnly(2024, 6, 14); // Day before birthday

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.EqualTo("043Y")); // Still 43 until birthday
    }

    [Test]
    public void CalculateAge_OnBirthday_ReturnsCorrectAge()
    {
        var birthDate = new DateOnly(1980, 6, 15);
        var studyDate = new DateOnly(2024, 6, 15); // On birthday

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.EqualTo("044Y")); // 44 on birthday
    }

    [Test]
    public void CalculateAge_NegativeAge_ReturnsZero()
    {
        var birthDate = new DateOnly(2025, 1, 1);
        var studyDate = new DateOnly(2024, 1, 1);

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.EqualTo("000Y"));
    }

    [Test]
    public void CalculateAge_NullBirthDate_ReturnsNull()
    {
        DateOnly? birthDate = null;
        var studyDate = new DateOnly(2024, 1, 1);

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.Null);
    }

    [Test]
    public void CalculateAge_NullStudyDate_ReturnsNull()
    {
        var birthDate = new DateOnly(1980, 1, 1);
        DateOnly? studyDate = null;

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.Null);
    }

    [Test]
    public void CalculateAge_Over999Years_ReturnsCapped()
    {
        var birthDate = new DateOnly(0001, 1, 1);
        var studyDate = new DateOnly(2024, 1, 1);

        var age = DateShifter.CalculateAge(birthDate, studyDate);

        Assert.That(age, Is.EqualTo("999Y")); // Capped at 999
    }

    [Test]
    public void ParseDate_ValidDate_ReturnsParsed()
    {
        var date = DateShifter.ParseDate("20240115");

        Assert.That(date, Is.EqualTo(new DateOnly(2024, 1, 15)));
    }

    [Test]
    public void ParseDate_InvalidDate_ReturnsNull()
    {
        var date = DateShifter.ParseDate("invalid");

        Assert.That(date, Is.Null);
    }

    [Test]
    public void ParseDate_ShortDate_ReturnsNull()
    {
        var date = DateShifter.ParseDate("2024");

        Assert.That(date, Is.Null);
    }

    [Test]
    public void ParseDate_EmptyDate_ReturnsNull()
    {
        var date = DateShifter.ParseDate("");

        Assert.That(date, Is.Null);
    }

    [Test]
    public void ParseDate_NullDate_ReturnsNull()
    {
        var date = DateShifter.ParseDate(null);

        Assert.That(date, Is.Null);
    }

    [Test]
    public void ParseDate_InvalidMonth_ReturnsNull()
    {
        var date = DateShifter.ParseDate("20241315"); // Month 13

        Assert.That(date, Is.Null);
    }

    [Test]
    public void ParseDate_InvalidDay_ReturnsNull()
    {
        var date = DateShifter.ParseDate("20240132"); // Day 32

        Assert.That(date, Is.Null);
    }
#endif

    [Test]
    public void Shift_DateTimeWithFractionalSeconds_PreservesFormat()
    {
        var element = CreateStringElement(AcquisitionDateTimeTag, DicomVR.DT, "20240115143022.123456");
        var offset = TimeSpan.FromDays(10);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        // Should shift date and preserve the fractional seconds portion
        Assert.That(se.GetString(), Does.StartWith("20240125143022"));
    }

    [Test]
    public void Shift_DateTimeWithTimezone_PreservesFormat()
    {
        var element = CreateStringElement(AcquisitionDateTimeTag, DicomVR.DT, "20240115143022+0100");
        var offset = TimeSpan.FromDays(10);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        // Should shift date and preserve the timezone portion
        Assert.That(se.GetString(), Does.StartWith("20240125143022"));
    }

    [Test]
    public void Shift_LargeOffset_HandlesCorrectly()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115");
        var offset = TimeSpan.FromDays(1000);

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20261011")); // Jan 15 2024 + 1000 days
    }

    [Test]
    public void Shift_ZeroOffset_ReturnsSameDate()
    {
        var element = CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115");
        var offset = TimeSpan.Zero;

        var shifted = DateShifter.Shift(element, offset, zeroTime: false);

        var se = (DicomStringElement)shifted;
        Assert.That(se.GetString(), Is.EqualTo("20240115"));
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}
