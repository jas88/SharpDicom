using System;
using System.IO;
using NUnit.Framework;
using SharpDicom.Cli.Configuration;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class ConfigLoaderTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpdcm_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        var nonExistent = Path.Combine(_tempDir!, "nonexistent.toml");
        var config = ConfigLoader.Load(nonExistent);

        Assert.That(config.OutputFormat, Is.EqualTo("text"));
        Assert.That(config.Verbosity, Is.EqualTo("normal"));
        Assert.That(config.Color, Is.True);
        Assert.That(config.ContinueOnError, Is.False);
        Assert.That(config.DefaultProfile, Is.Null);
        Assert.That(config.Profiles, Is.Empty);
    }

    [Test]
    public void Load_ValidToml_ParsesCorrectly()
    {
        var configPath = Path.Combine(_tempDir!, "config.toml");
        File.WriteAllText(configPath, """
            output_format = "json"
            verbosity = "verbose"
            color = false
            continue_on_error = true
            default_profile = "orthanc"

            [profiles.orthanc]
            host = "localhost"
            port = 4242
            called_ae = "ORTHANC"
            calling_ae = "MYSCU"
            use_tls = true
            """);

        var config = ConfigLoader.Load(configPath);

        Assert.That(config.OutputFormat, Is.EqualTo("json"));
        Assert.That(config.Verbosity, Is.EqualTo("verbose"));
        Assert.That(config.Color, Is.False);
        Assert.That(config.ContinueOnError, Is.True);
        Assert.That(config.DefaultProfile, Is.EqualTo("orthanc"));
        Assert.That(config.Profiles, Has.Count.EqualTo(1));
        Assert.That(config.Profiles.ContainsKey("orthanc"), Is.True);

        var profile = config.Profiles["orthanc"];
        Assert.That(profile.Host, Is.EqualTo("localhost"));
        Assert.That(profile.Port, Is.EqualTo(4242));
        Assert.That(profile.CalledAE, Is.EqualTo("ORTHANC"));
        Assert.That(profile.CallingAE, Is.EqualTo("MYSCU"));
        Assert.That(profile.UseTls, Is.True);
    }

    [Test]
    public void Load_MalformedToml_ReturnsDefaults()
    {
        var configPath = Path.Combine(_tempDir!, "bad.toml");
        File.WriteAllText(configPath, "this is not valid TOML {{{}}}");

        var config = ConfigLoader.Load(configPath);

        // Should not throw, just return defaults
        Assert.That(config.OutputFormat, Is.EqualTo("text"));
        Assert.That(config.Verbosity, Is.EqualTo("normal"));
    }

    [Test]
    public void ApplyEnvironmentVariables_OverridesOutputFormat()
    {
        var config = new CliConfig { OutputFormat = "text" };

        try
        {
            Environment.SetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT", "json");
            config = ConfigLoader.ApplyEnvironmentVariables(config);
            Assert.That(config.OutputFormat, Is.EqualTo("json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT", null);
        }
    }

    [Test]
    public void ApplyEnvironmentVariables_OverridesVerbosity()
    {
        var config = new CliConfig { Verbosity = "normal" };

        try
        {
            Environment.SetEnvironmentVariable("SHARPDCM_VERBOSITY", "debug");
            config = ConfigLoader.ApplyEnvironmentVariables(config);
            Assert.That(config.Verbosity, Is.EqualTo("debug"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPDCM_VERBOSITY", null);
        }
    }

    [Test]
    public void ApplyEnvironmentVariables_OverridesColor()
    {
        var config = new CliConfig { Color = true };

        try
        {
            Environment.SetEnvironmentVariable("SHARPDCM_COLOR", "false");
            config = ConfigLoader.ApplyEnvironmentVariables(config);
            Assert.That(config.Color, Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPDCM_COLOR", null);
        }
    }

    [Test]
    public void ApplyEnvironmentVariables_EmptyVar_NoOverride()
    {
        var config = new CliConfig { OutputFormat = "text" };

        try
        {
            Environment.SetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT", "");
            config = ConfigLoader.ApplyEnvironmentVariables(config);
            Assert.That(config.OutputFormat, Is.EqualTo("text"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT", null);
        }
    }

    [Test]
    public void Load_MultipleProfiles_ParsedCorrectly()
    {
        var configPath = Path.Combine(_tempDir!, "config.toml");
        File.WriteAllText(configPath, """
            [profiles.orthanc]
            host = "localhost"
            port = 4242
            called_ae = "ORTHANC"

            [profiles.dcm4chee]
            host = "remote.example.com"
            port = 11112
            called_ae = "DCM4CHEE"
            """);

        var config = ConfigLoader.Load(configPath);

        Assert.That(config.Profiles, Has.Count.EqualTo(2));
        Assert.That(config.Profiles["orthanc"].Host, Is.EqualTo("localhost"));
        Assert.That(config.Profiles["dcm4chee"].Host, Is.EqualTo("remote.example.com"));
    }
}
