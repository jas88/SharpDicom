using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Tests for NativeCodecs initialization and feature detection.
    /// </summary>
    [TestFixture]
    public class NativeCodecsTests
    {
        private bool _nativeAvailable;

        [OneTimeSetUp]
        public void Setup()
        {
            // Reset codec registry before tests
            CodecRegistry.Reset();

            // Attempt to initialize native codecs
            try
            {
                NativeCodecs.Initialize();
                _nativeAvailable = NativeCodecs.IsAvailable;
            }
            catch (NativeCodecException)
            {
                _nativeAvailable = false;
            }
        }

        [Test]
        [Category("Native")]
        public void IsAvailable_AfterInitialize_ReturnsCorrectState()
        {
            if (!_nativeAvailable)
            {
                Assert.Ignore("Native library not available on this system");
            }

            Assert.That(NativeCodecs.IsAvailable, Is.True);
        }

        [Test]
        [Category("Native")]
        public void SimdFeatures_WhenAvailable_ReturnsDetectedFeatures()
        {
            if (!_nativeAvailable)
            {
                Assert.Ignore("Native library not available on this system");
            }

            // At minimum, SSE2 (x86/x64) or NEON (ARM) should be detected on modern hardware
            var features = NativeCodecs.ActiveSimdFeatures;
            Assert.That(features, Is.Not.EqualTo(SimdFeatures.None),
                "Expected at least one SIMD feature to be detected on modern hardware");
        }

        [Test]
        [Category("Native")]
        public void Initialize_CalledTwice_DoesNotThrow()
        {
            if (!_nativeAvailable)
            {
                Assert.Ignore("Native library not available on this system");
            }

            // NativeCodecs.Initialize() is idempotent - calling it again should not throw
            Assert.DoesNotThrow(() => NativeCodecs.Initialize());
        }

        [Test]
        public void Initialize_WhenLibraryMissing_ThrowsNativeCodecException()
        {
            // This test verifies error handling when native library is not present
            // Reset to test fresh initialization path
            NativeCodecs.Reset();

            try
            {
                NativeCodecs.Initialize();
                // If we get here, native library is available
                Assert.Pass("Native library is available - error handling test not applicable");
            }
            catch (NativeCodecException ex)
            {
                // Expected when native library is not present
                Assert.That(ex.Message, Does.Contain("Native library").IgnoreCase.Or.Contain("not found").IgnoreCase);
                Assert.That(ex.InnerException, Is.InstanceOf<DllNotFoundException>().Or.Null);
            }
        }

        [Test]
        public void DisableAutoInit_AppContextSwitch_CanBeSet()
        {
            // Test that the AppContext switch can be set without error
            // This is more of a documentation test - the actual effect is at module load time
            AppContext.SetSwitch("SharpDicom.Codecs.DisableAutoInit", true);
            try
            {
                Assert.That(AppContext.TryGetSwitch("SharpDicom.Codecs.DisableAutoInit", out var disabled), Is.True);
                Assert.That(disabled, Is.True);
            }
            finally
            {
                // Reset to default
                AppContext.SetSwitch("SharpDicom.Codecs.DisableAutoInit", false);
            }
        }

        [Test]
        [Category("Native")]
        public void AvailableFeatures_WhenAvailable_ReturnsCodecSupport()
        {
            if (!_nativeAvailable)
            {
                Assert.Ignore("Native library not available on this system");
            }

            var features = NativeCodecs.AvailableFeatures;

            // The native library should support at least one codec type
            Assert.That(features, Is.Not.EqualTo(CodecFeatures.None),
                "Expected native library to support at least one codec");
        }

        [Test]
        [Category("Native")]
        public void PreferCpu_CanBeToggled()
        {
            // Test that PreferCpu property can be read and written
            var original = NativeCodecs.PreferCpu;
            try
            {
                NativeCodecs.PreferCpu = true;
                Assert.That(NativeCodecs.PreferCpu, Is.True);

                NativeCodecs.PreferCpu = false;
                Assert.That(NativeCodecs.PreferCpu, Is.False);
            }
            finally
            {
                NativeCodecs.PreferCpu = original;
            }
        }

        [Test]
        [Category("Native")]
        public void EnableJpeg_DefaultsToTrue()
        {
            Assert.That(NativeCodecs.EnableJpeg, Is.True);
        }

        [Test]
        [Category("Native")]
        public void EnableJpeg2000_DefaultsToTrue()
        {
            Assert.That(NativeCodecs.EnableJpeg2000, Is.True);
        }

        [Test]
        [Category("Native")]
        public void EnableJpegLs_DefaultsToTrue()
        {
            Assert.That(NativeCodecs.EnableJpegLs, Is.True);
        }

        [Test]
        [Category("Native")]
        public void EnableGpu_DefaultsToTrue()
        {
            Assert.That(NativeCodecs.EnableGpu, Is.True);
        }

        [Test]
        [Category("Native")]
        public void GpuAvailable_ReturnsBoolean()
        {
            // Just verify this property doesn't throw
            var gpuAvailable = NativeCodecs.GpuAvailable;
            Assert.That(gpuAvailable, Is.TypeOf<bool>());
        }

        [Test]
        public void ExpectedVersion_IsPositive()
        {
            Assert.That(NativeCodecs.ExpectedVersion, Is.GreaterThan(0));
        }

        [Test]
        public void Reset_ClearsInitializationState()
        {
            // Reset should clear the initialization state
            NativeCodecs.Reset();

            // After reset, IsAvailable should be false
            Assert.That(NativeCodecs.IsAvailable, Is.False);
            Assert.That(NativeCodecs.GpuAvailable, Is.False);
            Assert.That(NativeCodecs.ActiveSimdFeatures, Is.EqualTo(SimdFeatures.None));
            Assert.That(NativeCodecs.AvailableFeatures, Is.EqualTo(CodecFeatures.None));
        }
    }
}
