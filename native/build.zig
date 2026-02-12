const std = @import("std");

/// SharpDicom native codecs build script
/// Cross-compiles to 6 target platforms: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
///
/// Vendor libraries:
/// - libjpeg-turbo: vendor/libjpeg-turbo/src (downloaded in CI)
/// - OpenJPEG: vendor/openjpeg/src (downloaded in CI)
/// - CharLS: vendor/charls/src (downloaded in CI)
/// - FFmpeg: vendor/ffmpeg/ (downloaded in CI) - decoding and encoding
/// - x264: vendor/x264/ (downloaded in CI) - H.264 software encoder
/// - x265: vendor/x265/ (downloaded in CI) - HEVC software encoder
/// - stb_image: vendor/stb/ (downloaded in CI) - image sequence loading
pub fn build(b: *std.Build) void {
    // All vendor libraries must be present - CI downloads them before build.
    // For local development, run scripts/download-vendors.sh first.
    //
    // Verify required vendor sources exist. Build fails if any are missing.
    const have_libjpeg = detectVendorLibrary("vendor/libjpeg-turbo/src");
    const have_libjpeg12 = have_libjpeg; // 12-bit uses same source with different flags
    const have_openjpeg = detectVendorLibrary("vendor/openjpeg/src");
    const have_charls = detectVendorLibrary("vendor/charls/src");
    const have_ffmpeg = detectVendorLibrary("vendor/ffmpeg");
    const have_ffmpeg_enc = have_ffmpeg; // Encoding uses same FFmpeg build
    const have_tesseract = detectVendorLibrary("vendor/tesseract/src");
    const have_stb_image = detectVendorLibrary("vendor/stb"); // stb_image is header-only

    // Also check for encoder libraries when FFmpeg encoding is enabled
    const have_x264 = detectVendorLibrary("vendor/x264");
    const have_x265 = detectVendorLibrary("vendor/x265/source");
    const have_leptonica = detectVendorLibrary("vendor/leptonica/src");

    // Report library status at build time
    if (!have_libjpeg) {
        std.log.warn("libjpeg-turbo not found at vendor/libjpeg-turbo/src - JPEG support disabled", .{});
    }
    if (!have_openjpeg) {
        std.log.warn("OpenJPEG not found at vendor/openjpeg/src - J2K support disabled", .{});
    }
    if (!have_charls) {
        std.log.warn("CharLS not found at vendor/charls/src - JLS support disabled", .{});
    }
    if (!have_ffmpeg) {
        std.log.warn("FFmpeg not found at vendor/ffmpeg - video support disabled", .{});
    }
    if (have_ffmpeg_enc and !have_x264) {
        std.log.warn("x264 not found at vendor/x264 - H.264 encoding disabled", .{});
    }
    if (have_ffmpeg_enc and !have_x265) {
        std.log.warn("x265 not found at vendor/x265/source - HEVC encoding disabled", .{});
    }
    if (!have_tesseract) {
        std.log.warn("Tesseract not found at vendor/tesseract/src - OCR support disabled", .{});
    }
    if (have_tesseract and !have_leptonica) {
        std.log.warn("Leptonica not found at vendor/leptonica/src - Tesseract requires Leptonica", .{});
    }
    _ = have_x264;
    _ = have_x265;
    _ = have_leptonica;
    // Target configurations for all supported platforms
    // Using GNU ABI for Windows for better Zig cross-compilation support
    const targets = [_]std.Target.Query{
        // Windows x64 (GNU ABI for cross-compilation)
        .{
            .cpu_arch = .x86_64,
            .os_tag = .windows,
            .abi = .gnu,
        },
        // Windows ARM64 (GNU ABI for cross-compilation)
        .{
            .cpu_arch = .aarch64,
            .os_tag = .windows,
            .abi = .gnu,
        },
        // Linux x64 (glibc for compatibility with .NET runtime)
        .{
            .cpu_arch = .x86_64,
            .os_tag = .linux,
            .abi = .gnu,
        },
        // Linux ARM64 (glibc for compatibility with .NET runtime)
        .{
            .cpu_arch = .aarch64,
            .os_tag = .linux,
            .abi = .gnu,
        },
        // macOS x64
        .{
            .cpu_arch = .x86_64,
            .os_tag = .macos,
        },
        // macOS ARM64
        .{
            .cpu_arch = .aarch64,
            .os_tag = .macos,
        },
    };

    const optimize = b.standardOptimizeOption(.{
        .preferred_optimize_mode = .ReleaseFast,
    });

    // Build shared library for each target
    for (targets) |target_query| {
        const target = b.resolveTargetQuery(target_query);
        const lib = b.addSharedLibrary(.{
            .name = "sharpdicom_codecs",
            .target = target,
            .optimize = optimize,
            .pic = true, // Position-independent code for ASLR
            .strip = true, // Strip debug symbols for smaller binaries (all platforms)
        });

        // Link libc for cross-compilation (provides standard headers like string.h, stdlib.h)
        lib.linkLibC();

        // Build flags common to all source files
        // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
        const common_flags = &[_][]const u8{
            "-std=c11",
            "-O2", // Required for _FORTIFY_SOURCE with glibc
            "-fstack-protector-strong", // Security hardening
            "-D_FORTIFY_SOURCE=2",
            "-Wall",
            "-Wextra",
            "-Werror",
        };

        // Feature flags (only defined when corresponding library is available)
        const jpeg_flags = common_flags ++ &[_][]const u8{
            "-DSHARPDICOM_WITH_JPEG",
        };

        const jpeg12_flags = common_flags ++ &[_][]const u8{
            "-DSHARPDICOM_WITH_JPEG12",
        };

        // Note: 12-bit libjpeg-turbo symbol prefix flags are defined in addLibjpegTurbo12Sources()

        // Add C source files (core) - feature flags based on available libraries
        // Build core flags from comptime constants first
        const core_flags_0 = if (have_libjpeg) jpeg_flags else common_flags;
        const core_flags_1 = if (have_libjpeg12)
            core_flags_0 ++ &[_][]const u8{"-DSHARPDICOM_WITH_JPEG12"}
        else
            core_flags_0;
        const core_flags_base = if (have_ffmpeg_enc)
            core_flags_1 ++ &[_][]const u8{"-DSHARPDICOM_WITH_FFMPEG_ENC"}
        else
            core_flags_1;

        // Handle runtime-detected features with explicit conditionals
        // We need separate flag arrays for each combination because Zig can't
        // concatenate slices when either operand depends on a runtime value
        if (have_openjpeg and have_charls and have_stb_image) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_J2K",
                    "-DSHARPDICOM_WITH_JLS",
                    "-DSHARPDICOM_WITH_STB_IMAGE",
                },
            });
        } else if (have_openjpeg and have_charls) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_J2K",
                    "-DSHARPDICOM_WITH_JLS",
                },
            });
        } else if (have_openjpeg and have_stb_image) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_J2K",
                    "-DSHARPDICOM_WITH_STB_IMAGE",
                },
            });
        } else if (have_charls and have_stb_image) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_JLS",
                    "-DSHARPDICOM_WITH_STB_IMAGE",
                },
            });
        } else if (have_openjpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_J2K"},
            });
        } else if (have_charls) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_JLS"},
            });
        } else if (have_stb_image) {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_STB_IMAGE"},
            });
        } else {
            lib.addCSourceFile(.{
                .file = b.path("src/sharpdicom_codecs.c"),
                .flags = core_flags_base,
            });
        }

        // JPEG wrapper (libjpeg-turbo)
        if (have_libjpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg_wrapper.c"),
                .flags = jpeg_flags,
            });
            // Compile libjpeg-turbo from source for static linking
            addLibjpegTurboSources(lib, b);
        } else {
            // Build stub version (JPEG functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg_wrapper.c"),
                .flags = common_flags,
            });
        }

        // J2K wrapper (OpenJPEG)
        if (have_openjpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/j2k_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_HAS_OPENJPEG",
                    "-DSHARPDICOM_WITH_J2K",
                },
            });

            // Add OpenJPEG include path
            lib.addIncludePath(b.path("vendor/openjpeg/src/src/lib/openjp2"));

            // Add OpenJPEG source files needed for compilation
            addOpenJpegSources(lib, b, common_flags);
        } else {
            // Build stub version
            lib.addCSourceFile(.{
                .file = b.path("src/j2k_wrapper.c"),
                .flags = common_flags,
            });
        }

        // GPU wrapper (dynamically loads nvJPEG2000)
        lib.addCSourceFile(.{
            .file = b.path("src/gpu_wrapper.c"),
            .flags = common_flags,
        });

        // JLS wrapper (CharLS)
        if (have_charls) {
            lib.addCSourceFile(.{
                .file = b.path("src/jls_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_HAS_CHARLS",
                    "-DSHARPDICOM_WITH_JLS",
                },
            });
            // Add CharLS include paths
            lib.addIncludePath(b.path("vendor/charls/src/include"));
            lib.addIncludePath(b.path("vendor/charls/src/src"));
            // Compile CharLS from source (C++17)
            addCharlsSources(lib, b);
        } else {
            // Build stub version (JLS functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/jls_wrapper.c"),
                .flags = common_flags,
            });
        }

        // Video wrapper (FFmpeg decoding)
        if (have_ffmpeg) {
            lib.addCSourceFile(.{
                .file = b.path("src/video_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_HAS_FFMPEG",
                    "-DSHARPDICOM_WITH_MPEG",
                },
            });
            // Add FFmpeg include paths
            lib.addIncludePath(b.path("vendor/ffmpeg/src"));
            // Link against FFmpeg libraries
            lib.linkSystemLibrary("avcodec");
            lib.linkSystemLibrary("avformat");
            lib.linkSystemLibrary("avutil");
            lib.linkSystemLibrary("swscale");
            lib.linkSystemLibrary("swresample");
        } else {
            // Build stub version (video functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/video_wrapper.c"),
                .flags = common_flags,
            });
        }

        // Video encoder (FFmpeg encoding with x264/x265 backends)
        if (have_ffmpeg_enc) {
            lib.addCSourceFile(.{
                .file = b.path("src/video_encoder.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_FFMPEG_ENC",
                    "-DSHARPDICOM_WITH_MPEG",
                },
            });
            // Compile x264 from source (H.264 software encoder)
            addX264Sources(lib, b);
            // Compile x265 from source (HEVC software encoder)
            addX265Sources(lib, b);
            // Compile FFmpeg encoding libraries from source
            addFfmpegEncSources(lib, b);
        } else {
            // Build stub version (video encoding functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/video_encoder.c"),
                .flags = common_flags,
            });
        }

        // Tesseract OCR wrapper
        if (have_tesseract) {
            lib.addCSourceFile(.{
                .file = b.path("src/tesseract_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_TESSERACT",
                },
            });
            lib.addIncludePath(b.path("vendor/tesseract/src"));
            lib.addIncludePath(b.path("vendor/leptonica/src"));
            lib.linkSystemLibrary("tesseract");
            lib.linkSystemLibrary("lept");
        } else {
            // Build stub version (Tesseract functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/tesseract_wrapper.c"),
                .flags = common_flags,
            });
        }

        // 12-bit JPEG wrapper (separate libjpeg-turbo build with symbol prefixes)
        if (have_libjpeg12) {
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg12_wrapper.c"),
                .flags = jpeg12_flags,
            });
            // Compile 12-bit libjpeg-turbo with symbol prefixes
            addLibjpegTurbo12Sources(lib, b);
        } else {
            // Build stub version (12-bit JPEG functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/jpeg12_wrapper.c"),
                .flags = common_flags,
            });
        }

        // stb_image wrapper (image sequence loading)
        if (have_stb_image) {
            lib.addCSourceFile(.{
                .file = b.path("src/stb_image_wrapper.c"),
                .flags = common_flags ++ &[_][]const u8{
                    "-DSHARPDICOM_WITH_STB_IMAGE",
                    "-Wno-unused-function",
                    "-Wno-sign-compare",
                },
            });
            lib.addIncludePath(b.path("vendor/stb"));
        } else {
            // Build stub version (stb_image functions will error at runtime)
            lib.addCSourceFile(.{
                .file = b.path("src/stb_image_wrapper.c"),
                .flags = common_flags,
            });
        }

        // Include paths
        lib.addIncludePath(b.path("src"));

        // Link -ldl for dynamic library loading on Linux
        if (target_query.os_tag == .linux) {
            lib.linkSystemLibrary("dl");
        }

        // Install the library to zig-out with platform-specific naming
        const rid = getRuntimeId(target_query);
        const install_step = b.addInstallArtifact(lib, .{
            .dest_dir = .{ .override = .{ .custom = rid } },
        });
        b.getInstallStep().dependOn(&install_step.step);
    }

    // Native target (for single-platform build and tests)
    const native_target = b.standardTargetOptions(.{});

    // Single-platform build step (for development)
    const single_step = b.step("native", "Build for native platform only");
    const native_lib = b.addSharedLibrary(.{
        .name = "sharpdicom_codecs",
        .target = native_target,
        .optimize = optimize,
        .pic = true,
        .strip = true, // Strip debug symbols for smaller binaries
    });

    // Link libc for standard library headers
    native_lib.linkLibC();

    const native_flags = &[_][]const u8{
        "-std=c11",
        "-fstack-protector-strong",
        "-Wall",
        "-Wextra",
        "-Werror",
    };

    // Native flags with JPEG enabled (only when libjpeg is available)
    const native_jpeg_flags_base = if (have_libjpeg)
        native_flags ++ &[_][]const u8{"-DSHARPDICOM_WITH_JPEG"}
    else
        native_flags;

    // Native flags with both JPEG and JPEG12 when available
    const native_core_flags_1 = if (have_libjpeg12)
        native_jpeg_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_JPEG12"}
    else
        native_jpeg_flags_base;

    // Native core flags with FFmpeg encoding when available
    const native_core_flags_base = if (have_ffmpeg_enc)
        native_core_flags_1 ++ &[_][]const u8{"-DSHARPDICOM_WITH_FFMPEG_ENC"}
    else
        native_core_flags_1;

    // Handle runtime-detected features with explicit conditionals
    if (have_openjpeg and have_charls and have_stb_image) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_J2K",
                "-DSHARPDICOM_WITH_JLS",
                "-DSHARPDICOM_WITH_STB_IMAGE",
            },
        });
    } else if (have_openjpeg and have_charls) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_J2K",
                "-DSHARPDICOM_WITH_JLS",
            },
        });
    } else if (have_openjpeg and have_stb_image) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_J2K",
                "-DSHARPDICOM_WITH_STB_IMAGE",
            },
        });
    } else if (have_charls and have_stb_image) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_JLS",
                "-DSHARPDICOM_WITH_STB_IMAGE",
            },
        });
    } else if (have_openjpeg) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_J2K"},
        });
    } else if (have_charls) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_JLS"},
        });
    } else if (have_stb_image) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base ++ &[_][]const u8{"-DSHARPDICOM_WITH_STB_IMAGE"},
        });
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/sharpdicom_codecs.c"),
            .flags = native_core_flags_base,
        });
    }

    // JPEG wrapper for native build
    if (have_libjpeg) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jpeg_wrapper.c"),
            .flags = native_core_flags_1,
        });
        // Compile libjpeg-turbo from source for static linking
        addLibjpegTurboSources(native_lib, b);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jpeg_wrapper.c"),
            .flags = native_flags,
        });
    }

    // J2K wrapper for native build
    if (have_openjpeg) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/j2k_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_HAS_OPENJPEG",
                "-DSHARPDICOM_WITH_J2K",
            },
        });
        native_lib.addIncludePath(b.path("vendor/openjpeg/src/src/lib/openjp2"));
        addOpenJpegSources(native_lib, b, native_flags);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/j2k_wrapper.c"),
            .flags = native_flags,
        });
    }

    // GPU wrapper for native build
    native_lib.addCSourceFile(.{
        .file = b.path("src/gpu_wrapper.c"),
        .flags = native_flags,
    });

    // JLS wrapper for native build
    if (have_charls) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jls_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_HAS_CHARLS",
                "-DSHARPDICOM_WITH_JLS",
            },
        });
        native_lib.addIncludePath(b.path("vendor/charls/src/include"));
        native_lib.addIncludePath(b.path("vendor/charls/src/src"));
        // Compile CharLS from source (C++17)
        addCharlsSources(native_lib, b);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jls_wrapper.c"),
            .flags = native_flags,
        });
    }

    // Video wrapper for native build (FFmpeg decoding)
    if (have_ffmpeg) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/video_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_HAS_FFMPEG",
                "-DSHARPDICOM_WITH_MPEG",
            },
        });
        native_lib.addIncludePath(b.path("vendor/ffmpeg/src"));
        native_lib.linkSystemLibrary("avcodec");
        native_lib.linkSystemLibrary("avformat");
        native_lib.linkSystemLibrary("avutil");
        native_lib.linkSystemLibrary("swscale");
        native_lib.linkSystemLibrary("swresample");
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/video_wrapper.c"),
            .flags = native_flags,
        });
    }

    // Video encoder for native build (FFmpeg encoding with x264/x265 backends)
    if (have_ffmpeg_enc) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/video_encoder.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_FFMPEG_ENC",
                "-DSHARPDICOM_WITH_MPEG",
            },
        });
        // Compile x264 from source (H.264 software encoder)
        addX264Sources(native_lib, b);
        // Compile x265 from source (HEVC software encoder)
        addX265Sources(native_lib, b);
        // Compile FFmpeg encoding libraries from source
        addFfmpegEncSources(native_lib, b);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/video_encoder.c"),
            .flags = native_flags,
        });
    }

    // Tesseract OCR wrapper for native build
    if (have_tesseract) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/tesseract_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_TESSERACT",
            },
        });
        native_lib.addIncludePath(b.path("vendor/tesseract/src"));
        native_lib.addIncludePath(b.path("vendor/leptonica/src"));
        native_lib.linkSystemLibrary("tesseract");
        native_lib.linkSystemLibrary("lept");
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/tesseract_wrapper.c"),
            .flags = native_flags,
        });
    }

    // 12-bit JPEG wrapper for native build
    if (have_libjpeg12) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jpeg12_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_JPEG12",
            },
        });
        // Compile 12-bit libjpeg-turbo with symbol prefixes
        addLibjpegTurbo12Sources(native_lib, b);
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/jpeg12_wrapper.c"),
            .flags = native_flags,
        });
    }

    // stb_image wrapper for native build
    if (have_stb_image) {
        native_lib.addCSourceFile(.{
            .file = b.path("src/stb_image_wrapper.c"),
            .flags = native_flags ++ &[_][]const u8{
                "-DSHARPDICOM_WITH_STB_IMAGE",
                "-Wno-unused-function",
                "-Wno-sign-compare",
            },
        });
        native_lib.addIncludePath(b.path("vendor/stb"));
    } else {
        native_lib.addCSourceFile(.{
            .file = b.path("src/stb_image_wrapper.c"),
            .flags = native_flags,
        });
    }

    native_lib.addIncludePath(b.path("src"));

    // Link -ldl on Linux for dynamic library loading
    if (native_target.result.os.tag == .linux) {
        native_lib.linkSystemLibrary("dl");
    }

    const native_install = b.addInstallArtifact(native_lib, .{});
    single_step.dependOn(&native_install.step);

    // Native test executable - links against native_lib to verify the full build
    const test_exe = b.addExecutable(.{
        .name = "test_version",
        .target = native_target,
        .optimize = optimize,
    });

    test_exe.linkLibC();
    test_exe.addCSourceFile(.{
        .file = b.path("test/test_version.c"),
        .flags = &.{
            "-std=c11",
            "-Wall",
            "-Wextra",
        },
    });
    test_exe.addIncludePath(b.path("src"));

    // Link against the native_lib artifact (this creates a build dependency)
    test_exe.linkLibrary(native_lib);

    // Link -ldl on Linux for dynamic library loading
    if (native_target.result.os.tag == .linux) {
        test_exe.linkSystemLibrary("dl");
    }

    const test_install = b.addInstallArtifact(test_exe, .{});

    // Test step
    const test_step = b.step("test", "Run native tests");
    const run_test = b.addRunArtifact(test_exe);
    test_step.dependOn(&test_install.step);
    test_step.dependOn(&run_test.step);
}

