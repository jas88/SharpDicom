using System;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class DicomUIDTests
    {
        [Test]
        public void Constructor_FromString_StoresCorrectly()
        {
            var uid = new DicomUID("1.2.840.10008.1.2");

            Assert.That(uid.Length, Is.EqualTo(17));
            Assert.That(uid.ToString(), Is.EqualTo("1.2.840.10008.1.2"));
            Assert.That(uid.IsEmpty, Is.False);
        }

        [Test]
        public void Constructor_FromBytes_StoresCorrectly()
        {
            var bytes = Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1");
            var uid = new DicomUID(bytes);

            Assert.That(uid.Length, Is.EqualTo(19));
            Assert.That(uid.ToString(), Is.EqualTo("1.2.840.10008.1.2.1"));
            Assert.That(uid.IsEmpty, Is.False);
        }

        [Test]
        public void Constructor_EmptyString_CreatesEmptyUID()
        {
            var uid = new DicomUID("");

            Assert.That(uid.Length, Is.EqualTo(0));
            Assert.That(uid.IsEmpty, Is.True);
            Assert.That(uid.ToString(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Constructor_NullString_CreatesEmptyUID()
        {
            var uid = new DicomUID((string)null!);

            Assert.That(uid.Length, Is.EqualTo(0));
            Assert.That(uid.IsEmpty, Is.True);
        }

        [Test]
        public void Constructor_MaxLength_AcceptsExactly64Characters()
        {
            var maxLengthUid = new string('1', 63) + "2"; // Exactly 64 chars
            var uid = new DicomUID(maxLengthUid);

            Assert.That(uid.Length, Is.EqualTo(64));
            Assert.That(uid.ToString(), Is.EqualTo(maxLengthUid));
        }

        [Test]
        public void Constructor_ExceedsMaxLength_ThrowsArgumentException()
        {
            var tooLongUid = new string('1', 65);

            Assert.Throws<ArgumentException>(() => new DicomUID(tooLongUid));
        }

        [Test]
        public void AsSpan_ReturnsCorrectBytes()
        {
            var uid = new DicomUID("1.2.3");
            var span = uid.AsSpan();
            var expected = Encoding.ASCII.GetBytes("1.2.3");

            Assert.That(span.Length, Is.EqualTo(5));
            Assert.That(span.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void AsSpan_EmptyUID_ReturnsEmptySpan()
        {
            var uid = new DicomUID("");
            var span = uid.AsSpan();

            Assert.That(span.Length, Is.EqualTo(0));
        }

        [Test]
        public void ToString_Roundtrip_PreservesValue()
        {
            var original = "1.2.840.10008.5.1.4.1.1.2";
            var uid = new DicomUID(original);
            var result = uid.ToString();

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void Equals_SameUID_ReturnsTrue()
        {
            var uid1 = new DicomUID("1.2.840.10008.1.2");
            var uid2 = new DicomUID("1.2.840.10008.1.2");

            Assert.That(uid1.Equals(uid2), Is.True);
            Assert.That(uid1 == uid2, Is.True);
            Assert.That(uid1 != uid2, Is.False);
        }

        [Test]
        public void Equals_DifferentUID_ReturnsFalse()
        {
            var uid1 = new DicomUID("1.2.840.10008.1.2");
            var uid2 = new DicomUID("1.2.840.10008.1.2.1");

            Assert.That(uid1.Equals(uid2), Is.False);
            Assert.That(uid1 == uid2, Is.False);
            Assert.That(uid1 != uid2, Is.True);
        }

        [Test]
        public void Equals_DifferentLength_ReturnsFalse()
        {
            var uid1 = new DicomUID("1.2.3");
            var uid2 = new DicomUID("1.2.3.4");

            Assert.That(uid1.Equals(uid2), Is.False);
        }

        [Test]
        public void GetHashCode_SameUID_ReturnsSameHash()
        {
            var uid1 = new DicomUID("1.2.840.10008.1.2");
            var uid2 = new DicomUID("1.2.840.10008.1.2");

            Assert.That(uid1.GetHashCode(), Is.EqualTo(uid2.GetHashCode()));
        }

        [Test]
        public void GetHashCode_DifferentUID_ReturnsDifferentHash()
        {
            var uid1 = new DicomUID("1.2.840.10008.1.2");
            var uid2 = new DicomUID("1.2.840.10008.1.2.1");

            // While hash collisions are possible, these should be different
            Assert.That(uid1.GetHashCode(), Is.Not.EqualTo(uid2.GetHashCode()));
        }

        [Test]
        public void IsValid_ValidUID_ReturnsTrue()
        {
            var validUIDs = new[]
            {
                "1.2.3",
                "1.2.840.10008.1.2",
                "2.25.123456789012345678901234567890",
                "1.2.3.4.5.6.7.8.9.0"
            };

            foreach (var uidString in validUIDs)
            {
                var uid = new DicomUID(uidString);
                Assert.That(uid.IsValid, Is.True, $"UID {uidString} should be valid");
            }
        }

        [Test]
        public void IsValid_InvalidUID_ReturnsFalse()
        {
            var invalidUIDs = new[]
            {
                "1.2.03", // Leading zero
                ".1.2.3", // Starts with period
                "1.2.3.", // Ends with period
                "1..2.3", // Consecutive periods
                "1.2.a.3", // Invalid character
                "1.2.3 ", // Space
                "01.2.3" // Leading zero on first component
            };

            foreach (var uidString in invalidUIDs)
            {
                var uid = new DicomUID(uidString);
                Assert.That(uid.IsValid, Is.False, $"UID {uidString} should be invalid");
            }
        }

        [Test]
        public void IsValid_EmptyUID_ReturnsFalse()
        {
            var uid = new DicomUID("");
            Assert.That(uid.IsValid, Is.False);
        }

        [Test]
        public void IsValid_SingleZero_ReturnsTrue()
        {
            var uid = new DicomUID("0");
            Assert.That(uid.IsValid, Is.True);
        }

        [Test]
        public void IsValid_ComponentWithSingleZero_ReturnsTrue()
        {
            var uid = new DicomUID("1.0.3");
            Assert.That(uid.IsValid, Is.True);
        }

        [Test]
        public void Generate_CreatesValidUID()
        {
            var uid = DicomUID.Generate();

            Assert.That(uid.IsEmpty, Is.False);
            Assert.That(uid.IsValid, Is.True);
            Assert.That(uid.ToString(), Does.StartWith("2.25."));
        }

        [Test]
        public void Generate_CreatesUniqueUIDs()
        {
            var uid1 = DicomUID.Generate();
            var uid2 = DicomUID.Generate();

            Assert.That(uid1, Is.Not.EqualTo(uid2));
        }

        [Test]
        public void GenerateWithRoot_CreatesValidUID()
        {
            var root = "1.2.840.999";
            var uid = DicomUID.Generate(root);

            Assert.That(uid.IsEmpty, Is.False);
            Assert.That(uid.IsValid, Is.True);
            Assert.That(uid.ToString(), Does.StartWith(root + "."));
        }

        [Test]
        public void GenerateWithRoot_NullRoot_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DicomUID.Generate(null!));
        }

        [Test]
        public void GenerateWithRoot_EmptyRoot_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DicomUID.Generate(""));
        }

        [Test]
        public void GenerateFromName_CreatesValidUID()
        {
            var root = "1.2.840.999";
            var name = "MyTestEntity";
            var uid = DicomUID.GenerateFromName(root, name);

            Assert.That(uid.IsEmpty, Is.False);
            Assert.That(uid.IsValid, Is.True);
            Assert.That(uid.ToString(), Does.StartWith(root + "."));
        }

        [Test]
        public void GenerateFromName_SameName_CreatesSameUID()
        {
            var root = "1.2.840.999";
            var name = "DeterministicTest";
            var uid1 = DicomUID.GenerateFromName(root, name);
            var uid2 = DicomUID.GenerateFromName(root, name);

            Assert.That(uid1, Is.EqualTo(uid2));
            Assert.That(uid1.ToString(), Is.EqualTo(uid2.ToString()));
        }

        [Test]
        public void GenerateFromName_DifferentName_CreatesDifferentUID()
        {
            var root = "1.2.840.999";
            var uid1 = DicomUID.GenerateFromName(root, "Name1");
            var uid2 = DicomUID.GenerateFromName(root, "Name2");

            Assert.That(uid1, Is.Not.EqualTo(uid2));
        }

        [Test]
        public void GenerateFromName_NullRoot_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DicomUID.GenerateFromName(null!, "name"));
        }

        [Test]
        public void GenerateFromName_NullName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DicomUID.GenerateFromName("1.2.3", null!));
        }
    }
}
