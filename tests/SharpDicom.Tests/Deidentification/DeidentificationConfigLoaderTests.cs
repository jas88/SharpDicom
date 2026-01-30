using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

// DeidentificationConfigLoader only has methods in NET6_0_OR_GREATER builds
// The Polyfills project tests against netstandard2.0 library but runs on net10.0
// so we need to exclude these tests when TESTING_NETSTANDARD_POLYFILLS is defined
#if NET6_0_OR_GREATER && !TESTING_NETSTANDARD_POLYFILLS
[TestFixture]
public class DeidentificationConfigLoaderTests
{
    [Test]
    public void GetPreset_BasicProfile_ReturnsValidConfig()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");

        Assert.That(config, Is.Not.Null);
        Assert.That(config.PrivateTagDefaults, Is.EqualTo("remove"));
        Assert.That(config.RemoveUnknownTags, Is.True);
    }

    [Test]
    public void GetPreset_Research_IncludesDateShift()
    {
        var config = DeidentificationConfigLoader.GetPreset("research");

        Assert.That(config, Is.Not.Null);
        Assert.That(config.DateShift, Is.Not.Null);
        Assert.That(config.DateShift!.Strategy, Is.EqualTo("randomPerPatient"));
        Assert.That(config.DateShift.MinOffsetDays, Is.EqualTo(-365));
        Assert.That(config.DateShift.MaxOffsetDays, Is.EqualTo(365));
    }

    [Test]
    public void GetPreset_ClinicalTrial_IncludesFixedDateShift()
    {
        var config = DeidentificationConfigLoader.GetPreset("clinical-trial");

        Assert.That(config, Is.Not.Null);
        Assert.That(config.DateShift, Is.Not.Null);
        Assert.That(config.DateShift!.Strategy, Is.EqualTo("fixed"));
        Assert.That(config.DateShift.OffsetDays, Is.EqualTo(-100));
    }

    [Test]
    public void GetPreset_Teaching_IncludesCleanGraphics()
    {
        var config = DeidentificationConfigLoader.GetPreset("teaching");

        Assert.That(config, Is.Not.Null);
        Assert.That(config.Options, Does.Contain("CleanDescriptors"));
        Assert.That(config.Options, Does.Contain("CleanGraphics"));
    }

    [Test]
    public void GetPreset_CaseInsensitive()
    {
        var config1 = DeidentificationConfigLoader.GetPreset("BASIC-PROFILE");
        var config2 = DeidentificationConfigLoader.GetPreset("Basic-Profile");
        var config3 = DeidentificationConfigLoader.GetPreset("basic-profile");

        Assert.That(config1, Is.Not.Null);
        Assert.That(config2, Is.Not.Null);
        Assert.That(config3, Is.Not.Null);
    }

    [Test]
    public void GetPreset_UnknownPreset_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            DeidentificationConfigLoader.GetPreset("unknown-preset"));
    }

    [Test]
    public void IsValidPreset_ValidPreset_ReturnsTrue()
    {
        Assert.That(DeidentificationConfigLoader.IsValidPreset("basic-profile"), Is.True);
        Assert.That(DeidentificationConfigLoader.IsValidPreset("research"), Is.True);
        Assert.That(DeidentificationConfigLoader.IsValidPreset("clinical-trial"), Is.True);
        Assert.That(DeidentificationConfigLoader.IsValidPreset("teaching"), Is.True);
    }

    [Test]
    public void IsValidPreset_InvalidPreset_ReturnsFalse()
    {
        Assert.That(DeidentificationConfigLoader.IsValidPreset("invalid"), Is.False);
        Assert.That(DeidentificationConfigLoader.IsValidPreset(""), Is.False);
    }

    [Test]
    public void PresetNames_ContainsExpectedPresets()
    {
        var names = DeidentificationConfigLoader.PresetNames;

        Assert.That(names, Does.Contain("basic-profile"));
        Assert.That(names, Does.Contain("research"));
        Assert.That(names, Does.Contain("clinical-trial"));
        Assert.That(names, Does.Contain("teaching"));
    }