/// Maps Zig target to .NET Runtime Identifier
fn getRuntimeId(target: std.Target.Query) []const u8 {
    const arch = switch (target.cpu_arch orelse .x86_64) {
        .x86_64 => "x64",
        .aarch64 => "arm64",
        else => "unknown",
    };

    const os = switch (target.os_tag orelse .linux) {
        .windows => "win",
        .linux => "linux",
        .macos => "osx",
        else => "unknown",
    };

    // Return static string based on combination
    if (std.mem.eql(u8, os, "win") and std.mem.eql(u8, arch, "x64")) return "win-x64";
    if (std.mem.eql(u8, os, "win") and std.mem.eql(u8, arch, "arm64")) return "win-arm64";
    if (std.mem.eql(u8, os, "linux") and std.mem.eql(u8, arch, "x64")) return "linux-x64";
    if (std.mem.eql(u8, os, "linux") and std.mem.eql(u8, arch, "arm64")) return "linux-arm64";
    if (std.mem.eql(u8, os, "osx") and std.mem.eql(u8, arch, "x64")) return "osx-x64";
    if (std.mem.eql(u8, os, "osx") and std.mem.eql(u8, arch, "arm64")) return "osx-arm64";

    return "unknown";
}

/// Detect if a vendor library directory exists
fn detectVendorLibrary(path: []const u8) bool {
    var dir = std.fs.cwd().openDir(path, .{}) catch return false;
    dir.close();
    return true;
}

