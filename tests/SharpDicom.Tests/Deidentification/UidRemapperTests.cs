using System.Collections.Generic;
using NUnit.Framework;
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
}
