using System;

namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM Value Multiplicity (VM) specification.
    /// </summary>
    /// <remarks>
    /// Value Multiplicity defines how many values are allowed for a DICOM element.
    /// Examples: 1 (single value), 1-n (one or more), 2-2n (pairs), 3 (exactly three), etc.
    /// </remarks>
    public readonly struct ValueMultiplicity : IEquatable<ValueMultiplicity>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValueMultiplicity"/> struct.
        /// </summary>
        /// <param name="min">The minimum number of values allowed.</param>
        /// <param name="max">The maximum number of values allowed (ushort.MaxValue for unlimited).</param>
        public ValueMultiplicity(ushort min, ushort max)
        {
            if (min > max && max != ushort.MaxValue)
                throw new ArgumentException("Minimum cannot be greater than maximum");

            Min = min;
            Max = max;
        }

        /// <summary>
        /// Gets the minimum number of values allowed.
        /// </summary>
        public ushort Min { get; }

        /// <summary>
        /// Gets the maximum number of values allowed.
        /// </summary>
        /// <remarks>
        /// ushort.MaxValue (65535) represents unlimited (n).
        /// </remarks>
        public ushort Max { get; }

        /// <summary>
        /// Gets a value indicating whether this VM allows unlimited values.
        /// </summary>
        public bool IsUnlimited => Max == ushort.MaxValue;

        /// <summary>
        /// Determines whether the specified value count is valid for this VM.
        /// </summary>
        /// <param name="count">The number of values to validate.</param>
        /// <returns>true if the count is within the valid range; otherwise, false.</returns>
        public bool IsValid(int count)
        {
            if (count < 0)
                return false;

            if (count < Min)
                return false;

            if (!IsUnlimited && count > Max)
                return false;

            return true;
        }

        /// <summary>
        /// Determines whether this VM equals another VM.
        /// </summary>
        /// <param name="other">The VM to compare with.</param>
        /// <returns>true if the VMs are equal; otherwise, false.</returns>
        public bool Equals(ValueMultiplicity other) => Min == other.Min && Max == other.Max;

        /// <summary>
        /// Determines whether this VM equals another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the object is a ValueMultiplicity and equals this VM; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is ValueMultiplicity other && Equals(other);

        /// <summary>
        /// Returns the hash code for this VM.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            unchecked
            {
                return (Min * 397) ^ Max;
            }
#else
            return HashCode.Combine(Min, Max);
#endif
        }

        /// <summary>
        /// Returns a string representation of this VM.
        /// </summary>
        /// <returns>A string in the format "min-max" or "min-n" for unlimited.</returns>
        public override string ToString()
        {
            if (Min == Max)
                return Min.ToString();

            return IsUnlimited ? $"{Min}-n" : $"{Min}-{Max}";
        }

        /// <summary>
        /// Determines whether two VMs are equal.
        /// </summary>
        /// <param name="left">The first VM.</param>
        /// <param name="right">The second VM.</param>
        /// <returns>true if the VMs are equal; otherwise, false.</returns>
        public static bool operator ==(ValueMultiplicity left, ValueMultiplicity right) => left.Equals(right);

        /// <summary>
        /// Determines whether two VMs are not equal.
        /// </summary>
        /// <param name="left">The first VM.</param>
        /// <param name="right">The second VM.</param>
        /// <returns>true if the VMs are not equal; otherwise, false.</returns>
        public static bool operator !=(ValueMultiplicity left, ValueMultiplicity right) => !left.Equals(right);

        /// <summary>
        /// Parses a VM string from the DICOM standard (e.g., "1", "1-n", "2-2n", "1-3").
        /// </summary>
        /// <param name="vm">The VM string to parse.</param>
        /// <returns>A ValueMultiplicity representing the parsed VM.</returns>
        public static ValueMultiplicity Parse(string vm)
        {
            if (string.IsNullOrWhiteSpace(vm))
                return VM_1;

            vm = vm.Trim().ToLowerInvariant();

            // Handle "n" or "1-n" patterns
            if (vm == "n")
                return VM_1_N;

            // Handle dash-separated patterns
            var dashIndex = vm.IndexOf('-');
            if (dashIndex < 0)
            {
                // Single value like "1", "2", "3"
                if (ushort.TryParse(vm, out var single))
                    return new ValueMultiplicity(single, single);
                return VM_1;
            }

            // Parse min
            var minStr = vm.Substring(0, dashIndex);
            if (!ushort.TryParse(minStr, out var min))
                min = 1;

            // Parse max
            var maxStr = vm.Substring(dashIndex + 1);
            if (maxStr == "n" || maxStr.EndsWith("n"))
                return new ValueMultiplicity(min, ushort.MaxValue);

            if (ushort.TryParse(maxStr, out var max))
                return new ValueMultiplicity(min, max);

            return new ValueMultiplicity(min, ushort.MaxValue);
        }

        // Common VM patterns
        /// <summary>VM 1 - Single value required.</summary>
        public static readonly ValueMultiplicity VM_1 = new(1, 1);

        /// <summary>VM 1-2 - One or two values.</summary>
        public static readonly ValueMultiplicity VM_1_2 = new(1, 2);

        /// <summary>VM 1-3 - One to three values.</summary>
        public static readonly ValueMultiplicity VM_1_3 = new(1, 3);

        /// <summary>VM 1-8 - One to eight values.</summary>
        public static readonly ValueMultiplicity VM_1_8 = new(1, 8);

        /// <summary>VM 1-n - One or more values (unlimited).</summary>
        public static readonly ValueMultiplicity VM_1_N = new(1, ushort.MaxValue);

        /// <summary>VM 1-32 - One to thirty-two values.</summary>
        public static readonly ValueMultiplicity VM_1_32 = new(1, 32);

        /// <summary>VM 1-99 - One to ninety-nine values.</summary>
        public static readonly ValueMultiplicity VM_1_99 = new(1, 99);

        /// <summary>VM 2 - Exactly two values.</summary>
        public static readonly ValueMultiplicity VM_2 = new(2, 2);

        /// <summary>VM 2-2n - Pairs of values (even count).</summary>
        public static readonly ValueMultiplicity VM_2_2N = new(2, ushort.MaxValue);

        /// <summary>VM 2-n - Two or more values.</summary>
        public static readonly ValueMultiplicity VM_2_N = new(2, ushort.MaxValue);

        /// <summary>VM 3 - Exactly three values.</summary>
        public static readonly ValueMultiplicity VM_3 = new(3, 3);

        /// <summary>VM 3-3n - Triplets of values (multiple of 3).</summary>
        public static readonly ValueMultiplicity VM_3_3N = new(3, ushort.MaxValue);

        /// <summary>VM 3-n - Three or more values.</summary>
        public static readonly ValueMultiplicity VM_3_N = new(3, ushort.MaxValue);

        /// <summary>VM 4 - Exactly four values.</summary>
        public static readonly ValueMultiplicity VM_4 = new(4, 4);

        /// <summary>VM 6 - Exactly six values.</summary>
        public static readonly ValueMultiplicity VM_6 = new(6, 6);

        /// <summary>VM 9 - Exactly nine values.</summary>
        public static readonly ValueMultiplicity VM_9 = new(9, 9);

        /// <summary>VM 16 - Exactly sixteen values.</summary>
        public static readonly ValueMultiplicity VM_16 = new(16, 16);
    }
}