/// Add OpenJPEG source files to compilation
fn addOpenJpegSources(lib: *std.Build.Step.Compile, b: *std.Build, _: []const []const u8) void {
    const opj_base = "vendor/openjpeg/src/src/lib/openjp2";

    // OpenJPEG core source files
    const opj_sources = [_][]const u8{
        "bio.c",
        "cio.c",
        "dwt.c",
        "event.c",
        "function_list.c",
        "ht_dec.c",
        "image.c",
        "invert.c",
        "j2k.c",
        "jp2.c",
        "mct.c",
        "mqc.c",
        "openjpeg.c",
        "opj_clock.c",
        "opj_malloc.c",
        "pi.c",
        "sparse_array.c",
        "t1.c",
        "t2.c",
        "tcd.c",
        "tgt.c",
        "thread.c",
    };

    // OpenJPEG-specific flags - defined as comptime constant to allow concatenation
    // Includes common flags plus OpenJPEG-specific suppressions for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const opj_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Werror",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-implicit-fallthrough",
        "-Wno-unused-but-set-variable", // OpenJPEG has some variables set but not used
        "-Wno-unused-function", // OpenJPEG has static functions not used in all configs
        "-DOPJ_STATIC",
        "-DUSE_JPIP=0",
    };

    for (opj_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ opj_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = opj_flags,
        });
    }

    // Add OpenJPEG include paths
    lib.addIncludePath(b.path(opj_base));
}

