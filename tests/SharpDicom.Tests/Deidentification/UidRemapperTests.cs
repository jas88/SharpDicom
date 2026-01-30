using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class UidRemapperTests
{
    [Test]
    public void Remap_SameUid_ReturnsSameMapping()
    {
        using var remapper = new UidRemapper();

        var uid1 = remapper.Remap("1.2.3.4.5.6");
        var uid2 = remapper.Remap("1.2.3.4.5.6");

        Assert.That(uid2, Is.EqualTo(uid1));
    }

    [Test]
    public void Remap_DifferentUids_ReturnsDifferentMappings()
    {
        using var remapper = new UidRemapper();

        var uid1 = remapper.Remap("1.2.3.4.5.6");
        var uid2 = remapper.Remap("1.2.3.4.5.7");

        Assert.That(uid2, Is.Not.EqualTo(uid1));
    }

    [Test]
    public void Remap_NewUid_Returns225Format()
    {
        using var remapper = new UidRemapper();

        var newUid = remapper.Remap("1.2.3.4.5.6");

        Assert.That(newUid, Does.StartWith("2.25."));
    }

    [Test]
    public void Remap_NewUid_ValidLength()
    {
        using var remapper = new UidRemapper();

        var newUid = remapper.Remap("1.2.3.4.5.6");

        Assert.That(newUid.Length, Is.LessThanOrEqualTo(64));
    }

    [Test]
    public void Remap_EmptyUid_ReturnsEmpty()
    {
        using var remapper = new UidRemapper();

        var result = remapper.Remap("");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Remap_WhitespaceUid_ReturnsWhitespace()
    {
        using var remapper = new UidRemapper();

        var result = remapper.Remap("  ");

        Assert.That(result, Is.EqualTo("  "));
    }

    [Test]
    public void TryGetOriginal_MappedUid_ReturnsTrue()
    {
        using var remapper = new UidRemapper();
        var originalUid = "1.2.3.4.5.6";

        var newUid = remapper.Remap(originalUid);
        var found = remapper.TryGetOriginal(newUid, out var retrievedOriginal);

        Assert.That(found, Is.True);
        Assert.That(retrievedOriginal, Is.EqualTo(originalUid));
    }

    [Test]
    public void TryGetOriginal_UnknownUid_ReturnsFalse()
    {
        using var remapper = new UidRemapper();

        var found = remapper.TryGetOriginal("2.25.12345", out _);

        Assert.That(found, Is.False);
    }

    [Test]
    public void MappingCount_AfterRemaps_ReturnsCorrectCount()
    {
        using var remapper = new UidRemapper();

        remapper.Remap("1.2.3.4.5.6");
        remapper.Remap("1.2.3.4.5.7");
        remapper.Remap("1.2.3.4.5.6"); // Duplicate

        Assert.That(remapper.MappingCount, Is.EqualTo(2));
    }

    [Test]
    public void Remap_UidWithTrailingSpace_TrimsFirst()
    {
        using var remapper = new UidRemapper();

        var uid1 = remapper.Remap("1.2.3.4.5.6 ");
        var uid2 = remapper.Remap("1.2.3.4.5.6");

        // Should be considered same UID after trimming
        Assert.That(uid2, Is.EqualTo(uid1));
    }

    [Test]
    public void Remap_TransferSyntaxUid_NotRemapped()
    {
        using var remapper = new UidRemapper();

        // Standard Transfer Syntax UIDs should not be remapped
        var transferSyntax = "1.2.840.10008.1.2.1"; // Explicit VR Little Endian

        var result = remapper.Remap(transferSyntax);

        Assert.That(result, Is.EqualTo(transferSyntax));
        Assert.That(remapper.MappingCount, Is.EqualTo(0));
    }

    [Test]
    public void Remap_SopClassUid_NotRemapped()
    {
        using var remapper = new UidRemapper();

        // Standard SOP Class UIDs should not be remapped
        var sopClass = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage

        var result = remapper.Remap(sopClass);

        Assert.That(result, Is.EqualTo(sopClass));
        Assert.That(remapper.MappingCount, Is.EqualTo(0));
    }

    [Test]
    public void Remap_DicomDefinedUid_NotRemapped()
    {
        using var remapper = new UidRemapper();

        // All UIDs with DICOM root should not be remapped
        var dicomUid = "1.2.840.10008.1.2.4.50"; // JPEG Baseline

        var result = remapper.Remap(dicomUid);

        Assert.That(result, Is.EqualTo(dicomUid));
    }

    [Test]
    public void IsStandardUid_DicomRoot_ReturnsTrue()
    {
        using var remapper = new UidRemapper();

        Assert.That(remapper.IsStandardUid("1.2.840.10008.1.2"), Is.True);
        Assert.That(remapper.IsStandardUid("1.2.840.10008.5.1.4.1.1.2"), Is.True);
    }

    [Test]
    public void IsStandardUid_InstanceUid_ReturnsFalse()
    {
        using var remapper = new UidRemapper();

        Assert.That(remapper.IsStandardUid("1.2.3.4.5.6.7.8.9"), Is.False);
        Assert.That(remapper.IsStandardUid("2.16.840.1.113883.3.42.10001"), Is.False);
    }

    [Test]
    public void AddStandardUid_CustomUid_PreservesIt()
    {
        using var remapper = new UidRemapper();
        var customVendorUid = "1.2.276.0.7230010.3.1.0.1"; // Custom vendor UID

        remapper.AddStandardUid(customVendorUid);

        Assert.That(remapper.IsStandardUid(customVendorUid), Is.True);
        Assert.That(remapper.Remap(customVendorUid), Is.EqualTo(customVendorUid));
    }

    [Test]
    public void RemapDataset_RemapsInstanceUids()
    {
        using var remapper = new UidRemapper();

        var dataset = new DicomDataset();
        var studyUid = "1.2.3.4.5.6.7";
        var sopInstanceUid = "1.2.3.4.5.6.8";
        dataset.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, Encoding.ASCII.GetBytes(studyUid)));
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, Encoding.ASCII.GetBytes(sopInstanceUid)));

        var remapped = remapper.RemapDataset(dataset);

        Assert.That(remapped.Count, Is.EqualTo(2));
        Assert.That(dataset.GetString(DicomTag.StudyInstanceUID), Does.StartWith("2.25."));
        Assert.That(dataset.GetString(DicomTag.SOPInstanceUID), Does.StartWith("2.25."));
    }

    [Test]
    public void RemapDataset_PreservesStandardUids()
    {
        using var remapper = new UidRemapper();

        var dataset = new DicomDataset();
        var sopClassUid = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage
        dataset.Add(new DicomStringElement(DicomTag.SOPClassUID, DicomVR.UI, Encoding.ASCII.GetBytes(sopClassUid)));

        var remapped = remapper.RemapDataset(dataset);

        Assert.That(remapped.Count, Is.EqualTo(0));
        Assert.That(dataset.GetString(DicomTag.SOPClassUID), Is.EqualTo(sopClassUid));
    }

    [Test]
    public void RemapDataset_TraversesSequences()
    {
        using var remapper = new UidRemapper();

        // Create referenced SOP sequence with UIDs inside
        var referencedSopUid = "1.2.3.4.5.6.9";
        var seqItem = new DicomDataset();
        seqItem.Add(new DicomStringElement(new DicomTag(0x0008, 0x1155), DicomVR.UI,
            Encoding.ASCII.GetBytes(referencedSopUid))); // Referenced SOP Instance UID

        var dataset = new DicomDataset();
        var sequence = new DicomSequence(new DicomTag(0x0008, 0x1115), seqItem); // Referenced Series Sequence
        dataset.Add(sequence);

        var remapped = remapper.RemapDataset(dataset);

        Assert.That(remapped.Count, Is.EqualTo(1));
        var sequenceItem = dataset.GetSequence(new DicomTag(0x0008, 0x1115))!.Items[0];
        Assert.That(sequenceItem.GetString(new DicomTag(0x0008, 0x1155)), Does.StartWith("2.25."));
    }

    [Test]
    public void RemapDataset_ConsistentMappingAcrossSequences()
    {
        using var remapper = new UidRemapper();

        var sharedUid = "1.2.3.4.5.6.10";

        // Create dataset with same UID at root level and in sequence
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, Encoding.ASCII.GetBytes(sharedUid)));

        var seqItem = new DicomDataset();
        seqItem.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, Encoding.ASCII.GetBytes(sharedUid)));
        dataset.Add(new DicomSequence(new DicomTag(0x0008, 0x1115), seqItem));

        var remapped = remapper.RemapDataset(dataset);

        var rootMapped = dataset.GetString(DicomTag.StudyInstanceUID);
        var seqMapped = dataset.GetSequence(new DicomTag(0x0008, 0x1115))!.Items[0].GetString(DicomTag.StudyInstanceUID);

        Assert.That(seqMapped, Is.EqualTo(rootMapped), "Same UID should map to same value");
    }
}

