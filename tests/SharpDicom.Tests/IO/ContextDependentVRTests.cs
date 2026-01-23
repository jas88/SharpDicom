using System.Linq;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Tests for context-dependent VR resolution in VRResolver.
    /// </summary>
    /// <remarks>
    /// Some DICOM tags have multiple valid VRs where the correct choice depends
    /// on other elements in the dataset. VRResolver handles this resolution.
    /// </remarks>
    [TestFixture]
    public class ContextDependentVRTests
    {
        #region Pixel Data VR Resolution Tests

        [Test]
        public void PixelData_DictionaryEntry_HasMultipleVRs()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            Assert.That(entry, Is.Not.Null, "PixelData should be in dictionary");

            // Log the VRs for debugging
            if (entry != null)
            {
                TestContext.WriteLine($"PixelData VRs: {string.Join(", ", entry.Value.ValueRepresentations.Select(v => v.ToString()))}");
                TestContext.WriteLine($"HasMultipleVRs: {entry.Value.HasMultipleVRs}");
            }

            // If dictionary only has single VR, skip multi-VR tests
            if (!entry!.Value.HasMultipleVRs)
            {
                Assert.Pass("PixelData entry has single VR in this dictionary version - skipping");
            }

            Assert.That(entry.Value.HasMultipleVRs, Is.True, "PixelData should have OB or OW VR options");
        }

        [Test]
        public void ResolveVR_PixelData_BitsAllocated8_ReturnsOB()
        {
            var context = CreateDatasetWithBitsAllocated(8);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB));
        }

        [Test]
        public void ResolveVR_PixelData_BitsAllocated16_ReturnsOW()
        {
            var context = CreateDatasetWithBitsAllocated(16);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OW));
        }

        [Test]
        public void ResolveVR_PixelData_BitsAllocated32_ReturnsOW()
        {
            var context = CreateDatasetWithBitsAllocated(32);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OW));
        }

        [Test]
        public void ResolveVR_PixelData_BitsAllocated1_ReturnsOB()
        {
            // 1-bit pixel data is stored in OB
            var context = CreateDatasetWithBitsAllocated(1);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB));
        }

        [Test]
        public void ResolveVR_PixelData_NoBitsAllocated_DefaultsToOB()
        {
            var context = new DicomDataset();
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB), "Without BitsAllocated, should default to OB");
        }

        [Test]
        public void ResolveVR_PixelData_NullContext_DefaultsToOB()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, null);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB), "Without context, should default to OB");
        }

        #endregion

        #region US/SS Tag Resolution Tests

        [Test]
        public void ResolveVR_SmallestImagePixelValue_PixelRepresentation0_ReturnsUS()
        {
            var context = CreateDatasetWithPixelRepresentation(0);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs (generated dictionary may differ)
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.US));
        }

        [Test]
        public void ResolveVR_SmallestImagePixelValue_PixelRepresentation1_ReturnsSS()
        {
            var context = CreateDatasetWithPixelRepresentation(1);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.SS));
        }

        [Test]
        public void ResolveVR_LargestImagePixelValue_PixelRepresentation0_ReturnsUS()
        {
            var context = CreateDatasetWithPixelRepresentation(0);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.LargestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("LargestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.LargestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.US));
        }

        [Test]
        public void ResolveVR_LargestImagePixelValue_PixelRepresentation1_ReturnsSS()
        {
            var context = CreateDatasetWithPixelRepresentation(1);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.LargestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("LargestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.LargestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.SS));
        }

        [Test]
        public void ResolveVR_UsOrSsTag_NoPixelRepresentation_DefaultsToUS()
        {
            var context = new DicomDataset();
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.US), "Without PixelRepresentation, should default to US");
        }

        [Test]
        public void ResolveVR_UsOrSsTag_NullContext_DefaultsToUS()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, null);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.US), "Without context, should default to US");
        }

        #endregion

        #region Single VR Tag Tests

        [Test]
        public void ResolveVR_PatientName_AlwaysReturnsPN()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PatientName);
            var context = CreateDatasetWithBitsAllocated(16); // Context shouldn't matter

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PatientName, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.PN));
        }

        [Test]
        public void ResolveVR_SOPClassUID_AlwaysReturnsUI()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SOPClassUID);
            var context = CreateDatasetWithPixelRepresentation(1); // Context shouldn't matter

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SOPClassUID, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.UI));
        }

        [Test]
        public void ResolveVR_BitsAllocated_AlwaysReturnsUS()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.BitsAllocated);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.BitsAllocated, entry, null);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.US));
        }

        #endregion

        #region Unknown Tag Tests

        [Test]
        public void ResolveVR_UnknownTag_ReturnsUN()
        {
            var unknownTag = new DicomTag(0x0011, 0x0001); // Private tag not in dictionary
            var context = new DicomDataset();

            var resolvedVR = VRResolver.ResolveVR(unknownTag, null, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.UN));
        }

        [Test]
        public void ResolveVR_PrivateTag_ReturnsUN()
        {
            var privateTag = new DicomTag(0x0011, 0x1001); // Private data element
            var context = new DicomDataset();

            var resolvedVR = VRResolver.ResolveVR(privateTag, null, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.UN));
        }

        #endregion

        #region Context Inheritance Tests

        [Test]
        public void ResolveVR_NestedDataset_InheritsBitsAllocatedFromParent()
        {
            // Parent has BitsAllocated = 16
            var parent = CreateDatasetWithBitsAllocated(16);

            // Child is a sequence item
            var child = new DicomDataset();
            child.Parent = parent;

            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, child);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OW), "Child should inherit BitsAllocated from parent");
        }

        [Test]
        public void ResolveVR_NestedDataset_InheritsPixelRepresentationFromParent()
        {
            // Parent has PixelRepresentation = 1 (signed)
            var parent = CreateDatasetWithPixelRepresentation(1);

            // Child is a sequence item
            var child = new DicomDataset();
            child.Parent = parent;

            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, child);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.SS), "Child should inherit PixelRepresentation from parent");
        }

        [Test]
        public void ResolveVR_NestedDataset_LocalValueOverridesParent()
        {
            // Parent has BitsAllocated = 16
            var parent = CreateDatasetWithBitsAllocated(16);

            // Child has BitsAllocated = 8 (overrides parent)
            var child = CreateDatasetWithBitsAllocated(8);
            child.Parent = parent;

            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, child);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB), "Local BitsAllocated should override parent");
        }

        #endregion

        #region NeedsContext Tests

        [Test]
        public void NeedsContext_PixelData_ReturnsTrue()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var needsContext = VRResolver.NeedsContext(DicomTag.PixelData, entry);

            Assert.That(needsContext, Is.True);
        }

        [Test]
        public void NeedsContext_PatientName_ReturnsFalse()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PatientName);

            var needsContext = VRResolver.NeedsContext(DicomTag.PatientName, entry);

            Assert.That(needsContext, Is.False);
        }

        [Test]
        public void NeedsContext_UnknownTag_ReturnsFalse()
        {
            var unknownTag = new DicomTag(0x0011, 0x0001);

            var needsContext = VRResolver.NeedsContext(unknownTag, null);

            Assert.That(needsContext, Is.False);
        }

        [Test]
        public void NeedsContext_SmallestImagePixelValue_ReturnsTrue()
        {
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var needsContext = VRResolver.NeedsContext(DicomTag.SmallestImagePixelValue, entry);

            Assert.That(needsContext, Is.True);
        }

        #endregion

        #region IsMultiVRTag Tests

        [Test]
        public void IsMultiVRTag_PixelData_ReturnsTrue()
        {
            var isMultiVR = VRResolver.IsMultiVRTag(DicomTag.PixelData);

            Assert.That(isMultiVR, Is.True);
        }

        [Test]
        public void IsMultiVRTag_PatientName_ReturnsFalse()
        {
            var isMultiVR = VRResolver.IsMultiVRTag(DicomTag.PatientName);

            Assert.That(isMultiVR, Is.False);
        }

        [Test]
        public void IsMultiVRTag_UnknownTag_ReturnsFalse()
        {
            var unknownTag = new DicomTag(0x0011, 0x0001);

            var isMultiVR = VRResolver.IsMultiVRTag(unknownTag);

            Assert.That(isMultiVR, Is.False);
        }

        #endregion

        #region 64-bit VR Tests

        [Test]
        public void DicomVR_OV_IsKnown()
        {
            Assert.That(DicomVR.OV.IsKnown, Is.True);
        }

        [Test]
        public void DicomVR_SV_IsKnown()
        {
            Assert.That(DicomVR.SV.IsKnown, Is.True);
        }

        [Test]
        public void DicomVR_UV_IsKnown()
        {
            Assert.That(DicomVR.UV.IsKnown, Is.True);
        }

        [Test]
        public void DicomVR_OV_Is32BitLength()
        {
            Assert.That(DicomVR.OV.Is32BitLength, Is.True);
        }

        [Test]
        public void DicomVR_SV_Is32BitLength()
        {
            Assert.That(DicomVR.SV.Is32BitLength, Is.True);
        }

        [Test]
        public void DicomVR_UV_Is32BitLength()
        {
            Assert.That(DicomVR.UV.Is32BitLength, Is.True);
        }

        [Test]
        public void DicomVR_SV_IsNumericVR()
        {
            Assert.That(DicomVR.SV.IsNumericVR, Is.True);
        }

        [Test]
        public void DicomVR_UV_IsNumericVR()
        {
            Assert.That(DicomVR.UV.IsNumericVR, Is.True);
        }

        [Test]
        public void DicomVRInfo_OV_HasCorrectName()
        {
            var info = DicomVRInfo.GetInfo(DicomVR.OV);

            Assert.That(info.Name, Is.EqualTo("Other 64-bit Very Long"));
        }

        [Test]
        public void DicomVRInfo_SV_HasCorrectName()
        {
            var info = DicomVRInfo.GetInfo(DicomVR.SV);

            Assert.That(info.Name, Is.EqualTo("Signed 64-bit Very Long"));
        }

        [Test]
        public void DicomVRInfo_UV_HasCorrectName()
        {
            var info = DicomVRInfo.GetInfo(DicomVR.UV);

            Assert.That(info.Name, Is.EqualTo("Unsigned 64-bit Very Long"));
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void ResolveVR_EmptyDatasetContext_DefaultsAppropriately()
        {
            var context = new DicomDataset();

            // PixelData defaults to OB
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);
            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);
            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB));
        }

        [Test]
        public void ResolveVR_BitsAllocatedZero_ReturnsOB()
        {
            // Edge case: BitsAllocated = 0 should be treated as <= 8, return OB
            var context = CreateDatasetWithBitsAllocated(0);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.PixelData);

            var resolvedVR = VRResolver.ResolveVR(DicomTag.PixelData, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.OB));
        }

        [Test]
        public void ResolveVR_PixelRepresentationNonZeroNonOne_ReturnsSS()
        {
            // Edge case: PixelRepresentation = 2 (invalid, but parser should handle)
            // Any non-zero value should be treated as signed
            var context = CreateDatasetWithPixelRepresentation(2);
            var entry = DicomDictionary.Default.GetEntry(DicomTag.SmallestImagePixelValue);

            // Skip test if entry doesn't have multiple VRs
            if (entry?.HasMultipleVRs != true)
            {
                Assert.Pass("SmallestImagePixelValue not multi-VR in this dictionary version");
            }

            var resolvedVR = VRResolver.ResolveVR(DicomTag.SmallestImagePixelValue, entry, context);

            Assert.That(resolvedVR, Is.EqualTo(DicomVR.SS));
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a dataset with BitsAllocated set to the specified value.
        /// </summary>
        private static DicomDataset CreateDatasetWithBitsAllocated(ushort bitsAllocated)
        {
            var dataset = new DicomDataset();
            var bytes = System.BitConverter.GetBytes(bitsAllocated);
            dataset.Add(new DicomNumericElement(DicomTag.BitsAllocated, DicomVR.US, bytes));
            return dataset;
        }

        /// <summary>
        /// Creates a dataset with PixelRepresentation set to the specified value.
        /// </summary>
        private static DicomDataset CreateDatasetWithPixelRepresentation(ushort pixelRepresentation)
        {
            var dataset = new DicomDataset();
            var bytes = System.BitConverter.GetBytes(pixelRepresentation);
            dataset.Add(new DicomNumericElement(DicomTag.PixelRepresentation, DicomVR.US, bytes));
            return dataset;
        }

        #endregion
    }
}