/// Add CharLS source files to compilation (JPEG-LS codec).
/// Vendor sources are downloaded by CI into vendor/charls/src/.
///
/// CharLS is a C++17 library that provides a C API (charls.h).
/// We compile the C++ sources and link them into our shared library.
///
/// Reference: https://github.com/team-charls/charls
fn addCharlsSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const charls_base = "vendor/charls/src/src";

    // CharLS C++17 compilation flags - relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const charls_flags = &[_][]const u8{
        "-std=c++17",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-missing-field-initializers",
        "-DCHARLS_STATIC", // Build as static library to link into our shared lib
    };

    // CharLS 2.4.2 source files (from src/CMakeLists.txt)
    // Note: Files like golomb_lut.cpp, make_scan_codec.cpp only exist in unreleased main branch.
    // Using 2.4.2 stable release file list.
    const charls_sources = [_][]const u8{
        "charls_jpegls_decoder.cpp",
        "charls_jpegls_encoder.cpp",
        "jpeg_stream_reader.cpp",
        "jpeg_stream_writer.cpp",
        "jpegls.cpp",
        "jpegls_error.cpp",
        "validate_spiff_header.cpp",
        "version.cpp",
    };

    for (charls_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ charls_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = charls_flags,
        });
    }

    // CharLS include paths
    lib.addIncludePath(b.path("vendor/charls/src/include"));
    lib.addIncludePath(b.path(charls_base));

    // Link C++ standard library for C++ code
    lib.linkLibCpp();
}