[TestFixture]
public class UidMapperTests
{
    [Test]
    public void Map_InstanceUid_Remaps()
    {
        using var mapper = new UidMapper();

        var original = "1.2.3.4.5.6.7.8";
        var mapped = mapper.Map(original);

        Assert.That(mapped, Is.Not.EqualTo(original));
        Assert.That(mapped, Does.StartWith("2.25."));
    }

    [Test]
    public void Map_StandardUid_Preserved()
    {
        using var mapper = new UidMapper();

        var transferSyntax = "1.2.840.10008.1.2";
        var mapped = mapper.Map(transferSyntax);

        Assert.That(mapped, Is.EqualTo(transferSyntax));
    }

    [Test]
    public void Map_ConsistentWithScope()
    {
        using var mapper = new UidMapper();

        var original = "1.2.3.4.5";
        var mapped1 = mapper.Map(original, "study-1");
        var mapped2 = mapper.Map(original, "study-1");

        Assert.That(mapped2, Is.EqualTo(mapped1));
    }

    [Test]
    public void TryGetOriginal_AfterMapping_ReturnsOriginal()
    {
        using var mapper = new UidMapper();

        var original = "1.2.3.4.5";
        var mapped = mapper.Map(original);

        Assert.That(mapper.TryGetOriginal(mapped, out var retrieved), Is.True);
        Assert.That(retrieved, Is.EqualTo(original));
    }