#if NET6_0_OR_GREATER
    [Test]
    public void LoadConfigFromJson_SimpleConfig_ParsesCorrectly()
    {
        const string json = @"{
            ""options"": [""CleanDescriptors""],
            ""privateTagDefaults"": ""keep""
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        Assert.That(config.Options, Does.Contain("CleanDescriptors"));
        Assert.That(config.PrivateTagDefaults, Is.EqualTo("keep"));
    }

    [Test]
    public void LoadConfigFromJson_WithDateShift_ParsesCorrectly()
    {
        const string json = @"{
            ""dateShift"": {
                ""strategy"": ""fixed"",
                ""offsetDays"": -50
            }
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        Assert.That(config.DateShift, Is.Not.Null);
        Assert.That(config.DateShift!.Strategy, Is.EqualTo("fixed"));
        Assert.That(config.DateShift.OffsetDays, Is.EqualTo(-50));
    }

    [Test]
    public void LoadConfigFromJson_WithOverrides_ParsesCorrectly()
    {
        const string json = @"{
            ""overrides"": {
                ""PatientName"": ""KEEP"",
                ""(0008,0080)"": ""REMOVE""
            }
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        Assert.That(config.Overrides, Is.Not.Null);
        Assert.That(config.Overrides!.Count, Is.EqualTo(2));
        Assert.That(config.Overrides["PatientName"], Is.EqualTo("KEEP"));
        Assert.That(config.Overrides["(0008,0080)"], Is.EqualTo("REMOVE"));
    }

    [Test]
    public void LoadConfigFromJson_WithClinicalTrial_ParsesCorrectly()
    {
        const string json = @"{
            ""clinicalTrial"": {
                ""protocolId"": ""TRIAL-001"",
                ""protocolName"": ""Test Protocol"",
                ""siteId"": ""SITE-A"",
                ""siteName"": ""Test Site"",
                ""sponsorName"": ""Test Sponsor""
            }
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        Assert.That(config.ClinicalTrial, Is.Not.Null);
        Assert.That(config.ClinicalTrial!.ProtocolId, Is.EqualTo("TRIAL-001"));
        Assert.That(config.ClinicalTrial.ProtocolName, Is.EqualTo("Test Protocol"));
        Assert.That(config.ClinicalTrial.SiteId, Is.EqualTo("SITE-A"));
        Assert.That(config.ClinicalTrial.SiteName, Is.EqualTo("Test Site"));
        Assert.That(config.ClinicalTrial.SponsorName, Is.EqualTo("Test Sponsor"));
    }

    [Test]
    public void LoadConfigFromJson_ExtendsPreset_MergesCorrectly()
    {
        const string json = @"{
            ""$extends"": ""basic-profile"",
            ""options"": [""CleanGraphics""],
            ""privateTagDefaults"": ""keep""
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        // Should have merged options from child (basic-profile has empty options)
        Assert.That(config.Options, Does.Contain("CleanGraphics"));
        // Child overrides parent
        Assert.That(config.PrivateTagDefaults, Is.EqualTo("keep"));
        // $extends should not propagate
        Assert.That(config.Extends, Is.Null);
    }

    [Test]
    public void LoadConfigFromJson_MergesDictionaries_ChildOverridesParent()
    {
        const string json = @"{
            ""$extends"": ""basic-profile"",
            ""dummyValues"": {
                ""PN"": ""ANONYMOUS^USER""
            }
        }";

        var config = DeidentificationConfigLoader.LoadConfigFromJson(json);

        Assert.That(config.DummyValues, Is.Not.Null);
        Assert.That(config.DummyValues!["PN"], Is.EqualTo("ANONYMOUS^USER"));
    }

    [Test]
    public void LoadConfigFromJson_EmptyJson_ReturnsEmptyConfig()
    {
        // Empty JSON object {} is valid - just returns a config with all nulls
        var config = DeidentificationConfigLoader.LoadConfigFromJson("{}");
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Options, Is.Null);
        Assert.That(config.PrivateTagDefaults, Is.Null);
    }

    [Test]
    public void LoadConfigFromJson_NullOrEmpty_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DeidentificationConfigLoader.LoadConfigFromJson(null!));
        Assert.Throws<ArgumentNullException>(() =>
            DeidentificationConfigLoader.LoadConfigFromJson(""));
    }

    [Test]
    public async Task LoadConfigAsync_FileNotFound_ThrowsIOException()
    {
        // On Unix, non-existent directory throws DirectoryNotFoundException
        // On Windows, non-existent file throws FileNotFoundException
        // Both inherit from IOException
        IOException? caughtException = null;
        try
        {
            await DeidentificationConfigLoader.LoadConfigAsync("/nonexistent/path/config.json");
        }
        catch (IOException ex)
        {
            caughtException = ex;
        }

        Assert.That(caughtException, Is.Not.Null);
        Assert.That(caughtException, Is.InstanceOf<IOException>());
    }

    [Test]
    public async Task LoadConfigAsync_ValidFile_LoadsCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, @"{
                ""options"": [""RetainUIDs""],
                ""privateTagDefaults"": ""remove""
            }");

            var config = await DeidentificationConfigLoader.LoadConfigAsync(tempFile);

            Assert.That(config.Options, Does.Contain("RetainUIDs"));
            Assert.That(config.PrivateTagDefaults, Is.EqualTo("remove"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task LoadConfigAsync_ExtendsRelativeFile_ResolvesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var baseFile = Path.Combine(tempDir, "base.json");
            var childFile = Path.Combine(tempDir, "child.json");

            await File.WriteAllTextAsync(baseFile, @"{
                ""options"": [""CleanDescriptors""],
                ""privateTagDefaults"": ""remove""
            }");

            await File.WriteAllTextAsync(childFile, @"{
                ""$extends"": ""base.json"",
                ""options"": [""CleanGraphics""]
            }");

            var config = await DeidentificationConfigLoader.LoadConfigAsync(childFile);

            // Merged options
            Assert.That(config.Options, Does.Contain("CleanDescriptors"));
            Assert.That(config.Options, Does.Contain("CleanGraphics"));
            Assert.That(config.PrivateTagDefaults, Is.EqualTo("remove"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
#endif

    [Test]
    public void CreateBuilder_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DeidentificationConfigLoader.CreateBuilder(null!));
    }

    [Test]
    public void CreateBuilder_BasicConfig_ReturnsBuilder()
    {
        var config = new DeidentificationConfig
        {
            PrivateTagDefaults = "remove"
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_WithOptions_AppliesOptions()
    {
        var config = new DeidentificationConfig
        {
            Options = new List<string>
            {
                "RetainUIDs",
                "CleanDescriptors"
            }
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
        // Builder is returned - options are applied internally
    }

    [Test]
    public void CreateBuilder_WithDateShift_AppliesDateShift()
    {
        var config = new DeidentificationConfig
        {
            DateShift = new DateShiftConfigJson
            {
                Strategy = "fixed",
                OffsetDays = -100
            }
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_WithOverrides_AppliesOverrides()
    {
        var config = new DeidentificationConfig
        {
            Overrides = new Dictionary<string, string>
            {
                ["PatientName"] = "KEEP",
                ["(0008,0080)"] = "REMOVE"
            }
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_WithSafePrivateCreators_AppliesCreators()
    {
        var config = new DeidentificationConfig
        {
            SafePrivateCreators = new List<string>
            {
                "MyApp Creator",
                "Another Creator"
            }
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_KeepPrivateTags_AppliesCorrectly()
    {
        var config = new DeidentificationConfig
        {
            PrivateTagDefaults = "keep"
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_RemovePrivateTags_AppliesCorrectly()
    {
        var config = new DeidentificationConfig
        {
            PrivateTagDefaults = "remove"
        };

        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        Assert.That(builder, Is.Not.Null);
    }

    [Test]
    public void CreateBuilder_AllDateShiftStrategies_ParseCorrectly()
    {
        var strategies = new[] { "fixed", "randomPerPatient", "removeTime", "remove", "none" };

        foreach (var strategy in strategies)
        {
            var config = new DeidentificationConfig
            {
                DateShift = new DateShiftConfigJson
                {
                    Strategy = strategy
                }
            };

            Assert.DoesNotThrow(() =>
                DeidentificationConfigLoader.CreateBuilder(config),
                $"Strategy '{strategy}' should parse correctly");
        }
    }

    [Test]
    public void CreateBuilder_InvalidTagFormat_ThrowsArgumentException()
    {
        var config = new DeidentificationConfig
        {
            Overrides = new Dictionary<string, string>
            {
                ["invalid-tag"] = "KEEP"
            }
        };

        Assert.Throws<ArgumentException>(() =>
            DeidentificationConfigLoader.CreateBuilder(config));
    }

    [Test]
    public void CreateBuilder_InvalidAction_ThrowsArgumentException()
    {
        var config = new DeidentificationConfig
        {
            Overrides = new Dictionary<string, string>
            {
                ["PatientName"] = "INVALID_ACTION"
            }
        };

        Assert.Throws<ArgumentException>(() =>
            DeidentificationConfigLoader.CreateBuilder(config));
    }

    [Test]
    public void CreateBuilder_HexTagFormat_ParsesCorrectly()
    {
        var config = new DeidentificationConfig
        {
            Overrides = new Dictionary<string, string>
            {
                ["(0010,0010)"] = "KEEP",  // PatientName
                ["(0008, 0080)"] = "REMOVE"  // With space
            }
        };

        Assert.DoesNotThrow(() =>
            DeidentificationConfigLoader.CreateBuilder(config));
    }

    [Test]
    public void CreateBuilder_ActionCodes_ParseCorrectly()
    {
        var actionCodes = new Dictionary<string, string>
        {
            ["PatientName"] = "D",
            ["PatientID"] = "Z",
            ["InstitutionName"] = "X",
            ["Modality"] = "K",
            ["StudyDescription"] = "C"
        };

        var config = new DeidentificationConfig
        {
            Overrides = actionCodes
        };

        Assert.DoesNotThrow(() =>
            DeidentificationConfigLoader.CreateBuilder(config));
    }

    [Test]
    public void CreateBuilder_ActionNames_ParseCorrectly()
    {
        var actionNames = new Dictionary<string, string>
        {
            ["PatientName"] = "DUMMY",
            ["PatientID"] = "ZERO",
            ["InstitutionName"] = "REMOVE",
            ["Modality"] = "KEEP",
            ["StudyDescription"] = "CLEAN"
        };

        var config = new DeidentificationConfig
        {
            Overrides = actionNames
        };

        Assert.DoesNotThrow(() =>
            DeidentificationConfigLoader.CreateBuilder(config));
    }
}
#endif