/// Add x264 source files to compilation (H.264 software encoder).
/// Vendor sources are downloaded by CI into vendor/x264/.
/// Requires a generated x264_config.h at vendor/x264/x264_config.h.
///
/// Build follows the pattern from x264's Makefile:
/// - Core encoder in common/ and encoder/ directories
/// - x264_config.h defines X264_BIT_DEPTH=8, X264_CHROMA_FORMAT=0 (all), X264_GPL=1
///
/// Reference: https://code.videolan.org/videolan/x264
fn addX264Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const x264_base = "vendor/x264";

    // x264 compilation flags - relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const x264_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Werror",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
    };

    // TODO: Populate with actual x264 source files when vendor sources are downloaded.
    // The file list below covers the core encoder (no CLI, no filters, no asm):
    //
    // common/ directory:
    //   base.c, bitstream.c, cabac.c, common.c, dct.c, deblock.c, frame.c,
    //   mc.c, mvpred.c, osdep.c, pixel.c, predict.c, quant.c, rectangle.c,
    //   set.c, vlc.c, threadpool.c, cpu.c, tables.c
    //
    // encoder/ directory:
    //   analyse.c, cabac.c, cavlc.c, encoder.c, lookahead.c,
    //   macroblock.c, me.c, ratecontrol.c, set.c, slicetype.c
    const x264_sources = [_][]const u8{
        // common/
        "common/base.c",
        "common/bitstream.c",
        "common/cabac.c",
        "common/common.c",
        "common/dct.c",
        "common/deblock.c",
        "common/frame.c",
        "common/mc.c",
        "common/mvpred.c",
        "common/osdep.c",
        "common/pixel.c",
        "common/predict.c",
        "common/quant.c",
        "common/rectangle.c",
        "common/set.c",
        "common/vlc.c",
        "common/threadpool.c",
        "common/cpu.c",
        "common/tables.c",
        // encoder/
        "encoder/analyse.c",
        "encoder/cabac.c",
        "encoder/cavlc.c",
        "encoder/encoder.c",
        "encoder/lookahead.c",
        "encoder/macroblock.c",
        "encoder/me.c",
        "encoder/ratecontrol.c",
        "encoder/set.c",
        "encoder/slicetype.c",
    };

    for (x264_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ x264_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = x264_flags,
        });
    }

    // x264 include paths (main dir contains x264.h and generated x264_config.h)
    lib.addIncludePath(b.path(x264_base));
    lib.addIncludePath(b.path("vendor/x264/common"));
}

/// Add x265 source files to compilation (HEVC software encoder).
/// Vendor sources are downloaded by CI into vendor/x265/.
/// Requires generated x265_config.h at vendor/x265/source/x265_config.h.
///
/// Build follows the pattern from x265's CMakeLists.txt:
/// - Core encoder in source/common/ and source/encoder/ directories
/// - x265_config.h defines X265_DEPTH=8, EXPORT_C_API=1, X265_NS=x265
///
/// NOTE: x265 is C++, compiled with Zig's C++ compiler mode.
/// Reference: https://bitbucket.org/multicoreware/x265_git
fn addX265Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const x265_base = "vendor/x265/source";

    // x265 compilation flags - C++ mode, relaxed warnings for third-party code
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const x265_flags = &[_][]const u8{
        "-std=c++14",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Werror",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-Wno-class-memaccess",
        "-DX265_DEPTH=8",
        "-DEXPORT_C_API=1",
        "-DX265_NS=x265",
    };

    // TODO: Populate with actual x265 source files when vendor sources are downloaded.
    // The file list below covers the core encoder (no CLI, no asm):
    //
    // common/ directory:
    //   bitstream.cpp, common.cpp, constants.cpp, cpu.cpp, cudata.cpp,
    //   dct.cpp, deblock.cpp, frame.cpp, framedata.cpp, intrapred.cpp,
    //   ipfilter.cpp, loopfilter.cpp, lowpassdct.cpp, lowres.cpp, md5.cpp,
    //   param.cpp, piclist.cpp, picyuv.cpp, pixel.cpp, predict.cpp,
    //   primitives.cpp, quant.cpp, ringmem.cpp, scalinglist.cpp, shortyuv.cpp,
    //   slice.cpp, threading.cpp, threadpool.cpp, wavefront.cpp, yuv.cpp
    //
    // encoder/ directory:
    //   analysis.cpp, api.cpp, bitcost.cpp, dpb.cpp, encoder.cpp,
    //   entropy.cpp, frameencoder.cpp, framefilter.cpp, level.cpp,
    //   motion.cpp, nal.cpp, ratecontrol.cpp, reference.cpp, sao.cpp,
    //   search.cpp, sei.cpp, slicetype.cpp, weightPrediction.cpp
    const x265_sources = [_][]const u8{
        // common/
        "common/bitstream.cpp",
        "common/common.cpp",
        "common/constants.cpp",
        "common/cpu.cpp",
        "common/cudata.cpp",
        "common/dct.cpp",
        "common/deblock.cpp",
        "common/frame.cpp",
        "common/framedata.cpp",
        "common/intrapred.cpp",
        "common/ipfilter.cpp",
        "common/loopfilter.cpp",
        "common/lowpassdct.cpp",
        "common/lowres.cpp",
        "common/md5.cpp",
        "common/param.cpp",
        "common/piclist.cpp",
        "common/picyuv.cpp",
        "common/pixel.cpp",
        "common/predict.cpp",
        "common/primitives.cpp",
        "common/quant.cpp",
        "common/ringmem.cpp",
        "common/scalinglist.cpp",
        "common/shortyuv.cpp",
        "common/slice.cpp",
        "common/threading.cpp",
        "common/threadpool.cpp",
        "common/wavefront.cpp",
        "common/yuv.cpp",
        // encoder/
        "encoder/analysis.cpp",
        "encoder/api.cpp",
        "encoder/bitcost.cpp",
        "encoder/dpb.cpp",
        "encoder/encoder.cpp",
        "encoder/entropy.cpp",
        "encoder/frameencoder.cpp",
        "encoder/framefilter.cpp",
        "encoder/level.cpp",
        "encoder/motion.cpp",
        "encoder/nal.cpp",
        "encoder/ratecontrol.cpp",
        "encoder/reference.cpp",
        "encoder/sao.cpp",
        "encoder/search.cpp",
        "encoder/sei.cpp",
        "encoder/slicetype.cpp",
        "encoder/weightPrediction.cpp",
    };

    for (x265_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ x265_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = x265_flags,
        });
    }

    // x265 include paths
    lib.addIncludePath(b.path(x265_base));
    lib.addIncludePath(b.path("vendor/x265/source/common"));
    lib.addIncludePath(b.path("vendor/x265/source/encoder"));

    // Link C++ standard library for x265 (C++ code)
    lib.linkLibCpp();
}