    [Test]
    public void AddStandardUid_PreservesCustomUid()
    {
        using var mapper = new UidMapper();
        var vendorUid = "1.2.276.0.7230010.3.1.0.1";

        mapper.AddStandardUid(vendorUid);

        Assert.That(mapper.IsStandardUid(vendorUid), Is.True);
        Assert.That(mapper.Map(vendorUid), Is.EqualTo(vendorUid));
    }
}

[TestFixture]
public class UidGeneratorTests
{
    [Test]
    public void GenerateUid_ReturnsValidUid()
    {
        var uid = UidGenerator.GenerateUid();

        Assert.That(uid, Does.StartWith("2.25."));
        Assert.That(UidGenerator.IsValidUid(uid), Is.True);
    }

    [Test]
    public void GenerateUid_ReturnsUniqueUids()
    {
        var uids = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            var uid = UidGenerator.GenerateUid();
            Assert.That(uids.Add(uid), Is.True, $"Duplicate UID generated: {uid}");
        }
    }

    [Test]
    public void GenerateDeterministicUid_SameSeed_ReturnsSameUid()
    {
        var uid1 = UidGenerator.GenerateDeterministicUid("test-seed-123");
        var uid2 = UidGenerator.GenerateDeterministicUid("test-seed-123");

        Assert.That(uid2, Is.EqualTo(uid1));
    }

    [Test]
    public void GenerateDeterministicUid_DifferentSeed_ReturnsDifferentUid()
    {
        var uid1 = UidGenerator.GenerateDeterministicUid("test-seed-123");
        var uid2 = UidGenerator.GenerateDeterministicUid("test-seed-456");

        Assert.That(uid2, Is.Not.EqualTo(uid1));
    }

    [Test]
    public void IsValidUid_ValidUid_ReturnsTrue()
    {
        Assert.That(UidGenerator.IsValidUid("1.2.3.4.5"), Is.True);
        Assert.That(UidGenerator.IsValidUid("2.25.12345"), Is.True);
        Assert.That(UidGenerator.IsValidUid("1.2.840.10008.1.2"), Is.True);
    }

    [Test]
    public void IsValidUid_InvalidUid_ReturnsFalse()
    {
        Assert.That(UidGenerator.IsValidUid(""), Is.False);
        Assert.That(UidGenerator.IsValidUid("1..2"), Is.False);  // Empty component
        Assert.That(UidGenerator.IsValidUid("1.2."), Is.False);  // Trailing dot
        Assert.That(UidGenerator.IsValidUid(".1.2"), Is.False);  // Leading dot
        Assert.That(UidGenerator.IsValidUid("1.02.3"), Is.False); // Leading zero
        Assert.That(UidGenerator.IsValidUid("abc"), Is.False);   // Non-numeric
    }

    [Test]
    public void IsValidUid_TooLong_ReturnsFalse()
    {
        var longUid = "1." + new string('1', 64);
        Assert.That(UidGenerator.IsValidUid(longUid), Is.False);
    }

    [Test]
    public void IsValidUid_SingleZero_IsValid()
    {
        // Single component of 0 is valid
        Assert.That(UidGenerator.IsValidUid("0"), Is.True);
        Assert.That(UidGenerator.IsValidUid("1.0.2"), Is.True);
    }
}

