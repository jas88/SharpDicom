using NUnit.Framework;
using SharpDicom.Data;
using System;

namespace SharpDicom.Tests.Data
{
    [TestFixture]
    public class ValueMultiplicityTests
    {
        [Test]
        public void Constructor_ValidRange_CreatesVM()
        {
            var vm = new ValueMultiplicity(1, 3);
            Assert.That(vm.Min, Is.EqualTo(1));
            Assert.That(vm.Max, Is.EqualTo(3));
            Assert.That(vm.IsUnlimited, Is.False);
        }

        [Test]
        public void Constructor_Unlimited_CreatesUnlimitedVM()
        {
            var vm = new ValueMultiplicity(1, ushort.MaxValue);
            Assert.That(vm.Min, Is.EqualTo(1));
            Assert.That(vm.Max, Is.EqualTo(ushort.MaxValue));
            Assert.That(vm.IsUnlimited, Is.True);
        }

        [Test]
        public void Constructor_MinGreaterThanMax_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => new ValueMultiplicity(3, 1));
        }

        [Test]
        public void IsValid_WithinRange_ReturnsTrue()
        {
            var vm = new ValueMultiplicity(1, 3);
            Assert.That(vm.IsValid(1), Is.True);
            Assert.That(vm.IsValid(2), Is.True);
            Assert.That(vm.IsValid(3), Is.True);
        }

        [Test]
        public void IsValid_BelowMin_ReturnsFalse()
        {
            var vm = new ValueMultiplicity(2, 5);
            Assert.That(vm.IsValid(0), Is.False);
            Assert.That(vm.IsValid(1), Is.False);
        }

        [Test]
        public void IsValid_AboveMax_ReturnsFalse()
        {
            var vm = new ValueMultiplicity(1, 3);
            Assert.That(vm.IsValid(4), Is.False);
            Assert.That(vm.IsValid(100), Is.False);
        }

        [Test]
        public void IsValid_Unlimited_AllowsLargeCounts()
        {
            var vm = new ValueMultiplicity(1, ushort.MaxValue);
            Assert.That(vm.IsValid(1), Is.True);
            Assert.That(vm.IsValid(100), Is.True);
            Assert.That(vm.IsValid(10000), Is.True);
            Assert.That(vm.IsValid(int.MaxValue), Is.True);
        }

        [Test]
        public void IsValid_NegativeCount_ReturnsFalse()
        {
            var vm = new ValueMultiplicity(1, 3);
            Assert.That(vm.IsValid(-1), Is.False);
        }

        [Test]
        public void Equality_SameMinMax_AreEqual()
        {
            var vm1 = new ValueMultiplicity(1, 3);
            var vm2 = new ValueMultiplicity(1, 3);

            Assert.That(vm1, Is.EqualTo(vm2));
            Assert.That(vm1 == vm2, Is.True);
            Assert.That(vm1 != vm2, Is.False);
        }

        [Test]
        public void Equality_DifferentMinOrMax_AreNotEqual()
        {
            var vm1 = new ValueMultiplicity(1, 3);
            var vm2 = new ValueMultiplicity(1, 4);
            var vm3 = new ValueMultiplicity(2, 3);

            Assert.That(vm1, Is.Not.EqualTo(vm2));
            Assert.That(vm1, Is.Not.EqualTo(vm3));
        }

        [Test]
        public void GetHashCode_SameMinMax_SameHash()
        {
            var vm1 = new ValueMultiplicity(1, 3);
            var vm2 = new ValueMultiplicity(1, 3);

            Assert.That(vm1.GetHashCode(), Is.EqualTo(vm2.GetHashCode()));
        }

        [Test]
        public void ToString_Fixed_ReturnsNumber()
        {
            var vm = new ValueMultiplicity(3, 3);
            Assert.That(vm.ToString(), Is.EqualTo("3"));
        }

        [Test]
        public void ToString_Range_ReturnsMinDashMax()
        {
            var vm = new ValueMultiplicity(1, 3);
            Assert.That(vm.ToString(), Is.EqualTo("1-3"));
        }

        [Test]
        public void ToString_Unlimited_ReturnsMinDashN()
        {
            var vm = new ValueMultiplicity(1, ushort.MaxValue);
            Assert.That(vm.ToString(), Is.EqualTo("1-n"));
        }

        [Test]
        public void StaticInstances_VM_1_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_1.Min, Is.EqualTo(1));
            Assert.That(ValueMultiplicity.VM_1.Max, Is.EqualTo(1));
            Assert.That(ValueMultiplicity.VM_1.IsValid(1), Is.True);
            Assert.That(ValueMultiplicity.VM_1.IsValid(0), Is.False);
            Assert.That(ValueMultiplicity.VM_1.IsValid(2), Is.False);
        }

        [Test]
        public void StaticInstances_VM_1_N_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_1_N.Min, Is.EqualTo(1));
            Assert.That(ValueMultiplicity.VM_1_N.IsUnlimited, Is.True);
            Assert.That(ValueMultiplicity.VM_1_N.IsValid(1), Is.True);
            Assert.That(ValueMultiplicity.VM_1_N.IsValid(100), Is.True);
            Assert.That(ValueMultiplicity.VM_1_N.IsValid(0), Is.False);
        }

        [Test]
        public void StaticInstances_VM_2_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_2.Min, Is.EqualTo(2));
            Assert.That(ValueMultiplicity.VM_2.Max, Is.EqualTo(2));
            Assert.That(ValueMultiplicity.VM_2.IsValid(2), Is.True);
            Assert.That(ValueMultiplicity.VM_2.IsValid(1), Is.False);
            Assert.That(ValueMultiplicity.VM_2.IsValid(3), Is.False);
        }

        [Test]
        public void StaticInstances_VM_3_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_3.Min, Is.EqualTo(3));
            Assert.That(ValueMultiplicity.VM_3.Max, Is.EqualTo(3));
        }

        [Test]
        public void StaticInstances_VM_1_2_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_1_2.Min, Is.EqualTo(1));
            Assert.That(ValueMultiplicity.VM_1_2.Max, Is.EqualTo(2));
            Assert.That(ValueMultiplicity.VM_1_2.IsValid(1), Is.True);
            Assert.That(ValueMultiplicity.VM_1_2.IsValid(2), Is.True);
            Assert.That(ValueMultiplicity.VM_1_2.IsValid(3), Is.False);
        }

        [Test]
        public void StaticInstances_VM_1_3_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_1_3.Min, Is.EqualTo(1));
            Assert.That(ValueMultiplicity.VM_1_3.Max, Is.EqualTo(3));
        }

        [Test]
        public void StaticInstances_VM_2_2N_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_2_2N.Min, Is.EqualTo(2));
            Assert.That(ValueMultiplicity.VM_2_2N.IsUnlimited, Is.True);
            Assert.That(ValueMultiplicity.VM_2_2N.IsValid(2), Is.True);
            Assert.That(ValueMultiplicity.VM_2_2N.IsValid(4), Is.True);
            Assert.That(ValueMultiplicity.VM_2_2N.IsValid(1), Is.False);
        }

        [Test]
        public void StaticInstances_VM_3_3N_IsCorrect()
        {
            Assert.That(ValueMultiplicity.VM_3_3N.Min, Is.EqualTo(3));
            Assert.That(ValueMultiplicity.VM_3_3N.IsUnlimited, Is.True);
            Assert.That(ValueMultiplicity.VM_3_3N.IsValid(3), Is.True);
            Assert.That(ValueMultiplicity.VM_3_3N.IsValid(6), Is.True);
            Assert.That(ValueMultiplicity.VM_3_3N.IsValid(2), Is.False);
        }

        [Test]
        public void StaticInstances_AllExist()
        {
            Assert.That(ValueMultiplicity.VM_1, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_2, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_3, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_8, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_N, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_32, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_1_99, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_2, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_2_2N, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_2_N, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_3, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_3_3N, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_3_N, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_4, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_6, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_9, Is.Not.Null);
            Assert.That(ValueMultiplicity.VM_16, Is.Not.Null);
        }
    }
}