/// Add FFmpeg encoding library source files to compilation.
/// Vendor sources are downloaded by CI into vendor/ffmpeg/.
/// Requires a generated config.h at vendor/ffmpeg/config.h.
///
/// This compiles a minimal subset of FFmpeg focused on encoding:
/// - libavcodec: core encoding framework + specific encoders
/// - libavutil: utility functions (math, memory, pixfmt, etc.)
/// - libswscale: pixel format conversion (RGB -> YUV)
/// - libswresample: audio format conversion
/// - libavformat: muxing (MPEG-TS, raw H.264/HEVC Annex-B output)
///
/// The FFmpeg build bypasses configure/make and compiles C sources directly
/// via Zig, following the allyourcodebase/ffmpeg pattern.
///
/// Minimal encoder configuration (matching RESEARCH.md):
/// - Video encoders: mpeg2video (built-in), libx264, libx265
/// - Audio encoders: aac, pcm_s16le
/// - Video decoders: mpeg2video, h264, hevc (already in have_ffmpeg decode path)
/// - Audio decoders: aac, pcm_s16le
/// - Muxers: mpegts, h264, hevc, rawvideo, adts, wav
///
/// Reference: https://github.com/FFmpeg/FFmpeg
fn addFfmpegEncSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const ffmpeg_base = "vendor/ffmpeg";

    // FFmpeg compilation flags - relaxed warnings for third-party code
    // config.h is generated by CI/scripts to enable only the needed codecs
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const ffmpeg_flags = &[_][]const u8{
        "-std=c11",
        "-O2", // Required for _FORTIFY_SOURCE with glibc
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Werror",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-unused-variable",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        "-Wno-pointer-sign",
        "-Wno-switch",
        "-Wno-parentheses",
        "-Wno-deprecated-declarations",
        "-DHAVE_CONFIG_H", // Use generated config.h
    };

    // TODO: Populate with actual FFmpeg source files when vendor sources are downloaded.
    // The file lists below cover the minimal encoding subset.
    // Each library's sources are listed separately for clarity.
    //
    // Source file discovery command (run from vendor/ffmpeg/):
    //   grep -l 'REGISTER_ENCODER\|ff_mpeg2video_encoder\|ff_libx264_encoder\|ff_libx265_encoder' libavcodec/*.c
    //
    // ========================================================================
    // libavutil sources (utility library - always needed)
    // ========================================================================
    // Core: avutil.c, buffer.c, channel_layout.c, cpu.c, crc.c, dict.c,
    //   error.c, eval.c, fifo.c, frame.c, hwcontext.c, imgutils.c, log.c,
    //   mathematics.c, mem.c, opt.c, parseutils.c, pixdesc.c, rational.c,
    //   samplefmt.c, time.c, timecode.c, utils.c
    //
    // ========================================================================
    // libavcodec sources (encoding/decoding framework)
    // ========================================================================
    // Core: allcodecs.c, avcodec.c, avpacket.c, bitstream.c, bsf.c,
    //   codec_desc.c, decode.c, encode.c, options.c, parser.c, profiles.c,
    //   utils.c
    // MPEG-2 encoder: mpeg12enc.c, mpeg12data.c, mpegvideo.c, mpegvideo_enc.c,
    //   motion_est.c, ratecontrol.c
    // H.264 via libx264: libx264.c
    // HEVC via libx265: libx265.c
    // AAC encoder: aacenc.c, aaccoder.c, aacenctab.c, aacpsy.c, psymodel.c
    // PCM encoder: pcm.c
    //
    // ========================================================================
    // libswscale sources (pixel format conversion)
    // ========================================================================
    // Core: input.c, options.c, output.c, rgb2rgb.c, slice.c, swscale.c,
    //   swscale_unscaled.c, utils.c, yuv2rgb.c
    //
    // ========================================================================
    // libswresample sources (audio format conversion)
    // ========================================================================
    // Core: audioconvert.c, dither.c, options.c, rematrix.c, resample.c,
    //   resample_dsp.c, swresample.c, swresample_frame.c
    //
    // ========================================================================
    // libavformat sources (muxing)
    // ========================================================================
    // Core: allformats.c, avio.c, aviobuf.c, format.c, id3v2.c, mux.c,
    //   mux_utils.c, options.c, protocols.c, url.c, utils.c
    // MPEG-TS muxer: mpegtsenc.c
    // Raw muxers: rawenc.c, h264_muxer.c, hevc_muxer.c
    // Audio muxers: adtsenc.c, wavenc.c
    //
    // NOTE: The exact file lists will vary by FFmpeg version. The CI script
    // that downloads vendor sources should also validate that these files exist
    // and update the list if needed.

    // For now, define the source file arrays. These will be populated
    // when the vendor source download script is finalized.

    const avutil_sources = [_][]const u8{
        "libavutil/avutil.c",
        "libavutil/buffer.c",
        "libavutil/channel_layout.c",
        "libavutil/cpu.c",
        "libavutil/crc.c",
        "libavutil/dict.c",
        "libavutil/error.c",
        "libavutil/eval.c",
        "libavutil/fifo.c",
        "libavutil/frame.c",
        "libavutil/hwcontext.c",
        "libavutil/imgutils.c",
        "libavutil/log.c",
        "libavutil/mathematics.c",
        "libavutil/mem.c",
        "libavutil/opt.c",
        "libavutil/parseutils.c",
        "libavutil/pixdesc.c",
        "libavutil/rational.c",
        "libavutil/samplefmt.c",
        "libavutil/time.c",
        "libavutil/timecode.c",
        "libavutil/utils.c",
    };

    const avcodec_sources = [_][]const u8{
        // Core framework
        "libavcodec/allcodecs.c",
        "libavcodec/avcodec.c",
        "libavcodec/avpacket.c",
        "libavcodec/bitstream.c",
        "libavcodec/bsf.c",
        "libavcodec/codec_desc.c",
        "libavcodec/decode.c",
        "libavcodec/encode.c",
        "libavcodec/options.c",
        "libavcodec/parser.c",
        "libavcodec/profiles.c",
        "libavcodec/utils.c",
        // MPEG-2 video encoder
        "libavcodec/mpeg12enc.c",
        "libavcodec/mpeg12data.c",
        "libavcodec/mpegvideo.c",
        "libavcodec/mpegvideo_enc.c",
        "libavcodec/motion_est.c",
        "libavcodec/ratecontrol.c",
        // libx264 wrapper (H.264 encoding via x264)
        "libavcodec/libx264.c",
        // libx265 wrapper (HEVC encoding via x265)
        "libavcodec/libx265.c",
        // AAC audio encoder
        "libavcodec/aacenc.c",
        "libavcodec/aaccoder.c",
        "libavcodec/aacenctab.c",
        "libavcodec/aacpsy.c",
        "libavcodec/psymodel.c",
        // PCM encoder
        "libavcodec/pcm.c",
    };

    const swscale_sources = [_][]const u8{
        "libswscale/input.c",
        "libswscale/options.c",
        "libswscale/output.c",
        "libswscale/rgb2rgb.c",
        "libswscale/slice.c",
        "libswscale/swscale.c",
        "libswscale/swscale_unscaled.c",
        "libswscale/utils.c",
        "libswscale/yuv2rgb.c",
    };

    const swresample_sources = [_][]const u8{
        "libswresample/audioconvert.c",
        "libswresample/dither.c",
        "libswresample/options.c",
        "libswresample/rematrix.c",
        "libswresample/resample.c",
        "libswresample/resample_dsp.c",
        "libswresample/swresample.c",
        "libswresample/swresample_frame.c",
    };

    const avformat_sources = [_][]const u8{
        // Core muxing framework
        "libavformat/allformats.c",
        "libavformat/avio.c",
        "libavformat/aviobuf.c",
        "libavformat/format.c",
        "libavformat/id3v2.c",
        "libavformat/mux.c",
        "libavformat/mux_utils.c",
        "libavformat/options.c",
        "libavformat/protocols.c",
        "libavformat/url.c",
        "libavformat/utils.c",
        // MPEG-TS muxer (for MPEG-2 video + audio interleave)
        "libavformat/mpegtsenc.c",
        // Raw video/audio muxers
        "libavformat/rawenc.c",
        // Audio container muxers
        "libavformat/adtsenc.c",
        "libavformat/wavenc.c",
    };

    // Compile all FFmpeg source files
    const all_source_groups = [_][]const []const u8{
        &avutil_sources,
        &avcodec_sources,
        &swscale_sources,
        &swresample_sources,
        &avformat_sources,
    };

    for (all_source_groups) |sources| {
        for (sources) |src| {
            const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ ffmpeg_base, src }) catch continue;
            lib.addCSourceFile(.{
                .file = b.path(full_path),
                .flags = ffmpeg_flags,
            });
        }
    }

    // FFmpeg include paths
    // Main directory contains the library headers and generated config.h
    lib.addIncludePath(b.path(ffmpeg_base));
    // Individual library directories for internal headers
    lib.addIncludePath(b.path("vendor/ffmpeg/libavutil"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libavcodec"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libavformat"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libswscale"));
    lib.addIncludePath(b.path("vendor/ffmpeg/libswresample"));
}

/// Add libjpeg-turbo source files to compilation (8-bit JPEG codec).
/// Vendor sources are downloaded by CI into vendor/libjpeg-turbo/src/.
/// Config headers jconfig.h and jconfigint.h are in vendor/libjpeg-turbo/.
///
/// Build configuration:
/// - Uses standard libjpeg 6b API (JPEG_LIB_VERSION 62)
/// - TurboJPEG API enabled for high-performance encoding/decoding
/// - SIMD disabled for portable cross-compilation
/// - 8-bit precision (BITS_IN_JSAMPLE 8)
///
/// Reference: https://github.com/libjpeg-turbo/libjpeg-turbo
fn addLibjpegTurboSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const jpeg_base = "vendor/libjpeg-turbo/src";

    // libjpeg-turbo compilation flags - 8-bit build, no SIMD
    // Note: -O2 required before -D_FORTIFY_SOURCE=2 for glibc compatibility
    const jpeg_flags = &[_][]const u8{
        "-std=c11",
        "-O2",
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-shift-negative-value",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
    };

    // Core libjpeg sources (from libjpeg-turbo CMakeLists.txt)
    // These are the standard libjpeg API files
    const jpeg_sources = [_][]const u8{
        // Compression
        "jcapimin.c",
        "jcapistd.c",
        "jccoefct.c",
        "jccolor.c",
        "jcdctmgr.c",
        "jchuff.c",
        "jcicc.c",
        "jcinit.c",
        "jcmainct.c",
        "jcmarker.c",
        "jcmaster.c",
        "jcomapi.c",
        "jcparam.c",
        "jcphuff.c",
        "jcprepct.c",
        "jcsample.c",
        "jctrans.c",
        // Decompression
        "jdapimin.c",
        "jdapistd.c",
        "jdatadst.c",
        "jdatasrc.c",
        "jdcoefct.c",
        "jdcolor.c",
        "jddctmgr.c",
        "jdhuff.c",
        "jdicc.c",
        "jdinput.c",
        "jdmainct.c",
        "jdmarker.c",
        "jdmaster.c",
        "jdmerge.c",
        "jdphuff.c",
        "jdpostct.c",
        "jdsample.c",
        "jdtrans.c",
        // Common
        "jerror.c",
        "jfdctflt.c",
        "jfdctfst.c",
        "jfdctint.c",
        "jidctflt.c",
        "jidctfst.c",
        "jidctint.c",
        "jidctred.c",
        "jmemmgr.c",
        "jmemnobs.c", // No backing store memory manager
        "jquant1.c",
        "jquant2.c",
        "jutils.c",
        // Arithmetic coding (lossless JPEG support)
        "jaricom.c",
        "jcarith.c",
        "jdarith.c",
    };

    // TurboJPEG API sources
    const turbojpeg_sources = [_][]const u8{
        "turbojpeg.c",
        "transupp.c",
        "jdatadst-tj.c",
        "jdatasrc-tj.c",
    };

    // Compile core libjpeg sources
    for (jpeg_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg_flags,
        });
    }

    // Compile TurboJPEG API
    for (turbojpeg_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg_flags,
        });
    }

    // Include paths: config headers in vendor/libjpeg-turbo/, source in vendor/libjpeg-turbo/src/
    lib.addIncludePath(b.path("vendor/libjpeg-turbo"));
    lib.addIncludePath(b.path(jpeg_base));
}