[TestFixture]
public class InMemoryUidStoreTests
{
    [Test]
    public void GetOrCreateMapping_NewUid_CreatesMapping()
    {
        using var store = new InMemoryUidStore();

        var mapped = store.GetOrCreateMapping("1.2.3.4");

        Assert.That(mapped, Is.Not.EqualTo("1.2.3.4"));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateMapping_ExistingUid_ReturnsSameMapping()
    {
        using var store = new InMemoryUidStore();

        var mapped1 = store.GetOrCreateMapping("1.2.3.4");
        var mapped2 = store.GetOrCreateMapping("1.2.3.4");

        Assert.That(mapped2, Is.EqualTo(mapped1));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryGetMapped_ExistingUid_ReturnsTrue()
    {
        using var store = new InMemoryUidStore();
        var mapped = store.GetOrCreateMapping("1.2.3.4");

        var found = store.TryGetMapped("1.2.3.4", out var result);

        Assert.That(found, Is.True);
        Assert.That(result, Is.EqualTo(mapped));
    }

    [Test]
    public void TryGetOriginal_ExistingMapping_ReturnsTrue()
    {
        using var store = new InMemoryUidStore();
        var mapped = store.GetOrCreateMapping("1.2.3.4");

        var found = store.TryGetOriginal(mapped, out var original);

        Assert.That(found, Is.True);
        Assert.That(original, Is.EqualTo("1.2.3.4"));
    }

    [Test]
    public void Clear_RemovesAllMappings()
    {
        using var store = new InMemoryUidStore();
        store.GetOrCreateMapping("1.2.3.4");
        store.GetOrCreateMapping("1.2.3.5");

        store.Clear();

        Assert.That(store.Count, Is.EqualTo(0));
    }

    [Test]
    public void AddMappings_BatchInsert_Works()
    {
        using var store = new InMemoryUidStore();

        var mappings = new List<(string, string, string)>
        {
            ("1.2.3.1", "2.25.1", "scope1"),
            ("1.2.3.2", "2.25.2", "scope1"),
            ("1.2.3.3", "2.25.3", "scope2")
        };

        store.AddMappings(mappings);

        Assert.That(store.Count, Is.EqualTo(3));
        Assert.That(store.TryGetMapping("1.2.3.1", out var result1), Is.True);
        Assert.That(result1, Is.EqualTo("2.25.1"));
    }

    [Test]
    public async Task ExportToJsonAsync_GeneratesValidJson()
    {
        using var store = new InMemoryUidStore();
        store.GetOrCreateMapping("1.2.3.4");
        store.GetOrCreateMapping("1.2.3.5");

        using var stream = new MemoryStream();
        await store.ExportToJsonAsync(stream);

        stream.Position = 0;
        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.That(json, Does.Contain("\"mappingCount\": 2"));
        Assert.That(json, Does.Contain("\"originalUid\""));
        Assert.That(json, Does.Contain("\"remappedUid\""));
    }
}

[TestFixture]
public class SqliteUidStoreTests
{
    private string _tempDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"uid_store_test_{Guid.NewGuid()}.db");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
            // Also delete WAL and SHM files
            var walPath = _tempDbPath + "-wal";
            var shmPath = _tempDbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Test]
    public void Constructor_CreatesDatabase()
    {
        using var store = new SqliteUidStore(_tempDbPath);

        Assert.That(File.Exists(_tempDbPath), Is.True);
    }

