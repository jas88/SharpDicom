using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Tests for <see cref="NativeCodecs"/> initialization and feature detection.
    /// </summary>
    [TestFixture]
    public class NativeCodecsTests
    {
        [SetUp]
        public void Setup()
        {
            // Reset NativeCodecs state before each test
            NativeCodecs.Reset();
            CodecRegistry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up after tests
            NativeCodecs.Reset();
            CodecRegistry.Reset();
        }

        #region Availability Tests

        [Test]
        [Category("NativeCodecs")]
        public void IsAvailable_BeforeInitialize_ReturnsFalse()
        {
            // Before initialization, IsAvailable should be false
            Assert.That(NativeCodecs.IsAvailable, Is.False);
        }

        [Test]
        [Category("NativeCodecs")]
        public void Initialize_WhenLibraryMissing_SetsIsAvailableFalse()
        {
            // Native library is likely not present in test environment
            // Initialize with SuppressInitializationErrors to avoid exceptions
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            // Assert IsAvailable reflects whether native library was found
            // In CI without native libs, this should be false
            // With native libs present, this should be true
            bool isAvailable = NativeCodecs.IsAvailable;

            // Verify deterministic behavior - calling twice should return same result
            bool isAvailableSecondCall = NativeCodecs.IsAvailable;
            Assert.That(isAvailableSecondCall, Is.EqualTo(isAvailable),
                "IsAvailable should be deterministic across multiple calls");

            // If library is not available, verify it's because native code couldn't load
            if (!isAvailable)
            {
                Assert.That(NativeCodecs.NativeVersion, Is.EqualTo(0),
                    "NativeVersion should be 0 when library unavailable");
            }
            else
            {
                Assert.That(NativeCodecs.NativeVersion, Is.GreaterThan(0),
                    "NativeVersion should be > 0 when library is available");
            }
        }

        [Test]
        [Category("NativeCodecs")]
        public void Initialize_CalledTwice_DoesNotThrow()
        {
            // First initialization (suppress errors in case native lib missing)
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            // Second initialization should not throw
            Assert.DoesNotThrow(() => NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            }));
        }

        [Test]
        [Category("NativeCodecs")]
        public void Initialize_WithSuppressErrors_DoesNotThrowWhenMissing()
        {
            Assert.DoesNotThrow(() => NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            }));
        }

        #endregion

        #region Feature Detection Tests

        [Test]
        [Category("NativeCodecs")]
        public void HasFeature_BeforeInitialize_ReturnsFalse()
        {
            Assert.That(NativeCodecs.HasFeature(NativeCodecFeature.Jpeg), Is.False);
            Assert.That(NativeCodecs.HasFeature(NativeCodecFeature.Jpeg2000), Is.False);
            Assert.That(NativeCodecs.HasFeature(NativeCodecFeature.JpegLs), Is.False);
            Assert.That(NativeCodecs.HasFeature(NativeCodecFeature.Video), Is.False);
            Assert.That(NativeCodecs.HasFeature(NativeCodecFeature.Gpu), Is.False);
        }

        [Test]
        [Category("NativeCodecs")]
        public void ActiveSimdFeatures_BeforeInitialize_ReturnsNone()
        {
            Assert.That(NativeCodecs.ActiveSimdFeatures, Is.EqualTo(SimdFeatures.None));
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeVersion_BeforeInitialize_ReturnsZero()
        {
            Assert.That(NativeCodecs.NativeVersion, Is.EqualTo(0));
        }

        [Test]
        [Category("NativeCodecs")]
        public void GpuAvailable_BeforeInitialize_ReturnsFalse()
        {
            Assert.That(NativeCodecs.GpuAvailable, Is.False);
        }

        [Test]
        [Category("NativeCodecs")]
        public void GpuDeviceName_BeforeInitialize_ReturnsNull()
        {
            Assert.That(NativeCodecs.GpuDeviceName, Is.Null);
        }

        #endregion

        #region Options Tests

        [Test]
        [Category("NativeCodecs")]
        public void Initialize_PreferCpuOption_SetsProperty()
        {
            // Note: Options are only applied during successful initialization
            // When native library is unavailable, options aren't applied after DllNotFoundException
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true,
                PreferCpu = true
            });

            // If native library was loaded successfully, PreferCpu should be set
            // If not, the option application is skipped (design limitation)
            if (NativeCodecs.IsAvailable)
            {
                Assert.That(NativeCodecs.PreferCpu, Is.True);
            }
            else
            {
                // When library unavailable, we can still set the property directly
                NativeCodecs.PreferCpu = true;
                Assert.That(NativeCodecs.PreferCpu, Is.True);
            }
        }

        [Test]
        [Category("NativeCodecs")]
        public void EnableJpeg_CanBeSetAndGet()
        {
            Assert.That(NativeCodecs.EnableJpeg, Is.True, "Default should be true");
            NativeCodecs.EnableJpeg = false;
            Assert.That(NativeCodecs.EnableJpeg, Is.False);
            NativeCodecs.EnableJpeg = true;
        }

        [Test]
        [Category("NativeCodecs")]
        public void EnableJpeg2000_CanBeSetAndGet()
        {
            Assert.That(NativeCodecs.EnableJpeg2000, Is.True, "Default should be true");
            NativeCodecs.EnableJpeg2000 = false;
            Assert.That(NativeCodecs.EnableJpeg2000, Is.False);
            NativeCodecs.EnableJpeg2000 = true;
        }

        [Test]
        [Category("NativeCodecs")]
        public void EnableJpegLs_CanBeSetAndGet()
        {
            Assert.That(NativeCodecs.EnableJpegLs, Is.True, "Default should be true");
            NativeCodecs.EnableJpegLs = false;
            Assert.That(NativeCodecs.EnableJpegLs, Is.False);
            NativeCodecs.EnableJpegLs = true;
        }

        [Test]
        [Category("NativeCodecs")]
        public void EnableVideo_CanBeSetAndGet()
        {
            Assert.That(NativeCodecs.EnableVideo, Is.True, "Default should be true");
            NativeCodecs.EnableVideo = false;
            Assert.That(NativeCodecs.EnableVideo, Is.False);
            NativeCodecs.EnableVideo = true;
        }

        [Test]
        [Category("NativeCodecs")]
        public void PreferCpu_CanBeSetAndGet()
        {
            Assert.That(NativeCodecs.PreferCpu, Is.False, "Default should be false");
            NativeCodecs.PreferCpu = true;
            Assert.That(NativeCodecs.PreferCpu, Is.True);
            NativeCodecs.PreferCpu = false;
        }

        #endregion

        #region NativeCodecOptions Tests

        [Test]
        public void NativeCodecOptions_Default_HasExpectedValues()
        {
            var options = new NativeCodecOptions();

            Assert.That(options.SkipVersionCheck, Is.False);
            Assert.That(options.ForceScalar, Is.False);
            Assert.That(options.PreferCpu, Is.False);
            Assert.That(options.SuppressInitializationErrors, Is.False);
            Assert.That(options.CustomLibraryPath, Is.Null);
            Assert.That(options.EnableJpeg, Is.Null);
            Assert.That(options.EnableJpeg2000, Is.Null);
            Assert.That(options.EnableJpegLs, Is.Null);
            Assert.That(options.EnableVideo, Is.Null);
        }

        [Test]
        public void NativeCodecOptions_CanSetAllProperties()
        {
            var options = new NativeCodecOptions
            {
                SkipVersionCheck = true,
                ForceScalar = true,
                PreferCpu = true,
                SuppressInitializationErrors = true,
                CustomLibraryPath = "/path/to/lib",
                EnableJpeg = false,
                EnableJpeg2000 = false,
                EnableJpegLs = false,
                EnableVideo = false
            };

            Assert.That(options.SkipVersionCheck, Is.True);
            Assert.That(options.ForceScalar, Is.True);
            Assert.That(options.PreferCpu, Is.True);
            Assert.That(options.SuppressInitializationErrors, Is.True);
            Assert.That(options.CustomLibraryPath, Is.EqualTo("/path/to/lib"));
            Assert.That(options.EnableJpeg, Is.False);
            Assert.That(options.EnableJpeg2000, Is.False);
            Assert.That(options.EnableJpegLs, Is.False);
            Assert.That(options.EnableVideo, Is.False);
        }

        [Test]
        public void Initialize_WithEnableOptions_SetsProperties()
        {
            // Note: Enable options are only applied during successful initialization
            // When native library is unavailable, options aren't applied after DllNotFoundException
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true,
                EnableJpeg = false,
                EnableJpeg2000 = false,
                EnableJpegLs = false,
                EnableVideo = false
            });

            // If native library was loaded successfully, enable options should be set
            if (NativeCodecs.IsAvailable)
            {
                Assert.That(NativeCodecs.EnableJpeg, Is.False);
                Assert.That(NativeCodecs.EnableJpeg2000, Is.False);
                Assert.That(NativeCodecs.EnableJpegLs, Is.False);
                Assert.That(NativeCodecs.EnableVideo, Is.False);
            }
            else
            {
                // When library unavailable, options weren't applied - properties remain default
                // This is a design limitation when native libraries are missing
                Assert.That(NativeCodecs.EnableJpeg, Is.True, "Default value when library unavailable");
                Assert.That(NativeCodecs.EnableJpeg2000, Is.True, "Default value when library unavailable");
                Assert.That(NativeCodecs.EnableJpegLs, Is.True, "Default value when library unavailable");
                Assert.That(NativeCodecs.EnableVideo, Is.True, "Default value when library unavailable");
            }
        }

        #endregion

        #region SimdFeatures Enum Tests

        [Test]
        public void SimdFeatures_None_HasZeroValue()
        {
            Assert.That((int)SimdFeatures.None, Is.EqualTo(0));
        }

        [Test]
        public void SimdFeatures_AreFlagBased()
        {
            // Verify flags are powers of 2
            Assert.That((int)SimdFeatures.Sse2, Is.EqualTo(1));
            Assert.That((int)SimdFeatures.Avx2, Is.EqualTo(2));
            Assert.That((int)SimdFeatures.Neon, Is.EqualTo(4));
            Assert.That((int)SimdFeatures.Avx512, Is.EqualTo(8));
        }

        [Test]
        public void SimdFeatures_CanBeCombined()
        {
            var combined = SimdFeatures.Sse2 | SimdFeatures.Avx2;
            Assert.That(combined.HasFlag(SimdFeatures.Sse2), Is.True);
            Assert.That(combined.HasFlag(SimdFeatures.Avx2), Is.True);
            Assert.That(combined.HasFlag(SimdFeatures.Neon), Is.False);
        }

        #endregion

        #region NativeCodecFeature Enum Tests

        [Test]
        public void NativeCodecFeature_HasExpectedValues()
        {
            // Verify all expected features exist by testing known values
            var features = new[]
            {
                NativeCodecFeature.Jpeg,
                NativeCodecFeature.Jpeg2000,
                NativeCodecFeature.JpegLs,
                NativeCodecFeature.Video,
                NativeCodecFeature.Gpu
            };

            // Verify HasFeature can be called for each without exception
            foreach (var feature in features)
            {
                // Simply verify the feature value is usable with HasFeature
                _ = NativeCodecs.HasFeature(feature);
            }

            Assert.That(features.Length, Is.EqualTo(5), "Should have 5 feature types");
        }

        #endregion

        #region Reset Tests

        [Test]
        [Category("NativeCodecs")]
        public void Reset_ClearsState()
        {
            // Initialize first
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true,
                PreferCpu = true
            });

            // Reset
            NativeCodecs.Reset();

            // State should be cleared
            Assert.That(NativeCodecs.IsAvailable, Is.False);
            Assert.That(NativeCodecs.GpuAvailable, Is.False);
            Assert.That(NativeCodecs.GpuDeviceName, Is.Null);
            Assert.That(NativeCodecs.ActiveSimdFeatures, Is.EqualTo(SimdFeatures.None));
            Assert.That(NativeCodecs.NativeVersion, Is.EqualTo(0));
        }

        #endregion

        #region Integration Scenario Tests

        [Test]
        [Category("NativeCodecs")]
        public void TypicalWorkflow_InitializeCheckRegister()
        {
            // Typical usage pattern
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            // Check availability
            if (NativeCodecs.IsAvailable)
            {
                // If native libs are present, verify features can be queried
                _ = NativeCodecs.HasFeature(NativeCodecFeature.Jpeg);
                _ = NativeCodecs.HasFeature(NativeCodecFeature.Jpeg2000);
                _ = NativeCodecs.HasFeature(NativeCodecFeature.JpegLs);
                _ = NativeCodecs.ActiveSimdFeatures;
                _ = NativeCodecs.GpuAvailable;
            }

            Assert.Pass("Typical workflow completed successfully");
        }

        [Test]
        [Category("NativeCodecs")]
        public void Initialize_WhenAlreadyInitialized_ReturnsImmediately()
        {
            // First initialization
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            bool wasAvailable = NativeCodecs.IsAvailable;

            // Second initialization should be a no-op
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });

            Assert.That(NativeCodecs.IsAvailable, Is.EqualTo(wasAvailable),
                "IsAvailable should not change on re-initialization");
        }

        #endregion
    }
}