/// Add 12-bit libjpeg-turbo source files to compilation.
/// Uses symbol prefixes to avoid collisions with 8-bit build.
///
/// The 12-bit build:
/// - Uses raw libjpeg API (no TurboJPEG - incompatible with WITH_12BIT)
/// - All public symbols prefixed with "jpeg12_" via -D flags
/// - SIMD disabled (not compatible with 12-bit)
/// - 12-bit precision (BITS_IN_JSAMPLE 12)
fn addLibjpegTurbo12Sources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    const jpeg_base = "vendor/libjpeg-turbo/src";

    // 12-bit libjpeg-turbo compilation flags with symbol prefixes
    const jpeg12_flags = &[_][]const u8{
        "-std=c11",
        "-O2",
        "-fstack-protector-strong",
        "-D_FORTIFY_SOURCE=2",
        "-Wall",
        "-Wextra",
        "-Wno-unused-parameter",
        "-Wno-sign-compare",
        "-Wno-shift-negative-value",
        "-Wno-implicit-fallthrough",
        "-Wno-missing-field-initializers",
        // Enable 12-bit mode
        "-DWITH_12BIT=1",
        "-DBITS_IN_JSAMPLE=12",
        // Symbol prefixes for all public libjpeg functions
        "-Djpeg_CreateCompress=jpeg12_jpeg_CreateCompress",
        "-Djpeg_CreateDecompress=jpeg12_jpeg_CreateDecompress",
        "-Djpeg_mem_src=jpeg12_jpeg_mem_src",
        "-Djpeg_mem_dest=jpeg12_jpeg_mem_dest",
        "-Djpeg_read_header=jpeg12_jpeg_read_header",
        "-Djpeg_start_decompress=jpeg12_jpeg_start_decompress",
        "-Djpeg_read_scanlines=jpeg12_jpeg_read_scanlines",
        "-Djpeg_finish_decompress=jpeg12_jpeg_finish_decompress",
        "-Djpeg_destroy_decompress=jpeg12_jpeg_destroy_decompress",
        "-Djpeg_start_compress=jpeg12_jpeg_start_compress",
        "-Djpeg_write_scanlines=jpeg12_jpeg_write_scanlines",
        "-Djpeg_finish_compress=jpeg12_jpeg_finish_compress",
        "-Djpeg_destroy_compress=jpeg12_jpeg_destroy_compress",
        "-Djpeg_set_defaults=jpeg12_jpeg_set_defaults",
        "-Djpeg_set_quality=jpeg12_jpeg_set_quality",
        "-Djpeg_std_error=jpeg12_jpeg_std_error",
        "-Djpeg_abort_compress=jpeg12_jpeg_abort_compress",
        "-Djpeg_abort_decompress=jpeg12_jpeg_abort_decompress",
        "-Djpeg_alloc_quant_table=jpeg12_jpeg_alloc_quant_table",
        "-Djpeg_alloc_huff_table=jpeg12_jpeg_alloc_huff_table",
        "-Djpeg_abort=jpeg12_jpeg_abort",
        "-Djpeg_destroy=jpeg12_jpeg_destroy",
        "-Djpeg_resync_to_restart=jpeg12_jpeg_resync_to_restart",
    };

    // Core libjpeg sources (same as 8-bit, but compiled with 12-bit flags)
    // Note: No TurboJPEG for 12-bit builds (incompatible)
    const jpeg12_sources = [_][]const u8{
        // Compression
        "jcapimin.c",
        "jcapistd.c",
        "jccoefct.c",
        "jccolor.c",
        "jcdctmgr.c",
        "jchuff.c",
        "jcicc.c",
        "jcinit.c",
        "jcmainct.c",
        "jcmarker.c",
        "jcmaster.c",
        "jcomapi.c",
        "jcparam.c",
        "jcphuff.c",
        "jcprepct.c",
        "jcsample.c",
        "jctrans.c",
        // Decompression
        "jdapimin.c",
        "jdapistd.c",
        "jdatadst.c",
        "jdatasrc.c",
        "jdcoefct.c",
        "jdcolor.c",
        "jddctmgr.c",
        "jdhuff.c",
        "jdicc.c",
        "jdinput.c",
        "jdmainct.c",
        "jdmarker.c",
        "jdmaster.c",
        "jdmerge.c",
        "jdphuff.c",
        "jdpostct.c",
        "jdsample.c",
        "jdtrans.c",
        // Common
        "jerror.c",
        "jfdctflt.c",
        "jfdctfst.c",
        "jfdctint.c",
        "jidctflt.c",
        "jidctfst.c",
        "jidctint.c",
        "jidctred.c",
        "jmemmgr.c",
        "jmemnobs.c",
        "jquant1.c",
        "jquant2.c",
        "jutils.c",
        // Arithmetic coding
        "jaricom.c",
        "jcarith.c",
        "jdarith.c",
    };

    // Compile 12-bit libjpeg sources with symbol prefixes
    for (jpeg12_sources) |src| {
        const full_path = std.fmt.allocPrint(b.allocator, "{s}/{s}", .{ jpeg_base, src }) catch continue;
        lib.addCSourceFile(.{
            .file = b.path(full_path),
            .flags = jpeg12_flags,
        });
    }

    // Include paths
    lib.addIncludePath(b.path("vendor/libjpeg-turbo"));
    lib.addIncludePath(b.path(jpeg_base));
}

/// Add Tesseract OCR source files to compilation.
/// Vendor sources are downloaded by CI into vendor/tesseract/src/ and vendor/leptonica/src/.
///
/// Build configuration:
/// - Tesseract 5.x with C API (capi.h)
/// - Leptonica for image processing (required dependency)
/// - No training tools or language data (handled at runtime)
///
/// Reference: https://github.com/tesseract-ocr/tesseract
fn addTesseractSources(lib: *std.Build.Step.Compile, b: *std.Build) void {
    _ = lib;
    _ = b;
    // TODO: Tesseract is a large C++ library with many dependencies.
    // For now, we link against system-installed Tesseract.
    // Full source compilation to be implemented when needed.
    //
    // When implementing:
    // 1. Add Leptonica sources first (image I/O, preprocessing)
    // 2. Add Tesseract CCMAIN sources (main API)
    // 3. Add CCSTRUCT sources (data structures)
    // 4. Add CLASSIFY sources (character recognition)
    // 5. Add DICT sources (dictionary lookup)
    // 6. Add LSTM sources (neural network recognition)
}