    [Test]
    public void GetOrCreateMapping_NewUid_CreatesMapping()
    {
        using var store = new SqliteUidStore(_tempDbPath);

        var mapped = store.GetOrCreateMapping("1.2.3.4", "test-scope");

        Assert.That(mapped, Is.Not.EqualTo("1.2.3.4"));
        Assert.That(mapped, Does.StartWith("2.25."));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateMapping_ExistingUid_ReturnsSameMapping()
    {
        using var store = new SqliteUidStore(_tempDbPath);

        var mapped1 = store.GetOrCreateMapping("1.2.3.4", "scope");
        var mapped2 = store.GetOrCreateMapping("1.2.3.4", "scope");

        Assert.That(mapped2, Is.EqualTo(mapped1));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryGetMapping_ExistingUid_ReturnsTrue()
    {
        using var store = new SqliteUidStore(_tempDbPath);
        var mapped = store.GetOrCreateMapping("1.2.3.4", "scope");

        var found = store.TryGetMapping("1.2.3.4", out var result);

        Assert.That(found, Is.True);
        Assert.That(result, Is.EqualTo(mapped));
    }

    [Test]
    public void TryGetOriginal_ExistingMapping_ReturnsTrue()
    {
        using var store = new SqliteUidStore(_tempDbPath);
        var mapped = store.GetOrCreateMapping("1.2.3.4", "scope");

        var found = store.TryGetOriginal(mapped, out var original);

        Assert.That(found, Is.True);
        Assert.That(original, Is.EqualTo("1.2.3.4"));
    }

    [Test]
    public void AddMappings_BatchInsert_Works()
    {
        using var store = new SqliteUidStore(_tempDbPath);

        var mappings = new List<(string, string, string)>
        {
            ("1.2.3.1", "2.25.1", "scope1"),
            ("1.2.3.2", "2.25.2", "scope1"),
            ("1.2.3.3", "2.25.3", "scope2")
        };

        store.AddMappings(mappings);

        Assert.That(store.Count, Is.EqualTo(3));
    }

    [Test]
    public void Persistence_MappingsSurviveReopen()
    {
        string mapped;

        // Create store, add mapping, dispose
        using (var store = new SqliteUidStore(_tempDbPath))
        {
            mapped = store.GetOrCreateMapping("1.2.3.4.5", "study");
        }

        // Reopen and verify mapping persisted
        using (var store = new SqliteUidStore(_tempDbPath))
        {
            var found = store.TryGetMapping("1.2.3.4.5", out var result);

            Assert.That(found, Is.True);
            Assert.That(result, Is.EqualTo(mapped));
        }
    }

    [Test]
    public async Task ExportToJsonAsync_GeneratesValidJson()
    {
        using var store = new SqliteUidStore(_tempDbPath);
        store.GetOrCreateMapping("1.2.3.4", "scope");
        store.GetOrCreateMapping("1.2.3.5", "scope");

        using var stream = new MemoryStream();
        await store.ExportToJsonAsync(stream);

        stream.Position = 0;
        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.That(json, Does.Contain("\"mappingCount\": 2"));
        Assert.That(json, Does.Contain("\"originalUid\""));
    }

    [Test]
    public void GetMappingsByScope_ReturnsOnlyScopedMappings()
    {
        using var store = new SqliteUidStore(_tempDbPath);

        store.GetOrCreateMapping("1.2.3.1", "scope-A");
        store.GetOrCreateMapping("1.2.3.2", "scope-A");
        store.GetOrCreateMapping("1.2.3.3", "scope-B");

        var scopeA = new List<(string, string)>(store.GetMappingsByScope("scope-A"));
        var scopeB = new List<(string, string)>(store.GetMappingsByScope("scope-B"));

        Assert.That(scopeA.Count, Is.EqualTo(2));
        Assert.That(scopeB.Count, Is.EqualTo(1));
    }

    [Test]
    public void Clear_RemovesAllMappings()
    {
        using var store = new SqliteUidStore(_tempDbPath);
        store.GetOrCreateMapping("1.2.3.4", "scope");
        store.GetOrCreateMapping("1.2.3.5", "scope");

        store.Clear();

        Assert.That(store.Count, Is.EqualTo(0));
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var store = new SqliteUidStore(_tempDbPath);
        store.GetOrCreateMapping("1.2.3.4", "scope");

        store.Dispose();
        Assert.DoesNotThrow(() => store.Dispose());
    }

    [Test]
    public void AfterDispose_ThrowsObjectDisposedException()
    {
        var store = new SqliteUidStore(_tempDbPath);
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.GetOrCreateMapping("1.2.3.4", "scope"));
        Assert.Throws<ObjectDisposedException>(() => _ = store.Count);
    }
}
