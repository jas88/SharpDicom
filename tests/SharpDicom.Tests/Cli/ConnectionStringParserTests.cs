using NUnit.Framework;
using SharpDicom.Cli.Helpers;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class ConnectionStringParserTests
{
    [Test]
    public void TryParse_ValidWithPort_ReturnsExpected()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://ORTHANC@localhost:11112",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.True);
        Assert.That(host, Is.EqualTo("localhost"));
        Assert.That(port, Is.EqualTo(11112));
        Assert.That(calledAe, Is.EqualTo("ORTHANC"));
    }

    [Test]
    public void TryParse_ValidWithoutPort_DefaultsTo104()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://MY_AET@dicom.example.com",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.True);
        Assert.That(host, Is.EqualTo("dicom.example.com"));
        Assert.That(port, Is.EqualTo(104));
        Assert.That(calledAe, Is.EqualTo("MY_AET"));
    }

    [Test]
    public void TryParse_ValidWithIpAddress_Succeeds()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://AET@192.168.1.100:4242",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.True);
        Assert.That(host, Is.EqualTo("192.168.1.100"));
        Assert.That(port, Is.EqualTo(4242));
        Assert.That(calledAe, Is.EqualTo("AET"));
    }

    [Test]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "", out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_NullString_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            null!, out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_NoScheme_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "ORTHANC@localhost:11112",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_MissingAE_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://localhost:11112",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_PortOutOfRange_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://AET@host:99999",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_PortZero_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://AET@host:0",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_MalformedNoHost_ReturnsFalse()
    {
        var result = ConnectionStringParser.TryParse(
            "pacs://AET@",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryParse_CaseInsensitiveScheme_Succeeds()
    {
        var result = ConnectionStringParser.TryParse(
            "PACS://AET@server:104",
            out var host, out var port, out var calledAe);

        Assert.That(result, Is.True);
        Assert.That(host, Is.EqualTo("server"));
    }
}
